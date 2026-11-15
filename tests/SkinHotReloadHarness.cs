// Executed without Unity. These doubles model API ordering, not rendering or GPU support.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Silksong.CoopAssist;

namespace BepInEx.Logging { public class ManualLogSource {public void LogInfo(object x){} public void LogWarning(object x){} public void LogError(object x){} } }
namespace HarmonyLib {
 public static class Priority {public const int First=800;}
 public class HarmonyMethod {public MethodInfo method;public int priority; public HarmonyMethod(Type t,string n){method=AccessTools.Method(t,n);} }
 public class Harmony {public Harmony(string id){} public void Patch(MethodInfo original,HarmonyMethod prefix=null,HarmonyMethod postfix=null){if(original==null)throw new Exception("Absent hook");Hooks[original.Name]=prefix?.method??postfix.method;} public static Dictionary<string,MethodInfo> Hooks=new Dictionary<string,MethodInfo>(); }
 public static class AccessTools {
  const BindingFlags Flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  public static Type TypeByName(string name)=>typeof(AccessTools).Assembly.GetType(name);
  public static MethodInfo Method(Type type,string name,Type[] args=null)=>args==null?type?.GetMethod(name,Flags):type?.GetMethod(name,Flags,null,args,null);
  public static FieldInfo Field(Type type,string name)=>type?.GetField(name,Flags);
  public static PropertyInfo Property(Type type,string name)=>type?.GetProperty(name,Flags);
 }
}
namespace UnityEngine.Experimental.Rendering {public enum GraphicsFormat {Original,RGBA} public enum TextureCreationFlags {DontInitializePixels} public static class GraphicsFormatUtility {public static bool IsSRGBFormat(GraphicsFormat format)=>true;} }
namespace UnityEngine.Rendering {[Flags] public enum CopyTextureSupport {Basic=1} }
namespace UnityEngine {
 using UnityEngine.Experimental.Rendering;
 public enum HideFlags {HideAndDontSave} public enum FilterMode {Point,Bilinear} public enum TextureWrapMode {Clamp,Repeat}
 public class Object {
  static int next;public int id=++next;public bool destroyed;public string name="";public HideFlags hideFlags;
  public int GetInstanceID()=>id;public static void Destroy(Object x){if(!ReferenceEquals(x,null))x.destroyed=true;}
  public static bool operator==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.destroyed,bn=ReferenceEquals(b,null)||b.destroyed;return an&&bn||!an&&!bn&&ReferenceEquals(a,b);}
  public static bool operator!=(Object a,Object b)=>!(a==b);public override bool Equals(object b)=>ReferenceEquals(this,b);public override int GetHashCode()=>id;
 }
 public class Texture:Object {
  public int width,height,mipmapCount;public GraphicsFormat graphicsFormat;public bool isReadable=true;public int gpu,cpu;
  public Dictionary<int,int> Mips=new Dictionary<int,int>();
  public int Pixel(int mip)=>Mips.TryGetValue(mip,out var value)?value:gpu;
  public FilterMode filterMode;public TextureWrapMode wrapMode;public int anisoLevel;public float mipMapBias;
 }
 public class Texture2D:Texture {
  public static int Reinitializations;
  public Texture2D(int w,int h,GraphicsFormat format,int mips,TextureCreationFlags flags){width=w;height=h;graphicsFormat=format;mipmapCount=mips;Resources.All.Add(this);}
  public void Apply(bool mips,bool unreadable){if(!isReadable)throw new Exception("Texture is not readable");gpu=cpu;Mips.Clear();if(unreadable)isReadable=false;}
  public bool Reinitialize(int w,int h,GraphicsFormat format,bool mips){Reinitializations++;if(!isReadable)throw new Exception("Texture is not readable");width=w;height=h;graphicsFormat=format;mipmapCount=mips?2:1;gpu=cpu=0;return true;}
 }
 public enum ColorSpace {Gamma,Linear}
 public static class QualitySettings {public static ColorSpace activeColorSpace=ColorSpace.Linear;}
 public static class GL {public static bool sRGBWrite;}
 public struct RenderTextureDescriptor {public int width,height,depthBufferBits,msaaSamples,mipCount;public GraphicsFormat graphicsFormat;public bool useMipMap,autoGenerateMips;public RenderTextureDescriptor(int w,int h){this=default;width=w;height=h;}}
 public class RenderTexture:Texture {
  public static RenderTexture active;public static int Outstanding;
  public static RenderTexture GetTemporary(RenderTextureDescriptor d){Outstanding++;return new RenderTexture{width=d.width,height=d.height,mipmapCount=d.mipCount,graphicsFormat=d.graphicsFormat};}
  public static void ReleaseTemporary(RenderTexture texture){Outstanding--;texture.destroyed=true;}
  public void GenerateMips(){for(var n=1;n<mipmapCount;n++)Mips[n]=gpu;}
 }
 public static class Graphics {
  public static bool FailNextBlit,LastSrgb;public static int Blits;
  public static void Blit(Texture source,RenderTexture target){Blits++;LastSrgb=GL.sRGBWrite;RenderTexture.active=target;if(FailNextBlit){FailNextBlit=false;throw new Exception("Simulated blit failure");}target.gpu=source.Pixel(0);}
  public static void CopyTexture(Texture source,int a,int mip,Texture target,int b,int m){if(source.graphicsFormat!=target.graphicsFormat)throw new Exception("Format mismatch");target.Mips[m]=source.Pixel(mip);if(m==0)target.gpu=source.Pixel(mip);}
 }
 public static class ImageConversion {public static bool LoadImage(Texture2D texture,byte[] bytes,bool nonReadable){texture.isReadable=texture.isReadable&&!nonReadable;texture.graphicsFormat=GraphicsFormat.RGBA;texture.width=texture.height=1;texture.gpu=texture.cpu=0;texture.Mips.Clear();return true;} }
 public static class Resources {public static List<Texture2D> All=new List<Texture2D>();public static T[] FindObjectsOfTypeAll<T>() where T:class{var list=new List<T>();foreach(var x in All)if(x!=null&&x is T typed)list.Add(typed);return list.ToArray();} }
 public static class SystemInfo {public static Rendering.CopyTextureSupport copyTextureSupport=Rendering.CopyTextureSupport.Basic;}
 public static class Application {public static string dataPath;}
 public static class Time {public static float realtimeSinceStartup;}
 public class MonoBehaviour:Object {public bool enabled=true;public static IEnumerator Running;public object StartCoroutine(IEnumerator flow){Running=flow;return null;} }
}
namespace SSMP.Api.Client {public class ClientAddon{} public class Api {public Net NetClient=new Net();public Manager ClientManager=new Manager();} public class Net {public bool IsConnected=true;} public class Manager {public int Sends;private void SendSkinLook(){Sends++;}} }
namespace SSMP.Game.Client.Skin {
 public static class OwnSkin {
  public static bool IsDressing {get;private set;} public static bool Hang;public static int Redresses;
  public static int Commits;
  public static void Choose(object selected){Commits++;var owner=CoopAssistPlugin.Active;foreach(var pack in owner.Packs){string active=Path.Combine(pack.Dir,"active.txt");if(File.Exists(active))File.Delete(active);}if(selected is Pack p)File.WriteAllText(Path.Combine(p.Dir,"active.txt"),"");DressLoaded();if(HarmonyLib.Harmony.Hooks.TryGetValue("Choose",out var hook))hook.Invoke(null,new[]{selected});}
  public static void DressLoaded(){Redresses++;IsDressing=Hang;}
  public static void LetGo(){IsDressing=false;HarmonyLib.Harmony.Hooks["LetGo"].Invoke(null,null);}
 }
 public static class CustomizerT2D {
  public static HashSet<string> Dressed=new HashSet<string>(),NotDressed=new HashSet<string>();
  public static HashSet<int> Decided=new HashSet<int>();public static Dictionary<string,string> ImageOfPage=new Dictionary<string,string>();
  public static List<string> Chosen;
  private static void AfterSkinsChosen(ref List<string> dirs){Chosen=new List<string>(dirs);}
 }
 public static class AtlasSlashes {public static int Refreshes;public static void Refresh(){Refreshes++;} }
}
namespace Customizer {public class Customizer {public static void GetTexturePacks(){} } }
namespace UnityEngine {public class Sprite:Object {public Texture2D texture;} }
namespace UnityEngine.UI {
 public class Graphic:UnityEngine.Object {public void OnEnable(){HarmonyLib.Harmony.Hooks["OnEnable"].Invoke(null,new object[]{this});} }
 public class Image:Graphic {public UnityEngine.Sprite sprite,overrideSprite;}
 public class RawImage:Graphic {public UnityEngine.Texture2D texture;}
}
namespace CustomizerT2DOnly {
 public class CustomizerT2DOnly:UnityEngine.MonoBehaviour {
  public Dictionary<string,(byte[],int,int)> _map=new Dictionary<string,(byte[],int,int)>();
  public HashSet<string> _keys=new HashSet<string>(),_satisfiedKeys=new HashSet<string>();
  public HashSet<int> _replacedIds=new HashSet<int>(),_notOurTex=new HashSet<int>();public bool FailNext;
  public bool TrySwap(UnityEngine.Texture2D texture){
   var args=new object[]{this,texture,false};bool run=(bool)HarmonyLib.Harmony.Hooks["TrySwap"].Invoke(null,args);if(!run)return false;
   if(!_map.TryGetValue(SkinReloadPlan.Normalize(texture.name),out var image)||_replacedIds.Contains(texture.id))return false;
   if(FailNext){FailNext=false;throw new IOException("Simulated image decode failure");}
   UnityEngine.ImageConversion.LoadImage(texture,image.Item1,false);texture.width=image.Item2;texture.height=image.Item3;
   texture.gpu=texture.cpu=image.Item1[32];_replacedIds.Add(texture.id);return true;
  }
 }
}
public class Pack {public string Dir {get;set;}public string Name {get;set;} }
namespace Silksong.CoopAssist {
 public class CoopAssistPlugin:UnityEngine.MonoBehaviour {
  public static CoopAssistPlugin Active;public static CoopAssistPlugin Instance=>Active;public List<Pack> Packs=new List<Pack>();public SSMP.Api.Client.Api SkinApi=new SSMP.Api.Client.Api();public bool SkinTransition,BlockSelector;public bool CanChangeSkin=>!SkinTransition&&!BlockSelector;
  public object CurrentSkin(){foreach(var p in Packs)if(File.Exists(Path.Combine(p.Dir,"active.txt")))return p;return null;}
  public static object Value(object target,string name)=>target?.GetType().GetProperty(name)?.GetValue(target);
 }
}
public static class SkinHotReloadHarness {
 static int checks;static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static byte[] Png(int value){var b=new byte[33];byte[] head={137,80,78,71,13,10,26,10};Array.Copy(head,b,8);b[11]=13;b[12]=73;b[13]=72;b[14]=68;b[15]=82;b[19]=2;b[23]=2;b[32]=(byte)value;return b;}
 static void Drain(Action<int> frame=null){int steps=0;while(SkinHotReload.Busy||SkinHotReload.HasQueuedSelection){if(!SkinHotReload.Busy)SkinHotReload.Tick(CoopAssistPlugin.Active);var flow=UnityEngine.MonoBehaviour.Running;bool next=flow.MoveNext();steps++;UnityEngine.Time.realtimeSinceStartup+=.02f;frame?.Invoke(steps);Thread.Sleep(1);if(steps>10000)throw new Exception("Coroutine stuck");}Check(!SkinHotReload.Busy,"Busy leaked");}
 public static void Main(string[] args){
  string root=Path.Combine(Path.GetTempPath(),"ssmp-hotreload-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var owner=CoopAssistPlugin.Active=new CoopAssistPlugin();UnityEngine.Application.dataPath=root;
   foreach(var item in new[]{("A",31),("B",72),("Broken",99)}){var path=Path.Combine(root,"Mods","Customizer",item.Item1);Directory.CreateDirectory(Path.Combine(path,"Texture2D"));File.WriteAllBytes(Path.Combine(path,"Texture2D","HUD.png"),Png(item.Item2));owner.Packs.Add(new Pack{Dir=path,Name=item.Item1});}
   var loader=new CustomizerT2DOnly.CustomizerT2DOnly();SkinHotReload.Install(loader,new BepInEx.Logging.ManualLogSource());Check(SkinHotReload.Available,"Hooks unavailable");
   var hud=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,2,0){name="HUD",gpu=15,cpu=15,isReadable=false};
   var body=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,1,0){name="peer independent",gpu=88};
   int hudId=hud.GetInstanceID();var priorTarget=new UnityEngine.RenderTexture();UnityEngine.RenderTexture.active=priorTarget;
   var probe=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,1,0){isReadable=false};
   UnityEngine.ImageConversion.LoadImage(probe,Png(1),false);Check(!probe.isReadable,"LoadImage must reproduce sticky unreadable state from the real log");UnityEngine.Object.Destroy(probe);
   for(int round=0;round<5;round++){
    foreach(var pick in new Pack[]{owner.Packs[0],owner.Packs[1],null}){
     SkinHotReload.Request(owner,pick);Drain();Check(ReferenceEquals(owner.CurrentSkin(),pick),"Wrong marker selection");
     Check(hud.gpu==(pick==null?15:pick.Name=="A"?31:72),"GPU art differs after repeated switch");
     Check(hud.GetInstanceID()==hudId,"Cached menu references must keep the same texture object");
     Check(UnityEngine.RenderTexture.Outstanding==0&&ReferenceEquals(UnityEngine.RenderTexture.active,priorTarget)&&!UnityEngine.GL.sRGBWrite,"GPU conversion leaked a render target or colour state");
     if(pick==null)Check(hud.Pixel(1)==15,"Lower UI mips retained the previous skin");
     Check(body.gpu==88,"Unrelated peer texture modified");Check(owner.SkinApi.ClientManager.Sends>0,"No appearance publish");
    }
   }
   // Reproduce the user's SSMP-native menu path rather than only calling Request directly.
   int commits=SSMP.Game.Client.Skin.OwnSkin.Commits;
   SSMP.Game.Client.Skin.OwnSkin.Choose(owner.Packs[1]);
   Check(ReferenceEquals(owner.CurrentSkin(),owner.Packs[1])&&SSMP.Game.Client.Skin.OwnSkin.Commits==commits+1,"Native character choice still waits for UI decoding");
   Check(SkinHotReload.Busy,"Native menu bypassed the full UI transaction");Drain();
   Check(hud.gpu==72&&ReferenceEquals(owner.CurrentSkin(),owner.Packs[1]),"Native menu changed body choice but left the old HUD map");
   Check(SSMP.Game.Client.Skin.OwnSkin.Commits==commits+1,"Native routing recursively committed the choice");
   var late=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,2,0){name="HUD",gpu=15,cpu=15,isReadable=false};
   new UnityEngine.UI.Image{sprite=new UnityEngine.Sprite{texture=late}}.OnEnable();
   Check(late.gpu==72,"Late save-slot image kept startup art until a scene change");
   var overridden=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,2,0){name="HUD",gpu=15,cpu=15,isReadable=false};
   var ignored=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,2,0){name="not selected",gpu=88};
   new UnityEngine.UI.Image{sprite=new UnityEngine.Sprite{texture=ignored},overrideSprite=new UnityEngine.Sprite{texture=overridden}}.OnEnable();
   Check(overridden.gpu==72&&ignored.gpu==88,"Late UI refreshed the wrong sprite instead of overrideSprite");
   var raw=new UnityEngine.Texture2D(2,2,UnityEngine.Experimental.Rendering.GraphicsFormat.Original,2,0){name="HUD",gpu=15,cpu=15,isReadable=false};
   new UnityEngine.UI.RawImage{texture=raw}.OnEnable();Check(raw.gpu==72,"Late raw menu image was not refreshed");
   owner.SkinTransition=true;SSMP.Game.Client.Skin.OwnSkin.Choose(owner.Packs[0]);
   Check(!SkinHotReload.Busy&&SkinHotReload.HasQueuedSelection&&ReferenceEquals(owner.CurrentSkin(),owner.Packs[0]),"Scene guard cancelled native character selection instead of deferring UI");
   owner.SkinTransition=false;Drain();
   Check(hud.gpu==31&&late.gpu==31&&overridden.gpu==31&&raw.gpu==31,"Already cached save/menu images were not refreshed by native selection");
   SSMP.Game.Client.Skin.OwnSkin.Choose(null);Drain();
   Check(hud.gpu==15&&late.gpu==15&&overridden.gpu==15&&raw.gpu==15,"Native vanilla selection failed to restore menu originals");
   UnityEngine.Object.Destroy(late);UnityEngine.Object.Destroy(overridden);UnityEngine.Object.Destroy(raw);
   Check(!hud.isReadable&&hud.graphicsFormat==UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA,"Vanilla restore must preserve decoded format and GPU-only texture identity");
   Check(UnityEngine.Texture2D.Reinitializations==0,"UI restore called the API that failed in the real game");
   Check(UnityEngine.Graphics.LastSrgb,"Linear colour space requires sRGB encoding for the UI render target");
   int before=SSMP.Game.Client.Skin.OwnSkin.Redresses;SkinHotReload.Request(owner,owner.Packs[0]);
   bool injected=false;Drain(step=>{if(!injected&&File.Exists(Path.Combine(owner.Packs[0].Dir,"active.txt"))){injected=true;SSMP.Game.Client.Skin.OwnSkin.LetGo();}});
   Check(injected&&SSMP.Game.Client.Skin.OwnSkin.Redresses>before+1,"Hero Start redress was swallowed");
   UnityEngine.Graphics.FailNextBlit=true;SkinHotReload.Request(owner,owner.Packs[1]);Drain();
   Check(ReferenceEquals(owner.CurrentSkin(),owner.Packs[1])&&hud.gpu==15,"GPU restore failure must preserve native character choice and recover UI");
   Check(UnityEngine.RenderTexture.Outstanding==0&&ReferenceEquals(UnityEngine.RenderTexture.active,priorTarget)&&!UnityEngine.GL.sRGBWrite,"Failed conversion leaked render state or a temporary texture");
   loader.FailNext=true;SkinHotReload.Request(owner,owner.Packs[1]);Drain();
   Check(ReferenceEquals(owner.CurrentSkin(),owner.Packs[1])&&hud.gpu==15,"UI decode error reverted native character choice instead of restoring only UI");
   File.WriteAllBytes(Path.Combine(owner.Packs[2].Dir,"Texture2D","HUD.png"),new byte[]{1,2,3});SkinHotReload.Request(owner,owner.Packs[2]);Drain();
   Check(ReferenceEquals(owner.CurrentSkin(),owner.Packs[2])&&hud.gpu==15,"Bad UI PNG cancelled native character selection");
   SkinHotReload.Request(owner,owner.Packs[1]);SkinHotReload.Request(owner,null);Drain();Check(owner.CurrentSkin()==null&&hud.gpu==15,"Latest queued native selection was overwritten by older UI work");
   owner.BlockSelector=true;SSMP.Game.Client.Skin.OwnSkin.Choose(owner.Packs[1]);
   Check(ReferenceEquals(owner.CurrentSkin(),owner.Packs[1]),"UI-only state check prevented native character selection");Drain();owner.BlockSelector=false;
   Check(hud.gpu==72,"Native skin UI did not refresh while custom selector was unavailable");
   SkinHotReload.Request(owner,owner.Packs[0]);bool mid=false;
   Drain(step=>{if(!mid&&hud.gpu==31){mid=true;SSMP.Game.Client.Skin.OwnSkin.Choose(owner.Packs[1]);}});
   Check(mid&&ReferenceEquals(owner.CurrentSkin(),owner.Packs[1])&&hud.gpu==72,"Selection made during GPU apply was lost or reverted");
   UnityEngine.QualitySettings.activeColorSpace=UnityEngine.ColorSpace.Gamma;UnityEngine.GL.sRGBWrite=true;
   SkinHotReload.Request(owner,owner.Packs[0]);Drain();SkinHotReload.Request(owner,null);Drain();
   Check(hud.gpu==15&&!UnityEngine.Graphics.LastSrgb,"Gamma colour space must not double-encode menu colours");
   Check(UnityEngine.GL.sRGBWrite&&ReferenceEquals(UnityEngine.RenderTexture.active,priorTarget)&&UnityEngine.RenderTexture.Outstanding==0,"Gamma switch did not restore the previous render state");
   UnityEngine.Object.Destroy(hud);SkinHotReload.Request(owner,null);Drain();
   Check(UnityEngine.Resources.All.FindAll(t=>t!=null&&t.name.StartsWith("CoopAssist original ")).Count==0,"Dead original GPU copy retained");
   Check(SkinReloadPlan.Normalize("sactx-0-4096x4096-BC7-Hornet-abc123")=="sactx-0-4096x4096-BC7-Hornet","Unity atlas hash mismatch");
   Check(SkinReloadPlan.Normalize("HUD|White")=="HUD_White","Pipe normalization mismatch");
   Check(SkinReloadPlan.Normalize("HUD-abc123")=="HUD-abc123","Non-atlas suffix removed");
   File.WriteAllBytes(Path.Combine(owner.Packs[1].Dir,"Texture2D","sactx-x-abc123.png"),Png(1));File.WriteAllBytes(Path.Combine(owner.Packs[1].Dir,"Texture2D","sactx-x-def456.png"),Png(2));
   bool duplicate=false;try{SkinReloadPlan.Read(owner.Packs[1].Dir);}catch(InvalidDataException){duplicate=true;}Check(duplicate,"Ambiguous atlas names accepted");
   Console.WriteLine("Passed "+checks+" hot-switch checks: sticky-unreadable regression, GPU format conversion/all mips, colour/render-state recovery, native menu, cached/late save UI, repeated switches, peers and error recovery. Simulated Unity API only; no real GPU/game test.");
   if(args.Length>0){int files=0;foreach(var pack in Directory.GetDirectories(args[0])){var map=SkinReloadPlan.Read(pack);files+=map.Count;}Console.WriteLine("Actual skin inventory: all packs parsed; "+files+" Texture2D images accepted without decoding on Unity thread.");}
  }finally{Directory.Delete(root,true);}
 }
}
