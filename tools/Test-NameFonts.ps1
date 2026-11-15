$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot;$outDir=Join-Path $root 'build'
$refs='C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.17\ref\net9.0'
$options=@('/nostdlib+','/target:exe','/langversion:latest',('/out:"'+$outDir+'\NameGlyphTests.dll"'))
$options+=Get-ChildItem -LiteralPath $refs -Filter '*.dll'|ForEach-Object {'/reference:"'+$_.FullName+'"'}
$options+=@('src/NameGlyphLayout.cs','tests/NameGlyphLayoutTests.cs')|ForEach-Object {'"'+(Join-Path $root $_)+'"'}
$rsp=Join-Path $outDir 'name-glyph-tests.rsp';$options|Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll' /noconfig "@$rsp"
if($LASTEXITCODE -ne 0){throw 'Name glyph compilation failed'}
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.17"}}}'|Set-Content -LiteralPath (Join-Path $outDir 'NameGlyphTests.runtimeconfig.json')
& dotnet (Join-Path $outDir 'NameGlyphTests.dll')
if($LASTEXITCODE -ne 0){throw 'Name glyph layout checks failed'}
Add-Type -Path 'D:\Game\Hollow Knight Silksong\BepInEx\core\Mono.Cecil.dll'
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'upstream-release/silksong-coop-mod-0.4.142/SSMP/SSMP.dll'))
try{
 $pick=@($native.MainModule.GetType('SSMP.Ui.Resources.FontManager').Methods|Where-Object Name -EQ 'PickInGameNameFont')
 if($pick.Count -ne 1 -or $pick[0].ReturnType.FullName -ne 'TMProOld.TMP_FontAsset' -or $pick[0].Parameters.Count -ne 1){throw 'Name font selector changed'}
 $manager=$native.MainModule.GetType('SSMP.Game.Client.PlayerManager')
 $colour=$manager.Methods|Where-Object Name -EQ 'ChangeNameColor'
 if($colour.Parameters[0].ParameterType.FullName -ne 'TMProOld.TextMeshPro'){throw 'Native name renderer changed'}
}finally{$native.Dispose()}
$assist=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'build/Silksong.CoopAssist.dll'))
try{
 $hook=$assist.MainModule.GetType('Silksong.CoopAssist.NameFontRepairs').Methods|Where-Object Name -EQ 'AfterPick'
 if($hook.Parameters[1].ParameterType.FullName -ne 'TMProOld.TMP_FontAsset&'){throw 'Name fallback targets the wrong TMP engine'}
 $fonts=$assist.MainModule.GetType('Silksong.CoopAssist.NameFontRepairs')
 if(($fonts.Fields|Where-Object Name -EQ 'NamePointSize').Constant -ne 27){throw 'Name font must be 27 pt'}
 foreach($name in @('AfterOwnName','AfterNameColour','StyleName')){
  if(($fonts.Methods|Where-Object Name -EQ $name).Parameters[0].ParameterType.FullName -ne 'TMProOld.TextMeshPro'){throw ('Wrong name component '+$name)}
 }
 $visual=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin/PeerVisual')
 if(($visual.Fields|Where-Object Name -EQ 'Names').FieldType.FullName -ne 'System.Collections.Generic.List`1<TMProOld.TextMeshPro>'){throw 'Reproduced wrong-modern-TMP colour regression'}
 $create=$fonts.Methods|Where-Object Name -EQ 'Create'
 $strings=@($create.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)
 if('AaXuanYuanTi-2.ttf' -notin $strings -or 'Fonts' -notin $strings -or 'msyh.ttc' -in $strings){throw 'Font must load the bundled Aa face'}
 $pickCalls=@(($fonts.Methods|Where-Object Name -EQ 'AfterPick').Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
 if('Complete' -in $pickCalls){throw 'Custom face must not be skipped because stock CJK glyphs exist'}
 $update=$assist.MainModule.GetType('Silksong.CoopAssist.CoopAssistPlugin').Methods|Where-Object Name -EQ 'UpdateEnhancements'
 if('ColourOwnName' -in @($update.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'ldstr'}|ForEach-Object Operand)){throw 'Local-only display toggle must not cancel shared identity colour'}
}finally{$assist.Dispose()}
$fontPath=Join-Path $root 'build/Fonts/AaXuanYuanTi-2.ttf'
if((Get-FileHash -LiteralPath $fontPath).Hash -ne 'FA0F0367B66A111EEB402E537770EC5DFB69FDDFCEF2E3CE4771D76FBD78AD58'){throw 'Bundled font differs from the user-provided font'}
Add-Type -AssemblyName PresentationCore
$face=[System.Windows.Media.GlyphTypeface]::new([Uri]$fontPath)
if('AaXuanYuanTi' -notin @($face.FamilyNames.Values)){throw 'Wrong font family'}
foreach($ch in ('禾煜阿克尼中文名字空洞骑士丝之歌Aa27').ToCharArray()){if(-not $face.CharacterToGlyphMap.ContainsKey([int]$ch)){throw ('Aa font lacks sample glyph '+$ch)}}
'Passed shipped legacy name-colour components/hooks, forced Aa face/27pt and bundled TTF identity/glyph coverage. GPU/font rasterization not executed.'

