using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ServerManager {
public sealed class Profile {
    public string Alias { get; set; }
    public string Group { get; set; }
    public string Host { get; set; }
    public string User { get; set; }
    public int Port { get; set; }
    public string Auth { get; set; }
    public string KeyPath { get; set; }
    public bool SudoSame { get; set; }
    public string LoginRef { get; set; }
    public string PassphraseRef { get; set; }
    public string SudoRef { get; set; }
    public string HostKeys { get; set; }
    public string Revision { get; set; }
    public Dictionary<string, Operation> Operations { get; set; }
    public Profile() { Port=22; Auth="key"; Group=""; KeyPath=""; Revision=""; Operations=new Dictionary<string,Operation>(); }
    public string Endpoint { get { return User+"@"+Host+":"+Port; } }
    public string GroupLabel { get { return String.IsNullOrEmpty(Group)?"未分组":Group; } }
    public string AuthLabel { get { return Auth=="key"?"私钥 / SSH Agent":"密码"; } }
}
public sealed class Operation { public string Command { get; set; } public bool Sudo { get; set; } }
public sealed class Request { public string command { get; set; } public bool sudo { get; set; } public int timeout { get; set; } public Request(){timeout=600;} }
public sealed class Result {
    public bool success { get; set; }
    public string stage { get; set; }
    public string code { get; set; }
    public string message { get; set; }
    public int? remoteExitCode { get; set; }
    public string stdout { get; set; }
    public bool outputTruncated { get; set; }
    public bool remoteStateUnknown { get; set; }
    public static Result Error(string stage,string code,string message) { return new Result{stage=stage,code=code,message=message,stdout=""}; }
}
public sealed class AccessException:Exception {
    public string Code; public string Stage;
    public AccessException(string code,string message,string stage="configuration"):base(message){Code=code;Stage=stage;}
}
internal sealed class Secrets {
    internal string Login, Passphrase, Sudo;
    internal string Redact(string s) { foreach(var v in new[]{Login,Passphrase,Sudo}) if(!String.IsNullOrEmpty(v)) s=s.Replace(v,"[已隐藏凭据]"); return s; }
    internal void Clear(){Login=Passphrase=Sudo=null;}
}
internal static class Vault {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Credential {
        public uint Flags,Type; public string TargetName,Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist,AttributeCount; public IntPtr Attributes; public string TargetAlias,UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ReadNative(string target,uint type,uint flags,out IntPtr p);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WriteNative(ref Credential c,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool DeleteNative(string target,uint type,uint flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr p);
    internal static string Read(string target) {
        if(String.IsNullOrEmpty(target)) return null;
        IntPtr p;
        if(!ReadNative(target,1,0,out p)) { if(Marshal.GetLastWin32Error()==1168) throw new AccessException("CREDENTIAL_MISSING","保存的凭据不存在，请在编辑窗口重新填写。","credential"); throw new AccessException("VAULT_DENIED","Windows 凭据库拒绝访问。","credential"); }
        try {var c=(Credential)Marshal.PtrToStructure(p,typeof(Credential)); return Marshal.PtrToStringUni(c.CredentialBlob,(int)c.CredentialBlobSize/2); } finally{CredFree(p);}
    }
    internal static void Write(string target,string user,string secret) {
        var data=Encoding.Unicode.GetBytes(secret); if(data.Length>2560) throw new AccessException("SECRET_TOO_LONG","凭据长度超过 Windows 限制。");
        var p=Marshal.AllocHGlobal(data.Length);
        try{Marshal.Copy(data,0,p,data.Length);var c=new Credential{Type=1,TargetName=target,UserName=user,CredentialBlobSize=(uint)data.Length,CredentialBlob=p,Persist=2};if(!WriteNative(ref c,0))throw new AccessException("VAULT_DENIED","无法写入 Windows 凭据库。","credential");}
        finally{for(int i=0;i<data.Length;i++)Marshal.WriteByte(p,i,0);Marshal.FreeHGlobal(p);Array.Clear(data,0,data.Length);}
    }
    internal static void DeleteOwned(string target,string prefix) { if(!String.IsNullOrEmpty(target)&&target.StartsWith(prefix,StringComparison.Ordinal)) { if(!DeleteNative(target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new AccessException("VAULT_DENIED","无法删除本工具的凭据。","credential"); } }
}
public sealed class Store {
    public readonly string Root;
    internal readonly string Prefix;
    public Store():this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ServerManager"),"ServerManager/"){}
    internal Store(string root,string prefix){Root=Path.GetFullPath(root);Prefix=prefix;}
    internal static string Digest(string s){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").Substring(0,24);}
    public static string Json(object o){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(o);}
    public static T Parse<T>(string s){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<T>(s);}
    public IDisposable Lock(){return new StoreLock("Local\\ServerManager.Store."+Digest(Root.ToLowerInvariant()),1500);}
    internal static void PrivateDirectory(string path) {
        Directory.CreateDirectory(path); var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.SetAccessControl(path,acl);
    }
    internal string ProfilePath(string alias){ValidateAlias(alias);return Path.Combine(Root,"profiles",alias+".json");}
    public List<Profile> List(){using(Lock())return ListUnlocked();}
    internal List<Profile> ListUnlocked(){var dir=Path.Combine(Root,"profiles"); if(!Directory.Exists(dir))return new List<Profile>(); return Directory.GetFiles(dir,"*.json").Select(f=>Read(Path.GetFileNameWithoutExtension(f))).OrderBy(p=>p.GroupLabel).ThenBy(p=>p.Alias).ToList();}
    public Profile Read(string alias){var path=ProfilePath(alias);if(!File.Exists(path))throw new AccessException("PROFILE_MISSING","未配置该别名，请打开配置窗口。");var p=Parse<Profile>(File.ReadAllText(path,Encoding.UTF8));Validate(p,false);if(p.Alias!=alias)throw new AccessException("INVALID_PROFILE","别名与配置文件不一致。");return p;}
    public static void ValidateAlias(string alias){if(alias==null||!Regex.IsMatch(alias,@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z"))throw new AccessException("INVALID_ALIAS","别名须为 1–64 位英文字母、数字、下划线或短横线，首位须为字母或数字。");}
    public static void Validate(Profile p,bool checkKey=true){
        ValidateAlias(p.Alias);
        if(p.Group==null)p.Group="";
        if(p.Group.Length>40||p.Group.Any(Char.IsControl))throw new AccessException("INVALID_GROUP","分组最多 40 个字符，不能含控制字符。");
        System.Net.IPAddress ip;
        if(String.IsNullOrEmpty(p.Host)||p.Host.Length>253||(!System.Net.IPAddress.TryParse(p.Host,out ip)&&!Regex.IsMatch(p.Host,@"\A[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\z")))throw new AccessException("INVALID_HOST","请填写有效 IP 或主机名。");
        if(p.User==null||!Regex.IsMatch(p.User,@"\A[A-Za-z0-9_][A-Za-z0-9_.@-]{0,63}\z"))throw new AccessException("INVALID_USER","账号格式无效，不能含空格、换行或选项。");
        if(p.Port<1||p.Port>65535)throw new AccessException("INVALID_PORT","端口须在 1–65535 之间。");
        if(p.Auth!="key"&&p.Auth!="password")throw new AccessException("INVALID_AUTH","请选择密码或私钥认证。");
        if(p.Auth=="key"&&checkKey){if(String.IsNullOrEmpty(p.KeyPath)||!File.Exists(p.KeyPath))throw new AccessException("KEY_MISSING","私钥文件不存在。","key"); CheckKeyAcl(p.KeyPath);}
        if(p.Auth=="key"&&p.SudoSame)throw new AccessException("INVALID_SUDO","私钥解锁口令不能用作 sudo 密码。");
        foreach(var r in new[]{p.LoginRef,p.PassphraseRef,p.SudoRef})if(r!=null&&!(r.StartsWith("ServerManager/",StringComparison.Ordinal)||r.StartsWith("ServerManagerTest/",StringComparison.Ordinal)||r=="CodexSudo:"+p.Host))throw new AccessException("INVALID_CREDENTIAL_REF","凭据引用不属于当前连接。");
        if(p.Operations==null)p.Operations=new Dictionary<string,Operation>();
    }
    public static void CheckKeyAcl(string path){
        try{using(var f=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){} // Do not inspect private key bytes.
            var acl=File.GetAccessControl(path);var user=WindowsIdentity.GetCurrent().User;
            foreach(FileSystemAccessRule r in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))){var sid=(SecurityIdentifier)r.IdentityReference;
                if(r.AccessControlType==AccessControlType.Allow&&(r.FileSystemRights&FileSystemRights.ReadData)!=0&&sid!=user&&!sid.IsWellKnown(WellKnownSidType.LocalSystemSid)&&!sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))throw new AccessException("KEY_ACL_BROAD","私钥允许其他用户或组读取。请核对该文件 ACL；工具不会自动修改权限。","key");}
        }catch(AccessException){throw;}catch(Exception){throw new AccessException("KEY_INACCESSIBLE","无法访问私钥或读取其 ACL。","key");}
    }
    internal static void AtomicWrite(string path,string contents){
        var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var b=new UTF8Encoding(false).GetBytes(contents);f.Write(b,0,b.Length);f.Flush(true);}if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    internal Secrets LoadSecrets(Profile p){return new Secrets{Login=Vault.Read(p.LoginRef),Passphrase=Vault.Read(p.PassphraseRef),Sudo=Vault.Read(p.SudoRef)};}
    internal static bool SameIdentity(Profile a,Profile b){return a!=null&&b!=null&&a.Host==b.Host&&a.Port==b.Port&&a.User==b.User;}
    internal static bool SameKey(Profile a,Profile b){return SameIdentity(a,b)&&a.Auth==b.Auth&&String.Equals(a.KeyPath,b.KeyPath,StringComparison.OrdinalIgnoreCase);}
    internal Secrets Resolve(Profile p,Profile old,string login,string phrase,string sudo,bool removeSudo){
        var s=new Secrets();
        if(p.Auth=="password")s.Login=!String.IsNullOrEmpty(login)?login:(SameIdentity(p,old)&&old.Auth=="password"?Vault.Read(old.LoginRef):null);
        else s.Passphrase=!String.IsNullOrEmpty(phrase)?phrase:(SameKey(p,old)?Vault.Read(old.PassphraseRef):null);
        s.Sudo=removeSudo?null:(!String.IsNullOrEmpty(sudo)?sudo:(SameIdentity(p,old)?Vault.Read(old.SudoRef):null));
        if(p.SudoSame)s.Sudo=s.Login;
        if(p.Auth=="password"&&String.IsNullOrEmpty(s.Login))throw new AccessException("CREDENTIAL_MISSING","密码认证需要填写 SSH 登录密码。","credential");
        return s;
    }
    internal void Recover(){
        var journal=Path.Combine(Root,"pending.json");if(!File.Exists(journal))return;
        var targets=Parse<List<string>>(File.ReadAllText(journal,Encoding.UTF8));
        var used=new HashSet<string>(ListUnlocked().SelectMany(p=>new[]{p.LoginRef,p.PassphraseRef,p.SudoRef}).Where(r=>r!=null));
        foreach(var target in targets)if(!used.Contains(target))Vault.DeleteOwned(target,Prefix);
        File.Delete(journal);
    }
    public void Save(Profile candidate,Profile expected,string login,string phrase,string sudo,bool removeSudo,Action<string> progress){
        using(Lock()){
            Validate(candidate);PrivateDirectory(Root);PrivateDirectory(Path.Combine(Root,"profiles"));Recover();
            Profile old=File.Exists(ProfilePath(candidate.Alias))?Read(candidate.Alias):null;
            if((expected==null&&old!=null)||(expected!=null&&(old==null||old.Revision!=expected.Revision)))throw new AccessException("PROFILE_CHANGED","该连接已被其他窗口修改，请重新打开编辑器。");
            if(String.IsNullOrEmpty(candidate.HostKeys))throw new AccessException("HOST_UNCONFIRMED","请先核对主机指纹。","host-key");
            var secrets=Resolve(candidate,old,login,phrase,sudo,removeSudo);
            var created=new List<string>();bool committed=false;
            try{
                progress("正在验证 SSH 登录…");var check=Engine.Execute(candidate,secrets,"true",false,30);if(!check.success)throw new AccessException(check.code,check.message,check.stage);
                if(secrets.Sudo!=null){progress("SSH 已成功，正在验证 sudo…");var su=Engine.Execute(candidate,secrets,"true",true,30);if(!su.success)throw new AccessException(su.code,"SSH 已成功；"+su.message,"sudo");}
                progress("正在提交本机配置和凭据…");candidate.Revision=Guid.NewGuid().ToString("N");
                string baseRef=Prefix+candidate.Alias+"/"+candidate.Revision+"/";
                candidate.LoginRef=secrets.Login==null?null:baseRef+"login";
                candidate.PassphraseRef=secrets.Passphrase==null?null:baseRef+"passphrase";
                candidate.SudoRef=secrets.Sudo==null?null:baseRef+"sudo";
                created.AddRange(new[]{candidate.LoginRef,candidate.PassphraseRef,candidate.SudoRef}.Where(v=>v!=null));
                var pending=new List<string>(created);if(old!=null)pending.AddRange(new[]{old.LoginRef,old.PassphraseRef,old.SudoRef}.Where(v=>v!=null&&v.StartsWith(Prefix,StringComparison.Ordinal)));
                AtomicWrite(Path.Combine(Root,"pending.json"),Json(pending));
                if(candidate.LoginRef!=null)Vault.Write(candidate.LoginRef,candidate.User,secrets.Login);
                if(candidate.PassphraseRef!=null)Vault.Write(candidate.PassphraseRef,candidate.User,secrets.Passphrase);
                if(candidate.SudoRef!=null)Vault.Write(candidate.SudoRef,candidate.User,secrets.Sudo);
                AtomicWrite(ProfilePath(candidate.Alias),Json(candidate));committed=true;
                // Committed configuration remains successful if old-credential cleanup needs a later retry.
                try{Recover();}catch(AccessException){}
            }finally{
                secrets.Clear();if(!committed)foreach(var r in created)Vault.DeleteOwned(r,Prefix);
            }
        }
    }
    internal void Cleanup(Profile p){foreach(var r in new[]{p.LoginRef,p.PassphraseRef,p.SudoRef})Vault.DeleteOwned(r,Prefix);}
    public void Delete(Profile expected){using(Lock()){Recover();var p=Read(expected.Alias);if(p.Revision!=expected.Revision)throw new AccessException("PROFILE_CHANGED","配置已变更，请刷新后重试。");AtomicWrite(Path.Combine(Root,"pending.json"),Json(new[]{p.LoginRef,p.PassphraseRef,p.SudoRef}.Where(v=>v!=null&&v.StartsWith(Prefix,StringComparison.Ordinal)).ToList()));File.Delete(ProfilePath(p.Alias));Recover();}}
    public Result Run(string alias,string command,bool sudo,int timeout){using(Lock()){var p=Read(alias);Validate(p);var s=LoadSecrets(p);try{return Engine.Execute(p,s,command,sudo,timeout);}finally{s.Clear();}}}
    public Result Named(string alias,string op,int timeout){using(Lock()){var p=Read(alias);Validate(p);string cmd;bool sudo=false;
        if(op=="status")cmd="uname -s; id; uptime";else if(op=="sudo-check"){cmd="true";sudo=true;}else{Operation item;if(!p.Operations.TryGetValue(op,out item))throw new AccessException("OPERATION_MISSING","该命名操作不存在；已获授权的精确命令可通过 once 执行。");cmd=item.Command;sudo=item.Sudo;}
        var s=LoadSecrets(p);try{return Engine.Execute(p,s,cmd,sudo,timeout);}finally{s.Clear();}}}
    public void Terminal(string alias,bool sftp){Profile p;Secrets s;using(Lock()){p=Read(alias);Validate(p);s=LoadSecrets(p);}try{Engine.Interactive(p,s,sftp);}finally{s.Clear();}}
    // Only public SSH metadata is imported. Existing global files are never modified.
    public int ImportOpenSsh(){using(Lock()){
        string config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".ssh","config");if(!File.Exists(config))return 0;
        var profiles=new List<Profile>();Profile current=null;
        foreach(var raw in File.ReadLines(config)){var m=Regex.Match(raw,@"^\s*(\w+)\s+(.+?)\s*$");if(!m.Success)continue;var key=m.Groups[1].Value.ToLowerInvariant();var val=m.Groups[2].Value.Trim().Trim('"');
            if(key=="host"){current=null;if(Regex.IsMatch(val,@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z")){current=new Profile{Alias=val,Group="已有 SSH",Auth="key"};profiles.Add(current);}}
            else if(current!=null){if(key=="hostname")current.Host=val;if(key=="user")current.User=val;if(key=="port"){int n;if(Int32.TryParse(val,out n))current.Port=n;}if(key=="identityfile")current.KeyPath=Environment.ExpandEnvironmentVariables(val.Replace("~/",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"/"));}
        }
        int count=0;foreach(var p in profiles){try{Validate(p,false);if(File.Exists(ProfilePath(p.Alias)))continue;
            p.HostKeys=Engine.ExistingKeys(p);if(String.IsNullOrEmpty(p.HostKeys))continue;
            try{var legacy=Vault.Read("CodexSudo:"+p.Host);if(legacy!=null)p.SudoRef="CodexSudo:"+p.Host;legacy=null;}catch(AccessException){}
            p.Revision=Guid.NewGuid().ToString("N");PrivateDirectory(Root);PrivateDirectory(Path.Combine(Root,"profiles"));AtomicWrite(ProfilePath(p.Alias),Json(p));count++;
        }catch(AccessException){}}
        return count;
    }}
}
internal sealed class StoreLock:IDisposable{
    Mutex mutex;bool acquired;
    internal StoreLock(string name,int timeout){mutex=new Mutex(false,name);try{try{acquired=mutex.WaitOne(timeout);}catch(AbandonedMutexException){acquired=true;}if(!acquired)throw new AccessException("BUSY","另一个连接任务正在运行，请完成后重试。","busy");}catch{mutex.Dispose();throw;}}
    public void Dispose(){if(acquired)mutex.ReleaseMutex();mutex.Dispose();}
}
internal sealed class Askpass:IDisposable {
    internal readonly string Name="ServerManager.Askpass."+Guid.NewGuid().ToString("N");
    readonly Secrets secrets;readonly Profile profile;readonly Task worker;volatile bool stopped;NamedPipeServerStream current;
    internal Askpass(Profile p,Secrets s){profile=p;secrets=s;worker=Task.Run((Action)Serve);}
    void Serve(){while(!stopped){
        var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
        try{using(var pipe=new NamedPipeServerStream(Name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl)){
            current=pipe;if(stopped)return;pipe.WaitForConnection();if(stopped)return;
            using(var reader=new BinaryReader(pipe,Encoding.UTF8,true))using(var writer=new BinaryWriter(pipe,Encoding.UTF8,true)){
                string prompt=reader.ReadString();string value=null;
                if(prompt.Length>8192)return;
                if(prompt.IndexOf("passphrase",StringComparison.OrdinalIgnoreCase)>=0&&profile.Auth=="key")value=secrets.Passphrase;
                else if(prompt.IndexOf("password",StringComparison.OrdinalIgnoreCase)>=0&&profile.Auth=="password")value=secrets.Login;
                writer.Write(value!=null);if(value!=null)writer.Write(value);writer.Flush();
            }
        }}catch(IOException){}catch(ObjectDisposedException){}finally{current=null;}
    }}
    public void Dispose(){stopped=true;var p=current;if(p!=null)try{p.Dispose();}catch{}try{worker.Wait(2000);}catch{}}
    internal static int Respond(string[] args){try{var name=Environment.GetEnvironmentVariable("SERVERMANAGER_PIPE");if(name==null||!Regex.IsMatch(name,@"\AServerManager\.Askpass\.[0-9a-f]{32}\z"))return 1;
        using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.None,TokenImpersonationLevel.Identification)){pipe.Connect(5000);using(var reader=new BinaryReader(pipe,Encoding.UTF8,true))using(var writer=new BinaryWriter(pipe,Encoding.UTF8,true)){
            writer.Write(String.Join(" ",args));writer.Flush();if(!reader.ReadBoolean())return 1;var secret=reader.ReadString();Console.OutputEncoding=new UTF8Encoding(false);Console.WriteLine(secret);secret=null;return 0;}}
    }catch{return 1;}}
}
internal sealed class ProcessOutput {internal int Exit;internal string Out,Err;internal bool Timeout,Truncated;}
internal sealed class Capture {
    readonly StringBuilder text=new StringBuilder();readonly int limit;internal bool Truncated;
    internal Capture(int n){limit=n;}
    internal async Task Read(StreamReader reader){var buf=new char[4096];int n;while((n=await reader.ReadAsync(buf,0,buf.Length))>0){int take=Math.Min(n,limit-text.Length);if(take>0)text.Append(buf,0,take);if(take<n)Truncated=true;}}
    public override string ToString(){return text.ToString();}
}
internal static class Engine {
    internal static readonly string Bin=AppDomain.CurrentDomain.BaseDirectory;
    internal static string Ssh(string name){var p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","OpenSSH",name+".exe");if(!File.Exists(p))throw new AccessException("OPENSSH_MISSING","本机未安装 Windows OpenSSH 客户端。","dependency");return p;}
    internal static string Quote(string value){var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}b.Append('\\',slashes*2);b.Append('"');return b.ToString();}
    internal static string Args(IEnumerable<string> a){return String.Join(" ",a.Select(Quote));}
    internal static string Shell(string value){return "'"+value.Replace("'","'\"'\"'")+"'";}
    internal static ProcessStartInfo StartInfo(string exe,IEnumerable<string> args){return new ProcessStartInfo(exe,Args(args)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};}
    internal static ProcessOutput Process(ProcessStartInfo info,int timeout,string input=null){
        // Process.Start creates a text writer immediately; suppress its UTF-8 BOM without changing the console code page.
        if(Console.InputEncoding.CodePage==65001&&Console.InputEncoding.GetPreamble().Length!=0)Console.InputEncoding=new UTF8Encoding(false);
        using(var p=new Process{StartInfo=info}){p.Start();var outbuf=new Capture(1024*1024);var errbuf=new Capture(128*1024);var o=outbuf.Read(p.StandardOutput);var e=errbuf.Read(p.StandardError);
            // The .NET Framework text writer inherits the console code page and can emit a BOM.
            var write=Task.Run(()=>{
                var bytes=new UTF8Encoding(false).GetBytes(input??"");
                try{using(var stdin=p.StandardInput.BaseStream)stdin.Write(bytes,0,bytes.Length);}
                catch(IOException){}catch(ObjectDisposedException){}
                finally{Array.Clear(bytes,0,bytes.Length);}
            });
            bool done=p.WaitForExit(timeout*1000);if(!done)KillTree(p);p.WaitForExit();Task.WaitAll(new[]{o,e,write},5000);
            return new ProcessOutput{Exit=p.ExitCode,Out=outbuf.ToString(),Err=errbuf.ToString(),Timeout=!done,Truncated=outbuf.Truncated};
        }
    }
    static void KillTree(Process process){try{using(var killer=System.Diagnostics.Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"taskkill.exe"),"/PID "+process.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){killer.WaitForExit(5000);}}catch{}try{if(!process.HasExited)process.Kill();}catch{}}
    internal static string[] KeyLines(string output,string alias){var lines=new List<string>();foreach(var line in output.Split('\n')){var parts=line.Trim().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);if(parts.Length>=3&&(parts[1].StartsWith("ssh-")||parts[1].StartsWith("ecdsa-"))){try{Convert.FromBase64String(parts[2]);lines.Add(alias+" "+parts[1]+" "+parts[2]);}catch(FormatException){}}}return lines.Distinct().ToArray();}
    internal static string ExistingKeys(Profile p){var hosts=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".ssh","known_hosts");if(!File.Exists(hosts))return null;var host=p.Port==22?p.Host:"["+p.Host+"]:"+p.Port;var result=Process(StartInfo(Ssh("ssh-keygen"),new[]{"-F",host,"-f",hosts}),5);return String.Join("\n",KeyLines(result.Out,p.Alias));}
    internal static string Scan(Profile p){Store.Validate(p,false);var result=Process(StartInfo(Ssh("ssh-keyscan"),new[]{"-T","8","-p",p.Port.ToString(),"-t","ed25519,ecdsa,rsa",p.Host}),25);var lines=KeyLines(result.Out,p.Alias);if(lines.Length==0)throw new AccessException("KEYSCAN_FAILED","未取得主机公钥。请检查地址、端口和网络。","host-key");return String.Join("\n",lines);}
    internal static string Fingerprints(string keys){var lines=new List<string>();foreach(var line in keys.Split('\n')){var p=line.Split(' ');using(var sha=SHA256.Create())lines.Add(p[1]+"  SHA256:"+Convert.ToBase64String(sha.ComputeHash(Convert.FromBase64String(p[2]))).TrimEnd('='));}return String.Join("\n",lines);}
    internal static List<string> Options(Profile p,string hosts,bool sftp){var a=new List<string>{"-F","NUL","-o","StrictHostKeyChecking=yes","-o","UserKnownHostsFile=\""+hosts.Replace('\\','/')+"\"","-o","GlobalKnownHostsFile=NUL","-o","HostKeyAlias="+p.Alias,"-o","UpdateHostKeys=no","-o","ConnectTimeout=12","-o","ConnectionAttempts=1","-o","NumberOfPasswordPrompts=1","-o","ServerAliveInterval=10","-o","ServerAliveCountMax=2","-o","ForwardAgent=no","-o","ClearAllForwardings=yes","-o","PermitLocalCommand=no","-o","ControlMaster=no"};
        if(p.Auth=="password")a.AddRange(new[]{"-o","PreferredAuthentications=password,keyboard-interactive","-o","PubkeyAuthentication=no"});
        else a.AddRange(new[]{"-o","PreferredAuthentications=publickey","-o","IdentitiesOnly=yes","-i",p.KeyPath});
        a.Add(sftp?"-P":"-p");a.Add(p.Port.ToString());if(!sftp){a.Add("-l");a.Add(p.User);}return a;
    }
    static void ConfigureAskpass(ProcessStartInfo info,Askpass ask){info.EnvironmentVariables["SSH_ASKPASS"]=Path.Combine(Bin,"server-askpass.exe");info.EnvironmentVariables["SSH_ASKPASS_REQUIRE"]="force";info.EnvironmentVariables["DISPLAY"]="ServerManager";info.EnvironmentVariables["SERVERMANAGER_PIPE"]=ask.Name;}
    static string TempHosts(Profile p){if(String.IsNullOrEmpty(p.HostKeys))throw new AccessException("HOST_UNCONFIRMED","缺少已确认的主机公钥。","host-key");var dir=Path.Combine(Path.GetTempPath(),"ServerManager",Guid.NewGuid().ToString("N"));Store.PrivateDirectory(dir);File.WriteAllText(Path.Combine(dir,"known_hosts"),p.HostKeys+"\n",new UTF8Encoding(false));return dir;}
    internal static Result Execute(Profile p,Secrets secrets,string command,bool sudo,int timeout){
        if(String.IsNullOrWhiteSpace(command)||command.Length>65536||command.IndexOf('\0')>=0)throw new AccessException("INVALID_COMMAND","命令为空、过长或包含 NUL。","request");
        if(timeout<1||timeout>86400)throw new AccessException("INVALID_TIMEOUT","超时须为 1–86400 秒。","request");
        var dir=TempHosts(p);
        try{using(var ask=new Askpass(p,secrets)){var a=Options(p,Path.Combine(dir,"known_hosts"),false);a.Add("-vv");a.Add("-T");a.Add(p.Host);a.Add((sudo?"sudo "+(secrets.Sudo==null?"-n":"-S -p ''")+" -- sh -c "+Shell(command):"sh -c "+Shell(command))+" 2>&1");
            var info=StartInfo(Ssh("ssh"),a);ConfigureAskpass(info,ask);var result=Process(info,timeout,sudo&&secrets.Sudo!=null?secrets.Sudo+"\n":null);
            if(result.Timeout){var r=Result.Error("execution","TIMEOUT","已终止本地 SSH 进程树；远端操作可能仍在执行，请核查后再决定是否重试。");r.remoteStateUnknown=true;r.stdout=secrets.Redact(result.Out);return r;}
            var classification=Classify(result.Err,p,secrets,sudo,result.Exit,result.Out);
            classification.stdout=secrets.Redact(result.Out);classification.outputTruncated=result.Truncated;return classification;
        }}finally{Directory.Delete(dir,true);}
    }
    internal static Result Classify(string err,Profile p,Secrets s,bool sudo,int exit,string remoteOutput=""){
        if(exit==0)return new Result{success=true,stage="execution",code="OK",message="执行完成。",remoteExitCode=0,stdout=""};
        int authenticated=err.IndexOf("\nAuthenticated to ",StringComparison.Ordinal);var e=(authenticated<0?err:err.Substring(0,authenticated)).ToLowerInvariant();string code=null,msg=null,stage="ssh";
        if(e.Contains("remote host identification has changed")||e.Contains("host key verification failed")||e.Contains("no ")&&e.Contains("host key is known")){code="HOST_KEY_MISMATCH";msg="主机密钥与已确认记录不一致，连接已拒绝。请独立核对后在编辑窗口更新。";stage="host-key";}
        else if(e.Contains("connection refused")){code="CONNECTION_REFUSED";msg="服务器拒绝连接，请检查 SSH 端口。";}
        else if(e.Contains("connection timed out")||e.Contains("no route to host")){code="CONNECTION_TIMEOUT";msg="连接超时或网络不可达。";}
        else if(e.Contains("could not resolve hostname")){code="DNS_FAILED";msg="无法解析主机名。";}
        else if(e.Contains("bad permissions")||e.Contains("unprotected private key")){code="KEY_ACL_BROAD";msg="私钥 ACL 过宽，OpenSSH 拒绝使用。";stage="key";}
        else if(e.Split('\n').Any(line=>line.Contains("load key")&&(line.Contains("invalid format")||line.Contains("error in libcrypto")))){code="KEY_FORMAT";msg="私钥格式不受当前 OpenSSH 支持或文件损坏。";stage="key";}
        else if(e.Contains("incorrect passphrase")||e.Contains("bad passphrase")||e.Contains("no passphrase given")){code=s.Passphrase==null?"PASSPHRASE_MISSING":"PASSPHRASE_WRONG";msg=s.Passphrase==null?"缺少私钥解锁口令，且 SSH Agent 未能使用该密钥。":"私钥解锁口令错误。";stage="key";}
        else if(e.Contains("agent refused operation")||e.Contains("signing failed")){code="AGENT_REFUSED";msg="SSH Agent 拒绝签名。";stage="key";}
        else if(e.Contains("no mutual signature")||e.Contains("no matching ")){code="ALGORITHM_MISMATCH";msg="客户端与服务器算法不兼容，工具未降低安全要求。";}
        else if(exit==255&&e.Contains("permission denied")){code=p.Auth=="password"?"AUTH_REJECTED":"PUBLICKEY_REJECTED";msg=p.Auth=="password"?"服务器拒绝密码或需要额外认证。":"服务器拒绝公钥；请核对账号、SSH Agent 和私钥解锁口令。";}
        else if(sudo&&exit!=0&&(remoteOutput.Contains("sudo:")||remoteOutput.Contains("try again"))){code="SUDO_FAILED";msg="SSH 已成功，sudo 认证或权限检查失败。";stage="sudo";}
        else if(exit==255){code="SSH_DISCONNECTED";msg="SSH 连接失败或中断；远端状态可能不确定。";}
        if(code!=null){var r=Result.Error(stage,code,msg);r.remoteStateUnknown=code=="SSH_DISCONNECTED";return r;}
        return new Result{success=exit==0,stage="execution",code=exit==0?"OK":"REMOTE_EXIT",message=exit==0?"执行完成。":"远端命令返回非零退出码。",remoteExitCode=exit,stdout=""};
    }
    internal static void Interactive(Profile p,Secrets s,bool sftp){var dir=TempHosts(p);try{using(var ask=new Askpass(p,s)){var args=Options(p,Path.Combine(dir,"known_hosts"),sftp);if(!sftp)args.Add("-tt");args.Add(sftp?p.User+"@"+p.Host:p.Host);var info=new ProcessStartInfo(Ssh(sftp?"sftp":"ssh"),Args(args)){UseShellExecute=false};ConfigureAskpass(info,ask);using(var proc=System.Diagnostics.Process.Start(info))proc.WaitForExit();}}finally{Directory.Delete(dir,true);}}
}
}
