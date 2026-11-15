param([string]$GameRoot = 'D:\Game\Hollow Knight Silksong')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$ssmpRelative = 'BepInEx\plugins\SSMP\SSMP.dll'
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot $ssmpRelative))) { $ssmpRelative = 'upstream-release\silksong-coop-mod-0.4.142\SSMP\SSMP.dll' }
Add-Type -Path (Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$dirs = @((Join-Path $GameRoot 'Hollow Knight Silksong_Data\Managed'), (Join-Path $GameRoot 'BepInEx\core'),
    (Split-Path -Parent (Join-Path $taskRoot $ssmpRelative)), (Join-Path $taskRoot 'build'))
foreach ($dir in $dirs) { $resolver.AddSearchDirectory($dir) }
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$missing = [Collections.Generic.List[string]]::new()
$checked = 0
foreach ($file in @($ssmpRelative, 'build\Silksong.CoopAssist.dll')) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskRoot $file), $parameters)
    foreach ($member in $assembly.MainModule.GetMemberReferences()) {
        $scope = $member.DeclaringType.Scope.Name
        if ($scope -notmatch '^(Assembly-CSharp|UnityEngine.*|PlayMaker|BepInEx|0Harmony|SSMP)$') { continue }
        $checked++
        try { if ($null -eq $member.Resolve()) { $missing.Add($member.FullName) } }
        catch { $missing.Add($member.FullName + ' : ' + $_.Exception.InnerException.Message) }
    }
    $assembly.Dispose()
}
$ssmp = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskRoot $ssmpRelative), $parameters)
$bindings = @{
    'SSMP.Game.Client.ClientManager' = @('_coopSave', '_modSettings')
    'SSMP.Game.Client.Save.CoopSave' = @('_rescue', '_checkedMembers', '_waitingMembers')
}
foreach ($name in $bindings.Keys) {
    $type = $ssmp.MainModule.GetType($name)
    foreach ($field in $bindings[$name]) {
        if ($null -eq $type -or $field -notin $type.Fields.Name) { $missing.Add("Binding $name.$field") }
    }
}
$rescue = $ssmp.MainModule.GetType('SSMP.Game.Client.Save.CoopSave').NestedTypes | Where-Object Name -eq 'PendingRescue'
foreach ($prop in @('Outcome', 'ScreenBack')) {
    if ($prop -notin $rescue.Properties.Name) { $missing.Add("Rescue property $prop") }
}
$nativeHooks = @{
    'SSMP.Game.Client.ScenePath' = @('Get')
    'SSMP.Game.Client.BossRoomCoop' = @('IsInStoryTriggers','IsLocalHeroInStoryTrigger')
    'SSMP.Game.Client.Save.CoopSave' = @('OnInteractionSwitchState','AfterSharedPurchase','Refund')
}
foreach ($name in $nativeHooks.Keys) {
    $type = $ssmp.MainModule.GetType($name)
    foreach ($method in $nativeHooks[$name]) {
        $checked++
        if ($null -eq $type -or $method -notin $type.Methods.Name) { $missing.Add("Hook $name.$method") }
    }
}
$ssmp.Dispose()
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'Hollow Knight Silksong_Data\Managed\Assembly-CSharp.dll'), $parameters)
$gameBindings = @{
    'GameManager' = @('hasFinishedEnteringScene','profileID','LastSceneLoad')
    'HeroController' = @('cState','controlReqlinquished')
    'PlayerData' = @('atBench','health')
}
foreach ($name in $gameBindings.Keys) {
    $type = $game.MainModule.GetType($name)
    foreach ($member in $gameBindings[$name]) {
        if ($member -notin $type.Fields.Name -and $member -notin $type.Properties.Name) { $missing.Add("Game binding $name.$member") }
    }
}
# The waiting view stops drawing the hero's vignette plates; pin the members it reads.
$vignetteBindings = @{
    'HeroController' = @('vignette','heroLight')
    'HeroLight' = @('vignette')
}
foreach ($name in $vignetteBindings.Keys) {
    $type = $game.MainModule.GetType($name)
    foreach ($field in $vignetteBindings[$name]) {
        $checked++
        if ($null -eq $type -or $field -notin $type.Fields.Name) { $missing.Add("Vignette binding $name.$field") }
    }
}
# The borrow flow opens the game's own yes/no panel and resumes the interrupted purchase through these.
$borrowHooks = @{
    'DialogueYesNoBox' = @('Open')
    'ShopItem' = @('SetPurchased')
    'SimpleShopMenu' = @('ConfirmYes','ConfirmNo')
}
foreach ($name in $borrowHooks.Keys) {
    $type = $game.MainModule.GetType($name)
    foreach ($method in $borrowHooks[$name]) {
        $checked++
        if ($null -eq $type -or $method -notin $type.Methods.Name) { $missing.Add("Borrow hook $name.$method") }
    }
}
$hero = $game.MainModule.GetType('HeroController')
$cstate = ($hero.Fields | Where-Object Name -eq 'cState').FieldType.Resolve()
foreach ($member in @('dead','onGround')) { if ($member -notin $cstate.Fields.Name -and $member -notin $cstate.Properties.Name) { $missing.Add("Hero state $member") } }
$paymentCalls = @(
    @{ Type='ShopItem'; Method='SetPurchased'; Debit='TakeGeo' },
    @{ Type='SimpleShopMenu'; Method='ConfirmYes'; Debit='TakeCurrency' },
    @{ Type='HutongGames.PlayMaker.Actions.TakeCurrency'; Method='OnEnter'; Debit='TakeCurrency' }
)
foreach ($entry in $paymentCalls) {
    $checked++
    $method = $game.MainModule.GetType($entry.Type).Methods | Where-Object Name -eq $entry.Method
    $debits = @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.Name -eq 'CurrencyManager' -and $_.Operand.Name -eq $entry.Debit
    })
    if ($debits.Count -ne 1) { $missing.Add("Expected one native charge in $($entry.Type).$($entry.Method)") }
}
$game.Dispose()
"Static references checked: $checked"
if ($missing.Count -gt 0) { $missing | Set-Content (Join-Path $taskRoot 'build\missing-references.txt'); $missing | Select-Object -First 25; throw "$($missing.Count) unresolved members" }
'Static reference and status-binding audit passed. This is not a runtime test.'
