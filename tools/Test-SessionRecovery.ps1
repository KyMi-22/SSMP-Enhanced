param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\SessionRecoveryTests.dll"'),('/reference:"'+$out+'\SSMP.dll"'),('/reference:"'+$out+'\Newtonsoft.Json.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src/CheckpointSchedule.cs','src/Waypoint.cs','src/HostSnapshotMessage.cs','src/HostResourceSync.cs','src/HostWorldSync.cs','src/ProgressSyncCatalog.cs','src/HostAnchor.cs','src/Appearance.cs','src/AppearanceWire.cs','src\RosaryLedger.cs','src\RosaryMessage.cs','src\EquipmentMessage.cs','src/SharedMapProgress.cs','src/SharedMapMessage.cs','src/TeamTravelMessage.cs','tests/SessionRecoveryTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'session-recovery.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\session-recovery.rsp"
if($LASTEXITCODE -ne 0){throw 'Session recovery tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'SessionRecoveryTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'SessionRecoveryTests.dll')
if($LASTEXITCODE -ne 0){throw 'Session recovery tests failed'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'SSMP.dll'))
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'Silksong.CoopAssist.dll'))
try {
 $init=$assist.MainModule.GetType('Silksong.CoopAssist.AssistServer').Methods|Where-Object Name -EQ 'Initialize'
 $receivers=@($init.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetNetworkReceiver'})
 if($receivers.Count -ne 1){throw 'Repeated GetNetworkReceiver clears the native packet instantiator'}
 $save=$native.MainModule.GetType('SSMP.Game.Client.Save.CoopSave')
 foreach($name in @('GetStoryFields','LogStoryReadersNow','EndRescueWait','OnClearSaveFile','TakeHostSave','IsGameSaving','GetSaveFilePath','GetSlotKey','ResetSession')) {
  if(@($save.Methods|Where-Object Name -EQ $name).Count -ne 1){throw "Native hook changed: $name"}
 }
 $bridge=$assist.MainModule.GetType('Silksong.CoopAssist.NativeSaveBridge')
 if('BackUp' -notin $bridge.Methods.Name){throw 'Manual replacement must require a confirmed backup'}
 'Passed single receiver acquisition and new native save/rescue/story bindings. Static only.'
 $plugin=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
 $hooks=@(($plugin.Methods|Where-Object Name -EQ ConfigureWaypoints).Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 if('OnClearSaveFile' -notin $hooks -or @($hooks|Where-Object {$_ -in @('ReadyForRespawn','Respawn','HeroRespawned','EndRescueWait')}).Count){throw 'Waypoints may hook save deletion but must be separate from death and rescue'}
 $constructors=@($plugin.Methods|Where-Object HasBody|Where-Object {
   @($_.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'newobj' -and $_.Operand.DeclaringType.Name -eq 'WaypointTrip'}).Count
 })
 if($constructors.Count -ne 1 -or $constructors[0].Name -ne 'StartWaypoint'){throw 'Only a manual request may start a waypoint trip'}
 $tickCalls=@(($plugin.Methods|Where-Object Name -EQ AdvanceWaypoint).Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 foreach($name in @('CanContinue','SendEvent','SafeAt','PlaceHeroAt','BeginSceneTransition')){if($name -notin $tickCalls){throw "Waypoint flow missing native stage: $name"}}
 if('RegainControl' -in $tickCalls -or 'set_atBench' -in $tickCalls){throw 'Waypoint travel must use native bench get-up'}
 $flags=@($save.Methods|Where-Object Name -EQ ApplyInteractionFlags)
 if($flags.Count -ne 1 -or $flags[0].Parameters[0].ParameterType.Name -ne 'CoopSaveUpdate' -or $flags[0].ReturnType.Name -ne 'Int32'){throw 'Native story flag handler changed'}
 $switch=@($save.Methods|Where-Object Name -EQ OnInteractionSwitchState)
 if($switch.Count -ne 1 -or $switch[0].Parameters[1].ParameterType.Name -ne 'Fsm' -or $switch[0].Parameters[2].ParameterType.Name -ne 'FsmState'){throw 'Native story state switch changed'}
 $judge=@(($plugin.Methods|Where-Object Name -EQ TickGrandGateProgress).Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 foreach($name in @('Boss Scene','Control','Battle','Last Judge','Wait for enter','Defeated')){if($name -notin $judge){throw "Grand Gate arena binding missing: $name"}}
 if(@($judge|Where-Object {$_ -in @('JUDGE DEFEATED','TEST_GATE','CINEMATIC END')}).Count){throw 'Completed gate refresh must not send cinematic events'}
 $redirect=@(($plugin.Methods|Where-Object Name -EQ RedirectCompletedGate).Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 foreach($name in @('Gate Open Scene','Gate Open To Cinematic','Start Fall','Start Refight','Defeated')){if($name -notin $redirect){throw "Completed gate must handle native state: $name"}}
 $repair=$assist.MainModule.GetType('Silksong.CoopAssist.HostSyncRepairs')
 $story=@(($repair.Methods|Where-Object Name -EQ CompleteStoryFields).Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 if('StoryRecordNames' -notin $story){throw 'Grand Gate records must participate in the native initial story check'}
 'Passed manual waypoint and completed Grand Gate bindings. Static only.'
} finally {$native.Dispose();$assist.Dispose()}
