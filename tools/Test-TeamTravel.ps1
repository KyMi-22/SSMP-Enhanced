$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$ssmp=Join-Path $root 'upstream-release\silksong-coop-mod-0.4.142\SSMP\SSMP.dll'
if(-not (Test-Path -LiteralPath $ssmp)){$ssmp=Join-Path $root '..\..\BepInEx\plugins\SSMP\SSMP.dll'}
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\TeamTravelTests.dll"'),('/reference:"'+$ssmp+'"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src\HostAnchor.cs','src\Appearance.cs','src\AppearanceWire.cs','src\RosaryLedger.cs','src\RosaryMessage.cs','src\EquipmentMessage.cs','src\SharedMapProgress.cs','src\SharedMapMessage.cs','src\HostSnapshotMessage.cs','src\TeamTravelMessage.cs','tests\TeamTravelTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'travel-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\travel-tests.rsp"
if($LASTEXITCODE -ne 0){throw 'Travel tests did not compile'}
Copy-Item -LiteralPath $ssmp -Destination (Join-Path $out 'SSMP.dll') -Force
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'TeamTravelTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'TeamTravelTests.dll')
if($LASTEXITCODE -ne 0){throw 'Travel tests failed'}
