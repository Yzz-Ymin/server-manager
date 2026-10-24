#!/usr/bin/env python3
"""Private JSON pipe for the native menu app. Never put secrets in argv or logs."""
import json
import fcntl
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile

import server_access as access
from connection import connection, host_keys


def describe(alias):
    path, profile = access.load(alias)
    info = profile.get('connection')
    if info is None:
        result = subprocess.run(['/usr/bin/ssh', '-G', '-F', str(path/'ssh_config'), alias], capture_output=True, text=True, timeout=10)
        if result.returncode:
            raise ValueError('无法解析已有 SSH 配置。')
        parsed = {}
        for line in result.stdout.splitlines():
            key, _, value = line.partition(' ')
            parsed.setdefault(key, value)
        info = {'host': parsed.get('hostname',''), 'account':profile['account'], 'port':parsed.get('port','22'),
                'key':parsed.get('identityfile','') if parsed.get('pubkeyauthentication') not in ('false','no') else ''}
    return {'alias':alias, 'group':profile.get('group','未分组'), **info,
            'credentials':profile.get('credentials',[]), 'operations':list(profile.get('operations',{})),
            'sudoSameAsLogin':profile.get('sudo_same_as_login',False)}


def listing():
    return sorted([describe(entry.parent.name) for entry in access.ROOT.glob('*/profile.json')], key=lambda item:(item['group'],item['alias']))


def validate(values, editing=False):
    alias=access.name(values.get('alias',''))
    group=values.get('group','').strip() or '未分组'
    if len(group)>40 or any(ord(c)<32 for c in group):
        raise ValueError('分组名称最多 40 个字符，不能包含控制字符。')
    # Validation must not require or consume a password during a public-key scan.
    public={key:values.get(key,'') for key in ('host','account','port','key')}
    public['login']='validation-only' if not public['key'] else ''
    host,user,port,key,_=connection(public)
    return alias,group,host,user,port,key


def scan(values):
    alias,group,host,user,port,key=validate(values)
    keys,fingerprints=host_keys(host,port)
    return {'keys':keys.decode(), 'fingerprints':fingerprints}


def verify(alias):
    _,profile=access.load(alias)
    for operation in ('status','sudo-check') if 'sudo' in profile['credentials'] else ('status',):
        label = 'sudo' if operation == 'sudo-check' else 'SSH'
        try:
            result=subprocess.run([sys.executable,'-B',str(Path(access.__file__).resolve()),'run',alias,operation,'--timeout','45','--diagnostic'], capture_output=True,timeout=65)
        except subprocess.TimeoutExpired:
            raise ValueError(f'{label} 验证超时。本次保存未完成；请检查网络及本机钥匙串授权提示。') from None
        if result.returncode:
            error=result.stderr.lower()
            key_errors=b"\n".join(line for line in error.splitlines() if b"load key" in line)
            if b'host key verification failed' in error:
                reason='主机密钥与已确认指纹不一致，请重新核对服务器指纹。'
            elif b'unprotected private key file' in error or b'bad permissions' in error:
                reason='所选私钥文件权限过宽，OpenSSH 拒绝使用。请将该文件权限设为仅当前用户可读写（600）后重试；私钥内容无需更改。'
            elif (b'invalid format' in key_errors or b'error in libcrypto' in key_errors):
                reason='系统 OpenSSH 无法解析所选文件。请确认选中的是 OpenSSH/PEM 私钥，不是 .pub 公钥或 PuTTY .ppk 文件。'
            elif (b'operation not permitted' in key_errors or b'permission denied' in key_errors):
                reason='本机系统权限阻止 SSH 打开私钥文件，请检查文件访问权限或 macOS 隐私授权。'
            elif b'no such identity' in error or b'no such file' in key_errors:
                reason='SSH 找不到所选私钥，请重新选择仍可访问的本机文件。'
            elif b'no mutual signature' in error or b'no mutual signature algorithm' in error:
                reason='私钥签名算法与服务器不兼容，请检查服务器支持的 SSH 算法；工具不会自动启用过时算法。'
            elif b'agent refused operation' in error:
                reason='本机 SSH Agent 拒绝使用该密钥签名，请检查钥匙串或硬件密钥授权。'
            elif b'no passphrase given' in error and 'passphrase' not in profile.get('credentials',[]):
                reason='所选私钥需要解锁口令，请填写“私钥解锁口令”（不是 SSH 登录密码）。'
            elif b'incorrect passphrase' in error or b'bad passphrase' in error:
                reason='私钥解锁口令不正确，请填写私钥本身的口令（不是服务器登录密码）。'
            elif b'server accepts key:' in error and 'passphrase' not in profile.get('credentials',[]) and profile.get('connection',{}).get('key'):
                reason='服务器已接受所选公钥，但私钥签名未完成。若私钥有加密，请填写“私钥解锁口令”；也请检查本机 Agent 授权。'
            elif b'offering public key:' in error and b'server accepts key:' not in error and b'permission denied' in error:
                reason='SSH 已提交所选公钥，但服务器未接受。请核对用户名、地址和端口，以及该账号 authorized_keys 中的公钥是否匹配。'
            elif b'authenticated to ' in error and operation=='sudo-check':
                reason='SSH 登录已成功，但 sudo 验证失败，请检查 sudo 密码及该账号的 sudo 权限。'
            elif b'permission denied' in error or b'authentication failed' in error:
                reason='登录认证失败，请检查 SSH 用户名、密码或私钥口令。'
            elif b'incorrect password' in error or b'sorry, try again' in error:
                reason='sudo 密码验证失败。'
            elif b'terminal is required' in error or b'must have a tty' in error:
                reason='服务器 sudo 策略要求交互式终端，当前自动验证方式不受支持。'
            elif b'connection refused' in error:
                reason='服务器拒绝连接，请检查地址、SSH 端口及服务状态。'
            elif b'timed out' in error or result.returncode==124:
                reason='连接超时，请检查网络、防火墙或本机钥匙串授权提示。'
            elif b'osstatus' in error or '钥匙串'.encode() in error:
                reason='本机钥匙串访问未完成，请检查系统授权提示。'
            else:
                reason='请检查网络、登录凭据、本机钥匙串授权或服务器 sudo 策略。'
            raise ValueError(f'{label} 验证失败：{reason}本次保存已回滚，尚未新增或覆盖配置。')
    return {'message':'SSH'+(' 和 sudo' if 'sudo' in profile['credentials'] else '')+' 验证通过。'}


def save(values):
    alias,group,host,user,port,key=validate(values)
    original=values.get('original','')
    if original and access.name(original)!=alias:
        raise ValueError('编辑时不能更改别名；请新增服务器后再删除旧项。')
    final=access.ROOT/alias
    if final.exists() and not original:
        raise ValueError('该别名已经存在，请更换别名。')
    if original and not final.exists():
        raise ValueError('原配置已不存在，请刷新列表。')
    old=access.load(alias)[1] if original else None
    old_user=old['account'] if old else user
    kinds=('passphrase','sudo') if key else ('login','sudo')
    values_to_write={}
    for kind in kinds:
        text=values.get(kind,'')
        if any(c in text for c in '\r\n\x00'):
            raise ValueError('密码不能包含换行或 NUL。')
        if text: values_to_write[kind]=text.encode()
    retained=[kind for kind in (old or {}).get('credentials',[]) if kind in kinds and old_user==user]
    if not key and 'login' not in values_to_write and 'login' not in retained:
        raise ValueError('密码登录需要填写 SSH 密码；更换登录用户时需重新输入。')
    same_login=values.get('sudo_same_as_login',False)
    if not isinstance(same_login,bool):
        raise ValueError('sudo 密码开关格式无效。')
    if same_login and key:
        raise ValueError('与 SSH 登录密码一致仅适用于密码登录，不使用私钥解锁口令。')
    keys=values.get('keys','').encode()
    if not keys or subprocess.run(['/usr/bin/ssh-keygen','-lf','-'],input=keys,capture_output=True,timeout=10).returncode:
        raise ValueError('请先核对主机指纹。')
    store=access.Keychain()
    if same_login:
        if 'login' in values_to_write:
            values_to_write['sudo']=values_to_write['login']
        elif 'login' in retained:
            values_to_write['sudo']=store.get(alias,'login',user)
        else:
            raise ValueError('请先填写 SSH 登录密码，再使用同一密码验证 sudo。')
    previous={}
    for kind in values_to_write:
        item,_=store.locate(alias,kind,user)
        if item:
            store.cf.CFRelease(item)
            if not old or old_user!=user:
                raise ValueError('此别名存在遗留凭据，请改用其他别名，避免覆盖。')
            previous[kind]=store.get(alias,kind,user)
        else: previous[kind]=None
    access.ROOT.mkdir(parents=True,exist_ok=True,mode=0o700)
    touched=[]
    committed=False
    with tempfile.TemporaryDirectory(prefix='.editing-',dir=access.ROOT) as temporary:
        stage_root=Path(temporary)
        root=access.ROOT
        try:
            access.ROOT=stage_root
            stage,profile=access.create_profile(alias,host,user,port,key)
        finally: access.ROOT=root
        profile['group']=group
        profile['sudo_same_as_login']=same_login
        profile['connection']={'host':host,'account':user,'port':str(port),'key':str(key) if key else ''}
        profile['credentials']=sorted(set(retained)|set(values_to_write))
        if old: profile['operations']=old['operations']
        if 'sudo' in profile['credentials']:
            profile['operations']['sudo-check']={'command':'id -u','sudo':True}
        else: profile['operations'].pop('sudo-check',None)
        access.private_write(stage/'profile.json',json.dumps(profile,ensure_ascii=False,indent=2)+'\n')
        access.private_write(stage/'known_hosts',keys.decode())
        quoted=str(final/'known_hosts').replace('\\','\\\\').replace('"','\\"')
        access.private_write(stage/'ssh_config',(stage/'ssh_config').read_text()+f'    UserKnownHostsFile "{quoted}"\n    GlobalKnownHostsFile /dev/null\n')
        backup=stage_root/'original'
        try:
            for kind,value in values_to_write.items():
                touched.append(kind)
                store.put(alias,kind,user,value)
            if old: final.rename(backup)
            stage.rename(final)
            committed=True
            verify(alias)
        except Exception:
            if committed: shutil.rmtree(final)
            if backup.exists(): backup.rename(final)
            for kind in touched:
                if previous[kind] is None: store.delete(alias,kind,user)
                else: store.put(alias,kind,user,previous[kind])
            raise
        # Old account/mode credentials are removed only after successful validation.
        if old:
            for kind in old['credentials']:
                if old_user!=user or kind not in profile['credentials']:
                    store.delete(alias,kind,old_user)
    return {'message':'配置已保存，连接验证通过。'}


def delete(alias):
    path,profile=access.load(alias)
    store=access.Keychain()
    for kind in access.KINDS:
        store.delete(alias,kind,profile['account'])
    for filename in ('profile.json','ssh_config','askpass','known_hosts'):
        (path/filename).unlink(missing_ok=True)
    path.rmdir()
    return {'message':'本机配置及对应钥匙串条目已删除，服务器未修改。'}


def terminal(alias, sftp=False):
    path,_=access.load(alias)
    folder=Path(tempfile.mkdtemp(prefix='server-manager-terminal-'))
    launcher=folder/'connect.command'
    command=shlex.join(['/usr/bin/sftp' if sftp else '/usr/bin/ssh','-F',str(path/'ssh_config'),alias])
    cleanup='rm -f -- '+shlex.quote(str(launcher))+'; rmdir -- '+shlex.quote(str(folder))
    exports='export SSH_ASKPASS='+shlex.quote(str(path/'askpass'))+'\nexport SSH_ASKPASS_REQUIRE=force\nexport SERVER_MANAGER_ALIAS='+shlex.quote(alias)+'\nexport LC_ALL=C\n'
    access.private_write(launcher,'#!/bin/sh\ntrap '+shlex.quote(cleanup)+' EXIT\n'+exports+command+'\n',0o700)
    result=subprocess.run(['/usr/bin/open','-a','/System/Applications/Utilities/Terminal.app',str(launcher)],capture_output=True,timeout=10)
    if result.returncode:
        shutil.rmtree(folder)
        raise ValueError('无法打开终端。可在自己的终端中通过 SSH 配置连接。')
    return {'message':'已打开终端，SSH 登录使用已保存的钥匙串凭据；终端里的交互式 sudo 仍由你自行输入。'}


def dispatch(request):
    action=request.get('action')
    if action=='list':return {'servers':listing()}
    if action=='scan':return scan(request)
    if action=='test':return verify(access.name(request['alias']))
    if action in ('terminal','sftp'):return terminal(access.name(request['alias']),sftp=action=='sftp')
    if action in ('save','delete'):
        access.ROOT.mkdir(parents=True,exist_ok=True,mode=0o700)
        fd=os.open(access.ROOT/'.manager.lock',os.O_CREAT|os.O_RDWR,0o600)
        with os.fdopen(fd,'w') as lock:
            fcntl.flock(lock,fcntl.LOCK_EX)
            return save(request) if action=='save' else delete(access.name(request['alias']))
    raise ValueError('不支持的操作。')


if __name__=='__main__':
    try:
        request=json.load(sys.stdin)
        response={'ok':True,**dispatch(request)}
    except ValueError as error:
        # Only fixed application errors are surfaced, never third-party output/request contents.
        response={'ok':False,'message':str(error) if not isinstance(error,json.JSONDecodeError) else '输入格式错误。'}
    except Exception:
        response={'ok':False,'message':'操作未完成。请检查本机权限、网络、钥匙串授权和配置完整性。'}
    print(json.dumps(response,ensure_ascii=False))
