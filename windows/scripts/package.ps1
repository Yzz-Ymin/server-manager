param([ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$RequireSigned)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$version='1.1.3'
$licensePath=Join-Path (Split-Path $projectRoot -Parent) 'LICENSE'
if(-not(Test-Path -LiteralPath $licensePath)){throw '缺少仓库根目录 LICENSE，请使用完整源码构建。'}
if(-not $RequireSigned){& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration}
$binaries=Join-Path $projectRoot "artifacts\bin\$Configuration"
$runtimeNames=@('ServerManager.exe','server-access.exe','server-askpass.exe','ServerManager.Core.dll')
$signed=@($runtimeNames | Where-Object {(Get-AuthenticodeSignature -LiteralPath (Join-Path $binaries $_)).Status -ne 'Valid'}).Count -eq 0
if($RequireSigned -and -not $signed){throw '发布检查失败：所有 EXE/DLL 必须具有有效的受信任代码签名。'}
$packageRoot=Join-Path $projectRoot 'artifacts\packages'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$stage=Join-Path $packageRoot ([Guid]::NewGuid().ToString('N'))
$archive=Join-Path $packageRoot $(if($signed){"ServerManager-$version-windows.zip"}else{"ServerManager-$version-windows-unsigned.zip"})
New-Item -ItemType Directory -Path (Join-Path $stage 'bin'),(Join-Path $stage 'scripts'),(Join-Path $stage 'integrations') -Force | Out-Null
try{
    $binaries=Join-Path $projectRoot "artifacts\bin\$Configuration"
    foreach($name in @('ServerManager.exe','server-access.exe','server-askpass.exe','ServerManager.Core.dll','AppIcon.ico','ServerManager.exe.config','server-access.exe.config','server-askpass.exe.config')){Copy-Item -LiteralPath (Join-Path $binaries $name) -Destination (Join-Path $stage 'bin')}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1'),(Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $stage 'scripts')
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $stage 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'integrations\assistant-skill') -Destination (Join-Path $stage 'integrations') -Recurse
    @('# ServerManager', '', '解压后执行 scripts/install.ps1，需要助手集成时添加 -InstallSkill。', '', '运行开始菜单中的服务器管理。关闭窗口后驻留托盘；更新前先从托盘退出。', '', '部分 Windows 自带扫描器的算法兼容问题会自动使用已安装 Git for Windows 的扫描器处理；没有可用兼容扫描器时请更新 Git 或 Windows OpenSSH。', '', '连接元数据统一存于 %USERPROFILE%\.server-manager；首次启动迁移旧配置并保留源文件，密码仍保存在 Windows 凭据管理器。', '', '需要 Windows 11、.NET Framework 4.8 和 OpenSSH 客户端，仅支持 Linux SSH 远端。', '', '卸载时运行安装目录中的 uninstall.ps1，连接配置与凭据保留。') | Set-Content -LiteralPath (Join-Path $stage 'README.md') -Encoding UTF8
    if(-not $signed){Add-Content -LiteralPath (Join-Path $stage 'README.md') -Value "`n此为未签名开发构建。启用应用控制的主机可能拒绝运行；本地编译不会自动获得发布者信任。" -Encoding UTF8}
    @{product='Windows-ServerManager';version=$version;signed=$signed} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding UTF8
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
    $checksum=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive)
    [IO.File]::WriteAllText((Join-Path $packageRoot 'SHA256SUMS'),$checksum+"`n",(New-Object Text.UTF8Encoding($false)))
    Write-Output "安装包：$archive"
}finally{
    $resolved=[IO.Path]::GetFullPath($stage)
    if($resolved.StartsWith([IO.Path]::GetFullPath($packageRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
