$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\NameLabelStyleTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src/NameLabelStyle.cs','tests/NameLabelStyleTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'name-style-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\name-style-tests.rsp"
if($LASTEXITCODE -ne 0){throw 'Name presentation tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'NameGlyphTests.runtimeconfig.json') -Destination (Join-Path $out 'NameLabelStyleTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'NameLabelStyleTests.dll')
if($LASTEXITCODE -ne 0){throw 'Name presentation tests failed'}
Add-Type -Path 'D:/Game/Hollow Knight Silksong/BepInEx/core/Mono.Cecil.dll'
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'Silksong.CoopAssist.dll'))
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'upstream-release/silksong-coop-mod-0.4.142/SSMP/SSMP.dll'))
try{
 $plugin=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
 function Calls($type,$name){@(($type.Methods|Where-Object Name -EQ $name).Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})}
 foreach($pair in @(@('LateUpdate','TickEnhancements'),@('TickEnhancements','UpdateEnhancements'),@('UpdateEnhancements','StyleName'),@('UpdateEnhancements','ApplyWeight'),@('UpdateEnhancements','RefreshOwnName'),@('ApplyPeerNameColour','StyleName'),@('ApplyPeerNameColour','ApplyWeight'))){
  if($pair[1] -notin (Calls $plugin $pair[0])){throw "Name reconciliation disconnected: $($pair -join ' -> ')"}
 }
 if('UpdateEnhancements' -in (Calls $plugin 'Update')){throw 'Name presentation must not depend on the co-op save update path'}
 $fonts=$assist.MainModule.GetType('Silksong.CoopAssist.NameFontRepairs')
 if('GetComponentsInChildren' -notin (Calls $fonts 'AfterNamed') -or 'Find' -in (Calls $fonts 'AfterNamed')){throw 'Nested/rebuilt labels must not depend on a direct child path'}
 $select=$plugin.Methods|Where-Object Name -EQ SelectNameColour
 $strings=@($select.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 foreach($field in @('OwnNameColour','ColourOwnName','ColourFromCloak')){if($field -notin $strings){throw 'Explicit colour selection leaves a stale colour source'}}
 $white=$fonts.Methods|Where-Object Name -EQ WhiteNameColour
 if('white' -notin @($white.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)){throw 'White identity must not fall through to native yellow'}
 $own=$native.MainModule.GetType('SSMP.Game.Client.PlayerManager').Methods|Where-Object Name -EQ OwnNameColour
 if($own.Parameters.Count -ne 1 -or $own.ReturnType.FullName -ne 'UnityEngine.Color'){throw 'Native white-colour hook signature changed'}
 foreach($name in @('AddNameToPlayer','ChangeNameColor','DrawAsOwnName')){
  if(@($native.MainModule.GetType('SSMP.Game.Client.PlayerManager').Methods|Where-Object Name -EQ $name).Count -ne 1){throw 'Native name lifecycle hook changed'}
 }
 $server=$assist.MainModule.GetType('Silksong.CoopAssist.AssistServer')
 $client=$assist.MainModule.GetType('Silksong.CoopAssist.AssistClient')
 foreach($type in @($server,$client)){
  $styleHandler=@(($type.Methods|Where-Object Name -EQ Initialize).Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'RegisterPacketHandler' -and $_.Operand.GenericArguments.FullName -contains 'Silksong.CoopAssist.NameStyleMessage'})
  if($styleHandler.Count -ne 1){throw 'Name style sender/receiver handler missing'}
 }
 'Passed native name lifecycle, independent polling, shared style application, white identity and style network bindings. Static only.'
}finally{$assist.Dispose();$native.Dispose()}
