param([string]$Version,[string]$OutputPath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dll=Join-Path $root 'build/Silksong.CoopAssist.dll'
Add-Type -Path (Join-Path $root 'upstream-release/silksong-coop-mod-0.4.142/BepInEx_win_x64_5.4.23.4/BepInEx/core/Mono.Cecil.dll')
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try{
    $release=$built.MainModule.GetType('Silksong.CoopAssist.ReleaseInfo')
    $actualVersion=($release.Fields|Where-Object Name -EQ Version).Constant
    $revision=($release.Fields|Where-Object Name -EQ Revision).Constant
    if($Version -and $Version -cne $actualVersion){throw 'Update version differs from the built plugin'}
    $Version=$actualVersion
    $resource=$built.MainModule.Resources|Where-Object Name -EQ 'Silksong.CoopAssist.ProgressSyncCatalog.json'
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$catalogHash=[BitConverter]::ToString($sha.ComputeHash($resource.GetResourceData())).Replace('-','')}finally{$sha.Dispose()}
    if($catalogHash -ne (Get-FileHash -LiteralPath (Join-Path $root 'assets/ProgressSyncCatalog.json')).Hash){throw 'Rebuild: embedded progress catalog differs from source'}
}finally{$built.Dispose()}
$package="Silksong-Coop-$Version-update"
if(-not $OutputPath){$OutputPath=Join-Path $root "dist/$package.zip"}
if(Test-Path -LiteralPath $OutputPath){throw 'Output already exists; archive the previous revision first'}
$stage=Join-Path $root "build/update-$Version-$revision"
if(Test-Path -LiteralPath $stage){throw 'Update staging directory already exists; choose a new revision'}
$files=@{
    'BepInEx/plugins/SSMP/Silksong.CoopAssist.dll'=$dll
    'BepInEx/plugins/SSMP/Fonts/AaXuanYuanTi-2.ttf'=Join-Path $root 'build/Fonts/AaXuanYuanTi-2.ttf'
    'Install.md'=Join-Path $root 'hotreload-docs/Install.md'
    "Validation-$Version.md"=Join-Path $root "hotreload-docs/Validation-$Version.md"
    'Rollback/CoopAssist-0.2.9-grand-gate-maps.zip'=Join-Path $root 'build/CoopAssist-0.2.9-grand-gate-maps.zip'
}
$priorUpdate=Join-Path $root 'dist/Silksong-Coop-0.3.0-preview-update.zip'
if($Version -like '*-standard' -and (Test-Path -LiteralPath $priorUpdate -PathType Leaf)){
    $files['Rollback/Silksong-Coop-0.3.0-preview-update.zip']=$priorUpdate
}
# The DLL of the revision this one replaces, so a bad build can be stepped back from in one unzip.
$priorRevision=Get-ChildItem -LiteralPath (Join-Path $root 'build') -Filter 'CoopAssist-*-standard-*.zip' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if($Version -like '*-standard' -and $priorRevision){
    $files['Rollback/'+$priorRevision.Name]=$priorRevision.FullName
}
foreach($source in $files.Values){
    if(-not (Test-Path -LiteralPath $source -PathType Leaf)){throw "Required update file missing: $source"}
}
$rows=@(foreach($pair in $files.GetEnumerator()){
    $target=Join-Path $stage $pair.Key
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
    Copy-Item -LiteralPath $pair.Value -Destination $target
    [pscustomobject]@{Path=$pair.Key;Bytes=(Get-Item -LiteralPath $target).Length;Sha256=(Get-FileHash -LiteralPath $target).Hash}
})
$manifest=[ordered]@{
    Package=$package;CoopAssist=$Version;Revision=$revision;SSMP='0.4.142'
    GameTested=$false;MultiplayerTested=$false;CoopAssistSha256=(Get-FileHash -LiteralPath $dll).Hash
    ProgressCatalogSha256=$catalogHash;Files=$rows
}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $stage 'Manifest.json') -Encoding utf8
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$OutputPath,[IO.Compression.CompressionLevel]::Optimal,$false)
Get-Item -LiteralPath $OutputPath|Select-Object FullName,Length
