param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$builder=Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if($builder){$builderPath=$builder.Source}else{
    $framework=if([Environment]::Is64BitOperatingSystem){'Framework64'}else{'Framework'}
    $builderPath=Join-Path $env:WINDIR "Microsoft.NET\$framework\v4.0.30319\MSBuild.exe"
}
if(-not(Test-Path -LiteralPath $builderPath)){throw '未找到 MSBuild。请安装 .NET Framework 4.8 或 Visual Studio .NET 桌面开发工具。'}
$referenceRoot=Join-Path $projectRoot 'artifacts\reference-assemblies'
$referenceFile=Join-Path $referenceRoot 'package\build\.NETFramework\v4.8\mscorlib.dll'
$programFiles=if(${env:ProgramFiles(x86)}){${env:ProgramFiles(x86)}}else{$env:ProgramFiles}
$systemReference=Join-Path $programFiles 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'
if(-not(Test-Path -LiteralPath $referenceFile) -and -not(Test-Path -LiteralPath $systemReference)){
    $dependency=(Get-Content -LiteralPath (Join-Path $projectRoot 'build\dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json).referenceAssemblies
    New-Item -ItemType Directory -Path $referenceRoot -Force | Out-Null
    $archive=Join-Path $referenceRoot 'net48.nupkg'
    if(-not(Test-Path -LiteralPath $archive)){
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $url="https://api.nuget.org/v3-flatcontainer/$($dependency.id)/$($dependency.version)/$($dependency.id).$($dependency.version).nupkg"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
    }
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256){throw '引用程序集校验失败，请删除 artifacts/reference-assemblies/net48.nupkg 后重试。'}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $destination=Join-Path $referenceRoot 'package'
    if(Test-Path -LiteralPath $destination){throw '引用程序集缓存不完整，请删除 artifacts/reference-assemblies/package 后重试。'}
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$destination)
}
& $builderPath (Join-Path $projectRoot 'ServerManager.sln') /nologo /verbosity:minimal /target:Build "/property:Configuration=$Configuration"
if($LASTEXITCODE){throw '构建失败。'}
Write-Output "构建完成：$(Join-Path $projectRoot "artifacts\bin\$Configuration")"
