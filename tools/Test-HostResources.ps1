param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$ssmp=Join-Path $out 'SSMP.dll'
$newtonsoft=Join-Path $GameRoot 'Hollow Knight Silksong_Data\Managed\Newtonsoft.Json.dll'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\HostResourceTests.dll"'),('/reference:"'+$ssmp+'"'),('/reference:"'+$newtonsoft+'"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src\HostResourceSync.cs','src\ProgressSyncCatalog.cs','tests\HostResourceTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'host-resource-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\host-resource-tests.rsp"
if($LASTEXITCODE -ne 0){throw 'Host resource tests did not compile'}
Copy-Item -LiteralPath $newtonsoft -Destination (Join-Path $out 'Newtonsoft.Json.dll') -Force
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'HostResourceTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'HostResourceTests.dll')
if($LASTEXITCODE -ne 0){throw 'Host resource tests failed'}
