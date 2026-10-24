"""Connection validation and host-key scan for the native app; no web service."""
from pathlib import Path
import re
import subprocess
import server_access as access

def connection(values):
    host = values.get('host', '').strip()
    user = values.get('account', '').strip()
    if not re.fullmatch(r'[a-zA-Z0-9_.:%-]+', host) or host.startswith('-'):
        raise ValueError('服务器域名/IP 无效。IPv6 请直接填写，不带方括号。')
    if not re.fullmatch(r'[a-zA-Z0-9_][a-zA-Z0-9_.-]*', user):
        raise ValueError('SSH 用户名无效。')
    try:
        port = int(values.get('port', '22'))
    except ValueError:
        raise ValueError('端口必须是数字。') from None
    if not 1 <= port <= 65535:
        raise ValueError('端口范围为 1–65535。')
    key_text = values.get('key', '').strip()
    key = Path(key_text).expanduser().resolve() if key_text else None
    if key and (not key.is_file() or any(c in str(key) for c in '\r\n\x00%')):
        raise ValueError('私钥路径不存在或不受支持；这里只检查路径，不读取内容。')
    passwords = {}
    for kind in access.KINDS:
        value = values.get(kind, '')
        if any(c in value for c in '\r\n\x00'):
            raise ValueError('密码不支持换行或 NUL。')
        if value:
            passwords[kind] = value.encode()
    if key and 'login' in passwords:
        raise ValueError('私钥登录请填写私钥口令，SSH 登录密码留空。')
    if not key and ('login' not in passwords or 'passphrase' in passwords):
        raise ValueError('密码登录请填写 SSH 登录密码，私钥口令留空。')
    return host, user, port, key, passwords


def host_keys(host, port):
    result = subprocess.run(['/usr/bin/ssh-keyscan', '-T', '8', '-p', str(port), host],
                            capture_output=True, timeout=30)
    lines = [line for line in result.stdout.splitlines() if line and not line.startswith(b'#')]
    if not lines:
        raise ValueError('未能取得主机公钥，请检查服务器地址、端口和网络。未保存配置或密码。')
    keys = b'\n'.join(lines) + b'\n'
    fingerprints = subprocess.run(['/usr/bin/ssh-keygen', '-lf', '-'], input=keys,
                                 capture_output=True, timeout=10)
    if fingerprints.returncode:
        raise ValueError('主机公钥格式验证失败。')
    return keys, fingerprints.stdout.decode('utf-8', 'replace')


