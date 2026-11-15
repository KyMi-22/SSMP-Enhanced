using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Silksong.ModMenu.Elements;
using Silksong.ModMenu.Models;
using Silksong.ModMenu.Plugin;
using Silksong.ModMenu.Screens;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin : IModMenuCustomMenu
    {
        public LocalizedText ModMenuName() => "联机体验";
        public AbstractMenuScreen BuildCustomMenu()
        {
            var menu=new ScrollingMenuScreen("联机体验");
            menu.ScrollPane.Axes=ScrollingPane.ScrollAxes.Vertical;
            bool refreshing=false;
            var choices=(IList)SkinChoices.Invoke(null,null);
            var packs=new Dictionary<string,object>{{"vanilla",null}};
            foreach(var pack in choices)packs[(string)Value(pack,"Name")]=pack;
            var skinModel=ChoiceModels.ForNamedValues(packs.Select(p=>(p.Key,p.Key=="vanilla"?"原版":p.Key)));
            var selected=choices.Cast<object>().FirstOrDefault(p=>Flag(p,"IsActive"));
            skinModel.Value=selected==null?"vanilla":(string)Value(selected,"Name");
            var skin=new ChoiceElement<string>("角色皮肤与个人 UI",skinModel,SkinHotReload.Available?"支持房间内热切换；加载期间请稍候。队友只同步角色外观。":"此设备的贴图接口不支持热切换；请退出房间回主菜单选择，再重启游戏。");
            var state=Value(global::GameManager.instance,"GameState")?.ToString();
            skin.Interactable=SkinHotReload.Available?CanChangeSkin:api?.NetClient.IsConnected!=true&&state!="PLAYING"&&state!="PAUSED";
            skin.OnValueChanged+=name=>{if(!refreshing)PickSkin(packs[name]);};menu.Add(skin);

            var nameModel=ChoiceModels.ForNamedValues(new[]{("white","原版白色"),("yellow","黄色"),("green","绿色"),("cyan","青色"),("orange","橙色"),("red","红色")});
            var settings=api==null?null:Value(api.ClientManager,"_modSettings");
            var current=Flag(settings,"ColourOwnName")?(Value(settings,"OwnNameColour") as string??"yellow"):"white";
            nameModel.Value=nameModel.Values.Contains(current)?current:"yellow";
            var colour=new ChoiceElement<string>("名字颜色",nameModel,"将你的名字颜色同步给队友的名字、地图与方位提示。");
            colour.OnValueChanged+=value=>{if(!refreshing)SelectNameColour(value);};menu.Add(colour);
            var switches=new List<(ChoiceElement<bool>,ConfigEntry<bool>)>();
            void AddSwitch(string label,ConfigEntry<bool> setting)
            {var element=Toggle(label,setting);switches.Add((element,setting));menu.Add(element);}
            AddSwitch("队友方位头像",directionHeads);
            AddSwitch("个人念珠与碎片吸附",Comfort.Magnet);
            AddSwitch("低延迟动作平滑",lowLatency);
            AddSwitch("倒地时观战",spectate);
            AddSwitch("加入后集合到房主",joinHost);
            var refreshTravelMenu=AddTravelMenu(menu);
            menu.OnShow+=_=>{
                refreshing=true;
                try
                {
                    var currentSettings=api==null?null:Value(api.ClientManager,"_modSettings");
                    var value=Flag(currentSettings,"ColourOwnName")?(Value(currentSettings,"OwnNameColour") as string??"yellow"):"white";
                    nameModel.Value=nameModel.Values.Contains(value)?value:"yellow";
                    var chosen=choices.Cast<object>().FirstOrDefault(p=>Flag(p,"IsActive"));
                    skinModel.Value=chosen==null?"vanilla":(string)Value(chosen,"Name");
                    var gameState=Value(global::GameManager.instance,"GameState")?.ToString();
                    skin.Interactable=SkinHotReload.Available?CanChangeSkin:api?.NetClient.IsConnected!=true&&gameState!="PLAYING"&&gameState!="PAUSED";
                    foreach(var pair in switches)pair.Item1.Value=pair.Item2.Value;
                    refreshTravelMenu();
                }
                finally{refreshing=false;}
            };
            return menu;
        }
        Action AddTravelMenu(ScrollingMenuScreen menu)
        {
            bool refreshing=false;
            foreach(var binding in assistBindings)
                if(binding.MenuRun!=null)menu.Add(new TextButton(binding.Label,binding.Hint){OnSubmit=binding.MenuRun});
            AddMenuFeedback(menu);
            bool expanded=false;
            var fold=new TextButton("▶ 快捷键设置（展开）","展开后修改操作按键；操作按钮位于上方。");
            menu.Add(fold);
            var keyRows=new List<MenuElement>();
            var models=new List<ValueModel<UnityEngine.KeyCode>>();
            foreach(var binding in assistBindings)
            {
                var model=new ValueModel<UnityEngine.KeyCode>(binding.Setting.Value);
                string label=binding.Label+"按键";
                var element=new KeyBindElement(label+"（任意键）",model);
                element.OnValueChanged+=key=>{
                    if(refreshing)return;
                    binding.Setting.Value=key;
                    refreshing=true;
                    try{model.Value=binding.Setting.Value;}
                    finally{refreshing=false;}
                    element.LabelText.text=binding.Setting.Value==key?label+"（任意键）":label+"（"+key+" 不可用）";
                };
                models.Add(model);menu.Add(element);keyRows.Add(element);
            }
            var help=new TextLabel("成茧时沿用游戏的快捷地图键，↑／↓ 在自己和同场景队友间切换；观战时移除本地黑暗。");
            menu.Add(help);keyRows.Add(help);
            void Expand(bool value)
            {
                expanded=value;
                fold.ButtonText.text=expanded?"▼ 快捷键设置（收起）":"▶ 快捷键设置（展开）";
                foreach(var row in keyRows)row.Visibility.VisibleSelf=expanded;
            }
            fold.OnSubmit=()=>Expand(!expanded);
            Expand(false);
            menu.OnShow+=_=>Expand(false);
            return ()=>{
                refreshing=true;
                try
                {
                    for(int i=0;i<assistBindings.Length;i++)models[i].Value=assistBindings[i].Setting.Value;
                }
                finally{refreshing=false;}
            };
        }
        static ChoiceElement<bool> Toggle(string title,ConfigEntry<bool> setting)
        {
            var model=ChoiceModels.ForBool("关闭","开启");model.Value=setting.Value;
            var element=new ChoiceElement<bool>(title,model);element.OnValueChanged+=value=>setting.Value=value;
            return element;
        }
        void SelectNameColour(string name)
        {
            if(api==null)return;
            var settings=Value(api.ClientManager,"_modSettings");
            ReflectionCache.Set(settings,"ColourFromCloak",false);
            ReflectionCache.Set(settings,"OwnNameColour",name);
            ReflectionCache.Set(settings,"ColourOwnName",true);
            settings.GetType().GetMethod("Save",BindingFlags.Public|BindingFlags.Instance)?.Invoke(settings,null);
            lastColour="";appearanceAt=0;enhancementsAt=0;
            Say("已选择名字颜色："+name+"。联机时会同步给队友。");
        }
    }
}
