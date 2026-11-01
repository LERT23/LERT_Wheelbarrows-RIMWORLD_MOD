using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
namespace LertWheelbarrows
{
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    public static class PawnCommands
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,Pawn __instance)
        {
            foreach(var value in values) yield return value;
            Pawn pawn=__instance;
            if(pawn.Map==null || !pawn.IsColonist || !pawn.RaceProps.Humanlike || pawn.DevelopmentalStage!=DevelopmentalStage.Adult) yield break;
            var state=pawn.Map.GetComponent<MapComponent_Wheelbarrows>();
            yield return new Command_Toggle{defaultLabel="LERT_WB_Use".Translate(),defaultDesc="LERT_WB_UseDesc".Translate(),icon=TexCommand.ForbidOff,isActive=()=>state.Enabled(pawn),toggleAction=()=>state.SetEnabled(pawn,!state.Enabled(pawn))};
        }
    }
}
