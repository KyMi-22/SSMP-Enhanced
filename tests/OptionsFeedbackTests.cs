using System;
using System.Collections.Generic;
using System.Linq;
using Silksong.CoopAssist;

// Execute the production F7 controls against a recording UI. No Unity/game is started.
namespace UnityEngine
{
    public enum KeyCode { None=0, Backspace=8, Escape=27, Space=32, Alpha0=48, Alpha9=57, A=97, K=107, Z=122, F1=282,F2,F3,F4,F5,F6,F7,F8,F9,F10,F11,F12,F13,F14,F15 }
    public enum EventType { Layout, KeyDown }
    public sealed class Event
    {
        public static Event current=new Event();
        public EventType type;
        public KeyCode keyCode;
        public bool Used;
        public void Use(){Used=true;}
    }
    public sealed class GUIContent
    {
        public string text;
        public GUIContent(){}
        public GUIContent(string value,string hint){text=value;}
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
    }
    public struct Color {public Color(float r,float g,float b,float a=1){} public static Color white=>new Color(1,1,1);}
    public enum TextAnchor { UpperLeft,MiddleLeft }
    public enum HorizontalWrapMode { Wrap }
    public static class Screen {public static int width=1920,height=1080;}
    public static class Time {public static float unscaledTime;}
    public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);}
    public sealed class GUIStyleState {public Color textColor;}
    public sealed class GUIStyle
    {
        public bool wordWrap,richText;
        public int fontSize;
        public TextAnchor alignment;
        public readonly GUIStyleState normal=new GUIStyleState();
        public GUIStyle(){}
        public GUIStyle(GUIStyle other){}
        // Approximate font measurement; tests check production placement, not font rasterization.
        public float CalcHeight(GUIContent content,float width)=>Math.Max(1,(int)Math.Ceiling(content.text.Length*15/width))*20;
    }
    public sealed class GUISkin {public readonly GUIStyle label=new GUIStyle(),box=new GUIStyle();}
    public sealed class Texture2D {public static readonly Texture2D whiteTexture=new Texture2D();}
    public static class GUI
    {
        public static readonly GUISkin skin=new GUISkin();
        public static Color color;
        public static readonly List<Rect> Backgrounds=new List<Rect>();
        public static readonly List<string> Messages=new List<string>();
        public static void Reset(){Backgrounds.Clear();Messages.Clear();}
        public static void DrawTexture(Rect rect,Texture2D texture)=>Backgrounds.Add(rect);
        public static void Label(Rect rect,GUIContent content,GUIStyle style)=>Messages.Add(content.text);
    }
    public sealed class GUILayoutOption {}
    public static class GUILayout
    {
        public static readonly List<string> Buttons=new List<string>();
        public static string Click;
        public static void Reset(string click=null){Buttons.Clear();Click=click;Event.current=new Event();}
        public static bool Button(string text)
        {
            Buttons.Add(text);
            if(text!=Click)return false;
            Click=null;return true;
        }
        public static bool Button(GUIContent content)=>Button(content.text);
        public static void Label(string text){}
        public static void Label(string text,GUIStyle style){}
        public static GUILayoutOption Height(float height)=>new GUILayoutOption();
        public static void BeginVertical(GUIStyle style,params GUILayoutOption[] options){}
        public static void EndVertical(){}
    }
    public static class Input
    {
        public static KeyCode Pressed;
        public static bool GetKeyDown(KeyCode key)=>Pressed==key;
    }
}
namespace Silksong.ModMenu.Elements
{
    public sealed class Text
    {
        public string text;
        public int fontSize;
        public bool supportRichText;
        public UnityEngine.HorizontalWrapMode horizontalOverflow;
        public UnityEngine.TextAnchor alignment;
    }
    public sealed class RectTransform {public UnityEngine.Vector2 sizeDelta;}
    public sealed class TextLabel
    {
        public readonly Text Text=new Text();
        public readonly RectTransform RectTransform=new RectTransform();
        public TextLabel(string value){Text.text=value;}
    }
}
namespace Silksong.ModMenu.Screens
{
    public sealed class ScrollingMenuScreen
    {
        public event Action<int> OnShow;
        public event Action OnDispose;
        public Silksong.ModMenu.Elements.TextLabel Label;
        public void Add(Silksong.ModMenu.Elements.TextLabel value){Label=value;}
        public void Show()=>OnShow?.Invoke(0);
        public void Dispose()=>OnDispose?.Invoke();
    }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        T value;
        public ConfigEntry(T initial){value=initial;}
        public T Value
        {
            get=>value;
            set{if(EqualityComparer<T>.Default.Equals(this.value,value))return;this.value=value;SettingChanged?.Invoke(this,EventArgs.Empty);}
        }
        public event EventHandler SettingChanged;
    }
    public sealed class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section,string key,T initial,string help)=>new ConfigEntry<T>(initial);
    }
}
namespace Silksong.CoopAssist
{
    using UnityEngine;
    using BepInEx.Configuration;
    public sealed partial class CoopAssistPlugin
    {
        bool EconomyOverlayOpen => false;
        bool Down, Loading, ledgerOpen;
        readonly ConfigFile Config=new ConfigFile();
        readonly ConfigEntry<KeyCode> escapeKey=new ConfigEntry<KeyCode>(KeyCode.F8);
        ConfigEntry<KeyCode> benchKey,teleportKey;
        bool optionsOpen=true,travelPickerOpen,Playing=true;
        sealed class Chat
        {
            public bool IsOpen;
            public void AddMessage(string message)=>ChatLog.Add(message);
        }
        sealed class Ui
        {
            readonly Chat chat=new Chat();
            public bool Initializing;
            public Chat ChatBox=>Initializing?throw new InvalidOperationException("Chat not initialized"):chat;
        }
        sealed class Net { public bool IsConnected=true; }
        sealed class Api { public readonly Ui UiManager=new Ui(); public readonly Net NetClient=new Net(); }
        internal static readonly List<string> ChatLog=new List<string>();
        sealed class TestLog
        {
            public void LogInfo(string message){} 
            public void LogWarning(string message){}
        }
        readonly TestLog Logger=new TestLog();
        Api api=new Api();
        readonly List<string> invoked=new List<string>();
        void Say(string message)=>feedback.Publish(message,Time.unscaledTime);
        bool ApplyBenchBinding(){invoked.Add("bench-bind");return true;}
        void OpenTravelPicker(){invoked.Add("travel");travelPickerOpen=true;Say("请选择目标。");}
        void ShowMenuTravelPicker()=>invoked.Add("native-travel");
        void RequestHostResync(){invoked.Add("sync");Say("正在获取房主快照。");}
        void SetWaypoint(){invoked.Add("set");Say("已设置传送点。");}
        void ClearWaypoint(){invoked.Add("clear");Say("已删除传送点。");}
        void RequestWaypoint(){invoked.Add("waypoint");Say("正在前往传送点。");}
        void RequestWaypointFromMenu()=>invoked.Add("menu-waypoint");
        void OpenRosaryLedger(){invoked.Add("ledger");optionsOpen=false;ledgerOpen=true;}
        void OpenRosaryLedgerFromMenu()=>OpenRosaryLedger();
        public void CheckControls(Action<bool,string> check)
        {
            ConfigureAssistBindings();
            GUILayout.Reset();DrawTravelOptions();
            check(GUILayout.Buttons.Count==7,"Default view has six actions and one collapsed settings control");
            check(!GUILayout.Buttons.Any(b=>b.Contains("：F")),"Default view hides all seven rebind rows");
            check(invoked.Count==0,"Drawing controls cannot run an operation");

            GUILayout.Reset("设置传送点  [F10]");DrawTravelOptions();
            check(invoked.SequenceEqual(new[]{"set"}) && feedback.Message=="已设置传送点。","Action click reports its actual result");
            invoked.Clear();
            GUILayout.Reset("▶ 快捷键设置（展开）");DrawTravelOptions();
            check(bindingsExpanded && GUILayout.Buttons.Count==14,"Expand reveals seven bindings without duplicating action rows");
            check(invoked.Count==0,"Expanding bindings does not execute an action");
            GUILayout.Reset("设置传送点：F10");DrawTravelOptions();
            check(bindingCapture!=null && invoked.Count==0,"Clicking a binding captures a key rather than executing it");
            check(feedback.Message.Contains("空闲按键"),"Capture instructions appear in the feedback channel");
            GUILayout.Reset();Event.current.type=EventType.KeyDown;Event.current.keyCode=KeyCode.F12;DrawTravelOptions();
            check(assistBindings[3].Setting.Value==KeyCode.F10,"Conflicting key keeps original binding");
            check(feedback.Message.Contains("已用于") && bindingCapture==null,"Conflict is explained and capture exits");

            GUILayout.Reset("设置传送点：F10");DrawTravelOptions();
            GUILayout.Reset();Event.current.type=EventType.KeyDown;Event.current.keyCode=KeyCode.F3;DrawTravelOptions();
            check(assistBindings[3].Setting.Value==KeyCode.F3 && feedback.Message.Contains("已修改"),"Successful key change has confirmation");
            GUILayout.Reset("设置传送点：F3");DrawTravelOptions();
            GUILayout.Reset();Event.current.type=EventType.KeyDown;Event.current.keyCode=KeyCode.K;DrawTravelOptions();
            check(assistBindings[3].Setting.Value==KeyCode.K,"A letter key outside F1-F12 can be assigned");
            GUILayout.Reset("设置传送点：K");DrawTravelOptions();
            GUILayout.Reset();Event.current.type=EventType.KeyDown;Event.current.keyCode=KeyCode.Escape;DrawTravelOptions();
            check(bindingCapture==null && assistBindings[3].Setting.Value==KeyCode.K && feedback.Message.Contains("已取消"),"Escape cancels capture without changing the key");
            GUILayout.Reset("设置传送点：K");DrawTravelOptions();
            GUILayout.Reset("▼ 快捷键设置（收起）");DrawTravelOptions();
            check(!bindingsExpanded && bindingCapture==null && GUILayout.Buttons.Count==7,"Collapse clears capture and hides all binding controls");

            Input.Pressed=KeyCode.F9;TickAssistHotkeys();
            check(invoked.Count==0,"Open settings prevent action hotkeys while editing");
            optionsOpen=false;travelPickerOpen=true;TickAssistHotkeys();
            check(invoked.Count==0,"Target picker prevents background hotkeys");
            travelPickerOpen=false;api.UiManager.ChatBox.IsOpen=true;TickAssistHotkeys();
            check(invoked.Count==0,"Chat entry prevents action hotkeys");
            api.UiManager.ChatBox.IsOpen=false;TickAssistHotkeys();
            check(invoked.SequenceEqual(new[]{"sync"}) && feedback.Message.Contains("正在获取"),"Hotkey uses the same action and feedback");
            api.UiManager.Initializing=true;Input.Pressed=KeyCode.K;TickAssistHotkeys();
            check(invoked.Last()=="set" && feedback.Message.Contains("已设置"),"Native chat initialization cannot swallow a local hotkey operation");
            Input.Pressed=KeyCode.F11;TickAssistHotkeys();
            check(invoked.Last()=="waypoint" && feedback.Message.Contains("正在前往"),"Waypoint key requests manual travel and reports it");
        }

        public void CheckLedgerKey(Action<bool,string> check)
        {
            ConfigureAssistBindings();
            optionsOpen=false;
            Input.Pressed=KeyCode.F1;TickAssistHotkeys();
            check(ledgerOpen && invoked.SequenceEqual(new[]{"ledger"}),"F1 opens the ledger by default");
            TickAssistHotkeys();
            check(!ledgerOpen && invoked.Count==1,"Ledger hotkey closes the open ledger");

            optionsOpen=true;
            GUILayout.Reset("▶ 快捷键设置（展开）");DrawTravelOptions();
            GUILayout.Reset("打开念珠记账本：F1");DrawTravelOptions();
            GUILayout.Reset();Event.current.type=EventType.KeyDown;Event.current.keyCode=KeyCode.F4;DrawTravelOptions();
            check(ledgerKey.Value==KeyCode.F4 && bindingCapture==null,"Ledger key can be changed in the F7 panel");
            ledgerKey.Value=KeyCode.F6;
            check(ledgerKey.Value==KeyCode.F4,"Ledger key rejects a conflict with another action");
            benchKey.Value=KeyCode.F4;
            check(benchKey.Value==KeyCode.F5,"F4 still follows the normal collision rules");
            optionsOpen=false;
            Input.Pressed=KeyCode.F1;TickAssistHotkeys();
            check(!ledgerOpen,"Former ledger hotkey no longer opens it");
            Input.Pressed=KeyCode.F4;TickAssistHotkeys();
            check(ledgerOpen,"New ledger hotkey opens it");
            Down=true;TickAssistHotkeys();
            check(!ledgerOpen,"Entering the cocoon closes the ledger");
            TickAssistHotkeys();
            check(!ledgerOpen,"Cocoon view keeps priority for its keys");
            Down=false;Input.Pressed=KeyCode.F4;TickAssistHotkeys();
            Loading=true;TickAssistHotkeys();
            check(!ledgerOpen,"Loading closes the ledger");

            updatingBindings=true;
            waypointKey.Value=KeyCode.F1;
            ledgerKey.Value=KeyCode.F1;
            updatingBindings=false;
            NormalizeAssistBindings();
            check(waypointKey.Value==KeyCode.F1 && ledgerKey.Value!=KeyCode.F1,"An existing F1 assignment is preserved during migration");
            benchKey.Value=KeyCode.F4;
            NormalizeAssistBindings();
            check(benchKey.Value==KeyCode.F4,"A free F4 is available for return to bench and survives reload");
        }
        public void CheckHud(Action<bool,string> check)
        {
            ConfigureAssistBindings();optionsOpen=false;
            api=null; // A closed/disconnected room must not make the keyboard depend on chat initialization.
            Time.unscaledTime=100;Input.Pressed=KeyCode.F10;TickAssistHotkeys();
            GUI.Reset();DrawFeedbackToast();
            check(GUI.Messages.SequenceEqual(new[]{"已设置传送点。"}),"F10 with no menu/chat produces actual HUD draw calls");
            foreach(var size in new[]{(1920,1080),(1280,720),(960,540)})
            {
                Screen.width=size.Item1;Screen.height=size.Item2;
                GUI.Reset();DrawFeedbackToast();
                var rect=GUI.Backgrounds.Single();
                check(rect.x==20 && rect.y+rect.height==Screen.height-20,"HUD anchors to bottom-left at different resolutions");
                check(rect.x+rect.width<=Screen.width-20 && rect.y>=12,"HUD remains inside the screen");
            }
            Time.unscaledTime=101;Input.Pressed=KeyCode.F12;TickAssistHotkeys();
            Time.unscaledTime=102;Input.Pressed=KeyCode.F9;TickAssistHotkeys();
            GUI.Reset();DrawFeedbackToast();
            check(GUI.Messages.Count==3 && GUI.Messages[0].Contains("正在获取"),"Fast shortcut feedback retains recent stages, newest at bottom");
            check(GUI.Backgrounds[0].y>GUI.Backgrounds[1].y,"Older notices stack upward");
            travelPickerOpen=true;GUI.Reset();DrawFeedbackToast();
            check(GUI.Messages.Count==1,"A teammate picker still displays the latest bottom-left notice");
            optionsOpen=true;travelPickerOpen=false;GUI.Reset();DrawFeedbackToast();
            check(GUI.Messages.Count==3,"An open settings panel does not suppress HUD feedback");
            Time.unscaledTime=114;GUI.Reset();DrawFeedbackToast();
            check(GUI.Messages.Count==0,"HUD notices expire individually without leaving stale messages");
            check(feedback.Message.Contains("正在获取"),"Latest result remains available in settings after HUD expiry");
            var menu=new Silksong.ModMenu.Screens.ScrollingMenuScreen();AddMenuFeedback(menu);
            feedback.Publish("同步完成。",115);
            check(menu.Label.Text.text.Contains("同步完成"),"Settings mirror the same HUD result");
            menu.Dispose();feedback.Publish("已清除。",116);
            check(menu.Label.Text.text.Contains("同步完成"),"Disposed menu unsubscribes from feedback");
            optionsOpen=false;api=new Api();Input.Pressed=KeyCode.F6;Time.unscaledTime=117;TickAssistHotkeys();
            GUI.Reset();DrawFeedbackToast();
            check(invoked.Last()=="travel" && GUI.Messages.Single()=="请选择目标。","F6 selection action also draws a notice while its picker is open");
        }
    }
}
static class OptionsFeedbackTests
{
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    static void Main()
    {
        var feedback=new AssistFeedback();
        Check(!feedback.IsRecent(0),"No startup toast before an operation");
        feedback.Publish("请先站在安全地面。",10);
        Check(feedback.Message=="请先站在安全地面。" && feedback.IsRecent(10),"Failures are immediately visible");
        Check(feedback.IsRecent(21.99) && !feedback.IsRecent(22),"Toast expires without clearing persistent settings result");
        Check(feedback.Message=="请先站在安全地面。","Result survives toast expiry");
        feedback.Publish("已设置传送点。",23);
        Check(feedback.Message=="已设置传送点。","New success replaces prior failure");
        feedback.Publish("已设置传送点。",30);
        Check(feedback.IsRecent(41),"Repeated clicks refresh identical feedback");
        feedback.Publish(" ",40);
        Check(feedback.ShownAt==30,"Empty messages do not erase useful feedback");
        feedback.Publish("正在获取房主快照。",45);
        feedback.Publish("已收到快照，请关闭设置。",46);
        feedback.Publish("正在备份并写入。",47);
        feedback.Publish("同步完成。",50);
        Check(feedback.Message=="同步完成。" && feedback.IsRecent(50),"Async completion replaces intermediate stages");
        Check(feedback.Notices.Count==3 && feedback.Notices.Last().Message=="同步完成。","Notice history has a fixed bound");
        feedback.Publish("同步完成。",51);
        Check(feedback.Notices.Count==3 && feedback.Notices.Last().ShownAt==51,"Repeated notices refresh without duplicate rows");
        new CoopAssistPlugin().CheckControls(Check);
        new CoopAssistPlugin().CheckLedgerKey(Check);
        new CoopAssistPlugin().CheckHud(Check);
        Console.WriteLine("Passed "+checks+" production shortcut/HUD draw, bottom-left anchoring, modal visibility, notice lifetime, folded controls and subscription checks. Recorded drawing, no Unity rendering.");
    }
}
