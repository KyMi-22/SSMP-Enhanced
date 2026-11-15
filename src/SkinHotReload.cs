using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Silksong.CoopAssist
{
    internal static class SkinHotReload
    {
        sealed class Original
        {
            internal WeakReference<Texture2D> Target;
            internal Texture2D Copy;
            internal FilterMode Filter;
            internal TextureWrapMode Wrap;
            internal int Aniso;
            internal float Bias;
        }
        static readonly Dictionary<int,Original> originals=new Dictionary<int,Original>();
        static readonly HashSet<int> captureFailed=new HashSet<int>();
        static readonly List<int> dead=new List<int>();
        static readonly Type own=typeof(SSMP.Api.Client.ClientAddon).Assembly.GetType("SSMP.Game.Client.Skin.OwnSkin");
        static readonly Type nativeT2D=typeof(SSMP.Api.Client.ClientAddon).Assembly.GetType("SSMP.Game.Client.Skin.CustomizerT2D");
        static MonoBehaviour loader;
        static ManualLogSource log;
        static MethodInfo swap,choose,dress,afterChosen;
        static FieldInfo mapField,keysField;
        static bool suspended,redressSoon,rescanSoon,sceneScanning,selectionQueued;
        static object queuedSelection;
        public static bool Available {get;private set;}
        public static bool Busy {get;private set;}
        internal static bool HasQueuedSelection=>selectionQueued;
        public static string Status {get;private set;}="";
        public static void Reject(string message){Status=message;log?.LogWarning("[SkinReload] "+message);}
        public static void Install(MonoBehaviour plugin,ManualLogSource logger)
        {
            log=logger;loader=plugin;
            if(loader==null||(SystemInfo.copyTextureSupport&CopyTextureSupport.Basic)==0)return;
            var type=loader.GetType();
            swap=AccessTools.Method(type,"TrySwap",new[]{typeof(Texture2D)});
            mapField=AccessTools.Field(type,"_map");keysField=AccessTools.Field(type,"_keys");
            choose=AccessTools.Method(own,"Choose");dress=AccessTools.Method(own,"DressLoaded");
            afterChosen=AccessTools.Method(nativeT2D,"AfterSkinsChosen");
            var classic=AccessTools.TypeByName("Customizer.Customizer");
            var sheets=AccessTools.Method(classic,"GetTexturePacks");
            if(swap==null||mapField==null||keysField==null||choose==null||dress==null||sheets==null||afterChosen==null)return;
            var harmony=new Harmony("silksong.coopassist.skinhotreload");
            harmony.Patch(swap,prefix:new HarmonyMethod(typeof(SkinHotReload),nameof(BeforeSwap)){priority=Priority.First});
            harmony.Patch(sheets,prefix:new HarmonyMethod(typeof(SkinHotReload),nameof(DressCollections)));
            harmony.Patch(AccessTools.Method(own,"LetGo"),postfix:new HarmonyMethod(typeof(SkinHotReload),nameof(AfterLetGo)));
            harmony.Patch(choose,postfix:new HarmonyMethod(typeof(SkinHotReload),nameof(AfterChoose)));
            harmony.Patch(AccessTools.Method(typeof(Graphic),"OnEnable"),postfix:new HarmonyMethod(typeof(SkinHotReload),nameof(AfterGraphicEnabled)));
            Available=true;
            log.LogInfo("[SkinReload] GPU-only menu/UI restore installed; no unreadable texture reinitialization; native character dressing retained.");
        }
        static void AfterChoose(object __0)
        {
            // Never cancel the native choice. Its character/peer update must not depend
            // on UI decoding, GPU restoration, or CoopAssist's rescue-state selector gate.
            if(!Available)return;
            var owner=CoopAssistPlugin.Instance;
            if(owner==null)return;
            queuedSelection=__0;selectionQueued=true;
            Status=Busy?"角色已选择，正在排队刷新最新 UI…":"角色已选择，正在准备刷新 UI…";
            log.LogInfo("[SkinReload] Native character choice accepted; UI refresh queued.");
            if(!Busy&&!owner.SkinTransition)BeginReload(owner);
        }
        static void AfterGraphicEnabled(Graphic __instance)
        {
            // Save-slot panels can become active inside Menu_Title without a scene change
            // or an Image.sprite setter call. Refresh just that graphic's texture.
            if(!Available||__instance==null)return;
            Texture2D texture=null;
            if(__instance is Image image)
            {
                var sprite=image.overrideSprite;if(sprite==null)sprite=image.sprite;
                if(sprite!=null)texture=sprite.texture;
            }
            else if(__instance is RawImage raw)texture=raw.texture as Texture2D;
            if(texture==null)return;
            if(!(mapField.GetValue(loader) is Dictionary<string,(byte[],int,int)> map)||
                !map.TryGetValue(SkinReloadPlan.Normalize(texture.name),out var art)||art.Item2!=texture.width||art.Item3!=texture.height)return;
            if(Busy||suspended){rescanSoon=true;return;}
            try{swap.Invoke(loader,new object[]{texture});}
            catch(Exception e){log.LogWarning("[SkinReload] Menu graphic not refreshed: "+e.GetBaseException().Message);}
        }
        static bool DressCollections()
        {
            // Avoid the old loader's anonymous textures and duplicate collection-name dictionary.
            // Native OwnSkin preserves originals and excludes the named clones used for peers.
            rescanSoon=true;
            if(Busy)redressSoon=true;
            else {Prune();dress.Invoke(null,null);}
            return false;
        }
        static void AfterLetGo(){redressSoon=true;rescanSoon=true;}
        public static void Tick(CoopAssistPlugin owner)
        {
            if(!Available||Busy||owner.SkinTransition)return;
            if(selectionQueued){BeginReload(owner);return;}
            if(redressSoon){redressSoon=false;dress.Invoke(null,null);Prune();}
            if(rescanSoon&&!sceneScanning){rescanSoon=false;sceneScanning=true;owner.StartCoroutine(ScanScene());}
        }
        static IEnumerator ScanScene()
        {
            try
            {
                foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if(Busy){rescanSoon=true;yield break;}
                    if(texture==null||texture.name.StartsWith("CoopAssist original ",StringComparison.Ordinal))continue;
                    if(!(mapField.GetValue(loader) is Dictionary<string,(byte[],int,int)> map)||!map.ContainsKey(SkinReloadPlan.Normalize(texture.name)))continue;
                    try{swap.Invoke(loader,new object[]{texture});}catch(Exception e){log.LogWarning("[SkinReload] Scene texture not refreshed: "+e.GetBaseException().Message);}
                    yield return null;
                }
            }
            finally{sceneScanning=false;}
        }
        static bool BeforeSwap(object __instance,Texture2D __0,ref bool __result)
        {
            __result=false;
            if(__0==null||suspended||__0.name.StartsWith("CoopAssist original ",StringComparison.Ordinal))return false;
            if(!ReferenceEquals(__instance,loader))return true;
            int id=__0.GetInstanceID();
            if(originals.TryGetValue(id,out var saved)&&saved.Target.TryGetTarget(out var previous)&&previous==__0)return true;
            if(saved!=null){Object.Destroy(saved.Copy);originals.Remove(id);}
            if(captureFailed.Contains(id))return false;
            if(!(mapField.GetValue(loader) is Dictionary<string,(byte[],int,int)> map)||
                !map.TryGetValue(SkinReloadPlan.Normalize(__0.name),out var image)||image.Item2!=__0.width||image.Item3!=__0.height)return true;
            Texture2D copy=null;
            try
            {
                int full=1,size=Math.Max(__0.width,__0.height);while((size>>=1)>0)full++;
                if(__0.mipmapCount!=1&&__0.mipmapCount!=full)
                    throw new NotSupportedException("Partial mip chains cannot be reversibly reinitialized");
                copy=new Texture2D(__0.width,__0.height,__0.graphicsFormat,__0.mipmapCount,TextureCreationFlags.DontInitializePixels);
                copy.name="CoopAssist original "+__0.name;copy.hideFlags=HideFlags.HideAndDontSave;
                copy.Apply(false,true);
                for(int mip=0;mip<__0.mipmapCount;mip++)Graphics.CopyTexture(__0,0,mip,copy,0,mip);
                originals[id]=new Original{Target=new WeakReference<Texture2D>(__0),Copy=copy,
                    Filter=__0.filterMode,Wrap=__0.wrapMode,Aniso=__0.anisoLevel,Bias=__0.mipMapBias};
                return true;
            }
            catch(Exception e)
            {
                if(copy!=null)Object.Destroy(copy);captureFailed.Add(id);
                log.LogWarning("[SkinReload] Left texture unchanged because its original could not be preserved: "+__0.name+" / "+e.Message);
                return false;
            }
        }
        static void Prune()
        {
            dead.Clear();
            foreach(var pair in originals)if(!pair.Value.Target.TryGetTarget(out var texture)||texture==null)dead.Add(pair.Key);
            foreach(var id in dead){Object.Destroy(originals[id].Copy);originals.Remove(id);captureFailed.Remove(id);}
        }
        static void Restore(Original original)
        {
            if(!original.Target.TryGetTarget(out var texture)||texture==null)return;
            var source=original.Copy;
            if(source==null)throw new InvalidOperationException("Original UI texture was released");
            // Loading another PNG does not restore CPU readability in the shipped Unity build.
            // Preserve this texture object, its dimensions and the current decoded format: menus,
            // cached sprites and materials already refer to it. Only its GPU pixels are restored.
            if(texture.width!=source.width||texture.height!=source.height)
                throw new InvalidOperationException("UI texture dimensions changed unexpectedly: "+texture.name);
            // Release a CPU shadow only while one exists. Applying after the GPU copy would
            // overwrite the restored image with the previous skin's CPU pixels.
            if(texture.isReadable)texture.Apply(false,true);
            if(texture.graphicsFormat==source.graphicsFormat&&texture.mipmapCount==source.mipmapCount)
                for(int mip=0;mip<texture.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,texture,0,mip);
            else RestoreConverted(source,texture);
            texture.filterMode=original.Filter;texture.wrapMode=original.Wrap;
            texture.anisoLevel=original.Aniso;texture.mipMapBias=original.Bias;
        }
        static void RestoreConverted(Texture2D source,Texture2D texture)
        {
            // The original BC7 atlas and a loaded RGBA PNG cannot be CopyTexture'd directly.
            // Convert on the GPU into the existing target format and regenerate every mip.
            var descriptor=new RenderTextureDescriptor(texture.width,texture.height){
                graphicsFormat=texture.graphicsFormat,depthBufferBits=0,msaaSamples=1,
                useMipMap=texture.mipmapCount>1,autoGenerateMips=false,mipCount=texture.mipmapCount};
            var previous=RenderTexture.active;var previousSrgb=GL.sRGBWrite;
            var converted=RenderTexture.GetTemporary(descriptor);
            try
            {
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear&&GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
                Graphics.Blit(source,converted);
                if(texture.mipmapCount>1)converted.GenerateMips();
                for(int mip=0;mip<texture.mipmapCount;mip++)Graphics.CopyTexture(converted,0,mip,texture,0,mip);
            }
            finally
            {
                RenderTexture.active=previous;GL.sRGBWrite=previousSrgb;
                RenderTexture.ReleaseTemporary(converted);
            }
        }
        static void ResetSets()
        {
            foreach(var name in new[]{"_replacedIds","_notOurTex"})
                (AccessTools.Field(loader.GetType(),name)?.GetValue(loader) as HashSet<int>)?.Clear();
            (AccessTools.Field(loader.GetType(),"_satisfiedKeys")?.GetValue(loader) as HashSet<string>)?.Clear();
            // Keep SSMP's true game-art cutouts and snapshots; only selection bookkeeping expires.
            foreach(var name in new[]{"Dressed","ImageOfPage","Decided","NotDressed"})
            {
                var collection=AccessTools.Field(nativeT2D,name)?.GetValue(null);
                collection?.GetType().GetMethod("Clear",Type.EmptyTypes)?.Invoke(collection,null);
            }
        }
        static void AnnounceDirectories()
        {
            var selected=new List<string>(SkinSelection.ActiveDirectories(System.IO.Path.Combine(Application.dataPath,"Mods","Customizer")));
            afterChosen.Invoke(null,new object[]{selected});
        }
        static void ForceLook(CoopAssistPlugin owner)
        {
            var api=owner.SkinApi;
            if(api?.NetClient.IsConnected==true)AccessTools.Method(api.ClientManager.GetType(),"SendSkinLook")?.Invoke(api.ClientManager,null);
            var attacks=own.Assembly.GetType("SSMP.Game.Client.Skin.AtlasSlashes");
            AccessTools.Method(attacks,"Refresh")?.Invoke(null,null);
        }
        public static void Request(CoopAssistPlugin owner,object selected)
        {
            // F7/ModMenu use exactly the same native path as SSMP's own selector.
            choose.Invoke(null,new[]{selected});
        }
        static void BeginReload(CoopAssistPlugin owner)
        {
            var selected=queuedSelection;queuedSelection=null;selectionQueued=false;
            Busy=true;Status="正在读取皮肤与 UI…";
            owner.StartCoroutine(Run(owner,selected));
        }
        static IEnumerator Run(CoopAssistPlugin owner,object selected)
        {
            Exception error=null;IEnumerator steps=null;
            try
            {
                var dir=selected==null?null:(string)CoopAssistPlugin.Value(selected,"Dir");
                var read=Task.Run(()=>SkinReloadPlan.Read(dir));
                while(!read.IsCompleted)yield return null;
                if(read.IsFaulted){error=read.Exception.GetBaseException();}
                else
                {
                    float deadline=Time.realtimeSinceStartup+30;
                    while(owner.SkinTransition&&Time.realtimeSinceStartup<deadline)yield return null;
                    if(owner.SkinTransition)error=new InvalidOperationException("等待换图结束超时。");
                    else if(selectionQueued){Status="正在转到最新皮肤的 UI…";yield break;}
                    else
                    {
                        steps=Apply(owner,selected,read.Result);
                        while(true)
                        {
                            bool next=false;object current=null;
                            try{next=steps.MoveNext();if(next)current=steps.Current;}catch(Exception e){error=e;}
                            if(error!=null||!next)break;yield return current;
                        }
                        (steps as IDisposable)?.Dispose();steps=null;
                    }
                }
                if(error==null&&selectionQueued){Status="正在转到最新皮肤的 UI…";yield break;}
                if(error!=null)
                {
                    log.LogWarning("[SkinReload] UI refresh failed; preserving native character choice: "+error);
                    steps=RecoverUi(owner);
                    Exception rollback=null;
                    while(true)
                    {
                        bool next=false;object current=null;
                        try{next=steps.MoveNext();if(next)current=steps.Current;}catch(Exception e){rollback=e;}
                        if(rollback!=null||!next)break;yield return current;
                    }
                    if(rollback!=null){log.LogError("[SkinReload] Recovery incomplete: "+rollback);Status="皮肤恢复失败，请保存日志并重启游戏。";}
                    else Status="皮肤选择已保留；UI 刷新失败，已恢复原版 UI："+error.GetBaseException().Message;
                }
                else Status=(selected==null?"已恢复原版外观与 UI":"已应用 "+CoopAssistPlugin.Value(selected,"Name"))+"，无需重启。";
                if(error==null)log.LogInfo("[SkinReload] Completed; originals="+originals.Count+"; "+Status);
            }
            finally
            {
                (steps as IDisposable)?.Dispose();suspended=false;Busy=false;
            }
        }
        static IEnumerator Apply(CoopAssistPlugin owner,object selected,Dictionary<string,(byte[],int,int)> map)
        {
            suspended=true;Status="正在恢复原图…";Prune();
            foreach(var original in new List<Original>(originals.Values)){Restore(original);yield return null;}
            mapField.SetValue(loader,map);keysField.SetValue(loader,new HashSet<string>(map.Keys,StringComparer.OrdinalIgnoreCase));ResetSets();
            AnnounceDirectories();suspended=false;loader.enabled=true;
            Status="正在应用角色与 UI…";
            foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if(texture==null||texture.name.StartsWith("CoopAssist original ",StringComparison.Ordinal))continue;
                if(!map.TryGetValue(SkinReloadPlan.Normalize(texture.name),out var image)||texture.width!=image.Item2||texture.height!=image.Item3)continue;
                bool applied=(bool)swap.Invoke(loader,new object[]{texture});
                var replaced=(HashSet<int>)AccessTools.Field(loader.GetType(),"_replacedIds").GetValue(loader);
                if(!applied&&!replaced.Contains(texture.GetInstanceID()))throw new InvalidOperationException("贴图未能加载："+texture.name);
                yield return null;
            }
            if(captureFailed.Count>0)throw new InvalidOperationException("部分 UI 原图无法保存");
            // New pages remain covered by the loader's sprite/material setters; its ordinary
            // scene watch also resumes with the new map. No persistent full-resource polling.
            float deadline=Time.realtimeSinceStartup+30;
            while(true)
            {
                if(Time.realtimeSinceStartup>=deadline)throw new TimeoutException("等待皮肤贴图超时");
                if(owner.SkinTransition){yield return null;continue;}
                if(redressSoon){redressSoon=false;dress.Invoke(null,null);}
                if((bool)AccessTools.Property(own,"IsDressing").GetValue(null)){yield return null;continue;}
                // Let scene/Hero Start callbacks settle before publishing completion.
                yield return null;
                if(!owner.SkinTransition&&!redressSoon&&!(bool)AccessTools.Property(own,"IsDressing").GetValue(null))break;
            }
            if(!selectionQueued)ForceLook(owner);
        }
        static IEnumerator RecoverUi(CoopAssistPlugin owner)
        {
            suspended=true;
            foreach(var original in new List<Original>(originals.Values)){Restore(original);yield return null;}
            mapField.SetValue(loader,new Dictionary<string,(byte[],int,int)>(StringComparer.OrdinalIgnoreCase));
            keysField.SetValue(loader,new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ResetSets();captureFailed.Clear();
            afterChosen.Invoke(null,new object[]{new List<string>()});
            suspended=false;
            if(!selectionQueued)ForceLook(owner);
        }
    }
}
