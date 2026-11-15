param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data/Managed/Assembly-CSharp.dll'))
try{
 $player=$assembly.MainModule.GetType('PlayerData')
 $fieldNames=@($player.Fields|ForEach-Object Name)
 $fields=@($player.Fields|ForEach-Object{[pscustomobject]@{Name=$_.Name;Type=$_.FieldType.FullName}})
 $fields|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'build/player-data-fields.json') -Encoding utf8
 $bases=@{};foreach($type in $assembly.MainModule.Types){if($type.BaseType){$bases[$type.Name]=$type.BaseType.Name}}
 $bases|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'build/game-type-bases.json') -Encoding utf8
 function Find-PlayerFields($method,$visited){
  if(-not $method -or -not $method.HasBody -or -not $visited.Add($method.FullName)){return}
  foreach($instruction in $method.Body.Instructions){
   $operand=$instruction.Operand
   if($operand -is [Mono.Cecil.FieldReference] -and $operand.DeclaringType.FullName -eq 'PlayerData'){$operand.Name}
   elseif($operand -is [Mono.Cecil.MethodReference] -and $operand.DeclaringType.FullName -eq 'PlayerData'){Find-PlayerFields ($operand.Resolve()) $visited}
   elseif($instruction.OpCode.Name -eq 'ldstr' -and $operand -in $fieldNames){$operand}
  }
 }
 $properties=@{}
 foreach($property in $player.Properties){$properties[$property.Name]=@(Find-PlayerFields $property.GetMethod ([Collections.Generic.HashSet[string]]::new())|Sort-Object -Unique)}
 $properties|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $root 'build/player-data-properties.json') -Encoding utf8
}finally{$assembly.Dispose()}
# UnityPy is an offline authoring dependency, not a runtime/plugin dependency. The
# checked catalog ships embedded in the DLL; ordinary builds never rescan the game.
Push-Location $root
try{
 python (Join-Path $PSScriptRoot 'Export-ProgressCatalog.py') --game-root $GameRoot
 if($LASTEXITCODE -ne 0){throw 'Asset audit incomplete; do not publish a partial catalog'}
}finally{Pop-Location}
