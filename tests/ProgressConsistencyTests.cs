using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Networking.Packet;
using Silksong.CoopAssist;

static class ProgressConsistencyTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Refused(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception(why);}
    static readonly Assembly Native=typeof(IPacket).Assembly;
    static readonly Type DataType=Native.GetType("SSMP.Game.Client.Save.CoopHostSyncData",true);
    static readonly MethodInfo Merge=Native.GetType("SSMP.Game.Client.Save.CoopHostSync",true).GetMethod("Merge");
    static object rules;
    static IDictionary<string,List<string>> fields;
    static IDictionary<string,Dictionary<string,string>> bools,ints;
    static ProgressSyncCatalog catalog;
    static JObject Record(string scene,string id,JToken value)=>new JObject{["SceneName"]=scene,["ID"]=id,["Value"]=value,["Mutator"]=0};
    static JArray Rows(JObject save,string name)=>(JArray)save["sceneData"][name]["serializedList"];
    static JObject Item(string name,int amount)=>new JObject{["Name"]=name,["Data"]=new JObject{["Amount"]=amount,["AmountWhileHidden"]=0,["IsSeenMask"]=1}};
    static JObject Quest(string name,bool accepted,bool complete,int count)=>new JObject{["Name"]=name,["Data"]=new JObject{
        ["IsAccepted"]=accepted,["IsCompleted"]=complete,["WasEverCompleted"]=complete,["CompletedCount"]=count,["HasBeenSeen"]=true}};
    static JObject Save(int amount,int money)=>new JObject{
        ["playerData"]=new JObject{
            ["permadeathMode"]=0,["maxHealth"]=5,["maxHealthBase"]=5,["health"]=4,["prevHealth"]=4,
            ["silkMax"]=9,["silk"]=9,["respawnType"]=1,["respawnScene"]="Mosstown_01",["respawnMarkerName"]="RestBench",
            ["geo"]=money,["TempGeoStore"]=money+1,["ShellShards"]=amount,
            ["Collectables"]=new JObject{["savedData"]=new JArray(Item("Mossberry",amount),Item("Rosary_Set_Large",money),Item("Tool Metal",amount))},
            ["QuestCompletionData"]=new JObject{["savedData"]=new JArray(Quest("Mossberry Collection 1",true,false,0),Quest("Courier Delivery Bonebottom",true,false,0))},
            ["QuestRumourData"]=new JObject{["savedData"]=new JArray()},
            ["SteelQuestSpots"]=new JArray(new JObject{["IsSeen"]=amount>0,["SceneName"]="Shellwood_26"}),
            ["FleasCollectedTargetOrder"]=new JArray("SavedFlea_Ant_03"),["fleaGames_juggling_highscore"]=amount,
            ["EnemyJournalKillData"]=new JObject{["list"]=new JArray()},
            ["Tools"]=new JObject{["savedData"]=new JArray()},["ToolEquips"]=new JObject{["savedData"]=new JArray()},
            ["Relics"]=new JObject{["savedData"]=new JArray()},["playTime"]=money},
        ["sceneData"]=new JObject{
            ["persistentBools"]=new JObject{["serializedList"]=new JArray()},
            ["persistentInts"]=new JObject{["serializedList"]=new JArray()},
            ["geoRocks"]=new JObject{["serializedList"]=new JArray(Record("Mosstown_01","personal-rock",money))}}
    };
    static JObject Baseline(JObject host,JObject client)=>(JObject)Merge.Invoke(null,new object[]{host,client,rules,null,"1.0.30000",(int?)28104});
    static JObject Align(JObject host,JObject client)=>HostProgressSync.Align(host,client,Baseline(host,client),fields,catalog);
    static int Amount(JObject save,string name)=>(int)save["playerData"]["Collectables"]["savedData"].Single(x=>(string)x["Name"]==name)["Data"]["Amount"];
    static JToken Value(JObject save,string list,string scene,string id)=>Rows(save,list).SingleOrDefault(x=>((string)x["SceneName"]).Equals(scene,StringComparison.OrdinalIgnoreCase) && (string)x["ID"]==id)?["Value"];
    static void Main(string[] args)
    {
        using(var stream=Native.GetManifestResourceStream("SSMP.Resource.coop-host-sync.json"))
        using(var reader=new StreamReader(stream))rules=JsonConvert.DeserializeObject(reader.ReadToEnd(),DataType);
        fields=(IDictionary<string,List<string>>)DataType.GetProperty("Fields").GetValue(rules);
        bools=(IDictionary<string,Dictionary<string,string>>)DataType.GetProperty("Bools").GetValue(rules);
        ints=(IDictionary<string,Dictionary<string,string>>)DataType.GetProperty("Ints").GetValue(rules);
        catalog=ProgressSyncCatalog.Bundled;
        CheckJoinResourceOwnership();
        var evidence=JObject.Parse(File.ReadAllText(args[0]));
        Check((bool)evidence["Complete"] && (int)evidence["SceneCount"]==590,"catalog covers every shipped scene bundle, not a partial scan");
        Check(evidence["Quests"]["Mossberry Collection 1"]["Targets"][0]["Asset"].ToString()=="Mossberry","quest material dependency taken from actual game asset");
        foreach(var quest in ((JObject)evidence["Quests"]).Properties())
            foreach(var field in quest.Value["Fields"].Values<string>())
                Check(field=="geo" || field=="permadeathMode" || catalog.PlayerDataFields.Contains(field),"every extracted quest counter/test dependency covered: "+quest.Name+" / "+field);
        var source=evidence["Pickups"].First(x=>x["Record"].Type==JTokenType.Array && (string)x["Item"]!=null && !((string)x["Item"]).StartsWith("Rosary_Set_",StringComparison.Ordinal) &&
            (!bools.TryGetValue(((string)x["Record"][0]).ToLowerInvariant(),out var ids) || !ids.ContainsKey((string)x["Record"][1])));
        var sourceScene=(string)source["Record"][0];var sourceId=(string)source["Record"][1];
        foreach(var flag in new[]{"bonegraveAspidBerryCollected","bonetownAspidBerryCollected","mosstownAspidBerryCollected"})
            Check(catalog.PlayerDataFields.Contains(flag),"all three actual berry acquisition flags covered: "+flag);
        var two=Save(2,100);var returning=Save(0,7);
        foreach(var flag in new[]{"bonegraveAspidBerryCollected","bonetownAspidBerryCollected","mosstownAspidBerryCollected"})
        {two["playerData"][flag]=false;returning["playerData"][flag]=true;}
        for(var join=0;join<5;join++)
        {
            if(join==0)
            {
                var old=BeforeSourceAudit.HostResourceSync.Align(two,returning,Baseline(two,returning),fields);
                Check((bool)old["playerData"]["bonegraveAspidBerryCollected"] && (bool)old["playerData"]["bonetownAspidBerryCollected"],"reproduced both specific missing berry source flags in old production source");
            }
            returning=Align(two,returning);
            Check(Amount(returning,"Mossberry")==2,"user's restored two berries stay two; never compensate twice");
            foreach(var flag in new[]{"bonegraveAspidBerryCollected","bonetownAspidBerryCollected","mosstownAspidBerryCollected"})
                Check(!(bool)returning["playerData"][flag],"host availability restores after client consumed source: "+flag);
        }
        for(var i=0;i<25;i++)
        {
            var host=Save(i,200);var client=Save(40-i,7);
            client["playerData"]["QuestCompletionData"]["savedData"][0]=Quest("Mossberry Collection 1",false,true,8);
            client["playerData"]["QuestCompletionData"]["savedData"][1]=Quest("Courier Delivery Bonebottom",false,true,2);
            Rows(client,"persistentBools").Add(Record(sourceScene,sourceId,true));
            Rows(client,"persistentBools").Add(Record("future-room","new-material-not-in-any-table",true));
            var baseline=Baseline(host,client);Check(baseline!=null,"real native merge fixture accepts snapshots");
            var previous=BeforeSourceAudit.HostResourceSync.Align(host,client,baseline,fields);
            BeforeSourceAudit.HostWorldSync.Align(host,previous,"persistentBools",bools);
            BeforeSourceAudit.HostWorldSync.Align(host,previous,"persistentInts",ints);
            Check(Value(previous,"persistentBools",sourceScene,sourceId)!=null,"reproduced previous unclassified pickup mismatch with a real shipped source");
            Check(!JToken.DeepEquals(previous["playerData"]["SteelQuestSpots"],host["playerData"]["SteelQuestSpots"]) || i>0,"previous policy misses dedicated target counter when states differ");
            var hb=host.ToString();var cb=client.ToString();var nb=baseline.ToString();
            var aligned=HostProgressSync.Align(host,client,baseline,fields,catalog);
            Check(JToken.DeepEquals(aligned["playerData"]["QuestCompletionData"],host["playerData"]["QuestCompletionData"]),"accept/completion/count and courier stages follow same host snapshot");
            Check(JToken.DeepEquals(aligned["playerData"]["SteelQuestSpots"],host["playerData"]["SteelQuestSpots"]),"steel quest dedicated records align");
            Check(JToken.DeepEquals(aligned["playerData"]["FleasCollectedTargetOrder"],host["playerData"]["FleasCollectedTargetOrder"]),"flea quest order aligns");
            Check((int)aligned["playerData"]["fleaGames_juggling_highscore"]==i,"counter can reset downward to host");
            Check(Amount(aligned,"Mossberry")==i && Amount(aligned,"Tool Metal")==i,"exact amounts including zero");
            Check(Value(aligned,"persistentBools",sourceScene,sourceId)==null && Value(aligned,"persistentBools","future-room","new-material-not-in-any-table")==null,"no stale consumed source after quantity overwrite");
            Check((int)aligned["playerData"]["geo"]==7 && Amount(aligned,"Rosary_Set_Large")==7,"rosary balances and strings remain exact and personal");
            Check(JToken.DeepEquals(aligned["sceneData"]["geoRocks"],client["sceneData"]["geoRocks"]),"personal rocks unchanged");
            Check(host.ToString()==hb && client.ToString()==cb && baseline.ToString()==nb,"snapshots and native baseline immutable");
            Check(JToken.DeepEquals(Align(host,aligned),aligned),"native merge and production sync remain idempotent on repeated rejoin");
            var loaded=JObject.Parse(aligned.ToString(Formatting.None));
            Check(JToken.DeepEquals(loaded,aligned),"saved JSON preserves the whole consistent snapshot");
        }
        foreach(var list in new[]{"persistentBools","persistentInts"})
        foreach(var row in (JArray)evidence["PersonalRecords"][list])
        {
            var scene=(string)row[0];var id=(string)row[1];var h=Save(3,200);var c=Save(0,7);
            Rows(h,list).Add(Record(scene,id,list=="persistentBools"?(JToken)true:5));
            var absent=Align(h,c);Check(Value(absent,list,scene,id)==null,"host-only personal source never imported");
            Rows(c,list).Add(Record(scene,id,list=="persistentBools"?(JToken)false:99));
            var kept=Align(h,c);Check(JToken.DeepEquals(Value(kept,list,scene,id),Rows(c,list)[0]["Value"]),"client personal source restored even when host differs");
        }
        var emptyHost=Save(0,100);var own=Save(10,7);
        ((JObject)emptyHost["playerData"]).Remove("QuestCompletionData");((JObject)emptyHost["playerData"]).Remove("Collectables");
        var empty=HostProgressSync.Align(emptyHost,own,own,fields,catalog);
        Check(!empty["playerData"]["QuestCompletionData"]["savedData"].Any(),"absent host quest table does not retain stale client wishes");
        Check(Amount(empty,"Rosary_Set_Large")==7 && !empty["playerData"]["Collectables"]["savedData"].Any(x=>(string)x["Name"]=="Mossberry"),"absent host inventory means zero material, personal strings retained");
        var invalid=Save(1,1);invalid["sceneData"]["persistentBools"]["serializedList"]="broken";
        Refused(()=>HostProgressSync.Align(invalid,own,own,fields,catalog),"corrupt source list cannot be treated as an empty world");
        invalid=Save(1,1);invalid["playerData"]["QuestCompletionData"]="broken";
        Refused(()=>HostProgressSync.Align(invalid,own,own,fields,catalog),"corrupt quest table refused");
        var modeHost=Save(0,1);var modeClient=Save(0,7);modeHost["playerData"]["permadeathMode"]=1;
        Check((int)Align(modeHost,modeClient)["playerData"]["permadeathMode"]==0,"quest condition never overwrites local permadeath mode");
        CheckPersonalCrests();
        CheckGrandGate();
        Console.WriteLine("Passed "+checks+" quest, inventory, personal crest and Grand Gate checks, including actual native merge. No Unity or sockets.");
    }

    static void CheckJoinResourceOwnership()
    {
        var host = Save(9, 200);
        var guest = Save(2, 20);
        ((JObject)host["playerData"])["HasMossGrottoMap"] = true;
        ((JObject)host["playerData"])["defeatedLastJudge"] = true;
        ((JObject)host["playerData"])["ToolKitUpgrades"] = 2;
        Rows(host, "persistentInts").Add(Record(HostWorldSync.LocalLedgerScene, "host ledger", 1));
        Rows(guest, "persistentInts").Add(Record(HostWorldSync.LocalLedgerScene, "guest ledger", 1));
        var automatic = HostProgressSync.AlignForJoin(host, guest, Baseline(host, guest), fields, catalog);
        Check(Amount(automatic, "Mossberry") == 2 && (int)automatic["playerData"]["ShellShards"] == 2,
            "automatic join retains personal material amounts");
        Check((int)automatic["playerData"]["geo"] == 20 && Amount(automatic, "Rosary_Set_Large") == 20,
            "automatic join retains loose and strung rosaries");
        Check((bool)automatic["playerData"]["HasMossGrottoMap"] && (bool)automatic["playerData"]["defeatedLastJudge"],
            "automatic join still receives maps and story");
        Check((int)automatic["playerData"]["ToolKitUpgrades"] == 2, "automatic join still receives equipment upgrades");
        var manual = Align(host, guest);
        Check(Amount(manual, "Mossberry") == 9 && (int)manual["playerData"]["ShellShards"] == 9,
            "manual sync still aligns resources");
        foreach (var save in new[] {automatic, manual})
            Check(Value(save, "persistentInts", HostWorldSync.LocalLedgerScene, "guest ledger") != null &&
                Value(save, "persistentInts", HostWorldSync.LocalLedgerScene, "host ledger") == null,
                "snapshot keeps only the joining player's ledger");
    }

    static void CheckGrandGate()
    {
        var host = Save(2, 100);
        var guest = Save(2, 7);
        var hostData = (JObject)host["playerData"];
        var guestData = (JObject)guest["playerData"];
        foreach (var name in GrandGateProgress.StoryRecords) hostData[name] = true;
        hostData["visitedCitadel"] = true;
        guestData["visitedCitadel"] = false;
        var result = Align(host, guest);
        Check(GrandGateProgress.StoryRecords.All(name => (bool)result["playerData"][name]),
            "a returning guest receives the completed gate, visit and Act 2 from the host");
        Check((bool?)result["playerData"]["visitedCitadel"] != true, "a personal Citadel arrival is not forced");

        foreach (var name in GrandGateProgress.StoryRecords) guestData[name] = true;
        foreach (var name in GrandGateProgress.StoryRecords.Skip(1)) hostData[name] = false;
        result = Align(host, guest);
        Check(GrandGateProgress.StoryRecords.All(name => (bool)result["playerData"][name]),
            "an already-completed gate survives a same-victory host resync");
        Check(JToken.DeepEquals(Align(host, result), result), "a completed gate stays stable across resync");

        hostData[GrandGateProgress.Defeated] = false;
        result = Align(host, guest);
        Check(GrandGateProgress.StoryRecords.All(name => (bool?)result["playerData"][name] != true),
            "a guest's past victory does not skip an uncleared host fight");
    }

    static JObject Crest(string name, params string[] tools) => new JObject
    {
        ["Name"]=name, ["Data"]=new JObject
        {
            ["IsUnlocked"]=true,
            ["Slots"]=new JArray(tools.Select(tool=>new JObject{["EquippedTool"]=tool,["IsUnlocked"]=true}))
        }
    };
    static JObject Table(params JObject[] entries) => new JObject { ["savedData"]=new JArray(entries) };
    static JObject FloatingSlot(string name, string tool) => new JObject
    {
        ["Name"]=name,
        ["Data"]=new JObject { ["EquippedTool"]=tool, ["IsUnlocked"]=true }
    };
    static JToken Entry(JObject data, string table, string name) =>
        data[table]["savedData"].Single(row=>(string)row["Name"]==name)["Data"];

    static void CheckPersonalCrests()
    {
        var host=Save(2,100);
        var client=Save(1,7);
        var theirs=(JObject)host["playerData"];
        var mine=(JObject)client["playerData"];
        theirs["CurrentCrestID"]="Reaper";
        theirs["ToolEquips"]=Table(Crest("Hunter","HostTool","HostTool"), Crest("Reaper","HostTool"));
        theirs["ExtraToolEquips"]=Table(FloatingSlot("HostOnly","HostTool"));
        mine["CurrentCrestID"]="Hunter";
        mine["PreviousCrestID"]="Wanderer";
        mine["ToolEquips"]=Table(Crest("Hunter","LocalTool",""), Crest("Wanderer","LocalTool"));
        mine["ExtraToolEquips"]=Table(FloatingSlot("LocalOnly","LocalTool"));
        mine["Tools"]=Table(new JObject{["Name"]="LocalTool",["Data"]=new JObject{["IsUnlocked"]=true,["AmountLeft"]=2}});

        var result=Align(host,client);
        var player=(JObject)result["playerData"];
        Check((string)player["CurrentCrestID"]=="Hunter" && (string)player["PreviousCrestID"]=="Wanderer","local crest selection survives the native and addon merges");
        Check(JToken.DeepEquals(Entry(player,"ToolEquips","Hunter"),Entry(mine,"ToolEquips","Hunter")),"both equipped and empty local sockets survive");
        Check(JToken.DeepEquals(Entry(player,"ToolEquips","Wanderer"),Entry(mine,"ToolEquips","Wanderer")),"local-only unlocked crest stays available");
        Check((bool)Entry(player,"Tools","LocalTool")["IsUnlocked"],"keep the tool needed by the saved local loadout");
        Check(JToken.DeepEquals(Entry(player,"ExtraToolEquips","LocalOnly"),Entry(mine,"ExtraToolEquips","LocalOnly")),"local floating sockets survive");
        Check((string)Entry(player,"ExtraToolEquips","HostOnly")["EquippedTool"]=="","new floating slots do not import host tools");
        Check(!((JObject)player["ToolEquips"]).Descendants().OfType<JProperty>().Any(p=>p.Name=="EquippedTool" && (string)p.Value=="HostTool"),"new crests do not import host tools");
        Check(JToken.DeepEquals(Align(host,result),result),"resync keeps the same crest layout");

        theirs["ToolEquips"]["savedData"].Last.AddAfterSelf(Crest("Hunter_v2","HostTool"));
        var upgraded=(JObject)Align(host,client)["playerData"];
        Check((string)upgraded["CurrentCrestID"]=="Hunter_v2","game-required crest upgrade survives");
        Check((string)Entry(upgraded,"ToolEquips","Hunter_v2")["Slots"][0]["EquippedTool"]=="LocalTool","upgrade inherits the local slot");
    }
}
