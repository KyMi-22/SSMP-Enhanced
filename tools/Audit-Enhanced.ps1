param([string]$GameRoot='D:\Game\Hollow Knight Silksong',[string]$StageRoot)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $taskRoot 'dist\Silksong-Coop-Enhanced-0.2.8-beta'
if($StageRoot){$stage=[IO.Path]::GetFullPath($StageRoot)}
Add-Type -Path (Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll')
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $GameRoot 'Hollow Knight Silksong_Data\Managed'))
$resolver.AddSearchDirectory((Join-Path $stage 'BepInEx\core'))
Get-ChildItem -LiteralPath (Join-Path $stage 'BepInEx\plugins') -Directory | ForEach-Object { $resolver.AddSearchDirectory($_.FullName) }
if(Test-Path -LiteralPath (Join-Path $stage 'BepInEx\patchers')){$resolver.AddSearchDirectory((Join-Path $stage 'BepInEx\patchers'))}
$rp=[Mono.Cecil.ReaderParameters]::new();$rp.AssemblyResolver=$resolver
$missing=[Collections.Generic.List[string]]::new();$count=0
$inactive=[Collections.Generic.List[string]]::new()
$utils=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $stage 'BepInEx\core\MonoMod.Utils.dll'),$rp)
$legacyMonoMod=$null -eq $utils.MainModule.GetType('MonoMod.Utils.ArchitectureKind');$utils.Dispose()
$targets=@(Get-ChildItem -LiteralPath (Join-Path $stage 'BepInEx\plugins') -Recurse -Filter '*.dll')
if(Test-Path -LiteralPath (Join-Path $stage 'BepInEx\patchers')){$targets+=@(Get-ChildItem -LiteralPath (Join-Path $stage 'BepInEx\patchers') -Recurse -Filter '*.dll')}
foreach($file in $targets)
{
 $asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($file.FullName,$rp)
 foreach($member in $asm.MainModule.GetMemberReferences())
 {
  $scope=$member.DeclaringType.Scope.Name
  if($scope -notmatch '^(Assembly-CSharp.*|Unity.*|TeamCherry\..*|Newtonsoft\.Json|PlayMaker|BepInEx|0Harmony|SSMP|Silksong\..*|WavLib|com\.github\.MonoDetour.*|Mono\.Cecil.*|MonoMod.*)$'){continue}
  $count++
  $absent=$false
  try{$absent=$null -eq $member.Resolve()}catch{$absent=$true}
  if($absent){
   $detail=$file.Name+': '+$member.FullName
   if($legacyMonoMod -and $file.Name -eq 'com.github.MonoDetour.Bindings.Reorg.dll' -and $scope -like 'MonoMod.*'){$inactive.Add($detail)}
   else{$missing.Add($detail)}
  }
 }
 $asm.Dispose()
}
$ssmp=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $stage 'BepInEx\plugins\SSMP\SSMP.dll'),$rp)
$methods=@{
 'SSMP.Game.Client.Entity.EntityManager'=@('HandleReliableEntityUpdate','BecomeSceneHost','InitializeSceneHost','InitializeSceneClient')
 'SSMP.Game.Client.Entity.Entity'=@('UpdateIsActive')
 'SSMP.Game.Client.ClientManager'=@('OnSceneHostTransfer')
 'SSMP.Fsm.PredictiveInterpolation'=@('AdaptToRTT')
 'SSMP.Game.Client.Skin.OwnSkin'=@('Choices','Choose')
 'SSMP.Game.Client.Skin.CustomizerT2D'=@('AfterSkinsChosen')
 'SSMP.Game.Client.PlayerManager'=@('OwnColour')
}
foreach($typeName in $methods.Keys){$type=$ssmp.MainModule.GetType($typeName);foreach($name in $methods[$typeName]){if($name -notin $type.Methods.Name){$missing.Add("Hook missing: $typeName.$name")}}}
$ssmp.Dispose()
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data\Managed\Assembly-CSharp.dll'),$rp)
$currency=$game.MainModule.GetType('CurrencyObjectBase')
foreach($field in @('body','hero','activated','isDisabling','isAttracted')){if($field -notin $currency.Fields.Name){$missing.Add('Currency field '+$field)}}
if('OnFixedUpdate' -notin $currency.Methods.Name){$missing.Add('Currency hook')};$game.Dispose()
$t2d=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $stage 'BepInEx\plugins\SkinLoader\CustomizerT2D.dll'),$rp)
$builder=$t2d.MainModule.GetType('CustomizerT2DOnly.CustomizerT2DOnly').Methods | Where-Object Name -EQ 'BuildOverrideMap'
$call=@($builder.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.String[] System.IO.Directory::GetDirectories(System.String)' })
if($call.Count -ne 1){$missing.Add('Skin loader filter target changed')};$t2d.Dispose()
"Enhanced static references: $count; missing: $($missing.Count)"
"Inactive MonoMod >=25 adapter references on legacy MonoMod: $($inactive.Count)"
if($inactive.Count){$inactive|Set-Content (Join-Path $taskRoot 'build\inactive-reorg-references.txt')}
if($missing.Count -gt 0){$missing | Set-Content (Join-Path $taskRoot 'build\enhanced-missing.txt');$missing | Select-Object -First 20;throw 'Enhanced compatibility check failed'}
'Enhanced static audit passed; not a game runtime test.'

