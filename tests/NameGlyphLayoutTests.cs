using System;
using System.Linq;
using Silksong.CoopAssist;
class NameGlyphLayoutTests
{
    static int checks;
    static void Check(bool valid,string reason){checks++;if(!valid)throw new Exception(reason);}
    static void Main()
    {
        Check(!NameGlyphLayout.HasNonLatin("KYRIE_123"),"English name selected fallback");
        Check(NameGlyphLayout.HasNonLatin("小明ABC"),"Mixed Chinese name ignored");
        Check(NameGlyphLayout.Scalars("小\U00020BB7明").SequenceEqual(new uint[]{0x5c0f,0x20bb7,0x660e}),"Surrogate pair split into invalid glyphs");
        Check(NameGlyphLayout.Scalars("A\ud800B\udc00C").SequenceEqual(new uint[]{65,66,67}),"Malformed UTF16 created glyph IDs");
        Check(!NameGlyphLayout.Scalars(null).Any(),"Null name generated glyphs");
        var random=new Random(19);
        for(int i=0;i<2000;i++)
        {
            int height=random.Next(1,128),bottom=random.Next(0,2048-height);
            float top=NameGlyphLayout.LegacyTop(2048,bottom,height);
            float oldBottom=1-(top+height)/2048f,oldTop=1-top/2048f;
            Check(Math.Abs(oldBottom-bottom/2048f)<.000001,"Legacy bottom UV differs from TextCore");
            Check(Math.Abs(oldTop-(bottom+height)/2048f)<.000001,"Legacy top UV differs from TextCore");
        }
        Console.WriteLine("Passed "+checks+" name glyph checks: ASCII preservation, Chinese/mixed names, Unicode scalar IDs and legacy atlas UV conversion. Rendering not executed.");
    }
}
