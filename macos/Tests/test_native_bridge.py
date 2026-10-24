import sys,tempfile,json,subprocess,shutil
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).parents[1]/'Source'))
import native_bridge as bridge

stored={}
class CF:
    def CFRelease(self,item): pass
class FakeKeychain:
    cf=CF()
    def locate(self,alias,kind,user): return (1,None) if (alias,kind,user) in stored else (None,None)
    def get(self,alias,kind,user): return stored[(alias,kind,user)]
    def put(self,alias,kind,user,value): stored[(alias,kind,user)]=value
    def delete(self,alias,kind,user): stored.pop((alias,kind,user),None)

with tempfile.TemporaryDirectory() as temporary:
    root=Path(temporary)/'profiles'
    with patch.object(bridge.access,'ROOT',root),patch.object(bridge.access,'Keychain',FakeKeychain),patch.object(bridge,'verify',return_value={'message':'synthetic pass'}),patch.object(bridge.subprocess,'run',return_value=subprocess.CompletedProcess([],0)):
        base={'alias':'hk-relay','host':'example.invalid','account':'deploy','port':'22','key':'','group':'工作','login':'synthetic-old-password','keys':'synthetic-public-key'}
        bridge.save(base)
        assert bridge.listing()[0]['group']=='工作'
        old_profile=(root/'hk-relay/profile.json').read_bytes()
        for path in (root/'hk-relay').iterdir(): assert b'synthetic-old-password' not in path.read_bytes()
        bridge.save({**base,'original':'hk-relay','group':'家用','login':''})
        assert stored[('hk-relay','login','deploy')]==b'synthetic-old-password'
        assert bridge.listing()[0]['group']=='家用'
        good_profile=(root/'hk-relay/profile.json').read_bytes()
        with patch.object(bridge,'verify',side_effect=ValueError('synthetic verification failure')):
            try: bridge.save({**base,'original':'hk-relay','login':'synthetic-replacement-password','group':'不应保存'})
            except ValueError: pass
            else: raise AssertionError('Failed verification accepted')
        assert stored[('hk-relay','login','deploy')]==b'synthetic-old-password'
        assert (root/'hk-relay/profile.json').read_bytes()==good_profile
        assert not list(root.glob('.editing-*'))
        bridge.save({**base,'alias':'home-nas','group':'家用'})
        assert [row['alias'] for row in bridge.listing()]==['hk-relay','home-nas']
        for invalid in ('中文','name with spaces','../escape','-bad'):
            try: bridge.save({**base,'alias':invalid})
            except ValueError: pass
            else: raise AssertionError('Invalid alias accepted')
        with patch.object(bridge,'verify',side_effect=ValueError('synthetic verification failure')):
            try: bridge.save({**base,'alias':'should-rollback'})
            except ValueError: pass
            assert not (root/'should-rollback').exists()
            assert not any(key[0]=='should-rollback' for key in stored)
        bridge.save({**base,'alias':'shared-pass','sudo_same_as_login':True})
        assert stored[('shared-pass','sudo','deploy')]==stored[('shared-pass','login','deploy')]
        assert bridge.describe('shared-pass')['sudoSameAsLogin'] is True
        bridge.save({**base,'alias':'shared-pass','original':'shared-pass','login':'','sudo_same_as_login':True})
        assert stored[('shared-pass','sudo','deploy')]==b'synthetic-old-password'
        bridge.save({**base,'alias':'shared-pass','original':'shared-pass','login':'synthetic-new-password','sudo_same_as_login':True})
        assert stored[('shared-pass','sudo','deploy')]==b'synthetic-new-password'
        before_shared=(root/'shared-pass/profile.json').read_bytes()
        with patch.object(bridge,'verify',side_effect=ValueError('synthetic sudo failure')):
            try:bridge.save({**base,'alias':'shared-pass','original':'shared-pass','login':'must-rollback','sudo_same_as_login':True})
            except ValueError:pass
            else:raise AssertionError('Failed shared-password save accepted')
        assert stored[('shared-pass','sudo','deploy')]==b'synthetic-new-password'
        assert stored[('shared-pass','login','deploy')]==b'synthetic-new-password'
        assert (root/'shared-pass/profile.json').read_bytes()==before_shared
        bridge.save({**base,'alias':'shared-pass','original':'shared-pass','login':'','sudo':'synthetic-independent','sudo_same_as_login':False})
        assert stored[('shared-pass','sudo','deploy')]==b'synthetic-independent'
        assert bridge.describe('shared-pass')['sudoSameAsLogin'] is False
        bridge.delete('shared-pass')
        synthetic_key=Path(temporary)/'synthetic-key';synthetic_key.touch()
        try:bridge.save({**base,'alias':'invalid-key-sharing','key':str(synthetic_key),'sudo_same_as_login':True})
        except ValueError as error:assert '仅适用于密码登录' in str(error)
        else:raise AssertionError('Private-key passphrase used for sudo')
        assert not (root/'invalid-key-sharing').exists()
        for sftp in (False, True):
            directory=Path(temporary)/('sftp-launcher' if sftp else 'ssh-launcher')
            directory.mkdir()
            def inspect_launch(argv, **kwargs):
                launcher=Path(argv[-1])
                text=launcher.read_text()
                assert ('/usr/bin/sftp' if sftp else '/usr/bin/ssh') in text
                assert 'SSH_ASKPASS_REQUIRE=force' in text
                assert 'synthetic-old-password' not in text
                assert launcher.stat().st_mode & 0o777 == 0o700
                return subprocess.CompletedProcess(argv, 1)
            with patch.object(bridge.tempfile,'mkdtemp',return_value=str(directory)),patch.object(bridge.subprocess,'run',side_effect=inspect_launch):
                try:bridge.dispatch({'action':'sftp' if sftp else 'terminal','alias':'hk-relay'})
                except ValueError:pass
                else:raise AssertionError('Failed terminal launch was ignored')
            assert not directory.exists()
        bridge.delete('hk-relay');bridge.delete('home-nas')
        assert not stored and not list(root.iterdir())
print('PASS: groups, blank-password retention, update rollback, create rollback, invalid aliases, deletion and cleanup')

with patch.object(bridge.access,'load',return_value=(Path('/synthetic'),{'credentials':[]})):
    for diagnostic,expected in [(b'Permission denied synthetic-secret', '登录认证失败'),(b'Host key verification failed','主机密钥'),(b'Connection refused','拒绝连接')]:
        with patch.object(bridge.subprocess,'run',return_value=subprocess.CompletedProcess([],255,b'',diagnostic)):
            try: bridge.verify('synthetic')
            except ValueError as error:
                assert expected in str(error) and 'synthetic-secret' not in str(error) and '回滚' in str(error)
            else: raise AssertionError('Failed SSH verification accepted')
    with patch.object(bridge.subprocess,'run',side_effect=subprocess.TimeoutExpired([],65)):
        try:bridge.verify('synthetic')
        except ValueError as error:assert '超时' in str(error)
        else:raise AssertionError('Timeout accepted')
print('PASS: SSH failure categories, safe messages and timeout feedback')

with patch.object(bridge.access,'load',return_value=(Path('/synthetic'),{'credentials':[],'connection':{'key':'/synthetic/key'}})):
    cases=[
        (b'WARNING: UNPROTECTED PRIVATE KEY FILE! Permission denied (publickey).','权限过宽'),
        (b'Load key "synthetic-secret": invalid format\nPermission denied (publickey).','无法解析'),
        (b'Load key "synthetic-secret": Operation not permitted\nPermission denied (publickey).','系统权限'),
        (b'debug1: Offering public key: synthetic-secret\nPermission denied (publickey).','服务器未接受'),
        (b'debug1: Offering public key: synthetic-secret\ndebug1: Server accepts key: synthetic-secret\nPermission denied (publickey).','服务器已接受'),
        (b'no passphrase given, try next key\nPermission denied (publickey).','需要解锁口令'),
        (b'Load key "synthetic-secret": incorrect passphrase supplied to decrypt private key\nPermission denied (publickey).','口令不正确'),
        (b'sign_and_send_pubkey: signing failed: agent refused operation\nPermission denied (publickey).','Agent 拒绝'),
    ]
    for diagnostic,expected in cases:
        with patch.object(bridge.subprocess,'run',return_value=subprocess.CompletedProcess([],255,b'',diagnostic)):
            try:bridge.verify('synthetic')
            except ValueError as error:assert expected in str(error) and 'synthetic-secret' not in str(error),(expected,str(error))
            else:raise AssertionError('Failed key authentication accepted')
print('PASS: private-key permission, format, access, missing/wrong passphrase, server rejection and Agent diagnostics; raw traces never surfaced')
