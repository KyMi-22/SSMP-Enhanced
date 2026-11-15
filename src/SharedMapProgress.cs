using System;
using System.Collections.Generic;

namespace Silksong.CoopAssist
{
    // The ordering is the wire bit layout. Append new fields; never reorder it.
    public sealed class SharedMapProgress
    {
        public static readonly string[] OwnershipFields={
            "HasMossGrottoMap","HasWildsMap","HasBoneforestMap","HasDocksMap",
            "HasGreymoorMap","HasBellhartMap","HasShellwoodMap","HasCrawlMap",
            "HasHuntersNestMap","HasJudgeStepsMap","HasDustpensMap","HasSlabMap",
            "HasPeakMap","HasCitadelUnderstoreMap","HasCoralMap","HasSwampMap",
            "HasCloverMap","HasAbyssMap","HasHangMap","HasSongGateMap","HasHallsMap",
            "HasWardMap","HasCogMap","HasLibraryMap","HasCradleMap","HasArboriumMap",
            "HasAqueductMap","HasWeavehomeMap","hasQuill"
        };
        public static readonly string[] RoomFields={"scenesVisited","scenesMapped","scenesEncounteredBench"};
        internal static bool IsOwnershipField(string field) => Array.IndexOf(OwnershipFields, field) >= 0;
        internal const uint RegionalMapFlags=(1u<<28)-1;
        public const uint ValidFlags=RegionalMapFlags | (1u<<28);
        public uint Flags;
        public int Quill;
        public readonly HashSet<string>[] Rooms={new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),new HashSet<string>(StringComparer.Ordinal)};
        public bool Merge(SharedMapProgress source)
        {
            bool changed=(Flags|source.Flags)!=Flags || source.Quill>Quill;
            Flags|=source.Flags;Quill=Math.Max(Quill,source.Quill);
            for(int i=0;i<Rooms.Length;i++)foreach(var room in source.Rooms[i])changed|=Rooms[i].Add(room);
            return changed;
        }
        public SharedMapProgress Copy(){var copy=new SharedMapProgress();copy.Merge(this);return copy;}
        public bool Same(SharedMapProgress other)
        {
            if(other==null || Flags!=other.Flags || Quill!=other.Quill)return false;
            for(int i=0;i<Rooms.Length;i++)if(!Rooms[i].SetEquals(other.Rooms[i]))return false;
            return true;
        }

    }
}
