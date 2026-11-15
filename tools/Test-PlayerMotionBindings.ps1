param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll')
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx\plugins\SSMP\SSMP.dll'))
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskRoot 'build\Silksong.CoopAssist.dll'))
try {
    $interpolation=$native.MainModule.GetType('SSMP.Fsm.PredictiveInterpolation')
    $hooks=$assist.MainModule.GetType('Silksong.CoopAssist.PlayerMotionRepairs')
    $checked=0
    $binding=@{BeforePosition='SetNewState';BeforeUpdate='ManualUpdate';AfterEnable='OnEnable'}
    foreach($pair in $binding.GetEnumerator()){
        $hook=$hooks.Methods | Where-Object Name -EQ $pair.Key
        $target=$interpolation.Methods | Where-Object Name -EQ $pair.Value
        if(-not $hook -or -not $target){throw "Missing movement hook $($pair.Key)"}
        foreach($parameter in $hook.Parameters){
            $name=$parameter.Name
            if($name.StartsWith('___')){
                $field=$interpolation.Fields | Where-Object Name -EQ $name.Substring(3)
                $expected=$parameter.ParameterType.FullName.TrimEnd('&')
                if(-not $field -or $field.FieldType.FullName -ne $expected){throw "Bad field binding $name"}
                $checked++
            } elseif($name -match '^__(\d+)$') {
                $nativeParameter=$target.Parameters[[int]$Matches[1]]
                if($nativeParameter.ParameterType.FullName -ne $parameter.ParameterType.FullName){throw "Bad argument binding $name"}
                $checked++
            }
        }
    }
    if(-not ($interpolation.Methods | Where-Object Name -EQ 'SetPredictionEnabled')){throw 'Scripted prediction ownership hook missing'}
    $method=$native.MainModule.GetType('SSMP.Game.Client.ClientManager').Methods | Where-Object Name -EQ 'OnHeroControllerUpdate'
    $reads=@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Int64 System.Diagnostics.Stopwatch::get_ElapsedMilliseconds()'})
    if($reads.Count -ne 2){throw "Sampling gate has $($reads.Count) reads"}
    $constants=@($method.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldc.i4.s'} | ForEach-Object {[int]$_.Operand})
    if(16 -notin $constants){throw 'Expected native 16 ms comparison missing'}
    $allConstants=@($method.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldc.i4'} | ForEach-Object {[int]$_.Operand})
    if(250 -notin $allConstants){throw 'Expected native stationary comparison missing'}
    $manager=$native.MainModule.GetType('SSMP.Game.Client.PlayerManager')
    $name=$manager.Fields | Where-Object Name -EQ 'PlayerContainerName'
    if($name.Constant -ne 'Player Container'){throw 'Player-only object identity changed'}
    "Passed $checked native argument/field bindings, both sampling targets, player identity and lift ownership hook. Static check only."
} finally {$native.Dispose();$assist.Dispose()}
