param(
    [string]$InstallDirectory=(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\ServerManager'),
    [string]$BinaryDirectory,
    [switch]$InstallSkill,
    [string]$SkillDirectory=(Join-Path $env:USERPROFILE '.agents\skills\windows-server-access'),
    [switch]$NoLaunch
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$installRoot=[IO.Path]::GetFullPath($InstallDirectory)
if(-not $BinaryDirectory){
    $BinaryDirectory=Join-Path $projectRoot 'artifacts\bin\Release'
    if(-not(Test-Path -LiteralPath $BinaryDirectory)){$BinaryDirectory=Join-Path $projectRoot 'bin'}
}
$binaries=[IO.Path]::GetFullPath($BinaryDirectory)
$licensePath=Join-Path $projectRoot 'LICENSE'
if(-not(Test-Path -LiteralPath $licensePath)){$licensePath=Join-Path (Split-Path $projectRoot -Parent) 'LICENSE'}
if(-not(Test-Path -LiteralPath $licensePath)){throw '缺少 MIT 许可证，请使用完整源码或安装包。'}
$files=@('ServerManager.exe','server-access.exe','server-askpass.exe','ServerManager.Core.dll','AppIcon.ico','ServerManager.exe.config','server-access.exe.config','server-askpass.exe.config')
foreach($file in $files){if(-not(Test-Path -LiteralPath (Join-Path $binaries $file))){throw "缺少构建文件 $file，请先运行构建脚本。"}}
$running=Get-Process -Name ServerManager -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $installRoot 'ServerManager.exe')}
if($running){throw '应用正在运行。请先完成或取消编辑，并从托盘退出，再更新。'}
if($installRoot -eq $binaries){throw '安装目录须与构建输出分开。'}
try{
    $probe=New-Object Diagnostics.ProcessStartInfo
    $probe.FileName=Join-Path $binaries 'server-access.exe'
    $probe.Arguments='--help'
    $probe.UseShellExecute=$false
    $probe.CreateNoWindow=$true
    $probe.RedirectStandardOutput=$true
    $probe.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($probe)
    $stdout=$process.StandardOutput.ReadToEnd()
    $stderr=$process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw '执行助手未通过启动检查。'}
    $process.Dispose()
}catch{throw '新版程序无法启动，可能被 Windows 应用控制拦截。请使用受信任签名的构建；现有安装尚未修改。'}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
foreach($file in $files){Copy-Item -LiteralPath (Join-Path $binaries $file) -Destination (Join-Path $installRoot $file) -Force}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $installRoot -Force
Copy-Item -LiteralPath $licensePath -Destination (Join-Path $installRoot 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $installRoot -Force
$previousFile=Join-Path $installRoot 'installation.json'
$previous=$null
if(Test-Path -LiteralPath $previousFile){$previous=Get-Content -LiteralPath $previousFile -Raw -Encoding UTF8 | ConvertFrom-Json}
$skillRoot=$null
if($InstallSkill){
    $skillRoot=[IO.Path]::GetFullPath($SkillDirectory)
    if((Split-Path $skillRoot -Leaf) -ne 'windows-server-access'){throw 'Skill 目录名称须为 windows-server-access。'}
    New-Item -ItemType Directory -Path $skillRoot -Force | Out-Null
    foreach($item in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'integrations\assistant-skill') -Force){Copy-Item -LiteralPath $item.FullName -Destination $skillRoot -Recurse -Force}
    @{product='Windows-ServerManager';installDirectory=$installRoot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $skillRoot 'application.json') -Encoding UTF8
}elseif($previous){
    if($previous.skillDirectory){$skillRoot=$previous.skillDirectory}elseif($previous.skill){$skillRoot=$previous.skill}
}
$shell=New-Object -ComObject WScript.Shell
$links=@((Join-Path ([Environment]::GetFolderPath('Desktop')) '服务器管理.lnk'),(Join-Path ([Environment]::GetFolderPath('Programs')) '服务器管理.lnk'))
foreach($link in $links){$shortcut=$shell.CreateShortcut($link);$shortcut.TargetPath=Join-Path $installRoot 'ServerManager.exe';$shortcut.WorkingDirectory=$installRoot;$shortcut.IconLocation=(Join-Path $installRoot 'ServerManager.exe')+',0';$shortcut.Description='服务器管理与安全 SSH 访问';$shortcut.Save()}
@{product='Windows-ServerManager';version='1.1.3';installedAt=(Get-Date -Format o);installDirectory=$installRoot;skillDirectory=$skillRoot;shortcuts=$links} | ConvertTo-Json | Set-Content -LiteralPath $previousFile -Encoding UTF8
& (Join-Path $installRoot 'server-access.exe') import-openssh
if($LASTEXITCODE){throw '应用已安装，SSH 别名导入失败，可在界面中重试。'}
Write-Output "安装完成：$installRoot"
if(-not $NoLaunch){Start-Process -FilePath (Join-Path $installRoot 'ServerManager.exe')}
