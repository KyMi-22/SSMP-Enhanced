using System;
using System.Reflection;
using System.IO;
using Silksong.CoopAssist;
using SSMP.Networking.Packet;

static class EnhancementTests
{
    class Parent { private int number=4; public int Number=>number; }
    class State:Parent { public bool hasFinishedEnteringScene=true; public string Colour {get;set;}="yellow"; }
    static int count;
    static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
    static void Main()
    {
        var state=new State();
        var skinRoot=Path.Combine(AppContext.BaseDirectory,"skin-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(skinRoot,"A"));Directory.CreateDirectory(Path.Combine(skinRoot,"B"));
        File.WriteAllText(Path.Combine(skinRoot,"A","active.txt.off"),"");
        Check(SkinSelection.ActiveDirectories(skinRoot).Length==0,"none means vanilla, never all skins");
        File.WriteAllText(Path.Combine(skinRoot,"B","active.txt"),"");
        Check(SkinSelection.ActiveDirectories(skinRoot).Length==1&&SkinSelection.ActiveDirectories(skinRoot)[0].EndsWith("B"),"only selected pack");
        File.WriteAllText(Path.Combine(skinRoot,"A","active.txt"),"");
        Check(SkinSelection.ActiveDirectories(skinRoot).Length==1,"never merge active packs");
        Check(SkinSelection.ActiveDirectories(Path.Combine(skinRoot,"missing")).Length==0,"no folder means vanilla");
        for(int i=0;i<10000;i++)
        {
            Check((bool)ReflectionCache.Get(state,"hasFinishedEnteringScene"),"field read without property lookup");
            Check(ReflectionCache.Get(state,"Missing")==null,"missing member is silent");
        }
        ReflectionCache.Set(state,"number",9);Check(state.Number==9,"inherited private field write");
        ReflectionCache.Set(state,"Colour","red");Check((string)ReflectionCache.Get(state,"Colour")=="red","property read/write");
        foreach(var text in new[]{null,"","FFF","FFFFFFFFF","<red/>!!","GGFFFFFF","FFFFFFFF\n"})Check(!Appearance.Valid(text),"colour rejection");
        var packetType=typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.Packet",true);
        for(int i=0;i<500;i++)
        {
            var source=new Appearance{Id=(ushort)(i==499?65535:i),Colour=i%2==0?"FF1100FF":"aBcD09EF"};
            Check(Appearance.Valid(source.Colour),"valid colour");
            var writer=(IPacket)Activator.CreateInstance(packetType);source.WriteData(writer);
            var bytes=(byte[])packetType.GetMethod("ToArray").Invoke(writer,null);
            var reader=(IPacket)Activator.CreateInstance(packetType,new object[]{bytes});
            var destination=new Appearance();destination.ReadData(reader);
            Check(source.Id==destination.Id&&source.Colour==destination.Colour,"appearance round trip");
        }
        Console.WriteLine("Passed "+count+" enhancement checks (silent field access, inherited state, validated colour codec). No Unity or sockets.");
    }
}
