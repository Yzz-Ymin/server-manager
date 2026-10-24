"""Real loopback SSH protocol tests. No production credentials or hosts are used."""
import argparse
import json
import logging
import os
from pathlib import Path
import secrets
import shlex
import socket
import subprocess
import sys
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--configuration', default='Release')
args = parser.parse_args()
BIN = ROOT / 'artifacts' / 'bin' / args.configuration
sys.stdout.reconfigure(encoding='utf-8')
import paramiko
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

logging.getLogger('paramiko').addHandler(logging.NullHandler())
logging.getLogger('paramiko').propagate = False
password = secrets.token_urlsafe(28)
passphrase = secrets.token_urlsafe(28)
host_key = paramiko.RSAKey.generate(2048)
wrong_key = paramiko.RSAKey.generate(2048)
private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
key_public = private.public_key().public_bytes(serialization.Encoding.OpenSSH, serialization.PublicFormat.OpenSSH).decode().split()[1]
stop = threading.Event()
connections = []

class Server(paramiko.ServerInterface):
    def get_allowed_auths(self, username): return 'publickey,password'
    def check_auth_password(self, username, value): return paramiko.AUTH_SUCCESSFUL if username == 'tester' and secrets.compare_digest(value, password) else paramiko.AUTH_FAILED
    def check_auth_publickey(self, username, key): return paramiko.AUTH_SUCCESSFUL if username == 'tester' and key.get_base64() == key_public else paramiko.AUTH_FAILED
    def check_channel_request(self, kind, channelid): return paramiko.OPEN_SUCCEEDED if kind == 'session' else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
    def check_channel_exec_request(self, channel, command):
        threading.Thread(target=execute, args=(channel, command.decode()), daemon=True).start()
        return True

class SftpServer(paramiko.SFTPServerInterface):
    def canonicalize(self, path): return '/'
    def list_folder(self, path): return []

def execute(channel, command):
    try:
        argv = shlex.split(command)
        if argv[0] == 'sudo':
            value = b''
            while not value.endswith(b'\n'):
                data = channel.recv(1024)
                if not data: break
                value += data
            if value.rstrip(b'\r\n').decode() != password:
                channel.send(b'sudo: 1 incorrect password attempt\n')
                channel.send_exit_status(1)
                return
            argv = argv[argv.index('sh'):]
        value = argv[argv.index('-c') + 1] if '-c' in argv else argv[-1]
        if value == 'uname -s; id; uptime': channel.send(b'Linux\nuid=1000(tester)\nup 1 day\n')
        elif value == 'printf exact-one-off': channel.send(b'exact-one-off')
        elif value == 'exit 37': channel.send_exit_status(37); return
        elif value == 'sleep 5': time.sleep(5)
        channel.send_exit_status(0)
    except (OSError, EOFError): pass
    except Exception as error: print('Test server error:', type(error).__name__, str(error))
    finally:
        try: channel.close()
        except OSError: pass

def serve_connection(client):
    transport = paramiko.Transport(client)
    connections.append(transport)
    try:
        transport.add_server_key(host_key)
        transport.set_subsystem_handler('sftp', paramiko.SFTPServer, SftpServer)
        transport.start_server(server=Server())
        channels = []
        while transport.is_active() and not stop.is_set():
            channel = transport.accept(0.2)
            if channel is not None: channels.append(channel)
            channels = [channel for channel in channels if not channel.closed]
    except (paramiko.SSHException, EOFError, OSError): pass
    finally: transport.close()

def serve(listener):
    listener.settimeout(0.3)
    while not stop.is_set():
        try: client, _ = listener.accept()
        except socket.timeout: continue
        except OSError: return
        threading.Thread(target=serve_connection, args=(client,), daemon=True).start()

def main():
    runtime = ROOT / 'artifacts' / 'tests' / 'ssh'
    runtime.mkdir(parents=True, exist_ok=True)
    sid = subprocess.check_output(['whoami', '/user', '/fo', 'csv', '/nh'], text=True).strip().split(',')[-1].strip('"')
    subprocess.run(['icacls', str(runtime), '/inheritance:r', '/grant:r', f'*{sid}:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F'], check=True, capture_output=True)
    key = runtime / '私钥 space.key'
    encrypted = runtime / '加密 私钥.key'
    key.write_bytes(private.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.OpenSSH, serialization.NoEncryption()))
    encrypted.write_bytes(private.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.OpenSSH, serialization.BestAvailableEncryption(passphrase.encode())))
    listener = socket.socket()
    listener.bind(('127.0.0.1', 0))
    listener.listen()
    thread = threading.Thread(target=serve, args=(listener,), daemon=True)
    thread.start()
    cfg = dict(Port=listener.getsockname()[1], Password=password, Passphrase=passphrase, Key=str(key), EncryptedKey=str(encrypted), WrongHostKeys='wrong-host '+wrong_key.get_name()+' '+wrong_key.get_base64(), Root=str(runtime/'profiles'))
    try:
        result = subprocess.run([str(BIN/'ServerManager.Tests.exe')], input=json.dumps(cfg), text=True, encoding='utf-8', stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=180)
        report = (result.stdout + result.stderr).replace(password, '[SYNTHETIC REDACTED]').replace(passphrase, '[SYNTHETIC REDACTED]')
        print(report)
        (ROOT/'artifacts/tests/ssh-results.txt').write_text(report, encoding='utf-8')
        return result.returncode
    finally:
        stop.set()
        listener.close()
        for connection in connections: connection.close()
        key.unlink(missing_ok=True)
        encrypted.unlink(missing_ok=True)

if __name__ == '__main__':
    raise SystemExit(main())
