using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
namespace ServerManager {
internal static class CliProgram {
    public static int Main(string[] args){Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
        try{if(args.Length==0||args[0]=="--help"){Console.WriteLine("server-access list --json | configure [--alias NAME] | run ALIAS OP [--timeout SEC] | once ALIAS --request-stdin | ssh ALIAS | sftp ALIAS | import-openssh");return 0;}var store=new Store();
            if(args[0]=="list"){var profiles=store.List().Select(p=>new{alias=p.Alias,group=p.GroupLabel,host=p.Host,user=p.User,port=p.Port,remoteType="linux",auth=p.Auth,operations=new[]{"status","sudo-check"}.Concat(p.Operations.Keys).Distinct().ToArray()});Console.WriteLine(Store.Json(new{success=true,profiles=profiles}));return 0;}
            if(args[0]=="configure"){var i=Array.IndexOf(args,"--alias");string alias=i<0?null:args.ElementAtOrDefault(i+1);if(i>=0)Store.ValidateAlias(alias);var start=new ProcessStartInfo(Path.Combine(Engine.Bin,"ServerManager.exe"),alias==null?"--configure":Engine.Args(new[]{"--configure",alias})){UseShellExecute=true};Process.Start(start);Console.WriteLine(Store.Json(new{success=true,stage="configuration",code="GUI_OPENED",message="已打开原生配置窗口，请用户在窗口中填写。"}));return 0;}
            if(args[0]=="import-openssh"){Console.WriteLine(Store.Json(new{success=true,imported=store.ImportOpenSsh()}));return 0;}
            if(args.Length<2)throw new AccessException("INVALID_REQUEST","缺少服务器别名。","request");Store.ValidateAlias(args[1]);
            if(args[0]=="ssh"||args[0]=="sftp"){Console.Title="服务器管理 · "+args[1]+" · "+args[0].ToUpperInvariant();store.Terminal(args[1],args[0]=="sftp");return 0;}
            Result result;
            if(args[0]=="once"&&args.Contains("--request-stdin")){var requestText=ReadRequest();Request request;try{request=Store.Parse<Request>(requestText);}catch{throw new AccessException("INVALID_JSON","stdin 必须为含 command、sudo、timeout 的 JSON。","request");}if(request==null)throw new AccessException("INVALID_JSON","请求不能为空。","request");result=store.Run(args[1],request.command,request.sudo,request.timeout);}
            else if(args[0]=="run"&&args.Length>=3){int timeout=600;var i=Array.IndexOf(args,"--timeout");if(i>=0&&(args.Length<=i+1||!Int32.TryParse(args[i+1],out timeout)))throw new AccessException("INVALID_TIMEOUT","超时必须为秒数。","request");result=store.Named(args[1],args[2],timeout);}
            else throw new AccessException("INVALID_REQUEST","未知命令；运行 --help 查看接口。","request");Console.WriteLine(Store.Json(result));return result.success?0:result.remoteExitCode.HasValue&&result.remoteExitCode.Value>0&&result.remoteExitCode.Value<256?result.remoteExitCode.Value:1;
        }catch(AccessException ex){Console.WriteLine(Store.Json(Result.Error(ex.Stage,ex.Code,ex.Message)));return 1;}catch(Exception){Console.WriteLine(Store.Json(Result.Error("local","LOCAL_ERROR","本机操作失败，请检查配置、文件权限和安装依赖。")));return 1;}
    }
    static string ReadRequest(){var b=new StringBuilder();var buf=new char[4096];int n;while((n=Console.In.Read(buf,0,buf.Length))>0){if(b.Length+n>128*1024)throw new AccessException("REQUEST_TOO_LARGE","请求超过 128 KiB。","request");b.Append(buf,0,n);}return b.ToString();}
}
}
