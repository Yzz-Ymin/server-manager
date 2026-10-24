$ErrorActionPreference='Stop'
$installRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$manifestFile=Join-Path $installRoot 'installation.json'
if(-not(Test-Path -LiteralPath $manifestFile)){throw '请运行已安装应用目录中的卸载脚本。'}
$manifest=Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
if($manifest.product -ne 'Windows-ServerManager' -or $manifest.installDirectory -ne $installRoot){throw '安装目录校验失败。'}
if($installRoot -eq [IO.Path]::GetPathRoot($installRoot) -or $installRoot -eq $env:USERPROFILE){throw '拒绝删除系统根目录或用户目录。'}
$running=Get-Process -Name ServerManager -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $installRoot 'ServerManager.exe')}
if($running){throw '请先从托盘退出应用。'}
$shell=New-Object -ComObject WScript.Shell
$links=@((Join-Path ([Environment]::GetFolderPath('Desktop')) '服务器管理.lnk'),(Join-Path ([Environment]::GetFolderPath('Programs')) '服务器管理.lnk'))
foreach($link in $links){if(Test-Path -LiteralPath $link){$shortcut=$shell.CreateShortcut($link);if($shortcut.TargetPath -eq (Join-Path $installRoot 'ServerManager.exe')){Remove-Item -LiteralPath $link}}}
if($manifest.skillDirectory){
    $skillRoot=[IO.Path]::GetFullPath($manifest.skillDirectory)
    $bindingFile=Join-Path $skillRoot 'application.json'
    if((Split-Path $skillRoot -Leaf) -eq 'windows-server-access' -and (Test-Path -LiteralPath $bindingFile)){
        $binding=Get-Content -LiteralPath $bindingFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if($binding.product -eq 'Windows-ServerManager' -and $binding.installDirectory -eq $installRoot){Remove-Item -LiteralPath $skillRoot -Recurse -Force}
    }
}
Write-Output '连接配置、Windows 凭据与原有 SSH 配置已保留。'
Remove-Item -LiteralPath $installRoot -Recurse -Force
