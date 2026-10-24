# Windows 开发包

- `ServerManager-1.1.0-windows-unsigned.zip`：未签名开发构建，解压后在 PowerShell 中执行 `scripts/install.ps1`。
- `SHA256SUMS`：SHA-256 校验清单，可用 `Get-FileHash .\ServerManager-1.1.0-windows-unsigned.zip -Algorithm SHA256` 核对。

要求 Windows 11 x64、.NET Framework 4.8 和 Windows OpenSSH 客户端。默认按当前用户安装，运行后通过托盘退出。

此包未经过受信任发布者签名。Smart App Control 或企业应用控制可能拒绝运行；安装器启动检查失败时保留已有安装。需要受信任签名的环境应使用签名后的构建。

包内包含 MIT 许可证，不附带服务器配置、账号、私钥或密码。源码与构建、测试及签名说明见 [Windows 文档](../../windows/README.md)。
