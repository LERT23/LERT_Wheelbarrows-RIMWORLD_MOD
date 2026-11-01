using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
namespace LertWheelbarrows
{
    public class Zone_WheelbarrowParking : Zone
    {
        private bool wood=true,steel=true,plasteel=true;
        public StoragePriority Priority=StoragePriority.Normal;
        public bool ClearPlants;
        private Material displayMaterial;
        protected override Color NextZoneColor => new Color(0.92f,0.66f,0.18f,0.05f);
        public Zone_WheelbarrowParking() { }
        public Zone_WheelbarrowParking(ZoneManager manager):base("LERT_WB_Parking".Translate(),manager) { }
        public bool Allows(CartKind kind) => kind==CartKind.Wood?wood:kind==CartKind.Steel?steel:plasteel;
        protected override Material CreateMaterial()
        {
            displayMaterial=SolidColorMaterials.NewSolidColorMaterial(new Color(color.r,color.g,color.b,Find.Selector.IsSelected(this)?0.25f:0.05f),ShaderDatabase.SolidColor);
            displayMaterial.renderQueue=3600;
            return displayMaterial;
        }
        public void UpdateOpacity(bool selected)
        {
            if(displayMaterial!=null)displayMaterial.color=new Color(color.r,color.g,color.b,selected?0.25f:0.05f);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref wood,"allowWood",true);Scribe_Values.Look(ref steel,"allowSteel",true);Scribe_Values.Look(ref plasteel,"allowPlasteel",true);
            Scribe_Values.Look(ref Priority,"parkingPriority",StoragePriority.Normal);
            Scribe_Values.Look(ref ClearPlants,"clearPlants");
        }
        public override string GetInspectString() => "LERT_WB_ParkingInfo".Translate(Cells.Count,("LERT_WB_Priority_"+Priority).Translate());
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach(var gizmo in base.GetGizmos()) yield return gizmo;
            foreach(CartKind kind in new[]{CartKind.Wood,CartKind.Steel,CartKind.Plasteel})
            {
                CartKind k=kind;
                string iconName=k==CartKind.Wood?"Allow_Wooden_Wheelbarrows":k==CartKind.Steel?"Allow_Steel_Wheelbarrows":"Allow_Plasteel_Wheelbarrows";
                yield return new Command_Toggle{defaultLabel=("LERT_WB_"+k).Translate(),defaultDesc="LERT_WB_AllowKind".Translate(),icon=ContentFinder<Texture2D>.Get("UI/keys/"+iconName),isActive=()=>Allows(k),toggleAction=()=>{if(k==CartKind.Wood)wood=!wood;else if(k==CartKind.Steel)steel=!steel;else plasteel=!plasteel;}};
            }
            yield return new Command_Toggle{defaultLabel="LERT_WB_ClearPlants".Translate(),defaultDesc="LERT_WB_ClearPlantsDesc".Translate(),icon=ContentFinder<Texture2D>.Get("UI/keys/Clear_Vegetation"),isActive=()=>ClearPlants,toggleAction=()=>{ClearPlants=!ClearPlants;if(ClearPlants)DesignatePlants();}};
            yield return new Command_Action{defaultLabel="LERT_WB_Priority".Translate()+": "+("LERT_WB_Priority_"+Priority).Translate(),defaultDesc="LERT_WB_PriorityDesc".Translate(),icon=TexCommand.ForbidOff,action=()=>
            {
                var choices=new List<FloatMenuOption>();
                foreach(StoragePriority value in new[]{StoragePriority.Low,StoragePriority.Normal,StoragePriority.Preferred,StoragePriority.Important,StoragePriority.Critical})
                {var v=value;choices.Add(new FloatMenuOption(("LERT_WB_Priority_"+v).Translate(),()=>Priority=v));}
                Find.WindowStack.Add(new FloatMenu(choices));
            }};
        }
        public override IEnumerable<Gizmo> GetZoneAddGizmos() {yield return new Designator_WheelbarrowParking();}
        public void DesignatePlants()
        {
            if(!ClearPlants) return;
            foreach(var cell in Cells)
            {
                Plant plant=cell.GetPlant(Map);
                if(plant!=null && Map.designationManager.DesignationOn(plant,DesignationDefOf.CutPlant)==null)
                    Map.designationManager.AddDesignation(new Designation(plant,DesignationDefOf.CutPlant));
            }
        }
    }
}
