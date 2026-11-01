using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
namespace LertWheelbarrows
{
    [StaticConstructorOnStartup]
    public class Thing_Wheelbarrow : ThingWithComps, IThingHolder, IThingHolderEvents<Thing>
    {
        private ThingOwner<Thing> cargo;
        public Pawn User;
        public int RetryAfter;
        public bool ParkingRequested;
        public ThingOwner<Thing> Cargo => cargo ?? (cargo=new ThingOwner<Thing>(this));
        public CartKind Kind => def.GetModExtension<WheelbarrowProperties>().kind;
        public float BonusKg => def.GetModExtension<WheelbarrowProperties>().bonusKg;
        public float CargoMass => Cargo.Sum(t=>HaulPlanner.UnitMass(t)*t.stackCount);
        public override Vector3 DrawPos
        {
            get
            {
                if(User?.Spawned!=true)return base.DrawPos;
                Vector3 pos=User.DrawPos+User.Rotation.FacingCell.ToVector3()*0.7f;
                pos.y=Mathf.Max(pos.y,AltitudeLayer.PawnState.AltitudeFor())+0.001f;
                return pos;
            }
        }
        public override CellRect? CustomRectForSelector => User?.Spawned==true ? CellRect.SingleCell(DrawPos.ToIntVec3()) : base.CustomRectForSelector;
        public Thing_Wheelbarrow() { cargo=new ThingOwner<Thing>(this); }
        // A cart is a unique container, even if another mod increases its def's stack limit.
        public override bool CanStackWith(Thing other) => false;
        public ThingOwner GetDirectlyHeldThings() => Cargo;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children,Cargo);
        public void Notify_ItemAdded(Thing item){RefreshCargoGraphic();}
        public void Notify_ItemRemoved(Thing item){RefreshCargoGraphic();}
        private void RefreshCargoGraphic(){if(Spawned)Map.mapDrawer.MapMeshDirty(Position,MapMeshFlagDefOf.Things);}
        public string ReturnLabel => ("LERT_WB_Return"+Kind).Translate();
        public void RequestParking()
        {
            ParkingRequested=true;RetryAfter=0;
            if(User?.jobs?.curDriver is JobDriver_Wheelbarrow driver)driver.RequestFinish();
        }
        private static Material parkingOverlay;
        public void DrawParkingOverlay()
        {
            if(!ParkingRequested || MapHeld==null || PositionHeld.Fogged(MapHeld))return;
            if(parkingOverlay==null)parkingOverlay=MaterialPool.MatFrom("UI/keys/Park_Selected_Wheelbarrows_overlay",ShaderDatabase.MetaOverlay);
            Vector3 pos=DrawPos;
            pos.y=AltitudeLayer.MetaOverlays.AltitudeFor();
            pos.z+=0.5f;
            Graphics.DrawMesh(MeshPool.GridPlane(new Vector2(0.6f,0.6f)),pos,Quaternion.identity,parkingOverlay,0);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref cargo,"cargo",this);
            Scribe_References.Look(ref User,"user");
            Scribe_Values.Look(ref RetryAfter,"retryAfter");
            Scribe_Values.Look(ref ParkingRequested,"parkingRequested");
        }
        protected override void Tick() { base.Tick(); Cargo.DoTick(); }
        public string LoadSummary => User!=null ? "LERT_WB_Load".Translate(CargoMass.ToString("0.##"),HaulPlanner.Budget(User,this).ToString("0.##")) : "LERT_WB_Cargo".Translate(CargoMass.ToString("0.##"),BonusKg.ToString("0"));
        public override string GetInspectString()
        {
            var lines=new[]{base.GetInspectString(),LoadSummary,Cargo.ContentsString,ParkingRequested?(string)"LERT_WB_ParkingRequested".Translate():null};
            return string.Join("\n",lines.SelectMany(s=>(s??"").Split(new[]{'\r','\n'},System.StringSplitOptions.RemoveEmptyEntries)).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim()));
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach(var gizmo in base.GetGizmos())yield return gizmo;
            var selected=Find.Selector.SelectedObjects.OfType<Thing_Wheelbarrow>().Where(c=>c.MapHeld==MapHeld).ToList();
            if(selected.Count>0 && selected[0]==this)
            {
                var park=new Command_Action{defaultLabel=(selected.Count==1?"LERT_WB_ParkSingle":"LERT_WB_ParkSelected").Translate(),defaultDesc="LERT_WB_ParkSelectedDesc".Translate(),icon=ContentFinder<Texture2D>.Get("UI/keys/Park_Selected_Wheelbarrows"),action=()=>
                {
                    foreach(var cart in selected)
                    {
                        if(cart.Destroyed || cart.MapHeld==null)continue;
                        cart.RequestParking();
                    }
                }};
                if(selected.All(c=>c.ParkingRequested))park.Disable("LERT_WB_ParkingRequested".Translate());
                yield return park;
            }
            if(Cargo.Any && User?.jobs?.curDriver is JobDriver_Wheelbarrow driver)
                yield return new Command_Action{defaultLabel="LERT_WB_UnloadAll".Translate(),defaultDesc="LERT_WB_UnloadDesc".Translate(),icon=ContentFinder<Texture2D>.Get(def.graphicData.texPath),action=()=>driver.RequestUnload()};
        }
        public override void Destroy(DestroyMode mode=DestroyMode.Vanish)
        {
            if (Cargo.Any)
            {
                Map m=MapHeld;
                IntVec3 pos=User?.PositionHeld ?? PositionHeld;
                if(m==null || !Cargo.TryDropAll(pos,m,ThingPlaceMode.Near))
                {
                    Log.Warning("[LertWheelbarrows] Destruction postponed: cargo cannot be placed safely.");
                    return;
                }
            }
            base.Destroy(mode);
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            foreach(var option in base.GetFloatMenuOptions(pawn)) yield return option;
            foreach(bool finish in new[]{false,true})
            {
                string label=(finish?"LERT_WB_Finish":"LERT_WB_Continue").Translate();
                string reason=WheelbarrowJobs.Unavailable(pawn,this);
                if(reason!=null) yield return new FloatMenuOption(label+" ("+reason+")",null);
                else yield return new FloatMenuOption(label,()=>pawn.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(pawn,this,finish)));
            }
        }
    }
}
