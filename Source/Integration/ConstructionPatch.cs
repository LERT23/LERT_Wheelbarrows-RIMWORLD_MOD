using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources),"ResourceDeliverJobFor")]
    public static class ConstructionPatch
    {
        public static void Postfix(WorkGiver_ConstructDeliverResources __instance,Pawn pawn,IConstructible c,ref Job __result)
        {
            if(__result?.def!=JobDefOf.HaulToContainer || !(c is Thing target) || !(target is Frame || target is Blueprint_Build) || pawn.Map==null)return;
            if(!pawn.Map.GetComponent<MapComponent_Wheelbarrows>().Enabled(pawn) || !HaulPlanner.CanUse(pawn))return;
            Thing resource=__result.targetA.Thing;
            if(!HaulPlanner.Eligible(pawn,resource) || !ConstructionPlanner.ShouldUseCart(pawn,resource))return;
            var cart=HaulPlanner.FindCart(pawn,resource);
            if(cart==null)return;
            __result=WheelbarrowJobs.Create(pawn,cart,false,resource);
            __result.targetC=target;__result.workGiverDef=__instance.def;
        }
    }
}
