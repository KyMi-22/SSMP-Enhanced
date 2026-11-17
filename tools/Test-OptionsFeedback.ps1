param([string]$GameRoot='D:\Game\Hollow Knight Silksong')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\OptionsFeedbackTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src/AssistFeedback.cs','src/FeedbackPresentation.cs','src/TeamControlPolicy.cs','src/TeamControls.cs','tests/OptionsFeedbackTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$options|Set-Content -LiteralPath (Join-Path $out 'feedback-tests.rsp') -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$out\feedback-tests.rsp"
if($LASTEXITCODE -ne 0){throw 'Options feedback tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'TeamControlTests.runtimeconfig.json') -Destination (Join-Path $out 'OptionsFeedbackTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'OptionsFeedbackTests.dll')
if($LASTEXITCODE -ne 0){throw 'Options feedback tests failed'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $out 'Silksong.CoopAssist.dll'))
try{
    $plugin=$built.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin')
    function Calls($name){@(($plugin.Methods|Where-Object Name -EQ $name).Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})}
    foreach($pair in @(@('Say','Publish'),@('OnGUI','DrawFeedbackToast'),@('LateUpdate','TickAssistHotkeys'),@('DrawOptions','DrawOptionsFeedback'),@('AddTravelMenu','AddMenuFeedback'),@('ShowMenuTravelPicker','AddMenuFeedback'),@('SayChat','AddMessage'),@('SayChat','Say'),@('OpenTravelPicker','SayChat'),@('CancelTravelPicker','SayChat'),@('ApplyHostSnapshot','Say'))){
        if($pair[1] -notin (Calls $pair[0])){throw "Missing visible feedback integration: $($pair -join ' -> ')"}
    }
    if('AddMessage' -in (Calls 'Say')){throw 'Local operation feedback must not depend on native chat or draw twice'}
    foreach($name in @('RequestHostResync','AcceptBinding')){
        if('Say' -notin (Calls $name)){throw "Silent action: $name"}
    }
    foreach($name in @('SetWaypoint','ClearWaypoint','StartWaypoint')){
        if('SayChat' -notin (Calls $name)){throw "Waypoint action must report through native chat: $name"}
    }
    foreach($name in @('RequestTeamTravel','EndTravel','ConfirmPicker')){
        if('SayChat' -notin (Calls $name)){throw "Teammate travel must report through native chat: $name"}
    }
    foreach($pair in @(@('TickRosaryDialog','AskRosaries'),@('DrawBorrowDialog','CenterDialog'),@('CreditLoan','ChangeWallet'),@('CreditLoan','ResumePurchase'),@('CancelLoanPurchase','CloseNativeConfirm'))){
        if($pair[1] -notin (Calls $pair[0])){throw "Missing borrow wiring: $($pair -join ' -> ')"}
    }
    foreach($name in @('SettleGrant','RepayRosaries','ReceivePayment')){
        if('SayChat' -notin (Calls $name)){throw "Rosary result must report through native chat: $name"}
    }
    $make=$plugin.Methods|Where-Object Name -EQ 'MakeTransfer'
    if('Outgoing' -notin @($make.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference]}|ForEach-Object {$_.Operand.Name})){
        throw 'MakeTransfer must mark the lender side as outgoing'
    }
    if('RefundOfferedLoan' -notin (Calls 'ForgetRosaryPeer')){throw 'A departing lender must get its unclaimed loan back'}
    $after=$plugin.Methods|Where-Object Name -EQ AfterSaveLoaded
    $instructions=@($after.Body.Instructions)
    $identityBranch=@($instructions|Where-Object {$_.OpCode.Name -like 'bne.un*' -and $_.Previous.OpCode.Name -eq 'ldarg.2' -and $_.Previous.Previous.Operand.Name -eq 'pendingResyncReload'})
    if($identityBranch.Count -ne 1){throw 'Sync completion must remain tied to our actual reload object'}
    if('resyncCompletedSlot' -notin @($after.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference]}|ForEach-Object {$_.Operand.Name})){throw 'No completion acknowledgement after actual reload'}
    $nested=@($plugin.NestedTypes|ForEach-Object {$_.Methods})
    $nestedCalls=@($nested|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    if('set_VisibleSelf' -notin $nestedCalls -or 'remove_Changed' -notin $nestedCalls){throw 'Native key folding or feedback subscription cleanup missing'}
    'Passed F7/native feedback, key folding, async completion identity and menu subscription cleanup bindings. Static only.'
}finally{$built.Dispose()}
