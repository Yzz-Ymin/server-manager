param([string]$Python='python',[ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$SkipSsh)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
$binaries=Join-Path $projectRoot "artifacts\bin\$Configuration"
$testRoot=Join-Path $projectRoot 'artifacts\tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$uiResult=& (Join-Path $binaries 'ServerManager.UiTests.exe') (Join-Path $testRoot 'ui')
$uiExit=$LASTEXITCODE
$uiResult | Tee-Object -FilePath (Join-Path $testRoot 'ui-results.txt')
if($uiExit){throw 'UI 状态测试失败。'}
if(-not $SkipSsh){
    & $Python (Join-Path $projectRoot 'tests\ssh_server.py') --configuration $Configuration
    if($LASTEXITCODE){throw 'SSH 集成测试失败。'}
}
