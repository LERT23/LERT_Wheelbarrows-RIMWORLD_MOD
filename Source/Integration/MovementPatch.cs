using HarmonyLib;
using RimWorld;
using Verse;
namespace LertWheelbarrows
{
    [HarmonyPatch(typeof(StatWorker),nameof(StatWorker.FinalizeValue))]
    public static class MovementPatch
    {
        public static void Postfix(StatRequest req,ref float val,StatDef ___stat)
        {
            if(___stat!=StatDefOf.MoveSpeed || !(req.Thing is Pawn p) || p.Map==null) return;
            var cart=p.Map.GetComponent<MapComponent_Wheelbarrows>().ActiveCart(p);
            if(cart!=null) val=LogisticsRules.Speed(val,cart.Kind);
        }
    }
}
