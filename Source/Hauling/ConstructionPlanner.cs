using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public static class ConstructionPlanner
    {
        public static bool Valid(Pawn pawn,Thing target)
        {
            if(target==null || !target.Spawned || target.Map!=pawn.Map || target.Faction!=pawn.Faction || !(target is Frame || target is Blueprint_Build) || target.IsForbidden(pawn) || target.Fogged())return false;
            // Removing an existing floor and clearing blockers remain native work jobs.
            if(target is Blueprint_Build blue && (blue.def.entityDefToBuild is ThingDef td && td.plant!=null || blue.def.entityDefToBuild is TerrainDef && pawn.Map.terrainGrid.CanRemoveTopLayerAt(blue.Position)))return false;
            return GenConstruct.CanConstruct(target,pawn,checkSkills:false,forced:false);
        }
        public static List<Thing> Targets(Pawn pawn) => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
            .Concat(pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)).Where(t=>Valid(pawn,t))
            .OrderBy(t=>t.Position.DistanceToSquared(pawn.Position)).ThenBy(t=>t.thingIDNumber).ToList();
        public static int Remaining(Pawn pawn,Thing target,ThingDef material)
        {
            if(!(target is IConstructible c))return 0;
            int count=c.ThingCountNeeded(material);
            if(target is IHaulEnroute enroute)count=Math.Min(count,enroute.GetSpaceRemainingWithEnroute(material,pawn));
            return Math.Max(0,count);
        }
        private static Dictionary<ThingDef,int> Demand(Pawn pawn,List<Thing> targets)
        {
            var result=new Dictionary<ThingDef,int>();
            foreach(var target in targets)
                foreach(var cost in ((IConstructible)target).TotalMaterialCost())
                {
                    result.TryGetValue(cost.thingDef,out int total);
                    result[cost.thingDef]=total+Remaining(pawn,target,cost.thingDef);
                }
            return result;
        }
        public static int Needed(Pawn pawn,Thing_Wheelbarrow cart,ThingDef material)
        {
            int total=Targets(pawn).Sum(t=>Remaining(pawn,t,material));
            return Math.Max(0,total-cart.Cargo.Where(t=>t.def==material).Sum(t=>t.stackCount));
        }
        public static bool ShouldUseCart(Pawn pawn,Thing resource)
        {
            var demand=Demand(pawn,Targets(pawn));
            return demand.TryGetValue(resource.def,out int count) && count>0 &&
                (count>pawn.carryTracker.MaxStackSpaceEver(resource.def) || demand.Sum(n=>n.Value*n.Key.GetStatValueAbstract(StatDefOf.Mass))>MassUtility.FreeSpace(pawn));
        }
        public static Thing NextPickup(Pawn pawn,Thing_Wheelbarrow cart,ISet<int> seen)
        {
            var demand=Demand(pawn,Targets(pawn));
            foreach(var item in cart.Cargo)
                if(demand.ContainsKey(item.def))demand[item.def]=Math.Max(0,demand[item.def]-item.stackCount);
            foreach(var t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).OrderBy(t=>t.Position.DistanceToSquared(pawn.Position)).ThenBy(t=>t.thingIDNumber))
                if(!seen.Contains(t.thingIDNumber) && demand.TryGetValue(t.def,out int needed) && needed>0 && HaulPlanner.Eligible(pawn,t) && LogisticsRules.MaxUnits(HaulPlanner.Free(pawn,cart),HaulPlanner.UnitMass(t),Math.Min(needed,t.stackCount))>0)return t;
            return null;
        }
        public static DeliveryTarget NextDelivery(Pawn pawn,Thing_Wheelbarrow cart)
        {
            // Targets already checks native reachability and orders by distance. Do not compute
            // a complete path to every remaining floor tile after each individual delivery.
            foreach(var target in Targets(pawn))
            {
                var item=cart.Cargo.FirstOrDefault(t=>Remaining(pawn,target,t.def)>0);
                if(item==null)continue;
                return new DeliveryTarget{Item=item,Container=target,Cell=target.Position};
            }
            return null;
        }
        public static void RefreshEnroute(Pawn pawn,Thing_Wheelbarrow cart)
        {
            pawn.Map.enrouteManager.ReleaseAllClaimedBy(pawn);
            if(!cart.Cargo.Any)return;
            var available=cart.Cargo.GroupBy(t=>t.def).ToDictionary(g=>g.Key,g=>g.Sum(t=>t.stackCount));
            foreach(var target in Targets(pawn))
            {
                if(!(target is IHaulEnroute enroute))continue;
                foreach(var def in available.Keys.ToList())
                {
                    int count=Math.Min(available[def],Remaining(pawn,target,def));
                    if(count<=0)continue;
                    pawn.Map.enrouteManager.AddEnroute(enroute,pawn,def,count);available[def]-=count;
                }
            }
        }
    }
}
