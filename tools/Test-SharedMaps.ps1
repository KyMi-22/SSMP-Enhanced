param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\SharedMapTests.dll"'),('/reference:"'+$out+'\SSMP.dll"'),('/reference:"'+$out+'\Newtonsoft.Json.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src/HostAnchor.cs','src/HostMapSync.cs','src/SharedMapProgress.cs','src/SharedMapMessage.cs','src/NativeMapBridge.cs','src/SharedMaps.cs','src/SharedMapShop.cs','tests/SharedMapTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'shared-maps.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\shared-maps.rsp"
if($LASTEXITCODE -ne 0){throw 'Shared-map tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'SharedMapTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'SharedMapTests.dll')
if($LASTEXITCODE -ne 0){throw 'Shared-map tests failed'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data/Managed/Assembly-CSharp.dll'))
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'Silksong.CoopAssist.dll'))
try{
 $schema=$assist.MainModule.GetType('Silksong.CoopAssist.SharedMapProgress')
 $strings=@(($schema.Methods|Where-Object Name -EQ '.cctor').Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 $owned=@($strings|Where-Object {$_ -match '^Has.*Map$' -or $_ -eq 'hasQuill'})
 $nativeOwned=@($game.MainModule.GetType('PlayerData').Fields|Where-Object {$_.Name -match '^Has.*Map$' -and $_.FieldType.FullName -eq 'System.Boolean'}|ForEach-Object Name)
 if($owned.Count -ne 29 -or $nativeOwned.Count -ne 28 -or @(Compare-Object ($owned|Where-Object {$_ -ne 'hasQuill'}|Sort-Object) ($nativeOwned|Sort-Object)).Count -ne 0){throw 'Map schema must cover every actual Silksong regional ownership flag'}
 $bridge=$assist.MainModule.GetType('Silksong.CoopAssist.NativeMapBridge')
 $bindings=@(($bridge.Methods|Where-Object Name -EQ '.cctor').Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldftn'}|ForEach-Object {
  $method=$_.Operand.Resolve();$access=@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.DeclaringType.FullName -eq 'PlayerData'})
  if($access.Count -ne 1){throw 'Each typed map binding must access exactly one actual field'}
  $access[0].Operand.Name
 })
 if(($bindings -join '|') -ne (($owned+$owned) -join '|')){throw 'Map bit ordering differs between typed readers, grants and wire schema'}
 foreach($name in @('scenesVisited','scenesMapped','scenesEncounteredBench')){
  $field=$game.MainModule.GetType('PlayerData').Fields|Where-Object Name -EQ $name
  if($field.FieldType.FullName -ne 'System.Collections.Generic.HashSet`1<System.String>'){throw "Map collection binding changed: $name"}
 }
 $plugin=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
 $calls=@(($plugin.Methods|Where-Object Name -EQ LateUpdate).Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 if('TickSharedMaps' -notin $calls){throw 'Map sync must tick independently of movement/save readiness'}
 $methods=$assist.MainModule.GetType('Silksong.CoopAssist.HostProgressSync').Methods
 $shared=$methods|Where-Object Name -EQ AlignSharedProgress
 if('HostMapSync' -notin @($shared.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.DeclaringType.Name})){throw 'Maps must join the shared automatic/manual snapshot transaction'}
 foreach($name in @('Align','AlignForJoin')){
  $method=$methods|Where-Object Name -EQ $name
  if('AlignSharedProgress' -notin @($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})){throw "$name must align map progress"}
 }
 $shop=$assist.MainModule.GetType('Silksong.CoopAssist.SharedMapShop')
 $hooks=@(($shop.Methods|Where-Object Name -EQ Install).Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 if('get_Cost' -in $hooks -or 'CanBuy' -notin $hooks -or 'SetPurchased' -notin $hooks){throw 'Shop guard must preserve native pricing and reject stale purchases'}
 $available=$game.MainModule.GetType('ShopItem').Methods|Where-Object Name -EQ get_IsAvailable
 if('get_IsPurchased' -notin @($available.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})){throw 'Native shop no longer removes owned map stock'}
 'Passed all 28 native regional maps, typed bit ordering, actual room fields, join/manual wiring and unchanged price hooks. Static only.'
}finally{$game.Dispose();$assist.Dispose()}
