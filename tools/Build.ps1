param([string]$GameRoot = 'D:\Game\Hollow Knight Silksong')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$compiler = 'C:\Program Files\dotnet\sdk\9.0.315\Roslyn\bincore\csc.dll'
$referenceRoot = 'C:\Program Files\dotnet\packs\NETStandard.Library.Ref\2.1.0\ref\netstandard2.1'
$releaseRoot = Join-Path $taskRoot 'upstream-release\silksong-coop-mod-0.4.142'
$ssmpDll = Join-Path $taskRoot 'BepInEx\plugins\SSMP\SSMP.dll'
if (-not (Test-Path -LiteralPath $ssmpDll)) { $ssmpDll = Join-Path $taskRoot '..\..\BepInEx\plugins\SSMP\SSMP.dll' }
if (-not (Test-Path -LiteralPath $ssmpDll)) { $ssmpDll = Join-Path $GameRoot 'BepInEx\plugins\SSMP\SSMP.dll' }
if (-not (Test-Path -LiteralPath $ssmpDll)) { $ssmpDll = Join-Path $releaseRoot 'SSMP\SSMP.dll' }
$modMenuDll = Join-Path $taskRoot 'modmenu-release\Silksong.ModMenu.dll'
if (-not (Test-Path -LiteralPath $modMenuDll)) { $modMenuDll = Join-Path $taskRoot '..\..\BepInEx\plugins\ModMenu\Silksong.ModMenu.dll' }
$outDir = Join-Path $taskRoot 'build'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$options = @('/nostdlib+', '/target:library', '/langversion:latest', '/optimize+', '/deterministic+', '/debug:portable', "/out:`"$outDir\Silksong.CoopAssist.dll`"")
$options += Get-ChildItem -LiteralPath $referenceRoot -Filter '*.dll' | ForEach-Object { "/reference:`"$($_.FullName)`"" }
$options += @($ssmpDll, "$GameRoot\BepInEx\core\BepInEx.dll", "$GameRoot\BepInEx\core\0Harmony.dll") | ForEach-Object { "/reference:`"$_`"" }
$managed = Join-Path $GameRoot 'Hollow Knight Silksong_Data\Managed'
$catalogPath=Join-Path $taskRoot 'assets/ProgressSyncCatalog.json'
$catalog=Get-Content -LiteralPath $catalogPath -Raw|ConvertFrom-Json
if(-not $catalog.Complete -or $catalog.AssemblySha256 -ne (Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')).Hash){throw 'Progress catalog is incomplete or targets another game build; regenerate it before building'}
$options += @('Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','Unity.TextMeshPro.dll','UnityEngine.UI.dll','Assembly-CSharp-firstpass.dll','UnityEngine.TextRenderingModule.dll','TeamCherry.TK2D.dll') | ForEach-Object { "/reference:`"$managed\$_`"" }
$options += '/reference:"' + $modMenuDll + '"'
$options += @('UnityEngine.TextCoreFontEngineModule.dll','UnityEngine.TextCoreTextEngineModule.dll') | ForEach-Object { "/reference:`"$managed\$_`"" }
$options += '/reference:"' + $managed + '\UnityEngine.UIModule.dll"'
$options += '/reference:"' + $managed + '\PlayMaker.dll"'
$options += '/reference:"' + $managed + '\Newtonsoft.Json.dll"'
$options += '/resource:"' + $catalogPath + '",Silksong.CoopAssist.ProgressSyncCatalog.json'
$ssmpAssembly = [Reflection.Assembly]::LoadFile($ssmpDll)
$storyItemsPath = Join-Path $outDir 'NativeStoryItems.json'
$storyItemsStream = $ssmpAssembly.GetManifestResourceStream('SSMP.Resource.coop-story-items.json')
if (-not $storyItemsStream) { throw 'Bundled SSMP story item catalog missing' }
$storyItemsFile = [IO.File]::Create($storyItemsPath)
try { $storyItemsStream.CopyTo($storyItemsFile) }
finally { $storyItemsFile.Dispose(); $storyItemsStream.Dispose() }
$options += '/resource:"' + $storyItemsPath + '",Silksong.CoopAssist.NativeStoryItems.json'
$options += Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' }
$rsp = Join-Path $outDir 'build.rsp'
$options | Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet $compiler /noconfig "@$rsp"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$fontSource=Join-Path $taskRoot 'assets\Fonts\AaXuanYuanTi-2.ttf'
if(-not (Test-Path -LiteralPath $fontSource)){$fontSource=Join-Path $GameRoot 'BepInEx\plugins\SSMP\Fonts\AaXuanYuanTi-2.ttf'}
if(-not (Test-Path -LiteralPath $fontSource)){$fontSource=Join-Path $taskRoot '..\..\BepInEx\plugins\SSMP\Fonts\AaXuanYuanTi-2.ttf'}
if(-not (Test-Path -LiteralPath $fontSource)){throw 'AaXuanYuanTi font is missing: put it in assets/Fonts or install the coop package so the game folder has it'}
New-Item -ItemType Directory -Path (Join-Path $outDir 'Fonts') -Force|Out-Null
Copy-Item -LiteralPath $fontSource -Destination (Join-Path $outDir 'Fonts\AaXuanYuanTi-2.ttf') -Force




