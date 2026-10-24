#!/usr/bin/env python3
"""Offline tests; optional isolated native Keychain round trip."""
import ctypes as C
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('access', Path(__file__).parents[1]/'Source/server_access.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def offline():
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        m.ROOT = root / 'profiles'
        profile = m.ROOT / 'test'
        profile.mkdir(parents=True)
        credentials = {'login': b'synthetic-login-!$123', 'sudo': b'synthetic-sudo-!$456'}
        data = {'account': 'deploy', 'credentials': list(credentials), 'operations': {
            'normal': {'command': "printf 'ordinary output'", 'sudo': False},
            'elevated': {'command': "printf 'elevated output'; cat", 'sudo': True},
            'bad': {'command': 'exit 7', 'sudo': False},
            'slow': {'command': 'sleep 5', 'sudo': False},
            'leak': {'command': 'printf %s "$TEST_SECRET"', 'sudo': False}}}
        (profile / 'profile.json').write_text(json.dumps(data))
        fake_ssh = root / 'ssh'
        m.private_write(fake_ssh, '#!' + sys.executable + '\n' + '''import os, subprocess, sys
assert all(os.environ['TEST_SECRET'] not in arg for arg in sys.argv)
assert os.environ['SSH_ASKPASS_REQUIRE'] == 'force'
sys.exit(subprocess.call(sys.argv[-1], shell=True))
''', 0o700)
        fake_sudo = root / 'sudo'
        m.private_write(fake_sudo, '#!' + sys.executable + '\n' + '''import os, sys
assert '-S' in sys.argv and '-k' in sys.argv
assert sys.stdin.buffer.readline().rstrip(b'\\n') == os.environ['TEST_SUDO'].encode()
args = sys.argv[sys.argv.index('--') + 1:]
os.execvp(args[0], args)
''', 0o700)

        class FakeKeychain:
            def get(self, alias, kind, account):
                return credentials[kind]

        class Output:
            def __init__(self):
                self.buffer = io.BytesIO()
            def write(self, value):
                return self.buffer.write(value.encode())
            def flush(self):
                pass

        env = {'PATH': str(root) + ':' + os.environ['PATH'],
               'TEST_SECRET': credentials['login'].decode(), 'TEST_SUDO': credentials['sudo'].decode()}
        with patch.object(m, 'Keychain', FakeKeychain), patch.object(m, 'ssh_args', lambda p, a: [str(fake_ssh)]), patch.dict(os.environ, env):
            for operation, code, expected in [('normal', 0, b'ordinary output'), ('elevated', 0, b'elevated output'), ('bad', 7, b''), ('leak', 0, b'[REDACTED]'), ('slow', 124, b'')]:
                out, err = Output(), Output()
                with patch.object(sys, 'stdout', out), patch.object(sys, 'stderr', err):
                    actual = m.run('test', operation, 1 if operation == 'slow' else 10)
                assert actual == code, (operation, actual, err.buffer.getvalue())
                assert out.buffer.getvalue() == expected, (operation, out.buffer.getvalue())
                assert not any(v in out.buffer.getvalue() + err.buffer.getvalue() for v in credentials.values())
            with patch.dict(os.environ, {'SERVER_MANAGER_ALIAS': 'test'}):
                out = Output()
                with patch.object(sys, 'stdout', out):
                    assert m.askpass("deploy@host's password:") == 0
                assert out.buffer.getvalue() == credentials['login'] + b'\n'
                out = Output()
                with patch.object(sys, 'stdout', out):
                    assert m.askpass('Enter OTP code:') == 1
                    assert m.askpass('Are you sure you want to continue connecting?') == 1
                assert out.buffer.getvalue() == b''
            try:
                m.run('test', 'not-configured', 1)
                raise AssertionError('Unconfigured operation executed')
            except ValueError:
                pass
        for invalid in ('../other', '-oProxyCommand=bad', 'a\nb', 'a b'):
            try:
                m.name(invalid)
                raise AssertionError('Invalid name accepted')
            except ValueError:
                pass
        assert m.redact(b'abcdef abc', [b'abc', b'abcdef']) == b'[REDACTED] [REDACTED]'
        assert (fake_sudo.stat().st_mode & 0o777) == 0o700
        print('PASS: SSH execution, sudo stdin/no-echo, exit codes, timeout, redaction, operation/name validation, file permissions')


if __name__ == '__main__':
    offline()
