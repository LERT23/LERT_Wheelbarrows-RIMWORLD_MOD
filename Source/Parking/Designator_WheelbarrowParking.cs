using RimWorld;
using UnityEngine;
using Verse;
namespace LertWheelbarrows
{
    public class Designator_WheelbarrowParking : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "LERT_WB_Parking".Translate();
        public Designator_WheelbarrowParking()
        {
            zoneTypeToPlace=typeof(Zone_WheelbarrowParking);
            defaultLabel="LERT_WB_Parking".Translate();
            defaultDesc="LERT_WB_ParkingDesc".Translate();
            icon=ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile");
            soundSucceeded=SoundDefOf.Designate_ZoneAdd_Stockpile;
        }
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if(!base.CanDesignateCell(c).Accepted) return false;
            return c.Standable(Map) && !(c.GetEdifice(Map) is Building_Door);
        }
        protected override Zone MakeNewZone() => new Zone_WheelbarrowParking(Find.CurrentMap.zoneManager);
    }
}
