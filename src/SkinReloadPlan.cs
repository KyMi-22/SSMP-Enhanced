using System;
using System.Collections.Generic;
using System.IO;

namespace Silksong.CoopAssist
{
    public static class SkinReloadPlan
    {
        // Same matching rule as the pinned T2D loader, including Unity atlas hash suffixes.
        public static string Normalize(string name)
        {
            if(string.IsNullOrEmpty(name))return name;
            name=name.Replace('|','_');
            if(!name.StartsWith("sactx-",StringComparison.OrdinalIgnoreCase))return name;
            int dash=name.LastIndexOf('-'),length=name.Length-dash-1;
            if(dash<=0||length<6||length>10)return name;
            for(int i=dash+1;i<name.Length;i++)if(!Uri.IsHexDigit(name[i]))return name;
            return name.Substring(0,dash);
        }
        // File IO only; deliberately no Unity calls on this worker thread.
        public static Dictionary<string,(byte[],int,int)> Read(string pack)
        {
            var result=new Dictionary<string,(byte[],int,int)>(StringComparer.OrdinalIgnoreCase);
            if(pack==null)return result;
            var root=Path.Combine(pack,"Texture2D");
            if(!Directory.Exists(root))return result;
            foreach(var file in Directory.GetFiles(root,"*.png",SearchOption.TopDirectoryOnly))
            {
                var bytes=File.ReadAllBytes(file);
                if(bytes.Length<33||bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||
                    bytes[4]!=13||bytes[5]!=10||bytes[6]!=26||bytes[7]!=10||
                    bytes[8]!=0||bytes[9]!=0||bytes[10]!=0||bytes[11]!=13||
                    bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)
                    throw new InvalidDataException("Invalid skin PNG: "+Path.GetFileName(file));
                int width=Size(bytes,16),height=Size(bytes,20);
                if(width<=0||height<=0)throw new InvalidDataException("Invalid skin dimensions: "+Path.GetFileName(file));
                string key=Normalize(Path.GetFileNameWithoutExtension(file));
                if(result.ContainsKey(key))throw new InvalidDataException("Ambiguous skin atlas: "+key);
                result.Add(key,(bytes,width,height));
            }
            return result;
        }
        static int Size(byte[] bytes,int at) => (bytes[at]<<24)|(bytes[at+1]<<16)|(bytes[at+2]<<8)|bytes[at+3];
    }
}
