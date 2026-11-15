param(
    [string]$Version,
    [string]$TemplateRoot,
    [string]$OutputPath,
    [switch]$Force
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'build'
$dll=Join-Path $out 'Silksong.CoopAssist.dll'
$font=Join-Path $out 'Fonts/AaXuanYuanTi-2.ttf'
if(-not (Test-Path -LiteralPath $dll -PathType Leaf)){throw 'Run tools/Build.ps1 first'}
if(-not (Test-Path -LiteralPath $font -PathType Leaf)){throw 'Run tools/Build.ps1 so the font is staged in build/Fonts'}
if(-not $TemplateRoot){$TemplateRoot=Join-Path $root 'dist/Silksong-Coop-Enhanced-0.3.0-standard'}
if(-not (Test-Path -LiteralPath $TemplateRoot -PathType Container)){
    throw "Template root missing: $TemplateRoot (unzip the full package once and pass -TemplateRoot)"
}
# Version and revision come from the built plugin, never from the caller's guess.
Add-Type -Path (Join-Path $TemplateRoot 'BepInEx/core/Mono.Cecil.dll')
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try{
    $release=$built.MainModule.GetType('Silksong.CoopAssist.ReleaseInfo')
    $actualVersion=($release.Fields|Where-Object Name -EQ Version).Constant
    $revision=($release.Fields|Where-Object Name -EQ Revision).Constant
}finally{$built.Dispose()}
if($Version -and $Version -cne $actualVersion){throw "Release version differs from the built plugin: $Version vs $actualVersion"}
$Version=$actualVersion
$name="SSMP-Enhanced-$Version"
if(-not $OutputPath){$OutputPath=Join-Path $root "dist/$name.zip"}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if((Test-Path -LiteralPath $OutputPath) -and -not $Force){throw "Output already exists: $OutputPath (pass -Force to replace)"}

# Only the runtime files a player drops next to the game executable; no docs, no rollback, no game data.
$required=@('BepInEx','.doorstop_version','doorstop_config.ini','winhttp.dll')
foreach($item in $required){
    if(-not (Test-Path -LiteralPath (Join-Path $TemplateRoot $item))){throw "Template is missing $item"}
}
$stage=Join-Path $out "release-$Version-$revision"
if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
New-Item -ItemType Directory -Path $stage -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $TemplateRoot 'BepInEx') -Destination $stage -Recurse
foreach($file in '.doorstop_version','doorstop_config.ini','winhttp.dll'){
    Copy-Item -LiteralPath (Join-Path $TemplateRoot $file) -Destination (Join-Path $stage $file)
}
# The template carries the previous build of the plugin; replace the runtime files with this build.
$plugin='BepInEx/plugins/SSMP/Silksong.CoopAssist.dll'
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage $plugin) -Force
New-Item -ItemType Directory -Path (Join-Path $stage 'BepInEx/plugins/SSMP/Fonts') -Force|Out-Null
Copy-Item -LiteralPath $font -Destination (Join-Path $stage 'BepInEx/plugins/SSMP/Fonts/AaXuanYuanTi-2.ttf') -Force
# Personal state never ships: no config, no cache, no logs, no crash dumps.
foreach($drop in 'BepInEx/config','BepInEx/cache','BepInEx/LogOutput.log'){
    $target=Join-Path $stage $drop
    if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
}
Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object {$_.Extension -eq '.log'} | Remove-Item -Force

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$OutputPath,[IO.Compression.CompressionLevel]::Optimal,$false)

# Read the archive back: the four runtime items must be there and the plugin must be this build.
$expected=(Get-FileHash -LiteralPath $dll).Hash
$zip=[IO.Compression.ZipFile]::OpenRead($OutputPath)
try{
    $names=$zip.Entries|ForEach-Object {$_.FullName}
    foreach($item in $required){
        $prefix=$item.Replace('\','/')
        if(-not ($names|Where-Object {$_ -eq $prefix -or $_.StartsWith($prefix+'/')})){
            throw "Archive is missing $item"
        }
    }
    if($names|Where-Object {$_ -match '^Hollow Knight Silksong_Data/'}){
        throw 'Archive must not contain game data'
    }
    $entry=$zip.GetEntry($plugin)
    if(-not $entry){throw 'Archive is missing the plugin'}
    $stream=$entry.Open()
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
    if($hash -ne $expected){throw 'Archive plugin differs from build/Silksong.CoopAssist.dll'}
}finally{$zip.Dispose()}
Get-Item -LiteralPath $OutputPath|Select-Object FullName,Length
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256|Select-Object Hash
"Release: $name [$revision]"
