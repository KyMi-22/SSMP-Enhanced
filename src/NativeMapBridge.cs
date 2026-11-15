using System;
using System.Collections.Generic;

namespace Silksong.CoopAssist
{
    // Direct, typed game access. No scene/component scans or reflection in the map tick.
    internal static class NativeMapBridge
    {
        static readonly Func<PlayerData,bool>[] Owned={
            p=>p.HasMossGrottoMap,p=>p.HasWildsMap,p=>p.HasBoneforestMap,p=>p.HasDocksMap,
            p=>p.HasGreymoorMap,p=>p.HasBellhartMap,p=>p.HasShellwoodMap,p=>p.HasCrawlMap,
            p=>p.HasHuntersNestMap,p=>p.HasJudgeStepsMap,p=>p.HasDustpensMap,p=>p.HasSlabMap,
            p=>p.HasPeakMap,p=>p.HasCitadelUnderstoreMap,p=>p.HasCoralMap,p=>p.HasSwampMap,
            p=>p.HasCloverMap,p=>p.HasAbyssMap,p=>p.HasHangMap,p=>p.HasSongGateMap,p=>p.HasHallsMap,
            p=>p.HasWardMap,p=>p.HasCogMap,p=>p.HasLibraryMap,p=>p.HasCradleMap,p=>p.HasArboriumMap,
            p=>p.HasAqueductMap,p=>p.HasWeavehomeMap,p=>p.hasQuill
        };
        static readonly Action<PlayerData>[] Grant={
            p=>p.HasMossGrottoMap=true,p=>p.HasWildsMap=true,p=>p.HasBoneforestMap=true,p=>p.HasDocksMap=true,
            p=>p.HasGreymoorMap=true,p=>p.HasBellhartMap=true,p=>p.HasShellwoodMap=true,p=>p.HasCrawlMap=true,
            p=>p.HasHuntersNestMap=true,p=>p.HasJudgeStepsMap=true,p=>p.HasDustpensMap=true,p=>p.HasSlabMap=true,
            p=>p.HasPeakMap=true,p=>p.HasCitadelUnderstoreMap=true,p=>p.HasCoralMap=true,p=>p.HasSwampMap=true,
            p=>p.HasCloverMap=true,p=>p.HasAbyssMap=true,p=>p.HasHangMap=true,p=>p.HasSongGateMap=true,p=>p.HasHallsMap=true,
            p=>p.HasWardMap=true,p=>p.HasCogMap=true,p=>p.HasLibraryMap=true,p=>p.HasCradleMap=true,p=>p.HasArboriumMap=true,
            p=>p.HasAqueductMap=true,p=>p.HasWeavehomeMap=true,p=>p.hasQuill=true
        };
        internal static SharedMapProgress Capture(PlayerData player)
        {
            var state=new SharedMapProgress();MergeInto(player,state);return state;
        }
        internal static uint Flags(PlayerData player)
        {
            uint flags=0;for(int i=0;i<Owned.Length;i++)if(Owned[i](player))flags|=1u<<i;
            return flags;
        }
        internal static uint MergeInto(PlayerData player,SharedMapProgress state)
        {
            var flags=Flags(player);
            state.Flags|=flags;state.Quill=Math.Max(state.Quill,player.QuillState);
            MergeRooms(player.scenesVisited,state.Rooms[0]);
            MergeRooms(player.scenesMapped,state.Rooms[1]);
            MergeRooms(player.scenesEncounteredBench,state.Rooms[2]);
            return flags;
        }
        static void MergeRooms(HashSet<string> source, HashSet<string> target)
        {
            if (source == null) return;
            foreach (var room in source)
                if (!target.Contains(room) && SharedMapMessage.ValidRoom(room)) target.Add(room);
        }
        internal static bool Apply(PlayerData player,SharedMapProgress state)
        {
            bool changed=false;
            for(int i=0;i<Owned.Length;i++)if((state.Flags&(1u<<i))!=0 && !Owned[i](player)){Grant[i](player);changed=true;}
            if(player.QuillState<state.Quill){player.QuillState=state.Quill;changed=true;}
            changed|=ApplyRooms(player.scenesVisited,state.Rooms[0]);
            changed|=ApplyRooms(player.scenesMapped,state.Rooms[1]);
            changed|=ApplyRooms(player.scenesEncounteredBench,state.Rooms[2]);
            return changed;
        }
        static bool ApplyRooms(HashSet<string> target,HashSet<string> source)
        {
            if(target==null)throw new InvalidOperationException("Native map collection missing");
            bool changed=false;foreach(var room in source)changed|=target.Add(room);return changed;
        }
    }
}
