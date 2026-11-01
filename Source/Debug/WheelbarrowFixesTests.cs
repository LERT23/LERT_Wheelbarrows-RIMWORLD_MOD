#if WB_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    public class GameComponent_WBFixesTests : GameComponent
    {
        public static int TracePawn=-1;
        private bool started,finished,tabVerified;
        private string output;
        private Pawn worker;
        private Thing_Wheelbarrow cart;
        private Zone_WheelbarrowParking parking;
        private IntVec3 origin;
        private int stage,stageStart,recipeIndex,craftVariant;
        private Bill_Production bill;
        private Building_WorkTable table;
        private Dictionary<string,int> before;
        private static readonly string[] resources={"WoodLog","Steel","Plasteel","ComponentIndustrial"};
        private Map Map=>Find.CurrentMap;
        private MapComponent_Wheelbarrows State=>Map.GetComponent<MapComponent_Wheelbarrows>();
        public GameComponent_WBFixesTests(Game game){}
        public override void GameComponentOnGUI()
        {
            if(!started||tabVerified||cart==null||Find.Selector.SingleSelectedThing!=cart)return;
            tabVerified=true;
            GUI.BeginGroup(new Rect(0,0,500,440));
            try
            {
                var tab=new ITab_WheelbarrowCargo();
                typeof(ITab_WheelbarrowCargo).GetMethod("FillTab",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tab,null);
                Check(true,"Cargo inspector renders through native OnGUI without exception");
            }
            catch(Exception e){Check(false,"Cargo inspector OnGUI "+e);}
            finally{GUI.EndGroup();}
        }
        private void Check(bool ok,string label)
        {
            string line=(ok?"PASS ":"FAIL ")+label;
            Log.Message("[WBFIX] "+line);File.AppendAllText(Path.Combine(output,"results.txt"),line+Environment.NewLine);
        }
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("wbfixestest") || finished || started || Find.CurrentMap==null || Current.ProgramState!=ProgramState.Playing || LongEventHandler.ShouldWaitForEvent)return;
            started=true;GenCommandLine.TryGetCommandLineArg("wbtestout",out output);Directory.CreateDirectory(output);
            try{if(GenCommandLine.CommandLineArgPassed("wbusercolony"))SetupUserColony();else Setup();}catch(Exception e){Check(false,"Setup "+e);finished=true;}
        }
        private Thing Spawn(string name,int count,IntVec3 cell)
        {
            var def=ThingDef.Named(name);var t=ThingMaker.MakeThing(def,def.MadeFromStuff?ThingDefOf.WoodLog:null);t.stackCount=count;
            GenSpawn.Spawn(t,cell,Map);t.SetForbidden(false,false);if(t.def.CanHaveFaction)t.SetFaction(Faction.OfPlayer);return t;
        }
        private void Setup()
        {
            origin=Map.Center;Map.fogGrid.ClearAllFog();
            foreach(var p in Map.mapPawns.FreeColonistsSpawned)p.drafter.Drafted=true;
            foreach(var z in Map.zoneManager.AllZones.ToList())z.Delete(false);
            foreach(var c in CellRect.CenteredOn(origin,35,24))
            {
                foreach(var t in c.GetThingList(Map).ToList())if(!(t is Pawn))t.Destroy(DestroyMode.Vanish);
                Map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);
            }
            do{worker=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);}while(worker.WorkTagIsDisabled(WorkTags.Hauling)||worker.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)||worker.WorkTypeIsDisabled(WorkTypeDefOf.Crafting)||worker.WorkTypeIsDisabled(WorkTypeDefOf.Smithing));
            GenSpawn.Spawn(worker,origin,Map);TracePawn=worker.thingIDNumber;
            worker.workSettings.EnableAndInitialize();
            foreach(var type in DefDatabase<WorkTypeDef>.AllDefs)if(!worker.WorkTypeIsDisabled(type))worker.workSettings.SetPriority(type,0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);worker.workSettings.SetPriority(WorkTypeDefOf.Crafting,4);worker.workSettings.SetPriority(WorkTypeDefOf.Smithing,4);
            State.SetEnabled(worker,true);
            cart=(Thing_Wheelbarrow)Spawn("LERT_WB_Wood",1,origin+new IntVec3(1,0,0));
            string inspect=cart.GetInspectString();
            Check(!inspect.Split('\n').Any(string.IsNullOrWhiteSpace),"Empty cart inspect has no blank lines");
            string cargo="LERT_WB_Cargo".Translate("0","100");
            Check(!cargo.Contains("à")&&!cargo.Contains("ṟ")&&!cargo.Contains("ğ"),"Active-language text is not pseudo-translated");
            parking=new Zone_WheelbarrowParking(Map.zoneManager);Map.zoneManager.RegisterZone(parking);
            foreach(var c in CellRect.CenteredOn(origin+new IntVec3(-8,0,0),1))parking.AddCell(c);
            Find.Selector.ClearSelection();State.MapComponentUpdate();
            Check(Math.Abs(parking.Material.color.a-0.05f)<0.001f,"Unselected parking has 95 percent transparency");
            Find.Selector.Select(parking,false);State.MapComponentUpdate();
            Check(Math.Abs(parking.Material.color.a-0.25f)<0.001f,"Selected parking has 75 percent transparency");
            Find.Selector.ClearSelection();
            var distant=Spawn("Cloth",10,origin+new IntVec3(14,0,0));
            Check(HaulPlanner.NextPickup(worker,cart,new HashSet<int>())==distant,"Pickup finds reachable cargo beyond old radius");
            distant.Destroy(DestroyMode.Vanish);
            var cloth=ThingMaker.MakeThing(ThingDef.Named("Cloth"));cloth.stackCount=10;cart.Cargo.TryAdd(cloth);
            var gold=ThingMaker.MakeThing(ThingDefOf.Gold);gold.stackCount=1;cart.Cargo.TryAdd(gold);
            foreach(var name in new[]{"LERT_WB_Wood","LERT_WB_Steel","LERT_WB_Plasteel"})
            {
                var graphic=ThingDef.Named(name).graphic;
                Check(graphic is Graphic_Wheelbarrow && graphic.MatAt(Rot4.North).mainTexture!=graphic.MatAt(Rot4.East).mainTexture && graphic.MatAt(Rot4.North)==graphic.MatAt(Rot4.South) && graphic.MatAt(Rot4.East)==graphic.MatAt(Rot4.West),"Directional top/side materials for "+name);
                Check(graphic.MeshAt(Rot4.East)!=graphic.MeshAt(Rot4.West),"West mirrors side mesh for "+name);
            }
            State.TryAttach(worker,cart);
            Check(Selector.SelectableObjectsAt(worker.Position,Map).Contains(cart),"Active cart is selectable at its map position");
            Find.Selector.Select(cart,false);
            Check(Find.Selector.SingleSelectedThing==cart,"Native inspector can select held cart");
            Check(cart.CustomRectForSelector.HasValue,"Held cart supplies native selection bracket bounds");
            Check(cart.GetInspectTabs()?.Any(t=>t.GetType().Name=="ITab_WheelbarrowCargo")==true,"Active cart exposes cargo inspector tab");
            worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,false));
            var method=typeof(JobDriver_Wheelbarrow).GetMethod("RequestUnload",new[]{typeof(Thing)});
            if(method==null){Check(false,"Manual unload is available");State.Detach(worker,worker.Position);StartCraft();}
            else{method.Invoke(worker.jobs.curDriver,new object[]{cloth});stage=1;stageStart=Find.TickManager.TicksGame;}
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        public override void GameComponentTick()
        {
            if(!started||finished||worker==null)return;
            try
            {
                worker.needs.food.CurLevelPercentage=1;worker.needs.rest.CurLevelPercentage=1;if(worker.needs.joy!=null)worker.needs.joy.CurLevelPercentage=1;
                int elapsed=Find.TickManager.TicksGame-stageStart;
                if(stage==1&&elapsed>10)
                {
                    Check(!cart.Cargo.Any(t=>t.def==ThingDef.Named("Cloth"))&&cart.Cargo.Any(t=>t.def==ThingDefOf.Gold),"Unloading one stack preserves other cargo");
                    ((JobDriver_Wheelbarrow)worker.jobs.curDriver).RequestUnload();stage=3;stageStart=Find.TickManager.TicksGame;return;
                }
                if(stage==3&&elapsed>10)
                {
                    Check(!cart.Cargo.Any&&Map.listerThings.ThingsOfDef(ThingDef.Named("Cloth")).Sum(t=>t.stackCount)==10,"Manual unload places all cargo on ground without loss");
                    Check(Map.listerThings.ThingsOfDef(ThingDefOf.Gold).Sum(t=>t.stackCount)==1,"Remaining stack unloads without duplication");
                    worker.jobs.EndCurrentJob(JobCondition.InterruptForced);StartCraft();
                }
                if(stage==2)
                {
                    if(bill.repeatCount==0&&elapsed>5)
                    {
                        Check(true,"Prioritized crafting completes recipe "+recipeIndex+" with competing hauling work");
                        if(GenCommandLine.CommandLineArgPassed("wbusercolony")){Finish();return;}
                        var after=Counts();
                        int[][] costs={new[]{80,0,0,0},new[]{10,70,0,1},new[]{10,20,50,2}};
                        for(int i=0;i<resources.Length;i++)Check(before[resources[i]]-after[resources[i]]==costs[recipeIndex][i],"Recipe "+recipeIndex+" consumes "+costs[recipeIndex][i]+" "+resources[i]);
                        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        if(recipeIndex==0&&craftVariant<3)craftVariant++;else recipeIndex++;
                        if(recipeIndex==3)Finish();else StartCraft();
                    }
                    else if(elapsed>10000)
                    {
                        Check(false,"Prioritized crafting times out recipe "+recipeIndex+" job="+worker.CurJob);
                        if(GenCommandLine.CommandLineArgPassed("wbusercolony")){Finish();return;}
                        recipeIndex++;worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        if(recipeIndex==3)Finish();else StartCraft();
                    }
                }
            }
            catch(Exception e){Check(false,"Runtime "+e);Finish();}
        }
        private Dictionary<string,int> Counts()=>resources.ToDictionary(d=>d,d=>Map.listerThings.ThingsOfDef(ThingDef.Named(d)).Sum(t=>t.stackCount));
        private void StartCraft()
        {
            worker.jobs.EndCurrentJob(JobCondition.InterruptForced);State.Detach(worker,worker.Position);
            if(table!=null)table.Destroy(DestroyMode.Vanish);
            foreach(string def in resources)foreach(var t in Map.listerThings.ThingsOfDef(ThingDef.Named(def)).ToList())t.Destroy(DestroyMode.Vanish);
            for(int i=0;i<resources.Length;i++)
            {
                if(i==0&&recipeIndex==0&&craftVariant>0)
                    for(int n=0;n<3;n++)Spawn(resources[i],n==0?36:n==1?24:140,origin+new IntVec3(6+n*2,0,5));
                else Spawn(resources[i],i==0?200:i==3?10:200,origin+new IntVec3(6+i*2,0,5));
            }
            foreach(var old in Map.zoneManager.AllZones.OfType<Zone_Stockpile>().ToList())old.Delete(false);
            var stock=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,Map.zoneManager);Map.zoneManager.RegisterZone(stock);
            stock.settings.filter.SetDisallowAll();stock.settings.filter.SetAllow(ThingDef.Named("Jade"),true);
            foreach(var c in CellRect.CenteredOn(origin+new IntVec3(craftVariant==3?0:-15,0,craftVariant==3?4:10),1))stock.AddCell(c);
            Spawn("Jade",5,origin+new IntVec3(20,0,10));
            string[] bench={"CraftingSpot","FueledSmithy","TableMachining"};
            table=(Building_WorkTable)Spawn(bench[recipeIndex],1,origin+new IntVec3(0,0,craftVariant==3?18:6));
            if(recipeIndex==0&&craftVariant==2){stock.settings.filter.SetAllow(ThingDefOf.Steel,true);Spawn("Steel",1,table.Position);}
            table.TryGetComp<CompRefuelable>()?.Refuel(50);
            DebugSettings.unlimitedPower=true;
            table.TryGetComp<CompPowerTrader>()?.SetUpPowerVars();
            if(table.TryGetComp<CompPowerTrader>() is CompPowerTrader power)power.PowerOn=true;
            bill=(Bill_Production)DefDatabase<RecipeDef>.GetNamed(new[]{"LERT_WB_MakeWood","LERT_WB_MakeSteel","LERT_WB_MakePlasteel"}[recipeIndex]).MakeNewBill();
            bill.repeatMode=BillRepeatModeDefOf.RepeatCount;bill.repeatCount=1;table.BillStack.AddBill(bill);
            var giver=(WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed(new[]{"DoBillsUseCraftingSpot","DoBillsMakeWeapons","DoBillsMachiningTable"}[recipeIndex]).Worker;
            Job job=giver.JobOnThing(worker,table,true);
            Check(job!=null,"Native crafting job created for recipe "+recipeIndex+" variant "+craftVariant+" initial="+job);
            if(recipeIndex==0&&craftVariant==3&&job!=null)
            {
                var common=AccessTools.TypeByName("CommonSense.OpportunisticTasks");
                if(common!=null)
                {
                    stock.settings.filter.SetAllow(ThingDefOf.WoodLog,true);
                    AccessTools.Field(AccessTools.TypeByName("CommonSense.Settings"),"hauling_over_bills").SetValue(null,true);
                    var method=AccessTools.Method(common,"Hauling_Opportunity",new[]{typeof(Job),typeof(Pawn)});
                    var harmony=new Harmony("lert.wheelbarrows");
                    harmony.Unpatch(method,HarmonyPatchType.Prefix,"lert.wheelbarrows");
                    Check(method.Invoke(null,new object[]{job,worker}) is Job,"Common Sense detour reproduced with compatibility guard removed");
                    CommonSenseCompatibility.Apply(harmony);
                    Check(method.Invoke(null,new object[]{job,worker})==null,"Compatibility guard suppresses wheelbarrow bill detour");
                    var ownBill=job.bill;
                    job.bill=DefDatabase<RecipeDef>.AllDefs.First(r=>!r.defName.StartsWith("LERT_WB_")&&r.ingredients.Count>0).MakeNewBill();
                    Check(method.Invoke(null,new object[]{job,worker}) is Job,"Compatibility guard preserves other recipes' Common Sense behavior");
                    job.bill=ownBill;
                }
            }
            before=Counts();
            if(job!=null)worker.jobs.TryTakeOrderedJobPrioritizedWork(job,giver,table.Position);
            stage=2;stageStart=Find.TickManager.TicksGame;
        }
        private void SetupUserColony()
        {
            table=Map.listerThings.ThingsOfDef(ThingDef.Named("CraftingSpot")).OfType<Building_WorkTable>().First(t=>t.BillStack.Bills.Any(b=>b.recipe.defName=="LERT_WB_MakeWood"));
            bill=(Bill_Production)table.BillStack.Bills.First(b=>b.recipe.defName=="LERT_WB_MakeWood");
            worker=Map.mapPawns.FreeColonistsSpawned.Where(p=>!p.Drafted&&!p.WorkTypeIsDisabled(WorkTypeDefOf.Crafting)&&p.workSettings.GetPriority(WorkTypeDefOf.Crafting)>0).OrderBy(p=>p.Position.DistanceToSquared(table.Position)).First();
            TracePawn=worker.thingIDNumber;before=Counts();
            var giver=(WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsUseCraftingSpot").Worker;
            Job job=giver.JobOnThing(worker,table,true);
            Check(job!=null,"User colony prioritized wood job: pawn="+worker.LabelShort+" job="+job+" ingredients="+string.Join(",",job?.targetQueueB??new List<LocalTargetInfo>()));
            if(job==null){Finish();return;}
            Log.Message("[WBUSER] old="+worker.CurJob+" interruptible="+worker.jobs.IsCurrentJobPlayerInterruptible()+" driver="+worker.jobs.curDriver);
            bool accepted=worker.jobs.TryTakeOrderedJobPrioritizedWork(job,giver,table.Position);
            Log.Message("[WBUSER] accepted="+accepted+" current="+worker.CurJob+" queued="+worker.jobs.jobQueue.Count);
            stage=2;stageStart=Find.TickManager.TicksGame;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        private void Finish(){finished=true;Check(true,"RUN COMPLETE");File.WriteAllText(Path.Combine(output,"done.txt"),"done");Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;}
    }
    [HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.StartJob))]
    public static class CraftJobTrace
    {
        public static void Prefix(Job newJob,JobCondition lastJobEndCondition,Pawn ___pawn)
        {
            if(___pawn.thingIDNumber==GameComponent_WBFixesTests.TracePawn)
                Log.Message("[WBCRAFT] tick="+Find.TickManager.TicksGame+" previous="+___pawn.CurJob+" end="+lastJobEndCondition+" next="+newJob+" forced="+newJob.playerForced);
        }
    }
}
#endif
