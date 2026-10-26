# ServerManager for Windows

原生中文 SSH 服务器管理工具，提供分组搜索、Windows 安全凭据存储、SSH/SFTP 终端和命令行执行接口。仅支持 Linux 远端。

## 环境

Windows 11、.NET Framework 4.8 和 Windows OpenSSH 客户端。可用 Visual Studio 的“.NET 桌面开发”工具打开解决方案；运行应用不需要 Python。

## 构建与安装

从仓库根目录进入 `windows` 目录，在 PowerShell 中执行：

```powershell
.\scripts\build.ps1
.\scripts\install.ps1
```

缺少 .NET 4.8 Targeting Pack 时，首次构建会从 NuGet 下载固定版本的官方引用程序集并校验 SHA-256，缓存在 `artifacts/reference-assemblies`；运行应用不需要这份构建依赖。

构建输出位于 `artifacts/bin/Release`。安装器默认按当前用户安装，创建桌面与开始菜单快捷方式；可通过 `-InstallDirectory` 指定位置。源码不会复制进安装目录。

生成可复制到其他 Windows 主机的安装包：

```powershell
.\scripts\package.ps1
```

安装包与 `SHA256SUMS` 校验文件位于 `artifacts/packages`，包内包含仓库 MIT 许可证。将生成的 ZIP 复制到目标主机，解压后执行其中的 `scripts/install.ps1`，无需重新构建。完整仓库复制后也可使用相同构建命令，不依赖固定盘符或用户名。

### EXE 安装向导

预构建 EXE 位于仓库 `packages/windows`，可直接双击运行。安装前从托盘退出应用；向导默认使用现有快捷方式中的安装目录，也可自行选择目录。卸载使用 Windows 设置中的“已安装的应用”。更新与卸载保留用户连接配置和凭据。

使用 [Inno Setup 6.7.3 或更高版本](https://jrsoftware.org/isdl.php) 将 ZIP 包封装成可双击的 EXE：

```powershell
.\scripts\package-exe.ps1 -PackagePath <ZIP包路径> -CompilerPath <ISCC.exe路径>
```

EXE 与校验清单位于 `artifacts/packages`。构建会下载固定版本的官方简体中文语言资源并校验 SHA-256；EXE 包不依赖目标主机的 PowerShell 执行策略。它仍会检查应用控制，不会跳过系统拦截。助手集成继续使用 ZIP 中的安装脚本。

## 签名与应用控制

默认生成 `ServerManager-1.1.1-windows-unsigned.zip` 开发包。启用 Smart App Control 或企业应用控制的主机可能拒绝运行未签名 EXE/DLL；本地编译不等于受信任发布。安装器会先验证新版能否启动，失败时保留原安装。

具备受信任代码签名证书后，可通过 `scripts/sign.ps1` 使用证书指纹和时间戳服务签名，再运行 `scripts/package.ps1 -RequireSigned` 生成带签名校验的发布包。签名工具使用 Windows SDK 的 SignTool，不接收或导出私钥。详情见 [微软代码签名说明](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)。

## 使用

打开“服务器管理”，新增连接、选择认证方式并填写凭据。保存时独立核对主机指纹，验证成功后提交。编辑时密码留空保留匹配的旧值；私钥口令与 sudo 密码分开管理。

支持导入已有 SSH 别名及已知主机公钥，复用 SSH Agent；不导入跳板机或高级代理配置。密码保存在当前用户的 Windows 凭据管理器，不写入普通配置。关闭窗口后驻留托盘，退出入口仅在托盘菜单。

命令行在构建输出或安装目录运行：

```powershell
.\server-access.exe list --json
.\server-access.exe configure
.\server-access.exe run <alias> status --timeout 30
.\server-access.exe ssh <alias>
.\server-access.exe sftp <alias>
```

一次性命令通过 `once <alias> --request-stdin` 接收含 `command`、`sudo`、`timeout` 的 JSON。默认普通账号执行；输出是结构化结果，远端内容封装在 `stdout`。超时停止本地连接，但远端命令可能继续运行，不应盲目重试写入操作。

## 可选助手集成

安装时添加 `-InstallSkill`，可通过 `-SkillDirectory` 指定宿主的用户级技能目录。安装器自动生成应用绑定，技能无需写入固定安装路径。更换安装位置后重新运行安装器更新绑定。

## 测试、更新与卸载

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r tests\requirements.txt
.\scripts\test.ps1 -Python .\.venv\Scripts\python.exe
```

测试只使用回环 SSH 服务及合成凭据，结果位于 `artifacts/tests`。测试范围见 [测试说明](docs/testing.md)。

更新前从托盘退出，再安装新版本；连接配置和凭据保留。卸载时运行安装目录中的 `uninstall.ps1`。若还要删除连接及本工具保存的凭据，先在界面逐项删除连接，再卸载。秘密需要在新主机重新录入，不随源码或安装包迁移。

## 目录

`src` 应用源码；`tests` 合成与集成测试；`scripts` 构建、安装、测试和打包；`assets` 图标；`integrations` 可选助手技能；`build` 共享项目设置；`artifacts` 本地生成文件。
