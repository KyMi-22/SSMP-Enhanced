param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\ProgressConsistencyTests.dll"'),('/reference:"'+$out+'\SSMP.dll"'),('/reference:"'+$out+'\Newtonsoft.Json.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+='/resource:"'+(Join-Path $root 'assets/ProgressSyncCatalog.json')+'",Silksong.CoopAssist.ProgressSyncCatalog.json'
$options+='/resource:"'+(Join-Path $out 'NativeStoryItems.json')+'",Silksong.CoopAssist.NativeStoryItems.json'
$options+=@('src/HostResourceSync.cs','src/HostWorldSync.cs','src/WishProgressSync.cs','src/ProgressSyncCatalog.cs','src/HostProgressSync.cs','src/PersonalCrestLoadout.cs','src/GrandGateProgress.cs','src/HostMapSync.cs','src/SharedMapProgress.cs','tests/ProgressConsistencyTests.cs','tests/PreviousProgressPolicy.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'progress-consistency.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\progress-consistency.rsp"
if($LASTEXITCODE -ne 0){throw 'Progress consistency tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'ProgressConsistencyTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'ProgressConsistencyTests.dll') (Join-Path $root 'assets/ProgressSyncCatalog.json')
if($LASTEXITCODE -ne 0){throw 'Progress consistency tests failed'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'Silksong.CoopAssist.dll'))
try{
 $after=$assist.MainModule.GetType('Silksong.CoopAssist.HostSyncRepairs').Methods|Where-Object Name -EQ 'AfterMerge'
 $calls=@($after.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.DeclaringType.FullName+'.'+$_.Operand.Name})
 if('Silksong.CoopAssist.HostProgressSync.Align' -notin $calls -or
    'Silksong.CoopAssist.HostProgressSync.AlignForJoin' -notin $calls){throw 'Native merge must dispatch both manual and automatic policies'}
 $manual=$assist.MainModule.GetType('Silksong.CoopAssist.NativeSaveBridge').Methods|Where-Object Name -EQ 'Align'
 if('Invoke' -notin @($manual.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})){throw 'Manual sync must call the same patched native merge'}
 if('Silksong.CoopAssist.ProgressSyncCatalog.json' -notin $assist.MainModule.Resources.Name){throw 'The game asset catalog must ship inside the runtime DLL'}
 $loaded=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin').Methods|Where-Object Name -EQ 'AfterSaveLoaded'
 if('Loaded' -notin @($loaded.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})){throw 'Same-slot reload must reset old quest/source caches after loading'}
 $identity=@($loaded.Body.Instructions|Where-Object {$_.OpCode.Name -in @('ceq','beq','beq.s','bne.un','bne.un.s')})
 if($identity.Count -eq 0){throw 'Normal death/load must not reset native caches as a manual replacement'}
 'Passed shared automatic/manual transaction wiring and embedded progress catalog. Static only.'
}finally{$assist.Dispose()}
