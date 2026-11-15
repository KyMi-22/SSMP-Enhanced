param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll')
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx\plugins\SSMP\SSMP.dll'))
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'build\Silksong.CoopAssist.dll'))
try{
 $save=$native.MainModule.GetType('SSMP.Game.Client.Save.CoopSave')
 $spec=@{
  Hold=@('HeroController','SSMP.Game.Client.Save.CoopSaveMarker','System.Collections.Generic.List`1<SSMP.Game.Client.ClientPlayerData>')
  ReleaseHold=@('HeroController')
  UpdateHoldPrompt=@()
  TryOpen=@('UnityEngine.UI.SaveSlotButton','UnityEngine.EventSystems.BaseEventData','SSMP.Game.Client.Save.CoopSaveMarker')
  IsGroupComplete=@('SSMP.Game.Client.Save.CoopSaveMarker','System.Collections.Generic.List`1<SSMP.Game.Client.ClientPlayerData>')
  HasAllMembers=@('SSMP.Game.Client.Save.CoopSaveMarker')
  FindMembers=@('SSMP.Game.Client.Save.CoopSaveMarker')
  HasUncheckedMember=@('System.Collections.Generic.List`1<SSMP.Game.Client.ClientPlayerData>')
 }
 foreach($name in $spec.Keys){
  $method=@($save.Methods|Where-Object Name -EQ $name)
  if($method.Count -ne 1 -or (($method[0].Parameters|ForEach-Object {$_.ParameterType.FullName}) -join '|') -ne ($spec[$name] -join '|')){throw "Native session signature changed: $name"}
 }
 $plugin=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
 $freedom=$assist.MainModule.GetType('Silksong.CoopAssist.SessionFreedom')
 $nohold=$freedom.Methods|Where-Object Name -EQ 'NoHold'
 $calls=@($nohold.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 if('Invoke' -notin $calls -or 'RegainControl' -in $calls){throw 'Must use native owner-aware ReleaseHold'}
 foreach($name in @('NoHold','ReleaseBeforePrompt','OpenWithoutRoster','OnlineGroup','OnlineMembers')){
  if($name -notin @(($freedom.Methods|Where-Object Name -EQ 'Install').Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)){throw "Missing session patch: $name"}
 }
 $manager=$native.MainModule.GetType('SSMP.Game.Client.ClientManager')
 foreach($name in @('OnClientConnect','OpenSaveAfterReady')){if(@($manager.Methods|Where-Object Name -EQ $name).Count -ne 1){throw "Native ready-gate target changed: $name"}}
 $patchStrings=@(($freedom.Methods|Where-Object Name -EQ 'Install').Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 if('OnClientConnect' -in $patchStrings -or 'ChooseSaveWithoutReady' -in $freedom.Methods.Name){throw 'Regression: joining must not bypass the native ready handshake'}
 $earlySaveCalls=@($assist.MainModule.Types|ForEach-Object {$_.Methods}|ForEach-Object {$_.Body.Instructions}|Where-Object {
  ($_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'OpenSaveAfterReady') -or
  ($_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'OpenSaveAfterReady')
 })
 if($earlySaveCalls.Count){throw 'Regression: addon must not directly open the joiner save menu'}
 $initialize=$manager.Methods|Where-Object Name -EQ 'Initialize'
 $nativeReadyWiring=@($initialize.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 foreach($name in @('add_ReadyToggledEvent','add_BothReadyEvent','OpenSaveAfterReady')){if($name -notin $nativeReadyWiring){throw "Native ready-event wiring changed: $name"}}
 $merge=@($native.MainModule.GetType('SSMP.Game.Client.Save.CoopHostSync').Methods|Where-Object Name -EQ 'Merge')
 if($merge.Count -ne 1 -or $merge[0].Parameters.Count -ne 6 -or $merge[0].ReturnType.FullName -ne 'Newtonsoft.Json.Linq.JObject'){throw 'Native host merge signature changed'}
 foreach($name in @('GetJoinWarning','GetJoinSummary','OnJoinSlotSubmit','BackUpSave','OnSetLoadedGameData')){if(@($save.Methods|Where-Object Name -EQ $name).Count -ne 1){throw "Host alignment target missing: $name"}}
 $resourceType=$assist.MainModule.GetType('Silksong.CoopAssist.HostSyncRepairs')
 $after=$resourceType.Methods|Where-Object Name -EQ 'AfterMerge'
 if($after.Parameters[3].ParameterType.FullName -ne 'Newtonsoft.Json.Linq.JObject&'){throw 'Host merge result must be replaced before load'}
 $setLoaded=$save.Methods|Where-Object Name -EQ 'OnSetLoadedGameData'
 if('Merged' -notin @($setLoaded.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name.TrimStart('get_')})){throw 'Native loader no longer consumes the aligned pending snapshot'}
 $allCalls=@($freedom.Methods|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 if('RemoveMarker' -in $allCalls -or 'Clear' -in $allCalls -or 'RegainControl' -in $allCalls){throw 'Session patch must preserve roster/check/death ownership'}
 $init=$freedom.Methods|Where-Object Name -EQ '.cctor'
 if('HasUncheckedMember' -notin @($init.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)){throw 'Native online-member checks must be preserved'}
 $bench=$plugin.Methods|Where-Object Name -EQ 'ApplyBenchBinding'
 $benchCalls=@($bench.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 foreach($name in @('RemoveBinding','AddBinding')){if($name -notin $benchCalls){throw "Native keyboard rebind missing: $name"}}
 if('ClearBindings' -in $benchCalls){throw 'Controller binding must not be cleared'}
 $body=$bench.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.TypeReference]}|ForEach-Object {$_.Operand.FullName}
 if('InControl.DeviceBindingSource' -in $body){throw 'Keyboard rebinding must not remove the controller binding'}
 $answer=$plugin.Methods|Where-Object Name -EQ 'AnswerTravel'
 $finish=$plugin.Methods|Where-Object Name -EQ 'FinishTeamTravel'
 foreach($method in @($answer,$finish)){
  if('SafeAt' -notin @($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})){throw 'Destination must pass local hazard/terrain checks'}
 }
 $finishCalls=@($finish.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 foreach($name in @('GateExists','BeginSceneTransition','get_IsInLocalScene','MemberDown')){if($name -notin $finishCalls){throw "Missing cross-scene target check: $name"}}
 if('AddTravelMenu' -notin @(($plugin.Methods|Where-Object Name -EQ 'BuildCustomMenu').Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})){throw 'Custom controls are not exposed in ModMenu'}
 'Passed pinned session signatures, patch registrations, preserved online checks/controller/death ownership, travel safety and ModMenu bindings. Static only.'
}finally{$native.Dispose();$assist.Dispose()}
