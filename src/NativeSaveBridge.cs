using System;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    // All private SSMP save bindings live here, pinned by the plugin's assembly hash.
    // Pure policies do not know about Unity, reflection, disk paths or transport.
    internal sealed class NativeSaveBridge
    {
        static readonly Type Save=typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.Save.CoopSave",true);
        static readonly MethodInfo Merge=AccessTools.Method(typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.Save.CoopHostSync",true),"Merge");
        static readonly MethodInfo Rules=AccessTools.Method(Save,"GetHostSyncData");
        static readonly MethodInfo ResetSession=AccessTools.Method(Save,"ResetSession");
        static readonly FieldInfo SessionSlot=AccessTools.Field(Save,"_sessionSlot");
        static readonly Func<int,string> SavePath=(Func<int,string>)AccessTools.Method(Save,"GetSaveFilePath").CreateDelegate(typeof(Func<int,string>));
        static readonly Func<int,string> SlotKey=(Func<int,string>)AccessTools.Method(Save,"GetSlotKey").CreateDelegate(typeof(Func<int,string>));
        static readonly Func<string> Snapshot=(Func<string>)AccessTools.Method(Save,"TakeHostSave").CreateDelegate(typeof(Func<string>));
        internal static readonly Func<bool> IsWriting=(Func<bool>)AccessTools.Method(Save,"IsGameSaving").CreateDelegate(typeof(Func<bool>));
        readonly object owner;
        internal NativeSaveBridge(object owner){this.owner=owner;}
        internal static string Capture()=>Snapshot();
        internal static void Loaded(object owner)
        {
            // A same-slot manual reload otherwise keeps old wish/source caches alive.
            // Reset only AFTER the game installs its loaded PlayerData, so an intervening
            // frame cannot start a new check against the old in-memory inventory.
            ResetSession.Invoke(owner,new object[]{true});
            SessionSlot.SetValue(owner,-1);
        }
        internal SaveGameData Align(string host)
        {
            var theirs=JObject.Parse(host);var mine=JObject.Parse(Capture());
            // Invokes the same patched policy as automatic joining; no separate sync rules.
            var args=new object[]{theirs,mine,Rules.Invoke(owner,null),null,"1.0.30000",(int?)28104};
            JObject result;
            HostSyncRepairs.IncludeResources = true;
            try { result = Merge.Invoke(null,args) as JObject; }
            finally { HostSyncRepairs.IncludeResources = false; }
            if(result==null)throw new InvalidOperationException("Host snapshot refused by native save compatibility checks");
            return SaveDataUtility.DeserializeSaveData<SaveGameData>(result.ToString(Formatting.None));
        }
        internal static string Identity(int slot)=>slot<0?null:SlotKey(slot);
        internal void BackUp(int slot)
        {
            // Native BackUpSave logs and swallows IO failures. Manual replacement requires
            // a confirmed backup, so make a separate immutable copy or refuse the write.
            var path=SavePath(slot);
            if(string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))throw new System.IO.IOException("Current save path unavailable");
            var folder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"CoopAssist-Backups");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.Copy(path,System.IO.Path.Combine(folder,System.IO.Path.GetFileName(path)+"."+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".bak"),false);
        }
    }
}
