using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public static class CommonSenseCompatibility
    {
        public static void Apply(Harmony harmony)
        {
            // Optional integration: keep the normal DoBill driver and ingredient collection.
            // Common Sense's hauling_over_bills detour also runs on player-forced bills.
            var type=AccessTools.TypeByName("CommonSense.OpportunisticTasks");
            if(type==null)return;
            MethodInfo method=AccessTools.Method(type,"Hauling_Opportunity",new[]{typeof(Job),typeof(Pawn)});
            if(method==null || method.ReturnType!=typeof(Job))
            {
                Log.Warning("[LertWheelbarrows] Common Sense hauling compatibility hook unavailable in this version.");
                return;
            }
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(CommonSenseCompatibility),nameof(BeforeHaulingOpportunity)));
        }
        public static bool BeforeHaulingOpportunity(Job billJob,ref Job __result)
        {
            string recipe=billJob?.bill?.recipe?.defName;
            if(billJob?.def!=JobDefOf.DoBill || recipe!="LERT_WB_MakeWood" && recipe!="LERT_WB_MakeSteel" && recipe!="LERT_WB_MakePlasteel")return true;
            __result=null;
            return false;
        }
    }
}
