#!/usr/bin/env python3
"""macOS Keychain-backed SSH operations. Passwords never belong in argv."""
import argparse
import ctypes as C
import fcntl
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import signal
import subprocess
import sys
import uuid

ROOT = Path.home() / '.config' / 'server-manager'
KINDS = ('login', 'passphrase', 'sudo')


def name(value):
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}', value):
        raise ValueError('别名仅支持英文字母、数字、下划线和短横线，需以字母或数字开头，长度 1–64；不支持中文或空格。')
    return value


def private_write(path, content, mode=0o600):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if path.is_symlink():
        raise ValueError('拒绝写入符号链接。')
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, mode)
    with os.fdopen(fd, 'w') as stream:
        os.fchmod(stream.fileno(), mode)
        stream.write(content)


class Keychain:
    """Use native Security.framework; never launch a command containing a secret."""
    def __init__(self, keychain=None):
        self.keychain = keychain
        if sys.platform != 'darwin':
            raise ValueError('钥匙串功能仅支持 macOS。')
        self.s = C.CDLL('/System/Library/Frameworks/Security.framework/Security')
        self.cf = C.CDLL('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')
        self.cf.CFRelease.argtypes = [C.c_void_p]
        self.s.SecKeychainFindGenericPassword.argtypes = [C.c_void_p, C.c_uint32, C.c_char_p, C.c_uint32, C.c_char_p, C.POINTER(C.c_uint32), C.POINTER(C.c_void_p), C.POINTER(C.c_void_p)]
        self.s.SecKeychainAddGenericPassword.argtypes = [C.c_void_p, C.c_uint32, C.c_char_p, C.c_uint32, C.c_char_p, C.c_uint32, C.c_void_p, C.POINTER(C.c_void_p)]
        self.s.SecKeychainItemModifyAttributesAndData.argtypes = [C.c_void_p, C.c_void_p, C.c_uint32, C.c_void_p]
        self.s.SecKeychainItemFreeContent.argtypes = [C.c_void_p, C.c_void_p]
        self.s.SecKeychainItemDelete.argtypes = [C.c_void_p]

    @staticmethod
    def check(status):
        if status:
            raise ValueError(f'钥匙串操作失败（OSStatus {status}）。请在本机处理钥匙串系统授权提示。')

    def locate(self, alias, kind, account, read=False):
        service = f'org.servermanager.credentials.{alias}.{kind}'.encode()
        user = account.encode()
        size, data, item = C.c_uint32(), C.c_void_p(), C.c_void_p()
        status = self.s.SecKeychainFindGenericPassword(self.keychain, len(service), service, len(user), user,
            C.byref(size) if read else None, C.byref(data) if read else None, C.byref(item))
        if status == -25300:
            return None, None
        self.check(status)
        try:
            value = C.string_at(data, size.value) if read else None
        finally:
            if data.value:
                self.s.SecKeychainItemFreeContent(None, data)
        return item, value

    def get(self, alias, kind, account):
        item, value = self.locate(alias, kind, account, True)
        if item:
            self.cf.CFRelease(item)
        if value is None:
            raise ValueError(f'未保存 {alias} 的 {kind} 凭据。请在服务器管理应用中编辑并保存凭据。')
        return value

    def put(self, alias, kind, account, value):
        item, _ = self.locate(alias, kind, account)
        try:
            if item:
                self.check(self.s.SecKeychainItemModifyAttributesAndData(item, None, len(value), value))
            else:
                service = f'org.servermanager.credentials.{alias}.{kind}'.encode()
                user = account.encode()
                self.check(self.s.SecKeychainAddGenericPassword(self.keychain, len(service), service, len(user), user, len(value), value, None))
        finally:
            if item:
                self.cf.CFRelease(item)

    def delete(self, alias, kind, account):
        item, _ = self.locate(alias, kind, account)
        if item:
            try:
                self.check(self.s.SecKeychainItemDelete(item))
            finally:
                self.cf.CFRelease(item)


def load(alias):
    path = ROOT / name(alias)
    return path, json.loads((path / 'profile.json').read_text())


def ssh_args(path, alias, trust=False):
    return ['/usr/bin/ssh', '-F', str(path / 'ssh_config'), '-T',
        '-o', 'StrictHostKeyChecking=' + ('ask' if trust else 'yes'), alias]


def create_profile(alias, host, user, port, key=None):
    alias = name(alias)
    path = ROOT / alias
    if path.exists():
        raise ValueError("该服务器别名已存在，不会覆盖。")
    ROOT.mkdir(parents=True, exist_ok=True, mode=0o700)
    path.mkdir(mode=0o700)
    try:
        config = (f'Host {alias}\n    HostName {host}\n    User {user}\n    Port {port}\n'
            '    ForwardAgent no\n    ClearAllForwardings yes\n    ControlMaster no\n    ControlPath none\n'
            '    NumberOfPasswordPrompts 1\n    ConnectTimeout 20\n    ServerAliveInterval 30\n'
            '    ServerAliveCountMax 3\n    StrictHostKeyChecking yes\n')
        if key:
            quoted_key = str(key).replace('\\', '\\\\').replace('"', '\\"')
            config += f'    IdentityFile "{quoted_key}"\n    IdentitiesOnly yes\n    PreferredAuthentications publickey\n'
        else:
            config += '    PubkeyAuthentication no\n    PreferredAuthentications password\n    KbdInteractiveAuthentication no\n'
        private_write(path / 'ssh_config', config)
        profile = {'account': user, 'credentials': [], 'operations': {'status': {'command': 'hostname; id', 'sudo': False}}}
        private_write(path / 'profile.json', json.dumps(profile, indent=2, ensure_ascii=False) + '\n')
        helper = '#!/bin/sh\nexec ' + shlex.quote(sys.executable) + ' ' + shlex.quote(str(Path(__file__).resolve())) + ' _askpass "$@"\n'
        private_write(path / 'askpass', helper, 0o700)
    except Exception:
        shutil.rmtree(path)
        raise
    return path, profile


def askpass(prompt):
    alias = os.environ.get('SERVER_MANAGER_ALIAS', '')
    _, profile = load(alias)
    if 'passphrase for key' in prompt.lower():
        kind = 'passphrase'
    elif 'password' in prompt.lower():
        kind = 'login'
    else:
        return 1  # Never answer host-key, OTP or other prompts with a stored password.
    if kind not in profile['credentials']:
        return 1
    value = Keychain().get(alias, kind, profile['account'])
    sys.stdout.buffer.write(value + b'\n')  # Private SSH pipe; never call this mode from 外部程序.
    return 0


def redact(data, secrets):
    for value in sorted(set(secrets), key=len, reverse=True):
        if value:
            data = data.replace(value, b'[REDACTED]')
    return data


def run(alias, op, timeout, diagnostic=False):
    path, profile = load(alias)
    if op not in profile['operations']:
        raise ValueError('未配置该操作；请先按用户授权配置该命名操作。')
    action = profile['operations'][op]
    # Fetch only this profile's configured credentials, allowing output redaction too.
    store = Keychain() if profile['credentials'] else None
    secrets = {kind: store.get(alias, kind, profile['account']) for kind in profile['credentials']}
    command = 'sh -c ' + shlex.quote('exec </dev/null\n' + action['command'])
    payload = b''
    if action['sudo']:
        if 'sudo' not in secrets:
            raise ValueError('请先在服务器管理应用中保存 sudo 凭据。')
        # -k forces authentication; -T prevents TTY echo. The child command has no stdin.
        command = "sudo -k -S -p '' -- " + command
        payload = secrets['sudo'] + b'\n'
    env = os.environ.copy()
    env.update(SSH_ASKPASS=str(path / 'askpass'), SSH_ASKPASS_REQUIRE='force', SERVER_MANAGER_ALIAS=alias, LC_ALL='C')
    arguments=ssh_args(path,alias)
    if diagnostic: arguments.insert(1,'-v')
    proc = subprocess.Popen(arguments + [command], env=env,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
    try:
        out, err = proc.communicate(payload, timeout=timeout)
    except (subprocess.TimeoutExpired, KeyboardInterrupt):
        try:
            os.killpg(proc.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            out, err = proc.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, signal.SIGKILL)
            out, err = proc.communicate()
        sys.stdout.buffer.write(redact(out, secrets.values()))
        sys.stderr.buffer.write(redact(err, secrets.values()))
        print('本地 SSH 已停止；远端操作可能仍在运行，请先检查状态，勿盲目重试。', file=sys.stderr)
        return 124
    sys.stdout.buffer.write(redact(out, secrets.values()))
    sys.stderr.buffer.write(redact(err, secrets.values()))
    return proc.returncode


def run_once(alias, command, sudo=False, timeout=600):
    name(alias)
    if not command.strip() or '\x00' in command or timeout<=0:
        raise ValueError('命令不能为空或包含 NUL，超时必须大于零。')
    ROOT.mkdir(parents=True,exist_ok=True,mode=0o700)
    with (ROOT/'.manager.lock').open('a') as lock:
        fcntl.flock(lock,fcntl.LOCK_EX)
        path,profile=load(alias);file=path/'profile.json';original=file.read_text()
        operation='once-'+uuid.uuid4().hex
        profile['operations'][operation]={'command':command,'sudo':sudo}
        try:
            private_write(file,json.dumps(profile,ensure_ascii=False,indent=2)+'\n')
            return run(alias,operation,timeout)
        finally:
            private_write(file,original)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='mode', required=True)
    sub.add_parser('list')
    p = sub.add_parser('run')
    p.add_argument('alias', type=name)
    p.add_argument('operation', type=name)
    p.add_argument('--timeout', type=int, default=600)
    p.add_argument('--diagnostic', action='store_true', help=argparse.SUPPRESS)
    p = sub.add_parser('once',help='执行一次性命令，不保留操作配置')
    p.add_argument('alias',type=name)
    p.add_argument('--command',required=True)
    p.add_argument('--sudo',action='store_true')
    p.add_argument('--timeout',type=int,default=600)
    p = sub.add_parser('_askpass', help=argparse.SUPPRESS)
    p.add_argument('prompt')
    args = parser.parse_args()
    if args.mode == 'run':
        if args.timeout <= 0: raise ValueError('超时时间必须为正数。')
        return run(args.alias, args.operation, args.timeout, diagnostic=args.diagnostic)
    if args.mode == 'once': return run_once(args.alias,args.command,args.sudo,args.timeout)
    if args.mode == '_askpass': return askpass(args.prompt)
    for entry in sorted(ROOT.glob('*/profile.json')):
        profile = json.loads(entry.read_text())
        print(entry.parent.name + ': ' + ', '.join(k + (' [sudo]' if v['sudo'] else '') for k,v in profile['operations'].items()))
    return 0

if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError, KeyError) as exc:
        print(str(exc) if isinstance(exc, ValueError) else '本地配置操作失败，请检查权限和配置完整性。', file=sys.stderr)
        sys.exit(1)
