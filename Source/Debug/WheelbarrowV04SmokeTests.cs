#if WB_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LertWheelbarrows
{
    // Short acceptance run on a disposable map; never included in the release assembly.
    public class GameComponent_WBV04SmokeTests : GameComponent
    {
        private bool started,finished;
        private bool overloaded;
        private int stage,startTick;
        private string output;
        private Pawn worker;
        private Thing_Wheelbarrow cart;
        private IntVec3 origin;
        private Zone_Stockpile stock;
        private readonly List<Thing> projects=new List<Thing>();
        private Map Map=>Find.CurrentMap;
        private MapComponent_Wheelbarrows State=>Map.GetComponent<MapComponent_Wheelbarrows>();
        public GameComponent_WBV04SmokeTests(Game game){}
        private void Check(bool ok,string name){File.AppendAllText(Path.Combine(output,"results.txt"),(ok?"PASS ":"FAIL ")+name+Environment.NewLine);}
        private Thing Spawn(string def,int count,IntVec3 cell)
        {
            var thing=ThingMaker.MakeThing(ThingDef.Named(def));thing.stackCount=count;
            GenSpawn.Spawn(thing,cell,Map);thing.SetForbidden(false,false);return thing;
        }
        public override void GameComponentUpdate()
        {
            if(started||!GenCommandLine.CommandLineArgPassed("wbv04test")||Map==null||Current.ProgramState!=ProgramState.Playing||LongEventHandler.ShouldWaitForEvent||Map.mapPawns.FreeColonistsSpawned.Count==0)return;
            started=true;GenCommandLine.TryGetCommandLineArg("wbtestout",out output);Directory.CreateDirectory(output);
            try{Setup();}catch(Exception e){Check(false,"Setup "+e);Finish();}
        }
        private void Setup()
        {
            origin=Map.Center;Map.fogGrid.ClearAllFog();
            foreach(var p in Map.mapPawns.FreeColonistsSpawned)p.drafter.Drafted=true;
            foreach(var z in Map.zoneManager.AllZones.ToList())z.Delete(false);
            foreach(var c in CellRect.CenteredOn(origin,30,24))
            {
                foreach(var t in c.GetThingList(Map).ToList())if(!(t is Pawn))t.Destroy();
                Map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);
            }
            foreach(var t in Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).ToList())t.Destroy();
            do{worker=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);}while(worker.WorkTagIsDisabled(WorkTags.Hauling)||worker.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)||worker.WorkTypeIsDisabled(WorkTypeDefOf.Construction));
            GenSpawn.Spawn(worker,origin,Map);worker.workSettings.EnableAndInitialize();
            foreach(var wt in DefDatabase<WorkTypeDef>.AllDefs)if(!worker.WorkTypeIsDisabled(wt))worker.workSettings.SetPriority(wt,0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);State.SetEnabled(worker,true);
            cart=(Thing_Wheelbarrow)Spawn("LERT_WB_Wood",1,origin+new IntVec3(1,0,0));
            var parking=new Zone_WheelbarrowParking(Map.zoneManager);Map.zoneManager.RegisterZone(parking);parking.AddCell(origin+new IntVec3(-15,0,0));
            Find.Selector.ClearSelection();Find.Selector.Select(cart,false);
            var button=cart.GetGizmos().OfType<Command_Action>().FirstOrDefault(c=>c.icon==ContentFinder<Texture2D>.Get("UI/keys/Park_Selected_Wheelbarrows"));
            Check(button!=null&&!button.Disabled,"Single selected cart exposes parking button");
            if(button!=null)
            {
                button.action();
                Check(cart.ParkingRequested&&cart.GetGizmos().OfType<Command_Action>().Any(c=>c.icon==button.icon&&c.Disabled),"Parking request disables the selected cart button");
                cart.ParkingRequested=false;
            }
            Find.Selector.ClearSelection();
            stock=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,Map.zoneManager);stock.settings.filter.SetDisallowAll();stock.settings.filter.SetAllow(ThingDefOf.WoodLog,true);Map.zoneManager.RegisterZone(stock);
            foreach(var c in CellRect.CenteredOn(origin+new IntVec3(13,0,0),3))stock.AddCell(c);
            Thing initial=null;
            for(int i=0;i<8;i++)initial=Spawn("WoodLog",75,origin+new IntVec3(-5+i%4,0,5+i/4));
            if(GenCommandLine.CommandLineArgPassed("wbv04constructiononly")){StartConstruction();Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;}
            worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,false,initial));
            stage=1;startTick=Find.TickManager.TicksGame;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        public override void GameComponentTick()
        {
            if(!started||finished||worker==null)return;
            try
            {
                worker.needs.food.CurLevelPercentage=1;worker.needs.rest.CurLevelPercentage=1;
                foreach(var p in Map.mapPawns.FreeColonistsSpawned)if(p!=worker&&!p.Drafted)p.drafter.Drafted=true;
                if(worker.needs.joy!=null)worker.needs.joy.CurLevelPercentage=1;
                int elapsed=Find.TickManager.TicksGame-startTick;
                if(cart.CargoMass>HaulPlanner.Budget(worker,cart)+0.001f)overloaded=true;
                if(Find.TickManager.TicksGame%500==0)File.AppendAllText(Path.Combine(output,"progress.txt"),"stage="+stage+" elapsed="+elapsed+" job="+worker.CurJob?.def.defName+" phase="+(worker.jobs.curDriver as JobDriver_Wheelbarrow)?.Phase+" cargo="+cart.CargoMass+" frames="+Map.listerThings.AllThings.OfType<Frame>().Sum(f=>f.resourceContainer.TotalStackCount)+" groundWood="+Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(t=>t.stackCount)+Environment.NewLine);
                if(stage==1)
                {
                    int stored=Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Where(t=>stock.Cells.Contains(t.Position)).Sum(t=>t.stackCount);
                    bool returning=worker.jobs.curDriver is JobDriver_Wheelbarrow d&&(d.Phase==TransportPhase.Park||d.Phase==TransportPhase.Empty);
                    if(stored==600||returning||elapsed>20000)
                    {
                        Check(stored==600,"Two loads reach storage before first parking leg (stored="+stored+")");
                        Check(Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(t=>t.stackCount)+cart.Cargo.Sum(t=>t.stackCount)==600,"Repeated hauling conserves all 600 wood");
                        StartConstruction();
                    }
                }
                else if(stage==2)
                {
                    int supplied=Map.listerThings.AllThings.OfType<Frame>().Sum(f=>f.resourceContainer.TotalStackCount);
                    if(supplied>=360||elapsed>12000)
                    {
                        Check(supplied==360,"Cart supplies all 120 floor frames across multiple loads (supplied="+supplied+")");
                        int total=Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(t=>t.stackCount)+cart.Cargo.Sum(t=>t.stackCount)+supplied;
                        Check(total==400,"Construction conserves 400 wood including leftovers (total="+total+")");
                        if(worker.jobs.curDriver is JobDriver_Wheelbarrow driver)driver.RequestFinish();
                        stage=3;startTick=Find.TickManager.TicksGame;
                    }
                }
                else if(stage==3&&(cart.Spawned&&Map.zoneManager.ZoneAt(cart.Position) is Zone_WheelbarrowParking||elapsed>5000))
                {
                    Check(cart.Spawned&&!cart.Cargo.Any&&Map.zoneManager.ZoneAt(cart.Position) is Zone_WheelbarrowParking,"Finished cart is empty and parked");Finish();
                }
            }catch(Exception e){Check(false,"Runtime "+e);Finish();}
        }
        private void StartConstruction()
        {
            worker.jobs.EndCurrentJob(JobCondition.InterruptForced,startNewJob:false);State.Detach(worker,worker.Position);
            foreach(var t in Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).ToList())t.Destroy();cart.Cargo.ClearAndDestroyContents();
            stock.Delete(false);worker.Position=origin;
            var terrain=DefDatabase<TerrainDef>.GetNamed("WoodPlankFloor");
            for(int x=0;x<10;x++)for(int z=0;z<12;z++)
            {
                IntVec3 cell=origin+new IntVec3(x,0,10+z);
                foreach(var t in cell.GetThingList(Map).ToList())if(!(t is Pawn))t.Destroy();
                Map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
                projects.Add(GenConstruct.PlaceBlueprintForBuild(terrain,cell,Map,Rot4.North,Faction.OfPlayer,null));
            }
            for(int i=0;i<6;i++)Spawn("WoodLog",i==5?25:75,origin+new IntVec3(-8,0,5+i));
            var giver=DefDatabase<WorkGiverDef>.AllDefs.Select(g=>g.Worker).OfType<WorkGiver_ConstructDeliverResourcesToBlueprints>().First(g=>g.def.workType==WorkTypeDefOf.Hauling);
            Job job=giver.JobOnThing(worker,projects[0],false);
            Check(job?.def==WBDefOf.LERT_WB_Transport,"Native construction work giver chooses wheelbarrow for a large floor");
            if(job?.def!=WBDefOf.LERT_WB_Transport){Finish();return;}
            job.workGiverDef=giver.def;worker.jobs.TryTakeOrderedJob(job);stage=2;startTick=Find.TickManager.TicksGame;
        }
        private void Finish(){Check(!overloaded,"Cart stays within the pawn's mass budget");finished=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;File.WriteAllText(Path.Combine(output,"done.txt"),"done");}
    }
}
#endif
