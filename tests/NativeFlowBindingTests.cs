using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Silksong.CoopAssist;

static class NativeFlowBindingTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main(string[] args)
    {
        var game=args[0];
        var paths=new[]{Path.Combine(game,"Hollow Knight Silksong_Data","Managed"),Path.Combine(game,"BepInEx","core"),Path.Combine(game,"BepInEx","plugins","SSMP")};
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{
            foreach(var path in paths){var file=Path.Combine(path,name.Name+".dll");if(File.Exists(file))return context.LoadFromAssemblyPath(file);}return null;
        };
        var asm=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"BepInEx","plugins","SSMP","SSMP.dll"));
        Type T(string name)=>asm.GetType(name,true);
        var player=T("SSMP.Networking.Packet.Data.PlayerUpdate");
        var data=Activator.CreateInstance(player,true);
        player.GetProperty("Id").SetValue(data,(ushort)7);
        var id=NativeFlowBindings.Read<ushort>(player,"Id");Check(id(data)==7,"emitted inherited ID getter executes against shipped assembly");
        var position=NativeFlowBindings.Position(player);Check(!position(data),"animation-only data is not a position");
        var types=player.GetProperty("UpdateTypes").GetValue(data);var element=types.GetType().GetGenericArguments()[0];
        types.GetType().GetMethod("Add").Invoke(types,new[]{Enum.Parse(element,"Position")});
        Check(position(data),"emitted position predicate executes without enum boxing");
        for(var n=0;n<3000;n++){id(data);position(data);}
        var bytes=GC.GetAllocatedBytesForCurrentThread();
        for(var n=0;n<10000;n++){id(data);position(data);}
        Check(GC.GetAllocatedBytesForCurrentThread()==bytes,"native payload getters allocate no objects per sample");
        foreach(var name in new[]{"ServerUpdatePacket","ClientUpdatePacket"})
        {
            var type=T("SSMP.Networking.Packet.Update."+name);var packet=Activator.CreateInstance(type,true);
            var sequence=NativeFlowBindings.Read<ushort>(type,"Sequence");type.GetProperty("Sequence").SetValue(packet,(ushort)65535);
            Check(sequence(packet)==65535,"closed generic packet sequence getter "+name);
            var read=NativeFlowBindings.PlayerData(type);Check(read(packet)==null,"missing payload safely returns null "+name);
            var dictionary=(IDictionary)NativeFlowBindings.Method(type,"GetPacketData").Invoke(packet,null);
            var packetId=Enum.Parse(dictionary.GetType().GetGenericArguments()[0],"PlayerUpdate");
            object value=data;
            if(name=="ClientUpdatePacket")
            {
                var collection=T("SSMP.Networking.Packet.Data.PacketDataCollection`1").MakeGenericType(player);
                value=Activator.CreateInstance(collection,true);
                var list=NativeFlowBindings.Read<IList>(T("SSMP.Networking.Packet.Data.RawPacketDataCollection"),"DataInstances")(value);
                list.Add(data);Check(ReferenceEquals(list[0],data),"native collection reader preserves payload identity");
            }
            dictionary.Add(packetId,value);Check(ReferenceEquals(read(packet),value),"emitted dictionary lookup returns actual payload "+name);
        }
        var ui=T("SSMP.Ui.PingInterface");
        var closure=ui.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic).Single(t=>NativeFlowBindings.Field(t,"pingTextComponent")!=null);
        Check(NativeFlowBindings.Field(closure,"netClient")!=null,"native numeric callback retains expected client field");
        var callback=closure.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
            .Single(m=>m.Name.Contains("b__") && m.ReturnType==typeof(void) && m.GetParameters().Length==0);
        Check(callback!=null,"exactly one numeric callback is suppressed");
        NativeFlowBindings.Read<object>(closure,"netClient");
        NativeFlowBindings.Read<object>(T("SSMP.Networking.Client.NetClient"),"UpdateManager");
        NativeFlowBindings.Read<bool>(T("SSMP.Networking.Client.NetClient"),"IsConnected");
        NativeFlowBindings.Read<int>(NativeFlowBindings.Property(T("SSMP.Networking.Client.NetClient"),"UpdateManager").PropertyType,"AverageRtt");
        Check(NativeFlowBindings.Field(ui,"_pingComponentGroup")!=null && NativeFlowBindings.Field(ui,"_netClient")!=null,"native group and connection fields exist");
        Check(NativeFlowBindings.Field(T("SSMP.Ui.ComponentGroup"),"_components")!=null,"signal shares original visibility group");
        Check(NativeFlowBindings.Method(ui,"SetEnabled").GetParameters()[0].ParameterType==typeof(bool),"already-created UI can attach indicator on visibility change");
        Check(NativeFlowBindings.Property(T("SSMP.Ui.Component.Component"),"GameObject")!=null,"native component game object contract");
        foreach(var name in new[]{"Client","Server"})
        {
            var type=T("SSMP.Networking."+name+"."+name+"UpdateManager");
            var send=NativeFlowBindings.Method(type,"SendBytes");
            Check(send!=null && !send.ContainsGenericParameters,"closed generic send hook "+name);
            Check(send.GetParameters()[0].ParameterType==typeof(byte[]),"send buffer signature "+name);
        }
        foreach(var item in new[]{("SSMP.Networking.Packet.PacketManager","HandleClientUpdatePacket",1),
            ("SSMP.Networking.Packet.PacketManager","HandleServerUpdatePacket",2),
            ("SSMP.Game.Server.ServerManager","OnPlayerUpdate",2),
            ("SSMP.Networking.Server.NetServer","GetUpdateManagerForClient",1)})
            Check(NativeFlowBindings.Method(T(item.Item1),item.Item2).GetParameters().Length==item.Item3,"native hook signature "+item.Item2);
        Console.WriteLine("Passed "+checks+" shipped network/UI bindings, including executed emitted payload readers. Game/UI and sockets not started.");
    }
}
