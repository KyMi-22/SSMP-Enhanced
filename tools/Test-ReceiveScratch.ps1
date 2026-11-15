$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$out+'\ReceiveScratchTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object{'/reference:"'+$_.FullName+'"'}
$options+=@('src\ReceiveScratch.cs','tests\ReceiveScratchTests.cs')|ForEach-Object{'"'+(Join-Path $root $_)+'"'}
$response=Join-Path $out 'receive-scratch-tests.rsp'
$options|Set-Content -LiteralPath $response -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$response"
if($LASTEXITCODE -ne 0){throw 'Receive tests did not compile'}
Copy-Item -LiteralPath (Join-Path $out 'ArrivalTests.runtimeconfig.json') -Destination (Join-Path $out 'ReceiveScratchTests.runtimeconfig.json') -Force
& dotnet (Join-Path $out 'ReceiveScratchTests.dll')
if($LASTEXITCODE -ne 0){throw 'Receive tests failed'}
Add-Type -Path 'D:\Game\Hollow Knight Silksong\BepInEx\core\Mono.Cecil.dll'
$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:\Game\Hollow Knight Silksong\BepInEx\plugins\SSMP\SSMP.dll')
try{
 $loop=$asm.MainModule.GetType('SSMP.Networking.Client.DtlsClient').Methods|Where-Object Name -EQ 'DtlsReceiveLoop'
 $arrays=@($loop.Body.Instructions|Where-Object{$_.OpCode.Name -eq 'newarr' -and $_.Operand.FullName -eq 'System.Byte'})
 $receives=@($loop.Body.Instructions|Where-Object{$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Receive' -and $_.Operand.ReturnType.FullName -eq 'System.Int32' -and $_.Operand.Parameters.Count -eq 4})
 if($arrays.Count -ne 1 -or $receives.Count -ne 1){throw 'Native loop no longer matches the bounded allocation patch'}
 $client=$asm.MainModule.GetType('SSMP.Networking.Client.NetClient').Methods|Where-Object Name -EQ 'OnReceiveData'
 if('HandleReceivedData' -notin @($client.Body.Instructions|Where-Object{$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object{$_.Operand.Name})){throw 'Recheck native packet length boundary'}
}finally{$asm.Dispose()}
'Passed exact native receive allocation/call signatures and existing packet-length processing boundary. Static check only.'
