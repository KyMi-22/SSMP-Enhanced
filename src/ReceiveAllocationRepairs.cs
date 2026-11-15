using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    internal static class ReceiveAllocationRepairs
    {
        internal static void Install(ManualLogSource log)
        {
            var type=typeof(ClientAddon).Assembly.GetType("SSMP.Networking.Client.DtlsClient",true);
            new Harmony("silksong.coopassist.receiveallocations").Patch(AccessTools.Method(type,"DtlsReceiveLoop"),
                transpiler:new HarmonyMethod(typeof(ReceiveAllocationRepairs),nameof(ReuseTimeoutBuffer)));
            log.LogInfo("[ReceiveAlloc] Reuse unconsumed timeout buffers; successful packet arrays remain exclusive.");
        }
        static IEnumerable<CodeInstruction> ReuseTimeoutBuffer(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();
            var allocations=code.Count(i=>i.opcode==OpCodes.Newarr&&Equals(i.operand,typeof(byte)));
            var receives=code.Count(i=>(i.opcode==OpCodes.Call||i.opcode==OpCodes.Callvirt)&&i.operand is MethodInfo m&&
                m.Name=="Receive"&&m.ReturnType==typeof(int)&&m.GetParameters().Length==4);
            if(allocations!=1||receives!=1)throw new InvalidOperationException("Pinned DTLS receive loop changed; allocation patch refused.");
            foreach(var i in code)
            {
                if(i.opcode==OpCodes.Newarr&&Equals(i.operand,typeof(byte)))
                {i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(ReceiveScratch),nameof(ReceiveScratch.Buffer));}
                yield return i;
                if((i.opcode==OpCodes.Call||i.opcode==OpCodes.Callvirt)&&i.operand is MethodInfo method&&
                    method.Name=="Receive"&&method.ReturnType==typeof(int)&&method.GetParameters().Length==4)
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReceiveScratch),nameof(ReceiveScratch.Received)));
            }
        }
    }
}
