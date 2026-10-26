using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace ServerManager {
internal sealed class TestConfig {public int Port {get;set;}public string Password {get;set;}public string Passphrase {get;set;}public string Key {get;set;}public string EncryptedKey {get;set;}public string WrongHostKeys {get;set;}public string Root {get;set;}}
internal static class Tests {
    static int checks;static void Check(bool yes,string label){if(!yes)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    static void Fails(Action action,string code,string label){try{action();throw new Exception("Expected "+code);}catch(AccessException ex){Check(ex.Code==code,label+" ["+ex.Code+"]");}}
    public static int Main(){Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(true);var cfg=Store.Parse<TestConfig>(Console.In.ReadToEnd());var store=new Store(cfg.Root,"ServerManagerTest/"+Guid.NewGuid().ToString("N")+"/");
        try{
            StdinTest();
            Fails(()=>Store.ValidateAlias("a\n"),"INVALID_ALIAS","reject newline alias");Fails(()=>Store.ValidateAlias("-bad"),"INVALID_ALIAS","reject SSH option alias");
            var p=new Profile{Alias="test-password",Group="中文分组",Host="127.0.0.1",Port=cfg.Port,User="tester",Auth="password",SudoSame=true};
            var bad=Store.Parse<Profile>(Store.Json(p));bad.Host="localhost\nProxyCommand anything";Fails(()=>Store.Validate(bad,false),"INVALID_HOST","reject SSH config injection");
            p.HostKeys=Engine.Scan(p);Check(p.HostKeys.Contains("ssh-rsa"),"real OpenSSH keyscan against loopback SSH");Check(Engine.Fingerprints(p.HostKeys).Contains("SHA256:"),"public key fingerprint");ScanFallbackTests(p);
            store.Save(p,null,cfg.Password,null,null,false,s=>{});Check(store.List().Count==1,"password SSH + sudo verification and atomic save");
            var old=store.Read(p.Alias);Check(old.SudoSame&&old.SudoRef!=null,"sudo same-password saved");
            Check(!File.ReadAllText(store.ProfilePath(p.Alias)).Contains(cfg.Password),"password absent from JSON");Check(!Directory.GetFiles(cfg.Root,"*",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f).Contains(cfg.Password)),"password absent from configuration files");
            var status=store.Named(p.Alias,"status",15);Check(status.success&&status.stdout.Contains("Linux"),"headless named status over real SSH");
            var once=store.Run(p.Alias,"printf exact-one-off",false,15);Check(once.success&&once.stdout.Contains("exact-one-off"),"one-off command over real SSH");Check(store.Read(p.Alias).Operations.Count==0,"one-off leaves no permanent operation");
            var rc=store.Run(p.Alias,"exit 37",false,15);Check(!rc.success&&rc.remoteExitCode==37,"real remote exit code");
            var timeout=store.Run(p.Alias,"sleep 5",false,1);Check(timeout.code=="TIMEOUT"&&timeout.remoteStateUnknown,"timeout terminates SSH tree and marks remote uncertain");
            var edit=Store.Parse<Profile>(Store.Json(old));edit.Group="修改分组";store.Save(edit,old,null,null,null,false,s=>{});Check(store.Read(p.Alias).Group=="修改分组","blank secret preserves credentials");
            old=store.Read(p.Alias);edit=Store.Parse<Profile>(Store.Json(old));edit.Group="must-rollback";Fails(()=>store.Save(edit,old,cfg.Password+"wrong",null,null,false,s=>{}),"AUTH_REJECTED","failed edit reports password rejection");Check(store.Read(p.Alias).Revision==old.Revision,"failed edit restores exact old revision");Check(store.Named(p.Alias,"status",15).success,"old credential remains usable after rollback");
            edit=Store.Parse<Profile>(Store.Json(old));edit.SudoSame=false;Fails(()=>store.Save(edit,old,null,null,cfg.Password+"wrong",false,s=>{}),"SUDO_FAILED","SSH success + sudo failure identified");Check(store.Read(p.Alias).Revision==old.Revision,"sudo failure rolls back edit");
            edit=Store.Parse<Profile>(Store.Json(old));edit.User="different";Fails(()=>store.Save(edit,old,null,null,null,false,s=>{}),"CREDENTIAL_MISSING","changed account does not inherit login secret");
            var newBad=Store.Parse<Profile>(Store.Json(old));newBad.Alias="new-failure";newBad.HostKeys=old.HostKeys.Replace(old.Alias,newBad.Alias);Fails(()=>store.Save(newBad,null,"wrong",null,null,false,s=>{}),"AUTH_REJECTED","new failure reports SSH failure");Check(!File.Exists(store.ProfilePath(newBad.Alias)),"failed new connection creates no profile");
            var mismatch=Store.Parse<Profile>(Store.Json(old));mismatch.HostKeys=cfg.WrongHostKeys.Replace("wrong-host",old.Alias);var secret=store.LoadSecrets(old);try{Check(Engine.Execute(mismatch,secret,"true",false,15).code=="HOST_KEY_MISMATCH","changed host key is rejected");}finally{secret.Clear();}
            var k=new Profile{Alias="test-key",Group="中文分组",Host="127.0.0.1",Port=cfg.Port,User="tester",Auth="key",KeyPath=cfg.Key,HostKeys=p.HostKeys.Replace(p.Alias,"test-key")};store.Save(k,null,null,null,null,false,s=>{});Check(store.Named(k.Alias,"status",15).success,"unencrypted private key real SSH");
            var ek=Store.Parse<Profile>(Store.Json(k));ek.Alias="test-encrypted";ek.KeyPath=cfg.EncryptedKey;ek.HostKeys=p.HostKeys.Replace(p.Alias,ek.Alias);store.Save(ek,null,null,cfg.Passphrase,null,false,s=>{});Check(store.Named(ek.Alias,"status",15).success,"encrypted private key + private askpass pipe real SSH");
            var encrypted=store.Read(ek.Alias);var wrongPhrase=Store.Parse<Profile>(Store.Json(encrypted));Fails(()=>store.Save(wrongPhrase,encrypted,null,"wrong-phrase",null,false,s=>{}),"PASSPHRASE_WRONG","wrong private-key passphrase distinct");
            var classification=Engine.Classify("Load key: invalid format\nPermission denied (publickey).",k,new Secrets(),false,255);Check(classification.code=="KEY_FORMAT","specific key error wins over final generic denial");
            var missing=Store.Parse<Profile>(Store.Json(encrypted));missing.Alias="missing-phrase";missing.HostKeys=encrypted.HostKeys.Replace(encrypted.Alias,missing.Alias);Fails(()=>store.Save(missing,null,null,null,null,false,s=>{}),"PASSPHRASE_MISSING","missing private-key passphrase distinct");
            SftpTest(p,cfg);
            edit=Store.Parse<Profile>(Store.Json(old));edit.SudoSame=false;store.Save(edit,old,null,null,null,false,s=>{});Check(store.Read(p.Alias).SudoRef!=null,"turning off same-password preserves sudo");
            old=store.Read(p.Alias);edit=Store.Parse<Profile>(Store.Json(old));store.Save(edit,old,null,null,null,true,s=>{});Check(store.Read(p.Alias).SudoRef==null,"explicit sudo removal");
            var changed=Store.Parse<Profile>(Store.Json(old));Fails(()=>store.Save(changed,old,null,null,null,false,s=>{}),"PROFILE_CHANGED","stale editor cannot overwrite newer profile");
            Check(Engine.Quote("space 中文\\\"tail\\")=="\"space 中文\\\\\\\"tail\\\\\"","Win32 argument escaping");
            foreach(var item in store.List())store.Delete(item);Check(store.List().Count==0,"delete only isolated profiles and owned credentials");
            Console.WriteLine("PASSED "+checks+" checks");return 0;
        }catch(AccessException ex){Console.WriteLine("FAIL "+ex.Code+" "+ex.Message+"\n"+ex.StackTrace);return 1;}catch(Exception ex){Console.WriteLine("FAIL "+ex.Message+"\n"+ex.StackTrace);return 1;}
        finally{try{foreach(var item in store.List())store.Delete(item);store.Recover();}catch{}}
    }
    static void ScanFallbackTests(Profile p){
        const string unsupported="choose_kex: unsupported KEX method sntrup761x25519-sha512@openssh.com";bool retried=false;
        var keys=Engine.ResolveScan(p,new ProcessOutput{Exit=1,Err=unsupported},()=>{retried=true;return new ProcessOutput{Exit=0,Out=p.HostKeys,Err=""};});
        Check(retried&&keys==p.HostKeys,"unsupported Windows KEX uses the compatible scanner without changing keys");
        retried=false;keys=Engine.ResolveScan(p,new ProcessOutput{Exit=0,Out=p.HostKeys},()=>{retried=true;return null;});
        Check(!retried&&keys==p.HostKeys,"working native keyscan does not invoke another scanner");
        Fails(()=>Engine.ResolveScan(p,new ProcessOutput{Exit=1,Err=unsupported},()=>null),"KEYSCAN_CLIENT_UNSUPPORTED","missing compatible scanner reports client compatibility rather than password failure");
        Fails(()=>Engine.ResolveScan(p,new ProcessOutput{Exit=1,Timeout=true},()=>{throw new Exception("unexpected retry");}),"KEYSCAN_TIMEOUT","keyscan timeout is identified");
        Fails(()=>Engine.ResolveScan(p,new ProcessOutput{Exit=1,Err="Connection refused"},()=>{throw new Exception("unexpected retry");}),"CONNECTION_REFUSED","keyscan connection refusal is identified");
        Fails(()=>Engine.ResolveScan(p,new ProcessOutput{Exit=1,Err="no matching key exchange method found"},()=>{throw new Exception("unexpected retry");}),"ALGORITHM_MISMATCH","algorithm mismatch never enables weaker algorithms");
        Fails(()=>Engine.ResolveScan(p,new ProcessOutput{Exit=1,Err=unsupported},()=>{throw new System.ComponentModel.Win32Exception(5);}),"KEYSCAN_HELPER_UNAVAILABLE","blocked compatible scanner reports a local dependency failure");
    }
    static void StdinTest(){
        var executable=Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
        var arguments=new[]{"-NoLogo","-NoProfile","-Command","$stream=[Console]::OpenStandardInput();$buffer=New-Object IO.MemoryStream;$stream.CopyTo($buffer);[Console]::Write([Convert]::ToBase64String($buffer.ToArray()))"};
        var input="synthetic stdin 中文\n";
        var result=Engine.Process(Engine.StartInfo(executable,arguments),15,input);
        Check(result.Exit==0&&result.Out==Convert.ToBase64String(new UTF8Encoding(false).GetBytes(input)),"stdin uses exact UTF-8 bytes without a console BOM");
        result=Engine.Process(Engine.StartInfo(executable,arguments),15);
        Check(result.Exit==0&&result.Out=="","empty stdin has no encoding preamble");
    }
    static void SftpTest(Profile p,TestConfig cfg){var path=Path.Combine(cfg.Root,"sftp_hosts");File.WriteAllText(path,p.HostKeys);try{using(var ask=new Askpass(p,new Secrets{Login=cfg.Password})){var aa=Engine.Options(p,path,true);aa.Add(p.User+"@"+p.Host);var info=Engine.StartInfo(Engine.Ssh("sftp"),aa);info.EnvironmentVariables["SSH_ASKPASS"]=Path.Combine(Engine.Bin,"server-askpass.exe");info.EnvironmentVariables["SSH_ASKPASS_REQUIRE"]="force";info.EnvironmentVariables["DISPLAY"]="ServerManager";info.EnvironmentVariables["SERVERMANAGER_PIPE"]=ask.Name;
        Check(!info.Arguments.Contains(cfg.Password)&&!info.EnvironmentVariables.Values.Cast<string>().Any(v=>v.Contains(cfg.Password)),"synthetic secret absent from process arguments and environment");
        var rr=Engine.Process(info,15,"pwd\nquit\n");Check(rr.Exit==0&&rr.Out.Contains("/"),"real OpenSSH SFTP session reuses saved-password pipe");}}finally{File.Delete(path);}}
}
}
