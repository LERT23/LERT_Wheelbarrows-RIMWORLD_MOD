using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
namespace LertWheelbarrows
{
    public class MapComponent_Wheelbarrows : MapComponent,IThingHolder
    {
        private ThingOwner<Thing_Wheelbarrow> attached;
        private HashSet<int> enabled=new HashSet<int>();
        private HashSet<int> leftovers=new HashSet<int>();
        public MapComponent_Wheelbarrows(Map map):base(map) { attached=new ThingOwner<Thing_Wheelbarrow>(this); }
        public IThingHolder ParentHolder => map;
        public ThingOwner GetDirectlyHeldThings() => attached;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children,attached);
        public bool Enabled(Pawn pawn) => pawn!=null && enabled.Contains(pawn.thingIDNumber);
        public void SetEnabled(Pawn pawn,bool value) { if(value) enabled.Add(pawn.thingIDNumber); else enabled.Remove(pawn.thingIDNumber); }
        public void MarkLeftover(Thing thing,int count) { if(thing!=null) leftovers.Add(thing.thingIDNumber); }
        public bool IsLeftover(Thing thing) => leftovers.Contains(thing.thingIDNumber);
        public Thing_Wheelbarrow ActiveCart(Pawn pawn) => attached.InnerListForReading.FirstOrDefault(c=>c.User==pawn);
        public IEnumerable<Thing_Wheelbarrow> ActiveCarts => attached.InnerListForReading;
        public bool TryAttach(Pawn pawn,Thing_Wheelbarrow cart)
        {
            if(cart==null || !cart.Spawned || cart.Map!=map || cart.User!=null || ActiveCart(pawn)!=null) return false;
            IntVec3 old=cart.Position;
            cart.DeSpawn();
            if(!attached.TryAdd(cart,false)) { GenSpawn.Spawn(cart,old,map); return false; }
            cart.User=pawn;
            return true;
        }
        public bool Detach(Pawn pawn,IntVec3 position)
        {
            Thing_Wheelbarrow cart=ActiveCart(pawn);
            if(cart==null) return true;
            // TryDrop retains ownership on failure; cargo is never deleted to force a detach.
            if(!attached.TryDrop(cart,position,map,ThingPlaceMode.Near,out Thing_Wheelbarrow dropped)) return false;
            cart.User=null;
            cart.RetryAfter=Find.TickManager.TicksGame+300;
            return true;
        }
        public override void ExposeData()
        {
            Scribe_Deep.Look(ref attached,"attachedWheelbarrows",this);
            Scribe_Collections.Look(ref enabled,"wheelbarrowUsers",LookMode.Value);
            Scribe_Collections.Look(ref leftovers,"wheelbarrowLeftovers",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(attached==null) attached=new ThingOwner<Thing_Wheelbarrow>(this);
                if(enabled==null) enabled=new HashSet<int>();
                if(leftovers==null) leftovers=new HashSet<int>();
            }
        }
        public override void MapComponentTick()
        {
            foreach(var cart in attached.InnerListForReading.ToList())
            {
                cart.Cargo.DoTick();
                Pawn user=cart.User;
                if(user?.Spawned==true && user.Map==map) cart.Position=user.Position;
                if(user==null || !user.Spawned || user.Map!=map || user.Dead || user.Downed || user.Drafted || user.InMentalState || !(user.jobs?.curDriver is JobDriver_Wheelbarrow))
                {
                    IntVec3 pos=user!=null && user.Position.InBounds(map)?user.Position:map.Center;
                    if(attached.TryDrop(cart,pos,map,ThingPlaceMode.Near,out Thing_Wheelbarrow dropped))
                    { cart.User=null;cart.RetryAfter=Find.TickManager.TicksGame+300; }
                }
            }
            if(Find.TickManager.TicksGame%250==0)
                foreach(var zone in map.zoneManager.AllZones.OfType<Zone_WheelbarrowParking>()) zone.DesignatePlants();
        }
        public override void MapComponentUpdate()
        {
            foreach(var zone in map.zoneManager.AllZones.OfType<Zone_WheelbarrowParking>())zone.UpdateOpacity(Find.Selector.IsSelected(zone));
        }
        public override void MapComponentDraw()
        {
            foreach(var cart in attached)
            {
                if(cart.User?.Spawned!=true) continue;
                cart.Graphic.Draw(cart.DrawPos,cart.User.Rotation,cart);
                cart.DrawParkingOverlay();
            }
            foreach(var cart in map.listerThings.AllThings.OfType<Thing_Wheelbarrow>())cart.DrawParkingOverlay();
        }
    }
}
