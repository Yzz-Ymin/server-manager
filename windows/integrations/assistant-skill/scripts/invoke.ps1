param([switch]$Desktop,[Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
$ErrorActionPreference='Stop'
$bindingPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'application.json'
if(-not(Test-Path -LiteralPath $bindingPath)){throw '缺少应用绑定，请重新运行安装脚本并启用助手集成。'}
$binding=Get-Content -LiteralPath $bindingPath -Raw -Encoding UTF8 | ConvertFrom-Json
if($binding.product -ne 'Windows-ServerManager'){throw '应用绑定无效。'}
$name=if($Desktop){'ServerManager.exe'}else{'server-access.exe'}
$executable=Join-Path $binding.installDirectory $name
if(-not(Test-Path -LiteralPath $executable)){throw '应用不存在，请重新安装。'}
if($Desktop){if($Arguments.Count){Start-Process -FilePath $executable -ArgumentList $Arguments}else{Start-Process -FilePath $executable};exit 0}
$OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$request=@($input)
if($request.Count){$request | & $executable @Arguments}else{& $executable @Arguments}
exit $LASTEXITCODE
