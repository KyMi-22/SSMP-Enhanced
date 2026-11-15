$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\LobbyReadyTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('tests\NativeWaitingRoomMethods.cs','tests\LobbyReadyTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'lobby-ready-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\lobby-ready-tests.rsp"
if($LASTEXITCODE -ne 0){throw 'Lobby ready tests did not compile'}
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.17"}}}'|Set-Content -LiteralPath (Join-Path $out 'LobbyReadyTests.runtimeconfig.json')
& dotnet (Join-Path $out 'LobbyReadyTests.dll')
if($LASTEXITCODE -ne 0){throw 'Lobby ready regression failed'}
