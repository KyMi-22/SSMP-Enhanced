param([string]$GameRoot='D:\Game\Hollow Knight Silksong',[string]$StageRoot)
$ErrorActionPreference='Stop'
if(-not $StageRoot){$StageRoot=Join-Path (Split-Path -Parent $PSScriptRoot) 'dist/Silksong-Coop-Enhanced-0.2.8-beta'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$spec=@{
 'Customizer.dll'=@{'Customizer.Customizer'=@('GetTexturePacks')}
 'CustomizerT2D.dll'=@{'CustomizerT2DOnly.CustomizerT2DOnly'=@('TrySwap','BuildOverrideMap')}
}
foreach($name in $spec.Keys){
 $asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $StageRoot ('BepInEx/plugins/SkinLoader/'+$name)))
 try{foreach($typeName in $spec[$name].Keys){$type=$asm.MainModule.GetType($typeName);foreach($method in $spec[$name][$typeName]){if($method -notin $type.Methods.Name){throw ('Missing loader binding: '+$method)}}
 if($name -eq 'CustomizerT2D.dll'){
  foreach($field in @('_map','_keys','_replacedIds','_notOurTex','_satisfiedKeys')){if($field -notin $type.Fields.Name){throw ('Missing loader field: '+$field)}}
  $swap=$type.Methods|Where-Object Name -EQ 'TrySwap';if($swap.ReturnType.FullName -ne 'System.Boolean' -or $swap.Parameters[0].ParameterType.FullName -ne 'UnityEngine.Texture2D'){throw 'Unexpected TrySwap signature'}
  $map=$type.Fields|Where-Object Name -EQ '_map';if($map.FieldType.FullName -ne 'System.Collections.Generic.Dictionary`2<System.String,System.ValueTuple`3<System.Byte[],System.Int32,System.Int32>>'){throw 'Unexpected image map shape'}
 }}}finally{$asm.Dispose()}
}
$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $StageRoot 'BepInEx/plugins/SSMP/SSMP.dll'))
try{
 foreach($pair in @(@('SSMP.Game.Client.Skin.OwnSkin','Choose'),@('SSMP.Game.Client.Skin.OwnSkin','DressLoaded'),@('SSMP.Game.Client.Skin.OwnSkin','LetGo'),@('SSMP.Game.Client.Skin.CustomizerT2D','AfterSkinsChosen'),@('SSMP.Game.Client.Skin.AtlasSlashes','Refresh'),@('SSMP.Game.Client.ClientManager','SendSkinLook'))){
  $method=@($asm.MainModule.GetType($pair[0]).Methods|Where-Object Name -EQ $pair[1]);if($method.Count -ne 1){throw ('Ambiguous native target: '+$pair[1])}
  if($pair[1] -in @('Refresh','SendSkinLook','DressLoaded','LetGo') -and $method[0].Parameters.Count -ne 0){throw 'Unexpected target parameters'}
 }
 $choice=$asm.MainModule.GetType('SSMP.Game.Client.Skin.OwnSkin').Methods|Where-Object Name -EQ 'Choose'
 if(-not $choice.IsStatic -or $choice.Parameters.Count -ne 1 -or $choice.Parameters[0].ParameterType.FullName -ne 'SSMP.Game.Client.Skin.SkinPack'){throw 'Recheck native menu choice interception'}
 $type=$asm.MainModule.GetType('SSMP.Game.Client.Skin.CustomizerT2D');foreach($field in @('Dressed','Decided','NotDressed','ImageOfPage')){if($field -notin $type.Fields.Name){throw ('Missing native cache: '+$field)}}
 $after=$type.Methods|Where-Object Name -EQ 'AfterSkinsChosen';if($after.Parameters[0].ParameterType.FullName -ne 'System.Collections.Generic.List`1<System.String>&'){throw 'Unexpected directory callback signature'}
}finally{$asm.Dispose()}
$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data/Managed/UnityEngine.CoreModule.dll'))
try{
 $type=$asm.MainModule.GetType('UnityEngine.Texture2D');$method=@($type.Methods|Where-Object {$_.Name -eq 'Reinitialize' -and $_.Parameters.Count -eq 4 -and $_.Parameters[2].ParameterType.Name -eq 'GraphicsFormat'})
 if($method.Count -ne 1 -or @($method[0].Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_isReadable'}).Count -ne 1){throw 'Recheck texture reinitialization readability contract'}
}finally{$asm.Dispose()}
$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data/Managed/UnityEngine.UI.dll'))
try{
 $graphic=$asm.MainModule.GetType('UnityEngine.UI.Graphic');$enable=@($graphic.Methods|Where-Object Name -EQ 'OnEnable')
 if($enable.Count -ne 1 -or $enable[0].Parameters.Count -ne 0 -or $enable[0].ReturnType.FullName -ne 'System.Void'){throw 'Recheck UI activation target'}
 foreach($pair in @(@('UnityEngine.UI.Image','UnityEngine.UI.MaskableGraphic'),@('UnityEngine.UI.MaskableGraphic','UnityEngine.UI.Graphic'))){
  $enable=$asm.MainModule.GetType($pair[0]).Methods|Where-Object Name -EQ 'OnEnable'
  if(@($enable.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'OnEnable' -and $_.Operand.DeclaringType.FullName -eq $pair[1]}).Count -ne 1){throw 'Recheck UI OnEnable inheritance chain'}
 }
}finally{$asm.Dispose()}
$assistPath=Join-Path (Split-Path -Parent $PSScriptRoot) 'build/Silksong.CoopAssist.dll'
$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($assistPath)
try{
 $type=$asm.MainModule.GetType('Silksong.CoopAssist.SkinHotReload')
 if('BeforeChoose' -in $type.Methods.Name -or 'AfterChoose' -notin $type.Methods.Name){throw 'Native choice cancellation regression'}
 $restore=$type.Methods|Where-Object Name -EQ 'Restore'
 $restoreCalls=@($restore.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 if('Reinitialize' -in $restoreCalls -or 'LoadImage' -in $restoreCalls){throw 'Unreadable original-texture restore regression'}
 if('RestoreConverted' -notin $restoreCalls -or 'CopyTexture' -notin $restoreCalls){throw 'GPU-only restore paths missing'}
 $convert=$type.Methods|Where-Object Name -EQ 'RestoreConverted'
 $convertCalls=@($convert.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 foreach($required in @('GetTemporary','Blit','GenerateMips','CopyTexture','ReleaseTemporary','get_sRGBWrite','set_sRGBWrite','get_active','set_active')){if($required -notin $convertCalls){throw ('GPU conversion contract missing '+$required)}}
 if(-not @($convert.Body.ExceptionHandlers|Where-Object HandlerType -EQ 'Finally').Count){throw 'Temporary render texture cleanup is not protected by finally'}
 $gate=$asm.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin').Methods|Where-Object Name -EQ 'get_CanChangeSkin'
 $calls=@($gate.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 if('get_SkinTransition' -notin $calls -or 'get_Down' -in $calls -or 'dead' -in @($gate.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference]}|ForEach-Object {$_.Operand.Name})){throw 'Skin selector incorrectly depends on death/rescue state'}
}finally{$asm.Dispose()}
'Passed shipped loader/skin/UI bindings, non-cancelling choice route and GPU-only original restore with all-mip conversion and render-state cleanup. Static only.'

