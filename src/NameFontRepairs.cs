using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Modern=TMPro;
using Legacy=TMProOld;

namespace Silksong.CoopAssist
{
    internal static class NameFontRepairs
    {
        internal const float NamePointSize=NameLabelStyle.PointSize;
        static ManualLogSource log;
        static Modern.TMP_FontAsset dynamicFont;
        static readonly List<Legacy.TMP_FontAsset> pages=new List<Legacy.TMP_FontAsset>();
        static readonly HashSet<string> prepared=new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> failed=new HashSet<string>(StringComparer.Ordinal);
        static int characterCount=-1;
        public static void Install(ManualLogSource logger)
        {
            log=logger;
            var manager=typeof(SSMP.Api.Client.ClientAddon).Assembly.GetType("SSMP.Ui.Resources.FontManager");
            var pick=AccessTools.Method(manager,"PickInGameNameFont",new[]{typeof(string)});
            if(pick==null||pick.ReturnType!=typeof(Legacy.TMP_FontAsset))
            {log.LogWarning("[NameFont] Unsupported in-game name font API.");return;}
            var harmony=new Harmony("silksong.coopassist.namefonts");
            harmony.Patch(pick,
                postfix:new HarmonyMethod(typeof(NameFontRepairs),nameof(AfterPick)));
            var players=typeof(SSMP.Api.Client.ClientAddon).Assembly.GetType("SSMP.Game.Client.PlayerManager",true);
            harmony.Patch(AccessTools.Method(players,"AddNameToPlayer"),postfix:new HarmonyMethod(typeof(NameFontRepairs),nameof(AfterNamed)));
            harmony.Patch(AccessTools.Method(players,"DrawAsOwnName"),postfix:new HarmonyMethod(typeof(NameFontRepairs),nameof(AfterOwnName)));
            harmony.Patch(AccessTools.Method(players,"ChangeNameColor"),postfix:new HarmonyMethod(typeof(NameFontRepairs),nameof(AfterNameColour)));
            harmony.Patch(AccessTools.Method(players,"OwnNameColour"),prefix:new HarmonyMethod(typeof(NameFontRepairs),nameof(WhiteNameColour)));
            log.LogInfo("[NameFont] AaXuanYuanTi Chinese player names at 27 pt; native legacy name colour hooks installed.");
        }
        static void AfterPick(string __0,ref Legacy.TMP_FontAsset __result)
        {
            if(string.IsNullOrEmpty(__0)||!NameGlyphLayout.HasNonLatin(__0))return;
            try
            {
                // The requested face must be chosen even if the game's stock CJK face has every glyph.
                if(failed.Contains(__0))return;
                if(dynamicFont==null&&!Create()){failed.Add(__0);return;}
                if(!prepared.Contains(__0))
                {
                    // Names are short, but joins and renames must not create unbounded atlases.
                    if(prepared.Count>=512||dynamicFont.atlasTextureCount>=4)
                    {failed.Add(__0);log.LogWarning("[NameFont] Name atlas capacity reached; retaining native fallback.");return;}
                    dynamicFont.TryAddCharacters(__0+" _?",out string missing);
                    if(dynamicFont.characterTable.Count!=characterCount)Rebuild(__result);
                    if(!string.IsNullOrEmpty(missing))
                    {failed.Add(__0);log.LogWarning("[NameFont] The local font lacks "+missing.Length+" requested name character(s).");}
                    prepared.Add(__0);
                }
                if(pages.Count>0)__result=pages[0];
            }
            catch(Exception e)
            {
                failed.Add(__0);
                log.LogWarning("[NameFont] Retaining native name font after fallback error: "+e.GetBaseException().Message);
            }
        }
        static bool Create()
        {
            var directory=Path.GetDirectoryName(typeof(NameFontRepairs).Assembly.Location);
            var path=Path.Combine(directory,"Fonts","AaXuanYuanTi-2.ttf");
            if(File.Exists(path))
            {
                try
                {
                    dynamicFont=Modern.TMP_FontAsset.CreateFontAsset(path,0,48,6,GlyphRenderMode.SDFAA,2048,2048);
                    if(dynamicFont==null)throw new InvalidOperationException("Could not create AaXuanYuanTi font asset");
                    dynamicFont.isMultiAtlasTexturesEnabled=true;
                    dynamicFont.name="CoopAssist AaXuanYuanTi names";
                    dynamicFont.hideFlags=HideFlags.HideAndDontSave;
                    log.LogInfo("[NameFont] Loaded bundled AaXuanYuanTi-2.ttf; Chinese names use 27 pt.");
                    return true;
                }
                catch(Exception e){log.LogWarning("[NameFont] Could not read bundled AaXuanYuanTi: "+e.GetBaseException().Message);}
            }
            log.LogWarning("[NameFont] Bundled font unavailable: "+path+". Keep the Fonts directory beside Silksong.CoopAssist.dll.");return false;
        }
        static void AfterNamed(GameObject __0)
        {
            if(__0==null)return;
            foreach(var label in __0.GetComponentsInChildren<Legacy.TextMeshPro>(true))
                if(label.gameObject.name=="Username")
                {StyleName(label);CoopAssistPlugin.Instance?.ApplyPeerNameColour(label);}
            CoopAssistPlugin.Instance?.InvalidateNameLabels();
        }
        static bool WhiteNameColour(object __0,ref Color __result)
        {
            if(!string.Equals(ReflectionCache.Get(__0,"OwnNameColour") as string,"white",StringComparison.OrdinalIgnoreCase))return true;
            __result=Color.white;return false;
        }
        static void AfterOwnName(Legacy.TextMeshPro __0)=>StyleName(__0);
        static void AfterNameColour(Legacy.TextMeshPro __0)=>CoopAssistPlugin.Instance?.ApplyPeerNameColour(__0);
        internal static void StyleName(Legacy.TextMeshPro label)
        {
            if(label==null||!NameGlyphLayout.HasNonLatin(label.text))return;
            var font=label.font;AfterPick(label.text,ref font);
            NameLabelStyle.ApplyFont(label,font);
        }
        internal static void ApplyWeight(Legacy.TextMeshPro label,bool bold)
        {
            NameLabelStyle.ApplyWeight(label,bold);
        }
        static void Rebuild(Legacy.TMP_FontAsset template)
        {
            var atlases=dynamicFont.atlasTextures;var face=dynamicFont.faceInfo;
            for(int index=0;index<dynamicFont.atlasTextureCount;index++)
            {
                while(pages.Count<=index)
                {
                    var font=template!=null?UnityEngine.Object.Instantiate(template):ScriptableObject.CreateInstance<Legacy.TMP_FontAsset>();
                    font.name="CoopAssist Chinese names page "+pages.Count;
                    font.hideFlags=HideFlags.HideAndDontSave;font.hashCode=font.GetInstanceID();
                    font.material=new Material(dynamicFont.material){name=font.name+" material",hideFlags=HideFlags.HideAndDontSave};
                    font.materialHashCode=font.material.GetInstanceID();font.fontWeights=new Legacy.TMP_FontWeights[10];
                    font.fallbackFontAssets=new List<Legacy.TMP_FontAsset>();font.fontAssetType=Legacy.TMP_FontAsset.FontAssetTypes.SDF;
                    pages.Add(font);
                }
                var target=pages[index];target.atlas=atlases[index];target.material.mainTexture=atlases[index];
                var glyphs=new List<Legacy.TMP_Glyph>();
                foreach(var character in dynamicFont.characterTable)
                {
                    var glyph=character.glyph;if(glyph==null||glyph.atlasIndex!=index)continue;
                    var rect=glyph.glyphRect;var metrics=glyph.metrics;
                    glyphs.Add(new Legacy.TMP_Glyph{id=(int)character.unicode,x=rect.x,
                        y=NameGlyphLayout.LegacyTop(atlases[index].height,rect.y,rect.height),width=rect.width,height=rect.height,
                        xOffset=metrics.horizontalBearingX,yOffset=metrics.horizontalBearingY,xAdvance=metrics.horizontalAdvance,scale=glyph.scale});
                }
                target.AddFaceInfo(new Legacy.FaceInfo{Name=fontName(),PointSize=face.pointSize,Scale=face.scale==0?1:face.scale,
                    CharacterCount=glyphs.Count,LineHeight=face.lineHeight,Baseline=face.baseline,Ascender=face.ascentLine,
                    CapHeight=face.capLine,Descender=face.descentLine,CenterLine=(face.ascentLine+face.descentLine)*.5f,
                    SuperscriptOffset=face.superscriptOffset,SubscriptOffset=face.subscriptOffset,SubSize=face.subscriptSize,
                    Underline=face.underlineOffset,UnderlineThickness=face.underlineThickness,TabWidth=face.tabWidth,
                    Padding=dynamicFont.atlasPadding,AtlasWidth=atlases[index].width,AtlasHeight=atlases[index].height});
                target.AddKerningInfo(new Legacy.KerningTable());target.AddGlyphInfo(glyphs.ToArray());target.ReadFontDefinition();
            }
            // One directional fallback chain avoids recursive cycles and permits multiple atlas pages.
            pages[0].fallbackFontAssets.Clear();for(int i=1;i<pages.Count;i++)pages[0].fallbackFontAssets.Add(pages[i]);
            characterCount=dynamicFont.characterTable.Count;
            log.LogInfo("[NameFont] Prepared "+characterCount+" name glyphs on "+pages.Count+" legacy-compatible atlas page(s).");
        }
        static string fontName()=>"AaXuanYuanTi";
    }
}

