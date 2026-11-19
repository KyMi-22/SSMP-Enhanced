param([string]$GameRoot='D:\Game\Hollow Knight Silksong',[string]$ExpectedVersion='0.3.2-standard',[string]$ExpectedNetworkVersion='0.3.2-std3')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$nativePath=Join-Path $GameRoot 'BepInEx\plugins\SSMP\SSMP.dll'
if((Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash -ne '71E10C6808C24AB0F01E5B48CB463955A179166914652C9C4B16D9F380D11354'){throw 'SSMP differs from the pinned runtime'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll')
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($nativePath)
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskRoot 'build\Silksong.CoopAssist.dll'))
try {
    $hit=$native.MainModule.GetType('SSMP.Animation.Effects.HeroHit')
    foreach($name in @('OnTakenDamage','WatchLocal')){
        $method=$hit.Methods | Where-Object Name -EQ $name
        if(-not $method -or $method.Parameters.Count -ne 0){throw "Hurt target changed: $name"}
        $constants=@($method.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldc.r4' -and [Math]::Abs([double]$_.Operand-.1) -lt .000001})
        if($constants.Count -ne 1){throw "Expected exactly one retry interval in $name"}
    }
    $feedback=$assist.MainModule.GetType('Silksong.CoopAssist.HitFeedbackRepairs')
    $retry=$feedback.Fields | Where-Object Name -EQ 'RetrySeconds'
    if([Math]::Abs([double]$retry.Constant-1.0/30) -gt .000001){throw 'Expected 33 ms retry'}
    $animation=$native.MainModule.GetType('SSMP.Animation.AnimationManager')
    $lookup=$animation.Fields | Where-Object Name -EQ 'ClipEnumNames'
    if($lookup.FieldType.FullName -ne 'SSMP.Collection.BiLookup`2<System.String,SSMP.Animation.AnimationClip>'){throw 'Clip lookup changed'}
    $bilookup=$native.MainModule.GetType('SSMP.Collection.BiLookup`2')
    foreach($name in @('_normal','_inverse')){if($name -notin $bilookup.Fields.Name){throw "Missing clip lookup direction $name"}}
    $enum=$native.MainModule.GetType('SSMP.Animation.AnimationClip').Fields | Where-Object Name -EQ 'WeakenedStunEnd'
    $cctor=$animation.Methods | Where-Object Name -EQ '.cctor'
    $instructions=$cctor.Body.Instructions
    $found=$false
    for($i=0;$i -lt $instructions.Count-1;$i++){
        if($instructions[$i].OpCode.Name -eq 'ldstr' -and $instructions[$i].Operand -eq 'Weakened StunEnd'){
            if([int]$instructions[$i+1].Operand -ne [int]$enum.Constant){throw 'Stun name maps to a different enum'}
            $found=$true
        }
    }
    if(-not $found){throw 'Pinned incorrect stun name not found'}
    $timing=$assist.MainModule.GetType('Silksong.CoopAssist.RuntimeRepairs').Methods | Where-Object Name -EQ 'AfterTiming'
    $field=$native.MainModule.GetType('SSMP.Fsm.PredictiveInterpolation').Fields | Where-Object Name -EQ '_adaptedCorrectionTime'
    if($field.FieldType.FullName -ne 'System.Single' -or $timing.Parameters[1].Name -ne '____adaptedCorrectionTime' -or $timing.Parameters[1].ParameterType.FullName -ne 'System.Single&'){throw 'Direct RTT field binding invalid'}
    $decode=$native.MainModule.GetType('SSMP.Networking.Packet.Data.PlayerUpdate').Methods | Where-Object Name -EQ 'ReadData'
    $apply=$native.MainModule.GetType('SSMP.Game.Client.ClientManager').Methods | Where-Object Name -EQ 'OnPlayerUpdate'
    if($decode.Parameters.Count -ne 1 -or $apply.Parameters[0].ParameterType.FullName -ne 'SSMP.Networking.Packet.Data.PlayerUpdate'){throw 'Decode/apply diagnostic targets changed'}
    $release=$assist.MainModule.GetType('Silksong.CoopAssist.ReleaseInfo')
    if(($release.Fields|Where-Object Name -EQ Version).Constant -cne $ExpectedVersion){throw 'Unexpected release version'}
    $nativeAddon=$native.MainModule.GetType('SSMP.Api.Addon.Addon')
    $versionLimit=($nativeAddon.Fields|Where-Object Name -EQ MaxVersionLength).Constant
    if($ExpectedNetworkVersion.Length -gt $versionLimit){throw 'SSMP would truncate the addon version'}
    $versions=@($assist.MainModule.GetType('Silksong.CoopAssist.AssistClient'),$assist.MainModule.GetType('Silksong.CoopAssist.AssistServer'))
    foreach($type in $versions){
        $method=$type.Methods | Where-Object Name -EQ 'get_Version'
        if($ExpectedNetworkVersion -cnotin @($method.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldstr'} | ForEach-Object Operand)){throw 'Addon handshake version not updated'}
    }
    $plugin=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
    $metadata=$plugin.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'}
    $numericVersion=[version]($ExpectedVersion -split '-',2)[0]
    if([version]$metadata.ConstructorArguments[2].Value -ne $numericVersion){throw 'Unexpected BepInEx plugin version'}
    if($ExpectedVersion -like '*-standard' -and $metadata.ConstructorArguments[1].Value -cne 'Silksong Coop Assist (Standard)'){throw 'Unexpected BepInEx plugin name'}
    "Passed hurt retry, stun mapping, timing and decode/apply bindings. Release $ExpectedVersion, BepInEx $numericVersion and both SSMP addons $ExpectedNetworkVersion verified. Static check only."
}finally{$native.Dispose();$assist.Dispose()}

