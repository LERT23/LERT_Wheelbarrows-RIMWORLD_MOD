using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public class WorkGiver_WheelbarrowRecovery : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerThings.AllThings.OfType<Thing_Wheelbarrow>();
        public override bool ShouldSkip(Pawn pawn,bool forced=false) => !HaulPlanner.CanUse(pawn) || !pawn.Map.GetComponent<MapComponent_Wheelbarrows>().Enabled(pawn) && !pawn.Map.listerThings.AllThings.OfType<Thing_Wheelbarrow>().Any(c=>c.ParkingRequested);
        public override Job JobOnThing(Pawn pawn,Thing thing,bool forced=false)
        {
            var cart=thing as Thing_Wheelbarrow;
            if(ShouldSkip(pawn,forced) || cart==null || cart.RetryAfter>Find.TickManager.TicksGame || WheelbarrowJobs.Unavailable(pawn,cart)!=null) return null;
            if(!cart.ParkingRequested && !pawn.Map.GetComponent<MapComponent_Wheelbarrows>().Enabled(pawn))return null;
            var zone=pawn.Map.zoneManager.ZoneAt(cart.Position) as Zone_WheelbarrowParking;
            bool parked=zone!=null && zone.Allows(cart.Kind);
            if(parked && !cart.Cargo.Any){cart.ParkingRequested=false;return null;}
            if(cart.ParkingRequested && !parked && !HaulPlanner.FindParking(pawn,cart).HasValue)return null;
            if(cart.Cargo.Any) return WheelbarrowJobs.Create(pawn,cart,true);
            if(!HaulPlanner.FindParking(pawn,cart).HasValue) return null;
            return WheelbarrowJobs.Create(pawn,cart,true);
        }
    }
}
