# Windows 安装包

- `ServerManager-1.1.1-windows-setup-unsigned.exe`：可双击的中文安装向导，支持选择目录、升级与 Windows 应用卸载。会尝试识别现有快捷方式中的安装目录。
- `ServerManager-1.1.1-windows-unsigned.zip`：脚本安装包，解压后在 PowerShell 中执行 `scripts/install.ps1`；助手集成使用 `-InstallSkill`。
- `SHA256SUMS`：SHA-256 校验清单，可用 `Get-FileHash <文件名> -Algorithm SHA256` 核对。

1.1.1 在窗口加载和从托盘恢复时重新读取连接；配置目录读取失败会明确报错。旧版本保留用于回溯，校验清单包含各版本。

要求 Windows 11 x64、.NET Framework 4.8 和 Windows OpenSSH 客户端。默认按当前用户安装；更新前先从托盘退出。更新和卸载保留连接配置与凭据。

这些开发包未经过受信任发布者签名。Smart App Control 或企业应用控制可能拒绝运行；改用 EXE 向导不会改变签名状态。安装前启动检查失败时保留已有安装，需要受信任签名的环境应使用签名后的构建。

安装包内包含 MIT 许可证，不附带服务器配置、账号、私钥或密码。源码与构建、测试及签名说明见 [Windows 文档](../../windows/README.md)。
