using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
namespace LertWheelbarrows
{
    public class ITab_WheelbarrowCargo : ITab
    {
        private Vector2 scroll;
        public ITab_WheelbarrowCargo(){size=new Vector2(500,440);labelKey="LERT_WB_CargoTab";}
        public override bool IsVisible=>SelThing is Thing_Wheelbarrow;
        protected override void UpdateSize(){size=new Vector2(Mathf.Min(500,UI.screenWidth),Mathf.Clamp(PaneTopY-30,120,440));}
        protected override void FillTab()
        {
            var cart=(Thing_Wheelbarrow)SelThing;
            Rect area=new Rect(0,0,size.x,size.y).ContractedBy(12);
            Text.Font=GameFont.Small;
            Widgets.Label(new Rect(area.x,area.y,area.width,28),cart.LoadSummary);
            JobDriver_Wheelbarrow driver=cart.User?.jobs?.curDriver as JobDriver_Wheelbarrow;
            bool canUnload=driver!=null && cart.Cargo.Any && !cart.User.Drafted;
            GUI.enabled=canUnload;
            if(Widgets.ButtonText(new Rect(area.x,area.y+30,area.width,30),"LERT_WB_UnloadAll".Translate()))driver.RequestUnload();
            GUI.enabled=true;
            TooltipHandler.TipRegion(new Rect(area.x,area.y+30,area.width,30),"LERT_WB_UnloadDesc".Translate());
            var items=cart.Cargo.ToList();
            Rect outer=new Rect(area.x,area.y+68,area.width,area.height-68);
            Rect view=new Rect(0,0,outer.width-18,Mathf.Max(outer.height,items.Count*42));
            Widgets.BeginScrollView(outer,ref scroll,view);
            for(int i=0;i<items.Count;i++)
            {
                Thing item=items[i];float y=i*42;
                Widgets.ThingIcon(new Rect(0,y,32,32),item);
                Widgets.Label(new Rect(38,y,view.width-150,38),item.LabelCap+"\n"+(HaulPlanner.UnitMass(item)*item.stackCount).ToString("0.##")+" kg");
                GUI.enabled=canUnload;
                if(Widgets.ButtonText(new Rect(view.width-105,y+3,100,30),"LERT_WB_Unload".Translate()))driver.RequestUnload(item);
                GUI.enabled=true;
            }
            Widgets.EndScrollView();
        }
    }
}
