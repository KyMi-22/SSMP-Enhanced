using System;
using System.Collections.Generic;
namespace Silksong.CoopAssist
{
    public static class NameGlyphLayout
    {
        public static IEnumerable<uint> Scalars(string value)
        {
            if(value==null)yield break;
            for(int i=0;i<value.Length;i++)
            {
                char ch=value[i];
                if(char.IsHighSurrogate(ch)&&i+1<value.Length&&char.IsLowSurrogate(value[i+1]))
                    yield return (uint)char.ConvertToUtf32(ch,value[++i]);
                else if(!char.IsSurrogate(ch))yield return ch;
            }
        }
        public static bool HasNonLatin(string value)
        {foreach(uint ch in Scalars(value))if(ch>127)return true;return false;}
        // TextCore rectangles start at the lower left. TMProOld saves Y from the top;
        // SaveGlyphVertexInfo computes bottom UV as 1-(Y+height+padding)/atlasHeight.
        public static float LegacyTop(int atlasHeight,int bottom,int height)=>atlasHeight-bottom-height;
    }
}
