using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Networking.Packet;
using Silksong.CoopAssist;

static class HostResourceTests
{
    static int checks;
    static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static JObject Inventory(int material,int rosaries)=>new JObject{["savedData"]=new JArray(
        new JObject{["Name"]="Tool Metal",["Data"]=new JObject{["Amount"]=material}},
        new JObject{["Name"]="Rosary_Set_Large",["Data"]=new JObject{["Amount"]=rosaries}})};
    static JObject Save(int amount,int rosaries)=>new JObject{
        ["playerData"]=new JObject{
            ["permadeathMode"]=0,["geo"]=rosaries,["TempGeoStore"]=rosaries+1,["ShellShards"]=amount,
            ["dicePilgrimBank"]=rosaries+2,["CrawbellCurrency"]=new JArray(rosaries+3),
            ["maxHealth"]=5,["maxHealthBase"]=5,["health"]=4,["silkMax"]=9,["silk"]=3,
            ["nailUpgrades"]=1,["ToolKitUpgrades"]=1,["ToolPouchUpgrades"]=1,
            ["CurrentCrestID"]="Hunter",["PreviousCrestID"]="Hunter",
            ["respawnScene"]="Bone_11",["respawnMarkerName"]="RestBench",["respawnType"]=1,
            ["Collectables"]=Inventory(amount,rosaries),["Relics"]=new JObject{["savedData"]=new JArray()},
            ["Tools"]=new JObject{["savedData"]=new JArray()},["ToolEquips"]=new JObject{["savedData"]=new JArray()},
            ["playTime"]=rosaries,["disablePause"]=false,["hasDash"]=true
        },
        ["sceneData"]=new JObject{["persistentBools"]=new JObject{["serializedList"]=new JArray()},["persistentInts"]=new JObject{["serializedList"]=new JArray()}}
    };
    static int Amount(JObject save,string name)=>
        (int)save["playerData"]["Collectables"]["savedData"].Single(x=>(string)x["Name"]==name)["Data"]["Amount"];
    static int Main()
    {
        try{Run();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Run()
    {
        var native=typeof(IPacket).Assembly;
        using var stream=native.GetManifestResourceStream("SSMP.Resource.coop-host-sync.json");
        using var reader=new StreamReader(stream);
        var json=reader.ReadToEnd();
        var dataType=native.GetType("SSMP.Game.Client.Save.CoopHostSyncData",true);
        var data=JsonConvert.DeserializeObject(json,dataType);
        var fields=(IDictionary<string,List<string>>)dataType.GetProperty("Fields").GetValue(data);
        var merge=native.GetType("SSMP.Game.Client.Save.CoopHostSync",true).GetMethod("Merge",BindingFlags.Public|BindingFlags.Static);
        Check(HostResourceSync.Align(null,null,null,fields)==null,"native refusal must remain refused");
        for(int i=0;i<100;i++)
        {
            var host=Save(i,100+i);var client=Save(200-i,50+i);
            host["playerData"]["hasDash"]=false;
            client["playerData"]["silk"]=9;
            var hostBefore=host.ToString();var clientBefore=client.ToString();
            object[] args={host,client,data,null,"1.0.30000",(int?)28104};
            var baseline=(JObject)merge.Invoke(null,args);
            Check(baseline!=null,"real native merge accepts ordinary same-mode saves");
            var baselineBefore=baseline.ToString();
            var aligned=HostResourceSync.Align(host,client,baseline,fields);
            Check((int)aligned["playerData"]["ShellShards"]==i && Amount(aligned,"Tool Metal")==i,"exact host amounts including zero, not union/add/max");
            Check((int)aligned["playerData"]["geo"]==50+i && Amount(aligned,"Rosary_Set_Large")==50+i,"wallet and rosary strings remain client values");
            Check((int)aligned["playerData"]["TempGeoStore"]==51+i && (int)aligned["playerData"]["dicePilgrimBank"]==52+i && (int)aligned["playerData"]["CrawbellCurrency"][0]==53+i,"stored personal currency remains exact");
            Check(!(bool)aligned["playerData"]["hasDash"],"host abilities replace former OR merge");
            Check((int)aligned["playerData"]["silk"]==3,"current resource pool follows host");
            Check(JToken.DeepEquals(aligned["sceneData"],baseline["sceneData"]),"native personal pickups/platform classifications retained");
            Check((int)aligned["playerData"]["playTime"]==50+i && !(bool)aligned["playerData"]["disablePause"],"local metadata/runtime controls not cloned from host");
            Check(host.ToString()==hostBefore && client.ToString()==clientBefore && baseline.ToString()==baselineBefore,"no mutation of source snapshots or native baseline");
            var again=HostResourceSync.Align(host,aligned,aligned,fields);
            Check(JToken.DeepEquals(again,aligned),"rejoin idempotent: no duplicate rewards/resources");
        }
        var h=Save(0,99);var c=Save(10,4);h["playerData"]["health"]=0;h["playerData"]["silk"]=100;
        var safe=HostResourceSync.Align(h,c,c,fields);
        Check((int)safe["playerData"]["health"]==1 && (int)safe["playerData"]["silk"]==9,"downed host cannot create dead joiner or invalid silk pool");
        ((JArray)c["playerData"]["Collectables"]["savedData"]).RemoveAt(1);
        var empty=HostResourceSync.Align(h,c,c,fields);
        Check(!empty["playerData"]["Collectables"]["savedData"].Any(x=>(string)x["Name"]=="Rosary_Set_Large"),"host rosary packs never copied to a client with none");
        Console.WriteLine("Passed "+checks+" host resource/progress/personal-money checks, including actual native JSON merge and repeated joins. No game save or Unity runtime written/executed.");
    }
}
