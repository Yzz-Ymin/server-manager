# macOS

原生 AppKit 界面 + Python 标准库连接核心；调用系统 SSH / SFTP 和 Security.framework。无需本机网页服务。

## 构建

安装 Apple Command Line Tools（或 Xcode）和 Python 3.10+，在仓库根目录执行：

```sh
python3 macos/build.py
open macos/dist/ServerManager.app
```

默认构建当前处理器架构。`BUILD_ARCH=x86_64` 或 `BUILD_ARCH=arm64` 可指定目标，`SDKROOT` 可指定兼容的 macOS SDK；交叉构建成功不等于已在对应设备实测。应用启动时查找 Homebrew、标准 Python 安装路径及系统 Python，不内嵌构建机器的解释器路径。

生成应用位于 `macos/dist/ServerManager.app`，配置位于 `~/.config/server-manager/`。此公开版使用独立的配置和钥匙串命名空间，不自动导入其他工具的数据。

## 测试

```sh
python3 -B macos/Tests/test_server_access.py
python3 -B macos/Tests/test_native_bridge.py
python3 -B macos/Tests/test_run_once.py
```

测试使用隔离目录、假凭据及模拟连接，覆盖命令退出码、sudo stdin、超时、基本脱敏、保存回滚、分组、密码保留和错误分类。原生控件检查可在桌面会话中运行：

```sh
macos/dist/ServerManager.app/Contents/MacOS/ServerManager --smoke-test /tmp/server-manager-smoke.json
```

该模式打开合成界面后退出，并输出 JSON 检查结果，不验证真实服务器登录。不要将其作为真实环境验收的替代。

## CLI

以安装到 `/Applications` 为例：

```sh
HELPER=/Applications/ServerManager.app/Contents/Resources/scripts/server_access.py
python3 "$HELPER" list
python3 "$HELPER" run demo status --timeout 45
python3 "$HELPER" once demo --command 'find /srv -mindepth 1 -maxdepth 1 -type d -print' --timeout 45
```

`demo` 是示例别名，需先在应用中创建自己的配置。`once` 临时登记命令，正常返回或可处理异常时恢复操作配置；`--sudo` 明确使用保存的 sudo 凭据。它不是系统级命令白名单。不要直接调用内部凭据或认证子命令。

## 打包

```sh
python3 macos/package.py
```

生成 DMG、应用 ZIP、源码 ZIP 和 SHA-256 清单。DMG/应用 ZIP 不包括源码或配置；源码 ZIP 包含当前项目源码、图标和说明。发布前须检查源码与二进制中没有个人路径、凭据或真实服务器信息。当前发行包依赖已安装的 Python，并非自包含运行时。

卸载时先在应用内删除不再需要的服务器连接，以清理对应钥匙串条目；退出应用后删除应用。保留配置时，卸载应用不会自动清理配置或凭据。
