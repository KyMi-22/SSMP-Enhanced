param([string]$OutputPath,[string]$TemplateRoot,[string]$Version)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $taskRoot 'dist/Silksong-Coop-Enhanced-0.2.8-beta'
if($TemplateRoot){$stage=[IO.Path]::GetFullPath($TemplateRoot)}
$templateManifest=Join-Path $stage 'Manifest.json'
if(-not (Test-Path -LiteralPath $templateManifest -PathType Leaf)){throw 'Template manifest missing. Pass -TemplateRoot with the extracted full package directory.'}
$manifest=Get-Content -LiteralPath $templateManifest -Raw | ConvertFrom-Json
$manifest|Add-Member -NotePropertyName GameTested -NotePropertyValue $false -Force
$manifest|Add-Member -NotePropertyName MultiplayerTested -NotePropertyValue $false -Force
$manifest.Date=[TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow,[TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')).ToString('yyyy-MM-dd')
$plugin='BepInEx/plugins/SSMP/Silksong.CoopAssist.dll'
$dll=Join-Path $taskRoot 'build/Silksong.CoopAssist.dll'
if(-not (Test-Path -LiteralPath $dll)){throw 'Run tools/Build.ps1 first'}
Add-Type -Path (Join-Path $stage 'BepInEx/core/Mono.Cecil.dll')
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try{
    $release=$built.MainModule.GetType('Silksong.CoopAssist.ReleaseInfo')
    $actualVersion=($release.Fields|Where-Object Name -EQ Version).Constant
    if($Version -and $Version -cne $actualVersion){throw 'Package version differs from the tested plugin'}
    $Version=$actualVersion
    $revision=($release.Fields|Where-Object Name -EQ Revision).Constant
    $resource=$built.MainModule.Resources|Where-Object Name -EQ 'Silksong.CoopAssist.ProgressSyncCatalog.json'
    if(-not $resource){throw 'Progress/source catalog is not embedded in the plugin'}
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$catalogHash=[BitConverter]::ToString($sha.ComputeHash($resource.GetResourceData())).Replace('-','')}finally{$sha.Dispose()}
    if($catalogHash -ne (Get-FileHash -LiteralPath (Join-Path $taskRoot 'assets/ProgressSyncCatalog.json')).Hash){throw 'Catalog source changed after the tested build; rebuild before export'}
}finally{$built.Dispose()}
$manifest.Package="Silksong-Coop-Enhanced-$Version"
$manifest.CoopAssist=$Version
$manifest|Add-Member -NotePropertyName ProgressCatalogSha256 -NotePropertyValue $catalogHash -Force
$manifest|Add-Member -NotePropertyName Revision -NotePropertyValue $revision -Force
if(-not $OutputPath){$OutputPath=Join-Path $taskRoot "dist/$($manifest.Package).zip"}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if(Test-Path -LiteralPath $OutputPath){throw 'Output already exists; choose a new OutputPath'}
$stagePrefix=[IO.Path]::GetFullPath($stage)+[IO.Path]::DirectorySeparatorChar
if($OutputPath.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be outside the package directory'}
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
$replacements=@{$plugin=$dll}
$replacements['Sources/CoopAssist/assets/ProgressSyncCatalog.json']=Join-Path $taskRoot 'assets/ProgressSyncCatalog.json'
$manifest|Add-Member -NotePropertyName CoopAssistSha256 -NotePropertyValue (Get-FileHash -LiteralPath $dll).Hash -Force
$font='BepInEx/plugins/SSMP/Fonts/AaXuanYuanTi-2.ttf'
$fontSource=Join-Path $taskRoot 'build/Fonts/AaXuanYuanTi-2.ttf'
if(-not (Test-Path -LiteralPath $fontSource)){throw 'Run Build.ps1 to stage the bundled font'}
$replacements[$font]=$fontSource
$rollback=Join-Path $taskRoot 'build/CoopAssist-0.2.6.zip'
if(Test-Path -LiteralPath $rollback){$replacements['Rollback/CoopAssist-0.2.6.zip']=$rollback}
$flowRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.8-before-flow.zip'
if(Test-Path -LiteralPath $flowRollback){$replacements['Rollback/CoopAssist-0.2.8-before-flow.zip']=$flowRollback}
$previousRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.7.zip'
if(Test-Path -LiteralPath $previousRollback){$replacements['Rollback/CoopAssist-0.2.7.zip']=$previousRollback}
$latestRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.8.zip'
if(Test-Path -LiteralPath $latestRollback){$replacements['Rollback/CoopAssist-0.2.8.zip']=$latestRollback}
$progressRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9.zip'
if(Test-Path -LiteralPath $progressRollback){$replacements['Rollback/CoopAssist-0.2.9-before-progress-sync.zip']=$progressRollback}
$controlsRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-team-controls.zip'
if(Test-Path -LiteralPath $controlsRollback){$replacements['Rollback/CoopAssist-0.2.9-before-team-controls.zip']=$controlsRollback}
$feedbackRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-ui-feedback.zip'
if(Test-Path -LiteralPath $feedbackRollback){$replacements['Rollback/CoopAssist-0.2.9-before-ui-feedback.zip']=$feedbackRollback}
$hudRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-hud-feedback.zip'
if(Test-Path -LiteralPath $hudRollback){$replacements['Rollback/CoopAssist-0.2.9-before-hud-feedback.zip']=$hudRollback}
$nameRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-name-presentation.zip'
if(Test-Path -LiteralPath $nameRollback){$replacements['Rollback/CoopAssist-0.2.9-before-name-presentation.zip']=$nameRollback}
$mapRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-shared-maps-respawn.zip'
if(Test-Path -LiteralPath $mapRollback){$replacements['Rollback/CoopAssist-0.2.9-before-shared-maps-respawn.zip']=$mapRollback}
$waypointRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-waypoints-crest-judge.zip'
if(Test-Path -LiteralPath $waypointRollback){$replacements['Rollback/CoopAssist-0.2.9-before-waypoints-crest-judge.zip']=$waypointRollback}
$gateRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-before-grand-gate-maps.zip'
if(Test-Path -LiteralPath $gateRollback){$replacements['Rollback/CoopAssist-0.2.9-before-grand-gate-maps.zip']=$gateRollback}
$previewRollback=Join-Path $taskRoot 'build/CoopAssist-0.2.9-grand-gate-maps.zip'
if(-not (Test-Path -LiteralPath $previewRollback -PathType Leaf)){throw "Required rollback archive missing: $previewRollback"}
$replacements['Rollback/CoopAssist-0.2.9-grand-gate-maps.zip']=$previewRollback
$priorUpdate=Join-Path $taskRoot 'dist/Silksong-Coop-0.3.0-preview-update.zip'
if($Version -like '*-standard' -and (Test-Path -LiteralPath $priorUpdate -PathType Leaf)){
    $replacements['Rollback/Silksong-Coop-0.3.0-preview-update.zip']=$priorUpdate
}
# Keep the same one-step-back rollback the update package carries, so Install.md is true for both packages.
$priorRevision=Get-ChildItem -LiteralPath (Join-Path $taskRoot 'build') -Filter 'CoopAssist-*-standard-*.zip' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if($Version -like '*-standard' -and $priorRevision){
    $replacements['Rollback/'+$priorRevision.Name]=$priorRevision.FullName
}
$replacements['Install.md']=Join-Path $taskRoot 'hotreload-docs/Install.md'
$replacements['Sources/CoopAssist/README.md']=Join-Path $taskRoot 'README.md'
foreach($folder in @('src','tools','tests','skins-docs','hotreload-docs')){
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $taskRoot $folder) -File){
        $replacements['Sources/CoopAssist/'+$folder+'/'+$file.Name]=$file.FullName
        if($folder -eq 'hotreload-docs'){$replacements['Docs/'+$file.Name]=$file.FullName}
    }
}
# Validate the complete retained template; no earlier release or external skin download is needed.
foreach($row in $manifest.Files){
    if($row.Path -eq $plugin){$file=Get-Item -LiteralPath $dll;$row.Bytes=$file.Length;$row.Sha256=(Get-FileHash -LiteralPath $dll).Hash;continue}
    $file=Get-Item -LiteralPath (Join-Path $stage $row.Path)
    if($file.Length -ne $row.Bytes -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $row.Sha256){throw "Runtime template changed: $($row.Path)"}
}
$manifest.Files=@($manifest.Files|Where-Object Path -NE $font)+@([pscustomobject]@{Path=$font;Bytes=(Get-Item -LiteralPath $fontSource).Length;Sha256=(Get-FileHash -LiteralPath $fontSource).Hash})
$archive=[IO.Compression.ZipFile]::Open($OutputPath,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in Get-ChildItem -LiteralPath $stage -Recurse -File -Force){
        $name=$file.FullName.Substring($stagePrefix.Length).Replace('\','/')
        if($name -eq 'Manifest.json' -or $name.StartsWith('Sources/CoopAssist/') -or $replacements.ContainsKey($name)){continue}
        if($name -notmatch '/' -and $file.Extension -in @('.zip','.7z')){continue}
        if($name -match '^BepInEx/(config|cache)/|LogOutput|nexus-browser-profile'){throw "Personal files in package template: $name"}
        $level=if($file.Extension -in @('.png','.webm','.zip')){[IO.Compression.CompressionLevel]::NoCompression}else{[IO.Compression.CompressionLevel]::Optimal}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$name,$level)|Out-Null
    }
    foreach($pair in $replacements.GetEnumerator()){
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$pair.Value,$pair.Key,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
    }
    $writer=[IO.StreamWriter]::new($archive.CreateEntry('Manifest.json').Open(),[Text.UTF8Encoding]::new($false))
    try{$writer.Write(($manifest|ConvertTo-Json -Depth 10))}finally{$writer.Dispose()}
}finally{$archive.Dispose()}
$verify=[IO.Compression.ZipFile]::OpenRead($OutputPath)
try{
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $verify.Entries){if(-not $seen.Add($entry.FullName)){throw "Duplicate ZIP path: $($entry.FullName)"}}
    foreach($row in $manifest.Files){
        $entry=$verify.GetEntry($row.Path)
        if(-not $entry -or $entry.Length -ne $row.Bytes){throw "Wrong ZIP runtime size: $($row.Path)"}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
        if($hash -ne $row.Sha256){throw "Wrong ZIP runtime hash: $($row.Path)"}
    }
}finally{$verify.Dispose()}
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256 | Select-Object Hash

