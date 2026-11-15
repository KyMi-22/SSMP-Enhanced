using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using HarmonyLib;
using SSMP.Api.Client;
using UnityEngine;
using UnityEngine.UI;

namespace Silksong.CoopAssist
{
    internal static class SignalIndicator
    {
        sealed class Display
        {
            internal readonly Image[] Bars=new Image[4];
            internal readonly SignalPolicy Policy=new SignalPolicy();
            internal object Client;
            internal double Next;
            internal int Level=-1;
            internal void Tick()
            {
                var now=PlayerMotionRepairs.Now;
                if(now<Next)return;Next=now+.5;
                var online=connected(Client);var loading=online&&CoopAssistPlugin.SignalLoading;
                var rtt=online?ping(updateManager(Client)):-1;
                if(loading){NetworkDiagnostics.Metrics.ResetSignal();Policy.Reset();}
                var gap=loading?-1:NetworkDiagnostics.Metrics.RecentVisibleGap(now);
                var level=loading?0:Policy.Update(rtt,gap,now);
                if(level==Level)return;Level=level;
                logger.LogInfo("[Signal] bars="+level+" ping="+rtt+"ms visibleGap="+(gap<0?"n/a":(gap*1000).ToString("0")+"ms")+" loading="+loading);
                var colour=level>=4?new Color(.35f,.9f,.55f):level==3?new Color(.9f,.85f,.35f):
                    level==2?new Color(1f,.6f,.25f):new Color(.95f,.35f,.3f);
                for(var n=0;n<Bars.Length;n++)if(Bars[n])
                    Bars[n].color=n<level?colour:new Color(.5f,.5f,.5f,.45f);
            }
        }
        static readonly ConditionalWeakTable<object,Display> displays=new ConditionalWeakTable<object,Display>();
        static Func<object,object> closureClient,updateManager;
        static Func<object,bool> connected;
        static Func<object,int> ping;
        static Func<object,GameObject> gameObject;
        static ManualLogSource logger;

        internal static void Install(ManualLogSource log)
        {
            logger=log;
            var asm=typeof(ClientAddon).Assembly;
            var type=asm.GetType("SSMP.Ui.PingInterface",true);
            var client=asm.GetType("SSMP.Networking.Client.NetClient",true);
            var component=asm.GetType("SSMP.Ui.Component.Component",true);
            var closure=type.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic).Single(t=>AccessTools.Field(t,"pingTextComponent")!=null);
            var callback=closure.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                .Single(m=>m.Name.Contains("b__") && m.ReturnType==typeof(void) && m.GetParameters().Length==0);
            closureClient=NativeFlowBindings.Read<object>(closure,"netClient");
            connected=NativeFlowBindings.Read<bool>(client,"IsConnected");
            updateManager=NativeFlowBindings.Read<object>(client,"UpdateManager");
            ping=NativeFlowBindings.Read<int>(AccessTools.Property(client,"UpdateManager").PropertyType,"AverageRtt");
            gameObject=NativeFlowBindings.Read<GameObject>(component,"GameObject");
            var h=new Harmony("silksong.coopassist.signal");
            h.Patch(callback,prefix:new HarmonyMethod(typeof(SignalIndicator),nameof(UpdateSignal)));
            h.Patch(type.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(),
                postfix:new HarmonyMethod(typeof(SignalIndicator),nameof(Created)));
            // SSMP may have constructed its UI before this addon awakens.
            h.Patch(AccessTools.Method(type,"SetEnabled"),postfix:new HarmonyMethod(typeof(SignalIndicator),nameof(Created)));
            logger.LogInfo("[Signal] Numeric ping hidden; four-bar quality indicator refreshes at most twice a second.");
        }
        static bool UpdateSignal(object __instance)
        {
            var client=closureClient(__instance);
            if(displays.TryGetValue(client,out var display))display.Tick();
            return false; // Suppress the native per-frame numeric text update entirely.
        }
        static void Created(object __instance)
        {
            var client=ReflectionCache.Get(__instance,"_netClient");
            if(displays.TryGetValue(client,out var existing) && existing.Bars[0])return;
            var group=ReflectionCache.Get(__instance,"_pingComponentGroup");
            var components=(IList)ReflectionCache.Get(group,"_components");
            Image icon=null;
            for(var n=0;n<components.Count;n++)
            {
                var go=gameObject(components[n]);
                var text=go.GetComponent<Text>();
                if(text){text.text="";text.enabled=false;}
                var image=go.GetComponent<Image>();if(image)icon=image;
            }
            if(!icon){logger.LogWarning("[Signal] Native network icon was not found; numeric ping remains hidden.");return;}
            icon.enabled=false;icon.raycastTarget=false;
            var display=new Display{Client=client};
            for(var n=0;n<4;n++)
            {
                var go=new GameObject("Coop Signal "+(n+1),typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                var rect=(RectTransform)go.transform;rect.SetParent(icon.transform,false);
                rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;
                rect.anchoredPosition=new Vector2(n*5,0);rect.sizeDelta=new Vector2(3,4+n*4);
                var image=go.GetComponent<Image>();image.raycastTarget=false;
                image.color=new Color(.5f,.5f,.5f,.45f);display.Bars[n]=image;
            }
            displays.Remove(client);displays.Add(client,display);display.Tick();
        }
    }
}
