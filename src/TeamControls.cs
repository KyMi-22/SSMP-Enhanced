using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        sealed class AssistBinding
        {
            internal readonly string Label;
            internal readonly ConfigEntry<KeyCode> Setting;
            internal readonly Action Run;
            internal readonly Action MenuRun;
            internal readonly string Hint;
            internal KeyCode Accepted;
            internal AssistBinding(string label,ConfigEntry<KeyCode> setting,Action run,string hint,Action menuRun=null)
            {Label=label;Setting=setting;Run=run;Hint=hint;MenuRun=menuRun??run;Accepted=setting.Value;}
        }
        AssistBinding[] assistBindings;
        ConfigEntry<KeyCode> waypointKey, ledgerKey, setWaypointKey;
        AssistBinding bindingCapture;
        bool updatingBindings;
        bool bindingsExpanded;
        // DebugMod keeps F2 and the panel keeps F7; the escape key is ours but sits outside the list.
        KeyCode[] ReservedKeys=>new[]{KeyCode.F2,KeyCode.F7,escapeKey.Value};
        static readonly KeyCode[] BindingDefaults={
            KeyCode.F5,KeyCode.F6,KeyCode.F9,KeyCode.F10,KeyCode.F12,KeyCode.F11,KeyCode.F1
        };

        void ConfigureAssistBindings()
        {
            benchKey=Config.Bind("Play","ReturnToBenchKey",KeyCode.F5,"Any keyboard key: native cocoon return; controller binding retained.");
            teleportKey=Config.Bind("Team","TeleportKey",KeyCode.F6,"Any keyboard key: open teammate selection, then confirm a destination.");
            waypointKey=Config.Bind("Play","UseWaypointKey",KeyCode.F11,"Travel to the saved personal waypoint.");
            ledgerKey=Config.Bind("Team","RosaryLedgerKey",KeyCode.F1,"Any keyboard key: open or close the rosary ledger while alive.");
            setWaypointKey=Config.Bind("Play","SetReturnPointKey",KeyCode.F10,"Set a safe personal waypoint.");
            assistBindings=new[]{
                new AssistBinding("茧状态返回",benchKey,null,"成茧后返回长椅，支持原手柄绑定。"),
                new AssistBinding("传送到队友…",teleportKey,OpenTravelPicker,"打开队友列表；上下选择，确认传送，返回键取消。",ShowMenuTravelPicker),
                new AssistBinding("按房主一键同步",Config.Bind("Team","ResyncKey",KeyCode.F9,"Any keyboard key: synchronize from host while seated at a bench."),RequestHostResync,"客机坐长椅后使用。自动备份，再同步进度和物品；念珠、念珠串独立。"),
                new AssistBinding("设置传送点",setWaypointKey,SetWaypoint,"站在固定安全地面设置此存档的传送点。"),
                new AssistBinding("删除传送点",Config.Bind("Play","ClearReturnPointKey",KeyCode.F12,"Delete this save's personal waypoint."),ClearWaypoint,"删除此存档的传送点。"),
                new AssistBinding("前往传送点",waypointKey,RequestWaypoint,"角色存活且可以操作时，前往此存档的传送点。",RequestWaypointFromMenu),
                new AssistBinding("打开念珠记账本",ledgerKey,()=>OpenRosaryLedger(),"查看此存档的借款及还款记录。",OpenRosaryLedgerFromMenu)
            };
            NormalizeAssistBindings();
            foreach(var binding in assistBindings)
            {
                binding.Setting.SettingChanged+=(_,__)=>AcceptBinding(binding);
            }
            escapeKey.SettingChanged+=(_,__)=>{NormalizeAssistBindings();ApplyBenchBinding();};
        }
        void NormalizeAssistBindings()
        {
            var normalized=BindingKeyPolicy.Normalize(assistBindings.Select(b=>b.Setting.Value).ToArray(),BindingDefaults,ReservedKeys);
            updatingBindings=true;
            try
            {
                for(int i=0;i<assistBindings.Length;i++)
                {
                    var binding=assistBindings[i];
                    binding.Setting.Value=normalized[i];
                    binding.Accepted=binding.Setting.Value;
                }
            }
            finally{updatingBindings=false;}
        }
        bool AcceptBinding(AssistBinding binding)
        {
            if(updatingBindings)return true;
            var key=binding.Setting.Value;
            string error=null;
            if(!BindingKeyPolicy.CanUse(key,ReservedKeys))
                error="请选择键盘按键；Esc、F2、F7 和安全脱困键已保留。";
            else if(!BindingKeyPolicy.CanAssign(key,Array.IndexOf(assistBindings,binding),
                assistBindings.Select(b=>b.Accepted).ToArray(),ReservedKeys))
                error=key+" 已用于其他联机操作，请选择另一个按键。";
            updatingBindings=true;
            try
            {
                if(error!=null){binding.Setting.Value=binding.Accepted;Say(error);return false;}
                // The native cocoon-return action only accepts keys InControl can name; do not claim success first.
                if(binding.Setting==benchKey && !ApplyBenchBinding())
                {
                    Say("这个按键不能用于成茧返回，已保留 "+binding.Accepted+"。");
                    return false;
                }
                binding.Accepted=key;
                Say("已修改快捷键："+binding.Label+" → "+key+"。");
                return true;
            }
            finally{updatingBindings=false;}
        }
        void DrawTravelOptions()
        {
            GUILayout.Label("— 队伍操作 —");
            foreach(var binding in assistBindings)
            {
                if(binding.Run!=null && GUILayout.Button(new GUIContent(binding.Label+"  ["+binding.Setting.Value+"]",binding.Hint)))
                {
                    binding.Run();
                    if(!optionsOpen)return;
                }
            }
            GUILayout.Label("成茧返回长椅：["+benchKey.Value+"]　同步需先坐长椅；传送点需站在固定安全地面设置。");
            if(GUILayout.Button(bindingsExpanded?"▼ 快捷键设置（收起）":"▶ 快捷键设置（展开）"))
            {bindingsExpanded=!bindingsExpanded;bindingCapture=null;}
            if(!bindingsExpanded)return;
            GUILayout.Label("点一项再按新按键即可改绑；同一按键不能给两个操作。");
            foreach(var binding in assistBindings)
                if(GUILayout.Button(binding.Label+"："+binding.Setting.Value))
                {bindingCapture=binding;Say("请为“"+binding.Label+"”按一个空闲按键，Esc 取消。");}
            GUILayout.Label("可设置任意键盘按键；Esc、F2、F7 和安全脱困键保留。");
            if(bindingCapture==null)return;
            GUILayout.Label("请按任意键盘按键（Esc 取消）");
            var e=Event.current;
            if(e.type!=EventType.KeyDown || e.keyCode==KeyCode.None)return;
            if(e.keyCode!=KeyCode.Escape)bindingCapture.Setting.Value=e.keyCode;
            else Say("已取消改键，保留原快捷键。");
            bindingCapture=null;e.Use();
        }
        void TickAssistHotkeys()
        {
            if (ledgerOpen)
            {
                if (Down || Loading || (Input.GetKeyDown(ledgerKey.Value) && !AssistChatOpen())) ledgerOpen = false;
                return;
            }
            if(optionsOpen || travelPickerOpen || bindingCapture!=null || EconomyOverlayOpen || Down || !Playing || AssistChatOpen())return;
            foreach(var binding in assistBindings)
                if(binding.Run!=null && Input.GetKeyDown(binding.Setting.Value)){binding.Run();break;}
        }
        bool AssistChatOpen()
        {
            if(api==null)return false;
            try{return api.UiManager.ChatBox.IsOpen;}
            catch(InvalidOperationException){return false;} // Native chat may not exist before joining a room.
        }
        // Team operations report in the game's own chat window, where SSMP puts its own notices.
        void SayChat(string message)
        {
            if(api?.NetClient.IsConnected==true)
            {
                try
                {
                    if(api.UiManager.ChatBox is { } chat){chat.AddMessage(message);Logger.LogInfo(message);return;}
                }
                catch(Exception error) { Logger.LogWarning("[Chat] Falling back to the HUD notice: " + error.Message); }
            }
            Say(message);
        }
    }
}
