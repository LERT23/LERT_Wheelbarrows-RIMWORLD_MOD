using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public enum TransportPhase { Acquire,Gather,Deliver,Park,Empty,Done }
    public static class WheelbarrowJobs
    {
        public static Job Create(Pawn pawn,Thing_Wheelbarrow cart,bool finish,Thing initial=null)
        {
            Job job=JobMaker.MakeJob(WBDefOf.LERT_WB_Transport,cart);
            if(initial!=null) job.targetB=initial;
            job.count=finish?1:0;
            return job;
        }
        public static string Unavailable(Pawn p,Thing_Wheelbarrow cart)
        {
            if(!HaulPlanner.CanUse(p)) return "LERT_WB_CannotWork".Translate();
            if(cart==null || !cart.Spawned || cart.Map!=p.Map || cart.IsForbidden(p)) return "LERT_WB_Forbidden".Translate();
            if(cart.User!=null || !p.CanReserve(cart)) return "LERT_WB_Reserved".Translate();
            if(!p.CanReach(cart,PathEndMode.ClosestTouch,p.NormalMaxDanger())) return "LERT_WB_NoPath".Translate();
            return null;
        }
    }
    public class JobDriver_Wheelbarrow : JobDriver
    {
        private TransportPhase phase;
        private HashSet<int> seen=new HashSet<int>();
        private Thing pickup;
        private Thing deliveryItem;
        private Thing deliveryContainer;
        private IntVec3 deliveryCell=IntVec3.Invalid;
        private IntVec3 parkingCell=IntVec3.Invalid;
        private bool hasParking;
        private bool parkingIsZone;
        private HashSet<IntVec3> rejectedParking=new HashSet<IntVec3>();
        private bool walking,arrived,pathFailed;
        private int nextAction;
        private bool unloadRequested;
        private bool finishRequested;
        private Thing unloadItem;
        private Thing_Wheelbarrow Cart => job.targetA.Thing as Thing_Wheelbarrow;
        private MapComponent_Wheelbarrows State => pawn.Map.GetComponent<MapComponent_Wheelbarrows>();
        public TransportPhase Phase => phase;
        public void RequestUnload(Thing item=null) {unloadRequested=true;unloadItem=item;}
        public void RequestFinish(){finishRequested=true;job.count=1;}
        private bool Urgent => job.workGiverDef?.defName=="HaulUrgently";
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Cart!=null && (State.ActiveCart(pawn)==Cart || Cart.Spawned && pawn.Reserve(Cart,job,errorOnFailed:errorOnFailed));
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref phase,"wbPhase");
            Scribe_Collections.Look(ref seen,"wbSeen",LookMode.Value);
            Scribe_References.Look(ref pickup,"wbPickup");
            Scribe_References.Look(ref deliveryItem,"wbDeliveryItem");
            Scribe_References.Look(ref deliveryContainer,"wbDeliveryContainer");
            Scribe_Values.Look(ref deliveryCell,"wbDeliveryCell",IntVec3.Invalid);
            Scribe_Values.Look(ref parkingCell,"wbParkingCell",IntVec3.Invalid);
            Scribe_Values.Look(ref hasParking,"wbHasParking");
            Scribe_Values.Look(ref parkingIsZone,"wbParkingIsZone");
            Scribe_Collections.Look(ref rejectedParking,"wbRejectedParking",LookMode.Value);
            Scribe_Values.Look(ref unloadRequested,"wbUnloadRequested");
            Scribe_Values.Look(ref finishRequested,"wbFinishRequested");
            Scribe_References.Look(ref unloadItem,"wbUnloadItem");
            if(Scribe.mode==LoadSaveMode.PostLoadInit) {walking=false;arrived=false;seen=seen??new HashSet<int>();rejectedParking=rejectedParking??new HashSet<IntVec3>();}
        }
        public override string GetReport() => Cart!=null && (phase==TransportPhase.Park || phase==TransportPhase.Acquire && job.count==1) ? Cart.ReturnLabel : ("LERT_WB_Phase_"+phase).Translate();
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition=>
            {
                if(pawn.Map!=null) pawn.Map.GetComponent<MapComponent_Wheelbarrows>().Detach(pawn,pawn.Position);
            });
            var work=ToilMaker.MakeToil("WheelbarrowTransport");
            work.defaultCompleteMode=ToilCompleteMode.Never;
            work.tickAction=Step;
            yield return work;
        }
        public override void Notify_PatherArrived() { arrived=true; }
        public override void Notify_PatherFailed() { pathFailed=true;arrived=true; }
        private void ResetTravel() {walking=false;arrived=false;pathFailed=false;}
        private void Change(TransportPhase value) {phase=value;ResetTravel();}
        private bool Travel(LocalTargetInfo target,PathEndMode mode)
        {
            if(!walking)
            {
                arrived=false;pathFailed=false;walking=true;
                pawn.pather.StartPath(target,mode);
            }
            return arrived;
        }
        private void Step()
        {
            if(Find.TickManager.TicksGame<nextAction) return;
            if(!HaulPlanner.CanUse(pawn) || Cart==null || Cart.Destroyed) {EndJobWith(JobCondition.Incompletable);return;}
            if(finishRequested)
            {
                finishRequested=false;
                if(phase!=TransportPhase.Acquire && phase!=TransportPhase.Empty && phase!=TransportPhase.Done)
                {
                    pawn.pather.StopDead();pawn.Map.reservationManager.ReleaseClaimedBy(pawn,job);
                    pickup=null;deliveryItem=null;deliveryContainer=null;
                    hasParking=false;parkingIsZone=false;parkingCell=IntVec3.Invalid;
                    Change(TransportPhase.Deliver);
                }
            }
            if(unloadRequested && State.ActiveCart(pawn)==Cart)
            {
                pawn.pather.StopDead();
                if(!pawn.Position.ContainsStaticFire(pawn.Map))
                {
                    if(unloadItem==null)Cart.Cargo.TryDropAll(pawn.Position,pawn.Map,ThingPlaceMode.Near,State.MarkLeftover);
                    else if(Cart.Cargo.Contains(unloadItem))Cart.Cargo.TryDrop(unloadItem,pawn.Position,pawn.Map,ThingPlaceMode.Near,out Thing dropped,State.MarkLeftover);
                }
                unloadRequested=false;unloadItem=null;
                pickup=null;deliveryItem=null;deliveryContainer=null;
                pawn.Map.reservationManager.ReleaseClaimedBy(pawn,job);
                hasParking=false;parkingIsZone=false;parkingCell=IntVec3.Invalid;
                Change(TransportPhase.Deliver);
                return;
            }
            switch(phase)
            {
                case TransportPhase.Acquire:
                    if(State.ActiveCart(pawn)!=Cart)
                    {
                        if(!Cart.Spawned || Cart.IsForbidden(pawn)) {EndJobWith(JobCondition.Incompletable);return;}
                        if(!Travel(Cart,PathEndMode.ClosestTouch)) return;
                        if(pathFailed || !State.TryAttach(pawn,Cart)) {EndJobWith(JobCondition.Incompletable);return;}
                    }
                    pickup=job.targetB.Thing;
                    Change(job.count==1 || Cart.ParkingRequested || Cart.Cargo.Any ? TransportPhase.Deliver : TransportPhase.Gather);
                    break;
                case TransportPhase.Gather: Gather(); break;
                case TransportPhase.Deliver: Deliver(); break;
                case TransportPhase.Park: Park(); break;
                case TransportPhase.Empty:
                    if(parkingIsZone && !ParkingStillValid()) {RejectParking();return;}
                    if(pawn.Position.ContainsStaticFire(pawn.Map)) {EndJobWith(JobCondition.Incompletable);return;}
                    // Keep the selected cart berth free; otherwise its own leftovers push it out of the zone.
                    Cart.Cargo.TryDropAll(pawn.Position,pawn.Map,ThingPlaceMode.Near,State.MarkLeftover,c=>c!=pawn.Position && c.Standable(pawn.Map) && !(c.GetEdifice(pawn.Map) is Building_Door));
                    if(Cart.Cargo.Any) {Cart.RetryAfter=Find.TickManager.TicksGame+600;EndJobWith(JobCondition.Incompletable);return;}
                    if(State.Detach(pawn,pawn.Position)) {if(parkingIsZone)Cart.ParkingRequested=false;EndJobWith(JobCondition.Succeeded);}
                    else EndJobWith(JobCondition.Incompletable);
                    break;
                case TransportPhase.Done:
                    if(State.Detach(pawn,pawn.Position)) EndJobWith(JobCondition.Succeeded);
                    else EndJobWith(JobCondition.Incompletable);
                    break;
            }
        }
        private void Gather()
        {
            if(!job.playerForced && !State.Enabled(pawn)) {pickup=null;Change(TransportPhase.Deliver);return;}
            if(pickup==null)
            {
                pickup=HaulPlanner.NextPickup(pawn,Cart,seen,Urgent);
                if(pickup==null) {Change(TransportPhase.Deliver);return;}
                if(!Cart.Cargo.Any && !HaulPlanner.HasStorage(pawn,pickup)) {seen.Add(pickup.thingIDNumber);pickup=null;return;}
            }
            if(!HaulPlanner.Eligible(pawn,pickup) || Urgent && (!AllowToolCompatibility.IsUrgent(pawn.Map,pickup) || !HaulPlanner.HasStorage(pawn,pickup))) {SkipPickup();return;}
            int count=LogisticsRules.MaxUnits(HaulPlanner.Free(pawn,Cart),HaulPlanner.UnitMass(pickup),pickup.stackCount);
            if(count<=0 || !pawn.Reserve(pickup,job,1,count,errorOnFailed:false)) {SkipPickup();return;}
            if(!Travel(pickup,PathEndMode.ClosestTouch)) return;
            if(pathFailed) {SkipPickup();return;}
            // Reserve/forbidden/mass are checked again on arrival by Eligible and MaxUnits above.
            Thing source=pickup;
            // Stack-size mods can merge and destroy the source when it enters Cargo.
            // Native 1.6 reservation events require the still-spawned source; release before that atomic transfer.
            pawn.Map.reservationManager.Release(source,pawn,job);
            Thing part=source.SplitOff(count);
            if(part.Spawned) part.DeSpawn();
            if(!Cart.Cargo.TryAdd(part,true))
            {
                if(!GenPlace.TryPlaceThing(part,pawn.Position,pawn.Map,ThingPlaceMode.Near))
                    Cart.Cargo.TryAdd(part,false);
            }
            AllowToolCompatibility.NotifyLoaded(pawn.Map,source);
            seen.Add(source.thingIDNumber);
            pickup=null;ResetTravel();nextAction=Find.TickManager.TicksGame+25;
        }
        private void SkipPickup()
        {
            if(pickup!=null) {seen.Add(pickup.thingIDNumber);if(pickup.Spawned && !pickup.Destroyed && pawn.Map.reservationManager.ReservedBy(pickup,pawn,job))pawn.Map.reservationManager.Release(pickup,pawn,job);}
            pickup=null;ResetTravel();
        }
        private void Deliver()
        {
            if(deliveryItem==null || !Cart.Cargo.Contains(deliveryItem))
            {
                var dest=HaulPlanner.NextDelivery(pawn,Cart);
                if(dest==null) {Change(TransportPhase.Park);return;}
                deliveryItem=dest.Item;deliveryCell=dest.Cell;deliveryContainer=dest.Container;
                if(!pawn.Reserve(dest.Target,job,errorOnFailed:false)) {ClearDelivery();nextAction=Find.TickManager.TicksGame+30;return;}
            }
            LocalTargetInfo target=deliveryContainer!=null ? new LocalTargetInfo(deliveryContainer) : new LocalTargetInfo(deliveryCell);
            // Native IsGoodStoreCell calls CanReserveNew, which deliberately excludes our own reservation.
            // Release and reacquire atomically within this tick so current safety is validated natively.
            if(pawn.Map.reservationManager.ReservedBy(target,pawn,job)) pawn.Map.reservationManager.Release(target,pawn,job);
            bool valid=deliveryContainer!=null
                ? deliveryContainer.Spawned && !deliveryContainer.IsForbidden(pawn) && !deliveryContainer.Position.ContainsStaticFire(pawn.Map) && deliveryContainer is IHaulDestination hd && hd.HaulDestinationEnabled && hd.Accepts(deliveryItem) && deliveryContainer.TryGetInnerInteractableThingOwner()?.GetCountCanAccept(deliveryItem)>0
                : deliveryCell.IsValidStorageFor(pawn.Map,deliveryItem) && StoreUtility.IsGoodStoreCell(deliveryCell,pawn.Map,deliveryItem,pawn,pawn.Faction) && deliveryCell.GetItemStackSpaceLeftFor(pawn.Map,deliveryItem.def)>0;
            if(!valid || !pawn.Reserve(target,job,errorOnFailed:false)) {pawn.pather.StopDead();ClearDelivery();return;}
            if(!Travel(target,deliveryContainer!=null?PathEndMode.ClosestTouch:PathEndMode.OnCell)) return;
            if(!pathFailed)
            {
                if(deliveryContainer!=null)
                {
                    var owner=deliveryContainer.TryGetInnerInteractableThingOwner();
                    if(owner!=null) Cart.Cargo.TryTransferToContainer(deliveryItem,owner,deliveryItem.stackCount);
                }
                else
                {
                    int count=System.Math.Min(deliveryItem.stackCount,deliveryCell.GetItemStackSpaceLeftFor(pawn.Map,deliveryItem.def));
                    if(count>0) Cart.Cargo.TryDrop(deliveryItem,deliveryCell,pawn.Map,ThingPlaceMode.Direct,count,out Thing dropped);
                }
            }
            ReleaseDelivery(target);
            nextAction=Find.TickManager.TicksGame+15;
        }
        private void ClearDelivery() {deliveryItem=null;deliveryContainer=null;deliveryCell=IntVec3.Invalid;ResetTravel();}
        private void ReleaseDelivery(LocalTargetInfo target) {pawn.Map.reservationManager.Release(target,pawn,job);ClearDelivery();}
        private void Park()
        {
            if(!hasParking)
            {
                var cell=HaulPlanner.FindParking(pawn,Cart,rejectedParking);
                parkingIsZone=cell.HasValue;
                parkingCell=cell??pawn.Position;hasParking=true;
                if(cell.HasValue && !pawn.Reserve(parkingCell,job,errorOnFailed:false)) {RejectParking();return;}
            }
            if(!parkingIsZone)
            {
                // Emergency completion at the current location must also terminate outside a changed allowed area.
                // Do not walk into a forbidden area; simply preserve/unload the cart already here.
                pawn.pather.StopDead();
                if(!pawn.Position.Standable(pawn.Map) || pawn.Position.ContainsStaticFire(pawn.Map) || pawn.Position.GetEdifice(pawn.Map) is Building_Door) {EndJobWith(JobCondition.Incompletable);return;}
                Change(TransportPhase.Empty);return;
            }
            if(!ParkingStillValid()) {RejectParking();return;}
            if(!Travel(parkingCell,PathEndMode.OnCell)) return;
            if(pathFailed) {RejectParking();nextAction=Find.TickManager.TicksGame+60;return;}
            Change(TransportPhase.Empty);
        }
        private bool ParkingStillValid()
        {
            return parkingCell.InBounds(pawn.Map) && pawn.Map.zoneManager.ZoneAt(parkingCell) is Zone_WheelbarrowParking zone && pawn.Map.zoneManager.AllZones.Contains(zone) && zone.Allows(Cart.Kind) && HaulPlanner.ValidParkingCell(pawn,parkingCell) && pawn.Map.reservationManager.ReservedBy(parkingCell,pawn,job);
        }
        private void RejectParking()
        {
            if(parkingCell.IsValid)
            {
                rejectedParking.Add(parkingCell);
                if(pawn.Map.reservationManager.ReservedBy(parkingCell,pawn,job)) pawn.Map.reservationManager.Release(parkingCell,pawn,job);
            }
            hasParking=false;parkingIsZone=false;pawn.pather.StopDead();Change(TransportPhase.Park);
        }
    }
}
