using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Silksong.CoopAssist
{
    internal static class NativeFlowBindings
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly;
        internal static PropertyInfo Property(Type type,string name)
        {for(var t=type;t!=null;t=t.BaseType){var p=t.GetProperty(name,Flags);if(p!=null)return p;}return null;}
        internal static FieldInfo Field(Type type,string name)
        {for(var t=type;t!=null;t=t.BaseType){var f=t.GetField(name,Flags);if(f!=null)return f;}return null;}
        internal static MethodInfo Method(Type type,string name)
        {for(var t=type;t!=null;t=t.BaseType)foreach(var m in t.GetMethods(Flags))if(m.Name==name)return m;return null;}
        internal static Func<object,T> Read<T>(Type type,string name)
        {
            var property=Property(type,name);
            var field=property==null?Field(type,name):null;
            var memberType=property?.PropertyType??field?.FieldType;
            if(memberType==null || !typeof(T).IsAssignableFrom(memberType))
                throw new MissingMemberException(type.FullName,name);
            var method=new DynamicMethod("coop_read_"+name,typeof(T),new[]{typeof(object)},typeof(NativeFlowBindings).Module,true);
            var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass,property?.DeclaringType??field.DeclaringType);
            if(property!=null)il.Emit(OpCodes.Callvirt,property.GetGetMethod(true));
            else il.Emit(OpCodes.Ldfld,field);
            il.Emit(OpCodes.Ret);
            return (Func<object,T>)method.CreateDelegate(typeof(Func<object,T>));
        }
        internal static Func<object,bool> Position(Type type)
        {
            var get=Property(type,"UpdateTypes").GetGetMethod(true);
            var enumType=get.ReturnType.GetGenericArguments()[0];
            var contains=get.ReturnType.GetMethod("Contains");
            var method=new DynamicMethod("coop_has_position",typeof(bool),new[]{typeof(object)},typeof(NativeFlowBindings).Module,true);
            var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,type);
            il.Emit(OpCodes.Callvirt,get);il.Emit(OpCodes.Ldc_I4,Convert.ToInt32(Enum.Parse(enumType,"Position")));
            il.Emit(OpCodes.Callvirt,contains);il.Emit(OpCodes.Ret);
            return (Func<object,bool>)method.CreateDelegate(typeof(Func<object,bool>));
        }
        internal static Func<object,object> PlayerData(Type packetType)
        {
            var get=Method(packetType,"GetPacketData");
            var dictionary=get.ReturnType;var args=dictionary.GetGenericArguments();
            var method=new DynamicMethod("coop_player_data",typeof(object),new[]{typeof(object)},typeof(NativeFlowBindings).Module,true);
            var il=method.GetILGenerator();var value=il.DeclareLocal(args[1]);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,packetType);il.Emit(OpCodes.Callvirt,get);
            il.Emit(OpCodes.Ldc_I4,Convert.ToInt32(Enum.Parse(args[0],"PlayerUpdate")));il.Emit(OpCodes.Ldloca,value);
            il.Emit(OpCodes.Callvirt,dictionary.GetMethod("TryGetValue"));il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc,value);il.Emit(OpCodes.Ret);
            return (Func<object,object>)method.CreateDelegate(typeof(Func<object,object>));
        }
    }
}
