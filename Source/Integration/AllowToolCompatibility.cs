using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public static class AllowToolCompatibility
    {
        public static void Apply(Harmony harmony)
        {
            var type=AccessTools.TypeByName("AllowTool.WorkGiver_HaulUrgently");
            if(type==null)return;
            var method=AccessTools.Method(type,"JobOnThing",new[]{typeof(Pawn),typeof(Thing),typeof(bool)});
            if(method!=null && method.ReturnType==typeof(Job))
                harmony.Patch(method,postfix:new HarmonyMethod(typeof(AllowToolCompatibility),nameof(AfterUrgentJob)));
            else Log.Warning("[LertWheelbarrows] Allow Tool urgent hauling hook unavailable in this version.");
        }
        public static bool IsUrgent(Map map,Thing thing)
        {
            var def=DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
            return def!=null && thing!=null && map.designationManager.DesignationOn(thing,def)!=null;
        }
        public static void NotifyLoaded(Map map,Thing source)
        {
            // A partial source stack keeps its designation for the next trip.
            if(source.Spawned)return;
            var def=DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
            var designation=def==null?null:map.designationManager.DesignationOn(source,def);
            if(designation!=null)map.designationManager.RemoveDesignation(designation);
        }
        public static void AfterUrgentJob(Pawn pawn,Thing t,bool forced,ref Job __result)
        {
            if(pawn.Map==null || __result==null || (__result.def!=JobDefOf.HaulToCell && __result.def!=JobDefOf.HaulToContainer))return;
            if(!pawn.Map.GetComponent<MapComponent_Wheelbarrows>().Enabled(pawn) || !HaulPlanner.CanUse(pawn) || !HaulPlanner.Eligible(pawn,t))return;
            var cart=HaulPlanner.FindCart(pawn,t);
            if(cart==null)return;
            __result=WheelbarrowJobs.Create(pawn,cart,false,t);
            __result.workGiverDef=DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulUrgently");
        }
    }
}
