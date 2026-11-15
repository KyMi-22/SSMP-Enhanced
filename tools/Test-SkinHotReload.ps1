param([string]$SkinRoot)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$outDir=Join-Path $taskRoot 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$outDir+'\SkinHotReloadTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object {'/reference:"'+$_.FullName+'"'}
$options+=@('src/SkinReloadPlan.cs','src/SkinSelection.cs','src/SkinHotReload.cs','tests/SkinHotReloadHarness.cs')|ForEach-Object {'"'+(Join-Path $taskRoot $_)+'"'}
$rsp=Join-Path $outDir 'skin-hotreload-tests.rsp';$options|Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$rsp"
if($LASTEXITCODE -ne 0){throw 'Skin test compilation failed'}
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.17"}}}'|Set-Content -LiteralPath (Join-Path $outDir 'SkinHotReloadTests.runtimeconfig.json')
if($SkinRoot){& dotnet (Join-Path $outDir 'SkinHotReloadTests.dll') $SkinRoot}
else{& dotnet (Join-Path $outDir 'SkinHotReloadTests.dll')}
if($LASTEXITCODE -ne 0){throw 'Skin transaction checks failed'}
