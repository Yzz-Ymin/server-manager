param(
    [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
    [Parameter(Mandatory=$true)][uri]$TimestampServer,
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [string]$SignTool='signtool.exe',
    [switch]$LocalMachine
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$store=if($LocalMachine){'Cert:\LocalMachine\My'}else{'Cert:\CurrentUser\My'}
$certificate=Get-Item -LiteralPath (Join-Path $store $CertificateThumbprint) -ErrorAction Stop
if(-not $certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date)){throw '代码签名证书缺少私钥或已过期。'}
if(-not($certificate.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')){throw '证书不具备代码签名用途。'}
$tool=Get-Command $SignTool -ErrorAction Stop
$binaries=Join-Path $projectRoot "artifacts\bin\$Configuration"
$files=Get-ChildItem -LiteralPath $binaries -File | Where-Object {$_.Extension -in '.exe','.dll'}
foreach($file in $files){
    $arguments=@('sign','/s','My','/sha1',$CertificateThumbprint,'/fd','SHA256','/tr',$TimestampServer.AbsoluteUri,'/td','SHA256')
    if($LocalMachine){$arguments+='/sm'}
    $arguments+=$file.FullName
    & $tool.Source @arguments
    if($LASTEXITCODE){throw "签名失败：$($file.Name)"}
    & $tool.Source verify /pa $file.FullName
    if($LASTEXITCODE){throw "签名验证失败：$($file.Name)"}
}
Write-Output '程序与测试程序集已签名并验证。证书私钥未导出。'
