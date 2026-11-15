$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$out=Join-Path $taskRoot 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\NetworkDiagnosticsTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src\PeerFlowMetrics.cs','src\DispatchDelayMeter.cs','tests\NetworkDiagnosticsTests.cs')|ForEach-Object{'"'+(Join-Path $taskRoot $_)+'"'}
$response=Join-Path $out 'network-tests.rsp'
$options|Set-Content -LiteralPath $response -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$response"
if($LASTEXITCODE -ne 0){throw 'Network tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'NetworkDiagnosticsTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'NetworkDiagnosticsTests.dll')
if($LASTEXITCODE -ne 0){throw 'Network tests failed'}
