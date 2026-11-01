using HarmonyLib;
using RimWorld;
using Verse;
namespace LertWheelbarrows
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers),"GetWorkGiverOption")]
    public static class ReturnCartLabel
    {
        public static void Postfix(WorkGiverDef workGiver,LocalTargetInfo target,ref FloatMenuOption __result)
        {
            if(__result!=null && !__result.Disabled && workGiver.defName=="LERT_WB_Recovery" && target.Thing is Thing_Wheelbarrow cart)
                __result.Label=cart.ReturnLabel;
        }
    }
}
