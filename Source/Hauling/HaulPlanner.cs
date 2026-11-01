using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public sealed class DeliveryTarget
    {
        public Thing Item;
        public IntVec3 Cell;
        public Thing Container;
        public float Cost;
        public LocalTargetInfo Target => Container!=null ? new LocalTargetInfo(Container) : new LocalTargetInfo(Cell);
        public PathEndMode EndMode => Container!=null ? PathEndMode.ClosestTouch : PathEndMode.OnCell;
    }
    public static class HaulPlanner
    {
        public static bool CanUse(Pawn p) => p?.Spawned==true && p.IsColonistPlayerControlled && p.RaceProps.Humanlike && p.DevelopmentalStage==DevelopmentalStage.Adult && !p.Drafted && !p.Downed && !p.Dead && !p.InMentalState && !p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) && !p.WorkTagIsDisabled(WorkTags.Hauling) && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        public static float UnitMass(Thing thing) => Math.Max(0,(thing is MinifiedThing mini ? mini.InnerThing : thing).GetStatValue(StatDefOf.Mass));
        public static float Budget(Pawn p,Thing_Wheelbarrow cart) => Math.Max(0,MassUtility.Capacity(p)+cart.BonusKg-MassUtility.GearAndInventoryMass(p));
        public static float Free(Pawn p,Thing_Wheelbarrow cart) => Budget(p,cart)-cart.CargoMass;
        public static bool Eligible(Pawn p,Thing t)
        {
            if(t==null || !t.Spawned || t.Map!=p.Map || t.Destroyed || t is Pawn || t is Corpse || t is Thing_Wheelbarrow || !t.def.EverHaulable || (t.def.category!=ThingCategory.Item && !(t is MinifiedThing)) || t.IsForbidden(p)) return false;
            // Minified furniture is a holder of the furniture itself; exclude only additional nested cargo.
            Thing inspect=t is MinifiedThing m ? m.InnerThing : t;
            var inner=inspect.TryGetInnerInteractableThingOwner();
            if(inner!=null && inner.Any) return false;
            return HaulAIUtility.PawnCanAutomaticallyHaul(p,t,false);
        }
        public static float? PathCost(Pawn pawn,LocalTargetInfo target,PathEndMode mode=PathEndMode.OnCell)
        {
            if(!target.IsValid || target.Cell.IsForbidden(pawn)) return null;
            using(var path=pawn.Map.pathFinder.FindPathNow(pawn.Position,target,TraverseParms.For(pawn,pawn.NormalMaxDanger()),peMode:mode))
                return path.Found ? (float?)path.TotalCost : null;
        }
        public static bool HasStorage(Pawn p,Thing t) => StoreUtility.TryFindBestBetterStorageFor(t,p,p.Map,StoragePriority.Unstored,p.Faction,out _,out _);
        private static IEnumerable<Thing> Around(Pawn p,IntVec3 center)
        {
            // No fixed pickup radius: area restrictions, reachability and reservations remain authoritative.
            foreach(var t in p.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                if(t.Spawned)yield return t;
        }
        public static bool ShouldUseCart(Pawn p,Thing initial)
        {
            if(!CanUse(p) || !Eligible(p,initial)) return false;
            int count=p.carryTracker.MaxStackSpaceEver(initial.def);
            if(initial.stackCount>count || UnitMass(initial)*initial.stackCount>MassUtility.FreeSpace(p)) return true;
            // A second stack of another type needs a separate native carry trip.
            foreach(var t in Around(p,initial.Position))
                if(t!=initial && Eligible(p,t) && !t.IsInValidBestStorage() && HasStorage(p,t)) return true;
            return false;
        }
        public static Thing_Wheelbarrow FindCart(Pawn p,Thing initial=null)
        {
            Thing_Wheelbarrow best=null; float bestCost=float.MaxValue;
            foreach(var cart in p.Map.listerThings.AllThings.OfType<Thing_Wheelbarrow>())
            {
                if(cart.User!=null || cart.Cargo.Any || cart.ParkingRequested || cart.IsForbidden(p) || !p.CanReserve(cart)) continue;
                if(initial!=null && LogisticsRules.MaxUnits(Budget(p,cart),UnitMass(initial),1)==0) continue;
                var cost=PathCost(p,cart,PathEndMode.ClosestTouch);
                if(!cost.HasValue) continue;
                if(cost<bestCost || (cost==bestCost && (best==null || cart.BonusKg>best.BonusKg || cart.BonusKg==best.BonusKg && cart.thingIDNumber<best.thingIDNumber))) {best=cart;bestCost=cost.Value;}
            }
            return best;
        }
        public static Thing NextPickup(Pawn p,Thing_Wheelbarrow cart,ISet<int> seen,bool urgentOnly=false)
        {
            var state=p.Map.GetComponent<MapComponent_Wheelbarrows>();
            foreach(var t in Around(p,p.Position).Where(t=>!urgentOnly || AllowToolCompatibility.IsUrgent(p.Map,t)).OrderBy(t=>(t.Position-p.Position).LengthHorizontalSquared).ThenBy(t=>t.thingIDNumber))
            {
                if(seen.Contains(t.thingIDNumber) || !Eligible(p,t)) continue;
                if(urgentOnly && !HasStorage(p,t))continue;
                if(LogisticsRules.MaxUnits(Free(p,cart),UnitMass(t),t.stackCount)==0) continue;
                bool stored=t.IsInValidStorage();
                if(stored && !StoreUtility.TryFindBestBetterStorageFor(t,p,p.Map,StoreUtility.CurrentStoragePriorityOf(t),p.Faction,out _,out _)) continue;
                if(state.IsLeftover(t) && !HasStorage(p,t)) continue;
                return t;
            }
            return null;
        }
        public static DeliveryTarget NextDelivery(Pawn p,Thing_Wheelbarrow cart)
        {
            DeliveryTarget best=null;
            var costCache=new Dictionary<IntVec3,float?>();
            foreach(var item in cart.Cargo)
            {
                var choices=new List<DeliveryTarget>(); int topPriority=-1;
                foreach(var dest in p.Map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder)
                {
                    int priority=(int)dest.GetStoreSettings().Priority;
                    if(priority<topPriority) break;
                    if(!dest.HaulDestinationEnabled || !dest.Accepts(item) || priority==(int)StoragePriority.Unstored) continue;
                    if(dest is ISlotGroupParent slots)
                    {
                        foreach(var cell in slots.AllSlotCells())
                        {
                            if(!StoreUtility.IsGoodStoreCell(cell,p.Map,item,p,p.Faction) || cell.GetItemStackSpaceLeftFor(p.Map,item.def)<=0) continue;
                            if(!costCache.TryGetValue(cell,out var cost)) costCache[cell]=cost=PathCost(p,cell);
                            if(cost.HasValue) {choices.Add(new DeliveryTarget{Item=item,Cell=cell,Cost=cost.Value});topPriority=priority;}
                        }
                    }
                    else if(dest is Thing container && !container.IsForbidden(p) && p.CanReserve(container) && container.TryGetInnerInteractableThingOwner()?.GetCountCanAccept(item)>0)
                    {
                        var cost=PathCost(p,container,PathEndMode.ClosestTouch);
                        if(cost.HasValue) {choices.Add(new DeliveryTarget{Item=item,Cell=container.Position,Container=container,Cost=cost.Value});topPriority=priority;}
                    }
                }
                foreach(var choice in choices)
                    if(best==null || choice.Cost<best.Cost || choice.Cost==best.Cost && choice.Item.thingIDNumber<best.Item.thingIDNumber) best=choice;
            }
            return best;
        }
        public static bool ValidParkingCell(Pawn p,IntVec3 c)
        {
            return c.InBounds(p.Map) && c.Standable(p.Map) && !c.IsForbidden(p) && !c.ContainsStaticFire(p.Map) && !(c.GetEdifice(p.Map) is Building_Door) && (c.GetFirstPawn(p.Map)==null || c.GetFirstPawn(p.Map)==p) && !c.GetThingList(p.Map).Any(t=>t.def.category==ThingCategory.Item || t is Thing_Wheelbarrow) && p.CanReserve(c);
        }
        public static IntVec3? FindParking(Pawn p,Thing_Wheelbarrow cart,ISet<IntVec3> rejected=null)
        {
            IntVec3? best=null; float costBest=float.MaxValue; int priorityBest=-1;
            foreach(var zone in p.Map.zoneManager.AllZones.OfType<Zone_WheelbarrowParking>().Where(z=>z.Allows(cart.Kind)).OrderByDescending(z=>z.Priority))
            {
                if((int)zone.Priority<priorityBest) break;
                foreach(var c in zone.Cells)
                {
                    if(rejected?.Contains(c)==true || !ValidParkingCell(p,c)) continue;
                    var cost=PathCost(p,c);
                    if(cost.HasValue && ((int)zone.Priority>priorityBest || cost<costBest)) {best=c;costBest=cost.Value;priorityBest=(int)zone.Priority;}
                }
            }
            return best;
        }
    }
}
