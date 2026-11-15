param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\TeamControlTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src/TeamControlPolicy.cs','tests/TeamControlTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'controls-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\controls-tests.rsp"
if($LASTEXITCODE -ne 0){throw 'Control tests did not compile'}
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.17"}}}'|Set-Content -LiteralPath (Join-Path $out 'TeamControlTests.runtimeconfig.json')
& dotnet (Join-Path $out 'TeamControlTests.dll')
if($LASTEXITCODE -ne 0){throw 'Control tests failed'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'Silksong.CoopAssist.dll'))
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SSMP/SSMP.dll'))
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data/Managed/Assembly-CSharp.dll'))
try{
    $plugin=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
    function Calls($name){
        @((($plugin.Methods|Where-Object Name -EQ $name).Body.Instructions)|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    }
    foreach($pair in @(@('OpenTravelPicker','AddInputBlocker'),@('CloseTravelPicker','RemoveInputBlocker'),@('OnDestroy','CloseTravelPicker'),@('ClearTeamTravel','CloseTravelPicker'),@('Update','CloseTravelPicker'),@('ConfirmPicker','NearestTravelPeer'),@('ConfirmPicker','RequestTeamTravel'),@('LateUpdate','TickAssistHotkeys'),@('TickTeamTravel','TickTravelPicker'),@('DrawOverlay','DrawTravelPicker'),@('ShowMenuTravelPicker','Show'),@('TickMenuTravel','RequestTeamTravel'),@('AcceptBinding','CanAssign'),@('AcceptBinding','ApplyBenchBinding'),@('BeforePickerPause','SkipPickerPause'),@('ConfigureTeamTravel','InstallTravelPickerInput'))){
        if($pair[1] -notin (Calls $pair[0])){throw "Missing control integration: $($pair -join ' -> ')"}
    }
    foreach($name in @('OpenTravelPicker','CloseTravelPicker','TickTravelPicker','ConfirmPicker')){
        $calls=Calls $name
        if('RegainControl' -in $calls -or 'RelinquishControl' -in $calls -or 'Clear' -in $calls){throw 'Picker must retain native control ownership'}
    }
    if('RequestTeamTravel' -in (Calls 'OpenTravelPicker') -or 'RequestTeamTravel' -in (Calls 'TickAssistHotkeys')){throw 'Hotkey must open selector, never directly teleport'}
    $strings=@(($plugin.Methods|Where-Object Name -EQ 'ConfigureAssistBindings').Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
    foreach($key in @('TeleportKey','ReturnToBenchKey','ResyncKey','SetReturnPointKey','ClearReturnPointKey','UseWaypointKey')){if($key -notin $strings){throw "Missing configurable action $key"}}
    $cocoonTick=$plugin.Methods|Where-Object Name -EQ TickCocoonView
    $mapInputs=@($cocoonTick.Body.Instructions|Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.DeclaringType.Name -eq 'HeroActions'
    }|ForEach-Object {$_.Operand.Name})
    if('QuickMap' -notin $mapInputs -or 'get_WasPressed' -notin (Calls 'TickCocoonView') -or
        'get_IsPressed' -notin (Calls 'TickCocoonView')){throw 'Cocoon map must follow the native action press and release'}
    if('SendEvent' -notin (Calls 'OpenCocoonMap') -or 'SendEvent' -notin (Calls 'CloseCocoonMap')){
        throw 'Cocoon map must use the native HUD open and close flow'
    }
    if('get_IsInLocalScene' -notin (Calls 'CanWatchCocoonPeer')){
        throw 'Cocoon roster must be limited to living teammates in the local scene'
    }
    foreach($retired in @('CocoonWatchRequest','CocoonFramePiece','CocoonFrameBuffer','CocoonViewRelay','CocoonWatchRoutes')){
        if($null -ne $assist.MainModule.GetType('Silksong.CoopAssist.'+$retired)){
            throw "Retired cross-scene type is still shipped: $retired"
        }
    }
    $ids=@($assist.MainModule.GetType('Silksong.CoopAssist.AssistPacket').Fields|Where-Object {$_.HasConstant}).Count
    if($ids -ne 8){throw 'The addon packet list must stay at the eight local-scene, travel and economy ids'}
    if('GetComponentsInChildren' -notin (Calls 'AcquireWaitingVignette') -or 'set_enabled' -notin (Calls 'AcquireWaitingVignette')){
        throw 'The waiting view must read and stop the hero vignette renderers it found'
    }
    if('set_enabled' -notin (Calls 'HideWaitingVignette') -or 'set_enabled' -notin (Calls 'ShowWaitingVignette')){
        throw 'The waiting view must hide the vignette for the whole wait and switch it back afterwards'
    }
    if('HideWaitingVignette' -notin (Calls 'TickCocoonView') -or 'ShowWaitingVignette' -notin (Calls 'TickCocoonView')){
        throw 'TickCocoonView must both hide the vignette and put it back when the wait ends'
    }
    foreach($method in @('TickCocoonView','OpenCocoonMap','CloseCocoonMap','AcquireWaitingVignette','HideWaitingVignette')){
        if(@(Calls $method | Where-Object {$_ -in @('BeginSceneTransition','RelinquishControl','RegainControl','PlaceHeroAt')}).Count){
            throw "Cocoon view cannot move or take control of the local hero: $method"
        }
    }
    $position=@($native.MainModule.GetType('SSMP.Game.Client.MapManager').Methods|Where-Object Name -EQ TryGetMapPosition)
    if($position.Count -ne 1 -or $position[0].Parameters[0].ParameterType.FullName -ne 'UnityEngine.Vector2&'){throw 'Nearest-map bridge signature changed'}
    $projection=@($game.MainModule.GetType('GameMap').Methods|Where-Object Name -EQ GetMapPosition)
    if($projection.Count -ne 1 -or ($projection[0].Parameters.ParameterType.FullName -join '|') -ne 'UnityEngine.Vector2|GameMapScene|UnityEngine.GameObject|UnityEngine.Vector2|UnityEngine.Vector2'){throw 'World-to-map coordinate projection signature changed'}
    if('UnityEngine.Vector2' -ne ($game.MainModule.GetType('GameMap').Fields|Where-Object Name -EQ currentSceneSize).FieldType.FullName){throw 'Map scene-size field changed'}
    foreach($name in @('AddInputBlocker','RemoveInputBlocker')){
        $method=@($game.MainModule.GetType('HeroController').Methods|Where-Object Name -EQ $name)
        if($method.Count -ne 1 -or $method[0].Parameters[0].ParameterType.FullName -ne 'System.Object'){throw 'Owner-aware input blocker API changed'}
    }
    $pause=@($game.MainModule.GetType('GameManager').Methods|Where-Object Name -EQ PauseGameToggle)
    if($pause.Count -ne 1 -or $pause[0].Parameters[0].ParameterType.FullName -ne 'System.Boolean' -or $pause[0].ReturnType.FullName -ne 'System.Collections.IEnumerator'){throw 'Picker Esc pause target changed'}
    $cursor=@($game.MainModule.GetType('InputHandler').Methods|Where-Object Name -EQ SetCursorVisible)
    if($cursor.Count -ne 1 -or $cursor[0].Parameters[0].ParameterType.FullName -ne 'System.Boolean'){throw 'Native cursor hook changed'}
    'Passed native key binding, modal lifecycle, menu resume, selection-only trigger and map bridge checks. Static only.'
}finally{$assist.Dispose();$native.Dispose();$game.Dispose()}
