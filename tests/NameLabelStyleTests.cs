using System;
using Silksong.CoopAssist;
using TMProOld;
using UnityEngine;

namespace UnityEngine
{
    public struct Color
    {
        readonly uint value;
        public Color(uint value){this.value=value;}
        public static bool operator ==(Color a,Color b)=>a.value==b.value;
        public static bool operator !=(Color a,Color b)=>a.value!=b.value;
        public override bool Equals(object value)=>value is Color other && this==other;
        public override int GetHashCode()=>(int)value;
    }
}
namespace TMProOld
{
    public enum FontStyles {Normal,Bold}
    public sealed class TMP_FontAsset {}
    public sealed class TextMeshPro
    {
        public TMP_FontAsset font;
        public float fontSize=22,outlineWidth=.2f;
        public bool enableAutoSizing=true,havePropertiesChanged;
        public FontStyles fontStyle;
        public Color color;
    }
}
static class NameLabelStyleTests
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    static void Main()
    {
        var native=new TMP_FontAsset();var aa=new TMP_FontAsset();
        var first=new TextMeshPro{font=native};var second=new TextMeshPro{font=native};
        var red=new Color(0xFF4444FF);var green=new Color(0x44FF44FF);
        NameLabelStyle.ApplyFont(first,aa);NameLabelStyle.ApplyWeight(first,true);NameLabelStyle.ApplyColour(first,red);
        NameLabelStyle.ApplyFont(second,aa);NameLabelStyle.ApplyWeight(second,false);NameLabelStyle.ApplyColour(second,green);
        Check(first.font==aa && second.font==aa,"Local and peer use the same packaged face");
        Check(first.fontSize==27 && second.fontSize==27 && !first.enableAutoSizing && !second.enableAutoSizing,"Stable 27pt size, no autosizing drift");
        Check(first.fontStyle==FontStyles.Bold && second.fontStyle==FontStyles.Normal,"Each player's transmitted weight stays independent");
        Check(first.color==red && second.color==green,"Each player's transmitted colour stays independent");
        Check(first.havePropertiesChanged && second.havePropertiesChanged,"Renderer is dirtied after an actual style change");
        first.havePropertiesChanged=false;
        NameLabelStyle.ApplyFont(first,aa);NameLabelStyle.ApplyWeight(first,true);NameLabelStyle.ApplyColour(first,red);
        Check(!first.havePropertiesChanged,"Unchanged polling does not rebuild name geometry");
        // The native game, a skin, or a scene reload may restore its old face and size.
        for(int i=0;i<20;i++)
        {
            first.font=native;first.fontSize=22;first.enableAutoSizing=true;first.fontStyle=FontStyles.Normal;first.color=default;
            first.havePropertiesChanged=false;
            NameLabelStyle.ApplyFont(first,aa);NameLabelStyle.ApplyWeight(first,true);NameLabelStyle.ApplyColour(first,red);
            Check(first.font==aa && first.fontSize==27 && !first.enableAutoSizing,"Restore face/size after a native overwrite");
            Check(first.color==red && first.fontStyle==FontStyles.Bold && first.havePropertiesChanged,"Restore colour and weight after a native overwrite");
            var rebuilt=new TextMeshPro{font=native};
            NameLabelStyle.ApplyFont(rebuilt,aa);NameLabelStyle.ApplyWeight(rebuilt,true);NameLabelStyle.ApplyColour(rebuilt,red);
            Check(rebuilt.font==aa && rebuilt.fontSize==27 && rebuilt.color==red,"Newly rebuilt/late-created label receives current identity");
        }
        NameLabelStyle.ApplyWeight(first,false);
        Check(first.fontStyle==FontStyles.Normal && first.outlineWidth==.2f,"Switch bold off without leaving a thick outline");
        NameLabelStyle.ApplyWeight(first,true);
        Check(first.outlineWidth==.4f,"Peer bold outline matches native own-name styling");
        var fallback=new TextMeshPro{font=native};NameLabelStyle.ApplyFont(fallback,null);
        Check(fallback.font==native && fallback.fontSize==27,"Missing custom face retains a readable fallback");
        NameLabelStyle.ApplyFont(null,aa);NameLabelStyle.ApplyWeight(null,true);NameLabelStyle.ApplyColour(null,red);
        Check(true,"Destroyed/missing label is harmless");
        Console.WriteLine("Passed "+checks+" production name renderer recovery, rebuild, independent identity and idempotence checks. Simulated renderer only.");
    }
}
