$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$outDir=Join-Path $taskRoot 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$argsList=@('/nostdlib+','/target:exe','/langversion:latest',"/out:`"$outDir\PlayerMotionTests.dll`"")
$argsList+=Get-ChildItem -LiteralPath $refs -Filter '*.dll' | ForEach-Object {"/reference:`"$($_.FullName)`""}
$argsList+=@('src\PlayerMotionFilter.cs','src\DispatchDelayMeter.cs','tests\PlayerMotionBaseline.cs','tests\PlayerMotionTests.cs') | ForEach-Object {'"'+(Join-Path $taskRoot $_)+'"'}
$rsp=Join-Path $outDir 'player-motion-tests.rsp'
$argsList | Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$rsp"
if($LASTEXITCODE -ne 0){throw 'Movement tests failed to compile'}
Copy-Item -LiteralPath (Join-Path $outDir 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $outDir 'PlayerMotionTests.runtimeconfig.json') -Force
& dotnet (Join-Path $outDir 'PlayerMotionTests.dll')
if($LASTEXITCODE -ne 0){throw 'Movement tests failed'}
