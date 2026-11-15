using System;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    public static class HostMapSync
    {
        // Join and F9 replace maps, exactly like the other host progress. A previous
        // world's maps must never leak into a newly joined host's save.
        public static void Align(JObject host,JObject target)
        {
            foreach(var field in SharedMapProgress.OwnershipFields)
            {
                var value=host[field];
                if(value!=null && value.Type!=JTokenType.Null && value.Type!=JTokenType.Boolean)
                    throw new ArgumentException("Invalid host map flag: "+field);
                target[field]=(bool?)value??false;
            }
            var quill=host["QuillState"];
            if(quill!=null && quill.Type!=JTokenType.Null && quill.Type!=JTokenType.Integer)
                throw new ArgumentException("Invalid host quill state");
            target["QuillState"]=(int?)quill??0;
            foreach(var field in SharedMapProgress.RoomFields)
            {
                var value=host[field];
                if(value!=null && value.Type!=JTokenType.Null && !(value is JArray))
                    throw new ArgumentException("Invalid host map rooms: "+field);
                if(value is JArray rooms)
                    foreach(var room in rooms)if(room.Type!=JTokenType.String || string.IsNullOrEmpty((string)room))
                        throw new ArgumentException("Invalid host map room: "+field);
                target[field]=value?.Type==JTokenType.Array?value.DeepClone():new JArray();
            }
        }
    }
}
