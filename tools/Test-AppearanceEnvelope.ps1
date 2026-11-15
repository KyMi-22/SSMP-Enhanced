$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$outDir=Join-Path $taskRoot 'build'
$ssmp=Join-Path $taskRoot 'upstream-release\silksong-coop-mod-0.4.142\SSMP\SSMP.dll'
if(-not(Test-Path -LiteralPath $ssmp)){$ssmp=Join-Path $taskRoot '..\..\BepInEx\plugins\SSMP\SSMP.dll'}
New-Item -ItemType Directory -Path $outDir -Force|Out-Null
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$opts=@('/nostdlib+','/target:exe','/langversion:latest',"/out:`"$outDir\AppearanceEnvelopeTests.dll`"","/reference:`"$ssmp`"")
$opts+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$opts+=@('src\HostAnchor.cs','src\TeamTravelMessage.cs','src\Appearance.cs','src\AppearanceWire.cs','src\SharedMapProgress.cs','src\SharedMapMessage.cs','src\HostSnapshotMessage.cs','src\RosaryLedger.cs','src\RosaryMessage.cs','src\EquipmentMessage.cs','tests\AppearanceEnvelopeTests.cs')|ForEach-Object{'"'+(Join-Path $taskRoot $_)+'"'}
$rsp=Join-Path $outDir 'appearance-envelope.rsp'
$opts|Set-Content -LiteralPath $rsp -Encoding UTF8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$rsp"
if($LASTEXITCODE -ne 0){throw 'Envelope test compilation failed'}
Copy-Item -LiteralPath $ssmp -Destination (Join-Path $outDir 'SSMP.dll') -Force
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.17"}}}'|Set-Content -LiteralPath (Join-Path $outDir 'AppearanceEnvelopeTests.runtimeconfig.json')
& dotnet (Join-Path $outDir 'AppearanceEnvelopeTests.dll')
if($LASTEXITCODE -ne 0){throw 'Envelope regression failed'}
