$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $taskRoot 'build'
$references = 'C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options = @('/nostdlib+', '/target:exe', '/langversion:latest', "/out:`"$outDir\ArrivalTests.dll`"")
$options += Get-ChildItem $references -Filter '*.dll' | ForEach-Object { "/reference:`"$($_.FullName)`"" }
$options += @('src\ArrivalPolicy.cs','tests\ArrivalTests.cs') | ForEach-Object { '"' + (Join-Path $taskRoot $_) + '"' }
$options | Set-Content (Join-Path $outDir 'test.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$outDir\test.rsp"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.17"}}}' | Set-Content (Join-Path $outDir 'ArrivalTests.runtimeconfig.json')
& dotnet (Join-Path $outDir 'ArrivalTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Policy tests failed' }
$ssmp = Join-Path $taskRoot 'BepInEx\plugins\SSMP\SSMP.dll'
if (-not (Test-Path -LiteralPath $ssmp)) { $ssmp = Join-Path $taskRoot 'upstream-release\silksong-coop-mod-0.4.142\SSMP\SSMP.dll' }
$codec = @('/nostdlib+', '/target:exe', '/langversion:latest', "/out:`"$outDir\CodecTests.dll`"", "/reference:`"$ssmp`"")
$codec += Get-ChildItem $references -Filter '*.dll' | ForEach-Object { "/reference:`"$($_.FullName)`"" }
$codec += @('src\HostAnchor.cs','tests\CodecTests.cs') | ForEach-Object { '"' + (Join-Path $taskRoot $_) + '"' }
$codec | Set-Content (Join-Path $outDir 'codec.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$outDir\codec.rsp"
if ($LASTEXITCODE -ne 0) { throw 'Codec test compilation failed' }
Copy-Item -LiteralPath $ssmp -Destination (Join-Path $outDir 'SSMP.dll') -Force
Copy-Item -LiteralPath (Join-Path $outDir 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $outDir 'CodecTests.runtimeconfig.json') -Force
& dotnet (Join-Path $outDir 'CodecTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Codec tests failed' }
$enhance = @('/nostdlib+', '/target:exe', '/langversion:latest', "/out:`"$outDir\EnhancementTests.dll`"", "/reference:`"$ssmp`"")
$enhance += Get-ChildItem $references -Filter '*.dll' | ForEach-Object { "/reference:`"$($_.FullName)`"" }
$enhance += @('src\ReflectionCache.cs','src\Appearance.cs','src\SkinSelection.cs','tests\EnhancementTests.cs') | ForEach-Object { '"' + (Join-Path $taskRoot $_) + '"' }
$enhance | Set-Content (Join-Path $outDir 'enhancement-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$outDir\enhancement-tests.rsp"
if ($LASTEXITCODE -ne 0) { throw 'Enhancement compilation failed' }
Copy-Item -LiteralPath (Join-Path $outDir 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $outDir 'EnhancementTests.runtimeconfig.json') -Force
& dotnet (Join-Path $outDir 'EnhancementTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Enhancement tests failed' }
& (Join-Path $PSScriptRoot 'Test-AppearanceEnvelope.ps1')
& (Join-Path $PSScriptRoot 'Test-PlayerMotion.ps1')
& (Join-Path $PSScriptRoot 'Test-PlayerMotionBindings.ps1')
& (Join-Path $PSScriptRoot 'Test-FeedbackBindings.ps1')
& (Join-Path $PSScriptRoot 'Test-SkinHotReload.ps1')
& (Join-Path $PSScriptRoot 'Test-NameFonts.ps1')
& (Join-Path $PSScriptRoot 'Test-NamePresentation.ps1')
& (Join-Path $PSScriptRoot 'Test-NetworkDiagnostics.ps1')
& (Join-Path $PSScriptRoot 'Test-NativeFlowBindings.ps1')
& (Join-Path $PSScriptRoot 'Test-ReceiveScratch.ps1')

& (Join-Path $PSScriptRoot 'Test-TeamTravel.ps1')
& (Join-Path $PSScriptRoot 'Test-TeamControls.ps1')
& (Join-Path $PSScriptRoot 'Test-OptionsFeedback.ps1')
& (Join-Path $PSScriptRoot 'Test-SessionBindings.ps1')
& (Join-Path $PSScriptRoot 'Test-HostResources.ps1')

& (Join-Path $PSScriptRoot 'Test-LobbyReady.ps1')
& (Join-Path $PSScriptRoot 'Test-SessionRecovery.ps1')
& (Join-Path $PSScriptRoot 'Test-ProgressConsistency.ps1')
& (Join-Path $PSScriptRoot 'Test-SharedMaps.ps1')
