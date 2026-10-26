param([string]$PackagePath,[string]$CompilerPath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(-not $PackagePath){$PackagePath=Join-Path (Split-Path $projectRoot -Parent) 'packages\windows\ServerManager-1.1.3-windows-unsigned.zip'}
$archive=(Resolve-Path -LiteralPath $PackagePath).Path
if(-not $CompilerPath){
    $command=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if($command){$CompilerPath=$command.Source}else{
        $key=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1' -ErrorAction SilentlyContinue
        if($key.InstallLocation){$CompilerPath=Join-Path $key.InstallLocation 'ISCC.exe'}
    }
}
if(-not $CompilerPath -or -not(Test-Path -LiteralPath $CompilerPath)){throw '需要 Inno Setup 6.7.3 或更高版本，请通过 -CompilerPath 指定 ISCC.exe。'}
$packageRoot=Join-Path $projectRoot 'artifacts\packages'
$stage=Join-Path $packageRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try{
    $languageDir=Join-Path $projectRoot 'artifacts\installer-language'
    $languageFile=Join-Path $languageDir 'ChineseSimplified.isl'
    New-Item -ItemType Directory -Path $languageDir -Force | Out-Null
    if(-not(Test-Path -LiteralPath $languageFile)){
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl' -OutFile $languageFile
    }
    if((Get-FileHash -LiteralPath $languageFile -Algorithm SHA256).Hash -ne '7d544b9bb1d142cfa11f2e5d3cc8abe2e55f8e066c5124e3772675aa236e1278'){throw '官方中文语言资源校验失败。'}
    Expand-Archive -LiteralPath $archive -DestinationPath $stage
    $release=Get-Content -LiteralPath (Join-Path $stage 'release.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if($release.product -ne 'Windows-ServerManager' -or $release.version -notmatch '\A\d+\.\d+\.\d+\z'){throw '无效的 Windows 安装包。'}
    foreach($name in @('ServerManager.exe','server-access.exe','server-askpass.exe','ServerManager.Core.dll','AppIcon.ico','ServerManager.exe.config','server-access.exe.config','server-askpass.exe.config')){
        if(-not(Test-Path -LiteralPath (Join-Path $stage ('bin\'+$name)))){throw "安装包缺少 $name。"}
    }
    @('# 服务器管理', '', '从桌面或开始菜单打开服务器管理。关闭窗口后驻留托盘，退出入口在托盘菜单。', '', '连接配置与密码由当前 Windows 用户保存。更新或卸载保留连接配置和凭据，不随安装包迁移到其他主机。', '', '更新前先从托盘退出，再运行新版 EXE 安装向导。卸载请使用 Windows 设置中的“已安装的应用”。', '', '如需助手集成，请使用 ZIP 包内的 scripts/install.ps1 -InstallSkill；变更安装目录后需重新绑定执行助手。', '', '部分 Windows 自带扫描器的算法兼容问题会自动使用已安装 Git for Windows 的扫描器处理；没有可用兼容扫描器时请更新 Git 或 Windows OpenSSH。', '', '连接元数据统一存于 %USERPROFILE%\.server-manager；首次启动迁移旧配置并保留源文件，密码仍保存在 Windows 凭据管理器。', '', '需要 Windows 11、.NET Framework 4.8 和 OpenSSH 客户端，仅支持 Linux 远端。', '', '此为未签名开发构建。Windows 应用控制可能拒绝启动，安装前检查失败时保留现有版本。') | Set-Content -LiteralPath (Join-Path $stage 'README.md') -Encoding UTF8
    & $CompilerPath ("/DPayloadDir=$stage") ("/DChineseLanguageFile=$languageFile") ("/DAppVersion="+$release.version) ("/DOutputDir=$packageRoot") (Join-Path $projectRoot 'build\setup.iss')
    if($LASTEXITCODE){throw "EXE 编译失败，退出码 $LASTEXITCODE。"}
    $setup=Join-Path $packageRoot ("ServerManager-"+$release.version+'-windows-setup-unsigned.exe')
    if(-not(Test-Path -LiteralPath $setup)){throw '编译器没有生成安装包。'}
    $outputArchive=Join-Path $packageRoot ([IO.Path]::GetFileName($archive))
    if(-not [string]::Equals($archive,[IO.Path]::GetFullPath($outputArchive),[StringComparison]::OrdinalIgnoreCase)){Copy-Item -LiteralPath $archive -Destination $outputArchive -Force}
    $checksums=@($outputArchive,$setup) | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}
    [IO.File]::WriteAllText((Join-Path $packageRoot 'SHA256SUMS'),($checksums -join "`n")+"`n",(New-Object Text.UTF8Encoding($false)))
    Write-Output "EXE 安装包：$setup"
}finally{
    $resolved=[IO.Path]::GetFullPath($stage)
    if($resolved.StartsWith([IO.Path]::GetFullPath($packageRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
