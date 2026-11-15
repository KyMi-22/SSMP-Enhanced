param(
 [Parameter(Mandatory=$true)][string]$InputPath,
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$inputFile=Get-Item -LiteralPath $InputPath
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $destination){throw 'Choose a new output directory; recovery files are never overwritten'}
$original=[IO.File]::ReadAllBytes($inputFile.FullName)
# Game saves contain a single NRBF string, not an object graph. Read that string
# explicitly, without running BinaryFormatter on the supplied file.
$header=[byte[]](0,1,0,0,0,255,255,255,255,1,0,0,0,0,0,0,0,6,1,0,0,0)
if($original.Length -lt 24 -or [BitConverter]::ToString($original,0,22) -ne [BitConverter]::ToString($header)) {throw 'Unsupported save format'}
$stream=[IO.MemoryStream]::new($original)
$reader=[IO.BinaryReader]::new($stream)
$aes=[Security.Cryptography.Aes]::Create()
try {
 $stream.Position=22
 $encoded=$reader.ReadString()
 if($reader.ReadByte() -ne 11 -or $stream.Position -ne $stream.Length){throw 'Unexpected NRBF save content'}
 $aes.Key=[Text.Encoding]::UTF8.GetBytes('UKu52ePUBwetZ9wNX88o54dnfKRu0T1l')
 $aes.Mode='ECB';$aes.Padding='PKCS7'
 $cipher=[Convert]::FromBase64String($encoded)
 $decrypt=$aes.CreateDecryptor()
 try {$plain=[Text.Encoding]::UTF8.GetString($decrypt.TransformFinalBlock($cipher,0,$cipher.Length))} finally {$decrypt.Dispose()}
 $save=$plain|ConvertFrom-Json
 $berry=@($save.playerData.Collectables.savedData|Where-Object Name -EQ 'Mossberry')
 $quest=@($save.playerData.QuestCompletionData.savedData|Where-Object Name -EQ 'Mossberry Collection 1')
 if($berry.Count -ne 1 -or $quest.Count -ne 1 -or -not $quest[0].Data.IsAccepted -or $quest[0].Data.IsCompleted){throw 'No accepted incomplete Mossberry wish; no recovery performed'}
 $before=[int]$berry[0].Data.Amount
 if($before -lt 0 -or $before -ge 3 -or $save.playerData.druidMossBerriesSold -ne 0){throw 'Quantity or sale history requires manual review'}
 $pattern='("Name"\s*:\s*"Mossberry"\s*,\s*"Data"\s*:\s*\{\s*"Amount"\s*:\s*)(\d+)'
 $matches=[regex]::Matches($plain,$pattern)
 if($matches.Count -ne 1 -or [int]$matches[0].Groups[2].Value -ne $before){throw 'Ambiguous inventory record'}
 $amountRange=$matches[0].Groups[2]
 $fixed=$plain.Remove($amountRange.Index,$amountRange.Length).Insert($amountRange.Index,[string]($before+1))
 # The original JSON is preserved byte for byte except this one numeric value.
 $changed=$fixed|ConvertFrom-Json
 if(@($changed.playerData.Collectables.savedData|Where-Object Name -EQ 'Mossberry')[0].Data.Amount -ne $before+1){throw 'Repair verification failed'}
 $jsonBytes=[Text.Encoding]::UTF8.GetBytes($fixed)
 $encrypt=$aes.CreateEncryptor()
 try {$newCipher=$encrypt.TransformFinalBlock($jsonBytes,0,$jsonBytes.Length)} finally {$encrypt.Dispose()}
 $output=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($output)
 try {$writer.Write($header);$writer.Write([Convert]::ToBase64String($newCipher));$writer.Write([byte]11);$writer.Flush();$repaired=$output.ToArray()} finally {$writer.Dispose();$output.Dispose()}
 New-Item -ItemType Directory -Path $destination | Out-Null
 [IO.File]::WriteAllBytes((Join-Path $destination ($inputFile.Name+'.original')),$original)
 [IO.File]::WriteAllBytes((Join-Path $destination $inputFile.Name),$repaired)
 $receipt=[ordered]@{Source=$inputFile.FullName;SourceSha256=(Get-FileHash -LiteralPath $inputFile.FullName).Hash;RepairedSha256=(Get-FileHash -LiteralPath (Join-Path $destination $inputFile.Name)).Hash;Change='Only Collectables/Mossberry/Data/Amount +1';Before=$before;After=$before+1;Installed=$false}
 $receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $destination 'Repair.json') -Encoding utf8
 @"
此副本仅补回用户报告缺少的 1 个苔莓：$before → $($before+1)。没有修改任务、拾取点、念珠或其他物品，没有安装到游戏。
必须先退出游戏，再用这里的 $($inputFile.Name) 替换 Repair.json 中 Source 所指的同一存档。不要导入其他槽位，也不要重复执行补偿。
原档副本为 $($inputFile.Name).original，需要回滚时复制回原路径并去掉 .original 后缀。
这是按用户报告进行的定量补偿；售出数为 0、任务未完成不足以独立证明丢失原因。
不要将这个目录发给队友或放入公共安装包。房主恢复后，队友按房主同步即可。
"@ | Set-Content -LiteralPath (Join-Path $destination '使用说明.txt') -Encoding utf8
 $receipt|ConvertTo-Json
} finally {$reader.Dispose();$stream.Dispose();$aes.Dispose()}
