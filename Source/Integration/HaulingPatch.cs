using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    [HarmonyPatch(typeof(WorkGiver_HaulGeneral),nameof(WorkGiver_HaulGeneral.JobOnThing))]
    public static class HaulingPatch
    {
        public static void Postfix(Pawn pawn,Thing t,bool forced,ref Job __result)
        {
            if(__result?.def!=JobDefOf.HaulToCell || pawn.Map==null || !pawn.Map.GetComponent<MapComponent_Wheelbarrows>().Enabled(pawn) || !HaulPlanner.ShouldUseCart(pawn,t)) return;
            var cart=HaulPlanner.FindCart(pawn,t);
            if(cart!=null) __result=WheelbarrowJobs.Create(pawn,cart,false,t);
        }
    }
}
