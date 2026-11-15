using System.Reflection;
using SSMP.Api.Client;
using UnityEngine;

namespace Silksong.CoopAssist
{
    // Native SSMP and game map methods are private in the shipped assemblies.
    // Resolve them once; this adapter is used only when a destination is confirmed.
    internal sealed class TravelMapCoordinates
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly MethodInfo OwnPosition=typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.MapManager")
            .GetMethod("TryGetMapPosition",Flags);
        static readonly MethodInfo ProjectPosition=typeof(GameMap).GetMethod("GetMapPosition",Flags,null,
            new[]{typeof(Vector2),typeof(GameMapScene),typeof(GameObject),typeof(Vector2),typeof(Vector2)},null);
        readonly GameMap map;
        readonly GameMapScene scene;
        readonly GameObject room;
        readonly Vector2 offset,size;
        readonly bool canProject;
        internal readonly Vector2 Origin;

        TravelMapCoordinates(Vector2 origin,GameMap map,GameMapScene scene,GameObject room)
        {
            Origin=origin;this.map=map;this.scene=scene;this.room=room;
            var parent=room.transform.parent;
            canProject=ProjectPosition!=null && parent!=null;
            offset=parent!=null?(Vector2)(room.transform.localPosition+parent.localPosition):Vector2.zero;
            size=ReflectionCache.Get(map,"currentSceneSize") is Vector2 value?value:Vector2.zero;
        }
        internal static TravelMapCoordinates Read(IMapManager manager)
        {
            if(manager==null || OwnPosition==null)return null;
            object[] argument={Vector2.zero};
            if(!(bool)OwnPosition.Invoke(manager,argument))return null;
            var map=ReflectionCache.Get(manager,"_sceneInfoMap") as GameMap;
            var scene=ReflectionCache.Get(manager,"_sceneInfoScene") as GameMapScene;
            var room=ReflectionCache.Get(manager,"_sceneInfoObject") as GameObject;
            if(map==null || scene==null || room==null)return null;
            return new TravelMapCoordinates((Vector2)argument[0],map,scene,room);
        }
        internal bool TryProject(Vector2 world,out Vector2 position)
        {
            position=Vector2.zero;
            if(!canProject || map==null || room==null)return false;
            position=(Vector2)ProjectPosition.Invoke(map,new object[]{world,scene,room,offset,size});
            return true;
        }
    }
}
