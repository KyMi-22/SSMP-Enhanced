using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Silksong.CoopAssist
{
    public static class ReflectionCache
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static readonly ConcurrentDictionary<(Type,string), Func<object,object>> Readers = new ConcurrentDictionary<(Type,string), Func<object,object>>();
        static readonly ConcurrentDictionary<(Type,string), Action<object,object>> Writers = new ConcurrentDictionary<(Type,string), Action<object,object>>();
        public static object Get(object instance, string name)
        {
            if (instance == null) return null;
            return Readers.GetOrAdd((instance.GetType(), name), key => {
                for (var t = key.Item1; t != null; t = t.BaseType)
                {
                    var field = t.GetField(key.Item2, Flags);
                    if (field != null) return obj => field.GetValue(obj);
                    var property = t.GetProperty(key.Item2, Flags);
                    if (property != null && property.GetIndexParameters().Length == 0) return obj => property.GetValue(obj);
                }
                return obj => null;
            })(instance);
        }
        public static bool Set(object instance, string name, object value)
        {
            if (instance == null) return false;
            var writer=Writers.GetOrAdd((instance.GetType(),name),key=>{
                for(var t=key.Item1;t!=null;t=t.BaseType)
                {
                    var field=t.GetField(key.Item2,Flags);
                    if(field!=null)return (obj,v)=>field.SetValue(obj,v);
                    var p=t.GetProperty(key.Item2,Flags);
                    if(p?.GetSetMethod(true)!=null)return (obj,v)=>p.SetValue(obj,v);
                }
                return (obj,v)=>{};
            });
            writer(instance,value);return true;
        }
    }
}
