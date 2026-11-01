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
    // Compiled only for isolated acceptance runs, excluded from the release DLL.
    public class GameComponent_WBIntegrationTests : GameComponent
    {
        private bool started,finished;
        private int stage,startTick,stageTick;
        private Pawn worker,other;
        private Thing_Wheelbarrow cart;
        private IntVec3 anchor;
        private Dictionary<string,int> before;
        private List<int> deliveryOrder=new List<int>();
        private Zone_Stockpile food,materials,furniture;
        private Zone_WheelbarrowParking parking;
        private string outDir;
        private int lastCargo;
        private int updateFrames;
        private bool menuVerified;
        private bool activeSaved;
        private Thing untouched;
        private Zone_WheelbarrowParking highParking;
        private bool faultInjected;
        private Fire testFire;
        private IntVec3 fireCell;
        private Area_Allowed restricted;
        private bool worldCaptured;
        public GameComponent_WBIntegrationTests(Game game) { }
        private void Check(bool value,string name)
        {
            string line=(value?"PASS ":"FAIL ")+name;
            Log.Message("[WBTEST] "+line);
            File.AppendAllText(Path.Combine(outDir,"results.txt"),line+Environment.NewLine);
        }
        public override void GameComponentUpdate()
        {
            if(started && stage==3 && !worldCaptured)
            {
                worldCaptured=true;
                try
                {
                    Find.CameraDriver.SetRootPosAndSize((anchor+new IntVec3(-8,0,0)).ToVector3Shifted(),12f);
                    Map.MapUpdate();State.MapComponentDraw();
                    Camera camera=Find.Camera;
                    var texture=new RenderTexture(1366,768,24);
                    var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;
                    camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
                    var pixels=new Texture2D(1366,768,TextureFormat.RGB24,false);
                    pixels.ReadPixels(new Rect(0,0,1366,768),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(outDir,"world.png"),ImageConversion.EncodeToPNG(pixels));
                    camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                    texture.Release();UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(pixels);
                }
                catch(Exception e){Log.Warning("[WBTEST] Offscreen image unavailable: "+e);}
            }
            if(GenCommandLine.CommandLineArgPassed("lertwbtest") && !started && updateFrames++%300==0) Log.Message("[WBTEST] Waiting: "+Current.ProgramState+" map="+(Find.CurrentMap!=null)+" pawns="+(Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count??0));
            if(!GenCommandLine.CommandLineArgPassed("lertwbtest") || finished || Current.ProgramState!=ProgramState.Playing || Find.CurrentMap==null || LongEventHandler.ShouldWaitForEvent) return;
            if(!started)
            {
                if(Find.CurrentMap.mapPawns.FreeColonistsSpawned.Count==0) {Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;}
                started=true;
                GenCommandLine.TryGetCommandLineArg("wbtestout",out outDir);
                outDir=outDir??GenFilePaths.SaveDataFolderPath;Directory.CreateDirectory(outDir);
                try {if(GenCommandLine.CommandLineArgPassed("wbreload"))SetupLoaded();else Setup();} catch(Exception e){Check(false,"Setup "+e);finished=true;}
            }
        }
        public override void GameComponentTick()
        {
            if(!started || finished || worker==null) return;
            try {Run();} catch(Exception e){Check(false,"Runtime "+e);finished=true;}
        }
        public override void GameComponentOnGUI()
        {
            if(!started || worker==null || cart==null || menuVerified || !cart.Spawned) return;
            menuVerified=true;
            Check(cart.GetFloatMenuOptions(worker).Count(x=>x.Label.StartsWith("LERT_WB_Continue".Translate()) || x.Label.StartsWith("LERT_WB_Finish".Translate()))==2,"Both manual commands exist (OnGUI)");
        }
        private Map Map => worker?.Map ?? Find.CurrentMap;
        private MapComponent_Wheelbarrows State => Map.GetComponent<MapComponent_Wheelbarrows>();
        private Thing Spawn(string name,int count,IntVec3 cell)
        {
            var thing=ThingMaker.MakeThing(ThingDef.Named(name));thing.stackCount=count;
            GenSpawn.Spawn(thing,cell,Map);thing.SetForbidden(false,false);return thing;
        }
        private Zone_Stockpile Stock(string name,IntVec3 center,params string[] defs)
        {
            var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,Map.zoneManager);zone.label=name;
            zone.settings.filter.SetDisallowAll();foreach(string d in defs)zone.settings.filter.SetAllow(ThingDef.Named(d),true);
            Map.zoneManager.RegisterZone(zone);
            foreach(var c in CellRect.CenteredOn(center,2))zone.AddCell(c);
            return zone;
        }
        private Pawn GenerateWorker()
        {
            for(int i=0;i<100;i++)
            {
                var candidate=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);
                if(candidate.DevelopmentalStage==DevelopmentalStage.Adult && !candidate.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) && !candidate.WorkTagIsDisabled(WorkTags.Hauling) && candidate.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))return candidate;
                candidate.Destroy(DestroyMode.Vanish);
            }
            throw new InvalidOperationException("Could not generate a haul-capable test pawn.");
        }
        private Dictionary<string,int> Counts()
        {
            var all=Map.listerThings.AllThings.Where(t=>t.Spawned && t.def.category==ThingCategory.Item && !(t is Thing_Wheelbarrow)).ToList();
            foreach(var c in Map.listerThings.AllThings.OfType<Thing_Wheelbarrow>())all.AddRange(c.Cargo);
            if(State.ActiveCart(worker)!=null)all.AddRange(State.ActiveCart(worker).Cargo);
            return all.GroupBy(t=>t is MinifiedThing m?"Mini_"+m.InnerThing.def.defName:t.def.defName).ToDictionary(g=>g.Key,g=>g.Sum(t=>t.stackCount));
        }
        private void SetupLoaded()
        {
            worker=Find.CurrentMap.mapPawns.FreeColonistsSpawned.First(p=>!p.Drafted);
            cart=State.ActiveCart(worker);
            bool completed=GenCommandLine.CommandLineArgPassed("wbcompleted");
            if(cart==null) cart=Map.listerThings.AllThings.OfType<Thing_Wheelbarrow>().First(c=>c.Kind==CartKind.Wood);
            GenCommandLine.TryGetCommandLineArg("wbexpected",out string expectedFile);
            before=File.ReadAllLines(expectedFile).Select(s=>s.Split('=')).ToDictionary(s=>s[0],s=>int.Parse(s[1]));
            Check(Counts().OrderBy(x=>x.Key).SequenceEqual(before.OrderBy(x=>x.Key)),"Save/load exact item quantities");
            Check(!State.Enabled(worker),"Personal toggle survives save/load");
            parking=Map.zoneManager.AllZones.OfType<Zone_WheelbarrowParking>().First(z=>z.label=="WB parking");
            Check(parking.Priority==StoragePriority.Critical && parking.Allows(CartKind.Wood),"Parking settings survive save/load");
            if(completed)
            {
                Check(cart.Spawned && !cart.Cargo.Any && cart.User==null,"Completed empty cart survives save/load");
                Check(true,"RUN COMPLETE");finished=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                File.WriteAllText(Path.Combine(outDir,"done.txt"),"complete");return;
            }
            Check(State.ActiveCart(worker)==cart && cart.User==worker && !cart.Spawned,"Active cart ownership survives save/load");
            Check(cart.Cargo.Count>=11 && cart.Cargo.Any(t=>t is MinifiedThing),"Mixed cargo and minified chair survive save/load");
            Check(cart.Cargo.All(t=>t.holdingOwner==cart.Cargo),"Every loaded item has exactly one cargo owner");
            food=Map.zoneManager.AllZones.OfType<Zone_Stockpile>().First(z=>z.label=="WB food");
            materials=Map.zoneManager.AllZones.OfType<Zone_Stockpile>().First(z=>z.label=="WB materials");
            furniture=Map.zoneManager.AllZones.OfType<Zone_Stockpile>().First(z=>z.label=="WB furniture");
            anchor=Map.Center;lastCargo=cart.Cargo.Sum(t=>t.stackCount);activeSaved=true;
            startTick=stageTick=Find.TickManager.TicksGame;stage=2;
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
            CameraJumper.TryJumpAndSelect(worker);
        }
        private void Setup()
        {
            var map=Find.CurrentMap;anchor=map.Center;
            map.fogGrid.ClearAllFog();
            foreach(var p in map.mapPawns.FreeColonistsSpawned.ToList()) {p.drafter.Drafted=true;p.pather.StopDead();}
            foreach(var c in CellRect.CenteredOn(anchor,26,18))
            {
                foreach(var t in c.GetThingList(map).ToList()) if(!(t is Pawn))t.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);
            }
            GameDataSaveLoader.SaveGame("WB_base");
            worker=GenerateWorker();
            other=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);
            GenSpawn.Spawn(worker,anchor+new IntVec3(-9,0,0),map);GenSpawn.Spawn(other,anchor+new IntVec3(-20,0,-10),map);other.drafter.Drafted=true;
            worker.workSettings.EnableAndInitialize();
            foreach(var type in DefDatabase<WorkTypeDef>.AllDefs) if(!worker.WorkTypeIsDisabled(type))worker.workSettings.SetPriority(type,0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);
            worker.playerSettings.hostilityResponse=HostilityResponseMode.Ignore;
            Check(HaulPlanner.CanUse(worker),"Fixture worker is eligible to haul");
            parking=new Zone_WheelbarrowParking(map.zoneManager){label="WB parking",Priority=StoragePriority.Critical};map.zoneManager.RegisterZone(parking);
            foreach(var c in CellRect.CenteredOn(anchor+new IntVec3(-9,0,0),2))parking.AddCell(c);
            cart=(Thing_Wheelbarrow)Spawn("LERT_WB_Wood",1,anchor+new IntVec3(-8,0,0));
            var steel=(Thing_Wheelbarrow)Spawn("LERT_WB_Steel",1,anchor+new IntVec3(-10,0,1));
            var plasteel=(Thing_Wheelbarrow)Spawn("LERT_WB_Plasteel",1,anchor+new IntVec3(-10,0,-1));
            int originalStackLimit=cart.def.stackLimit;
            cart.def.stackLimit=50;
            Check(!cart.CanStackWith(ThingMaker.MakeThing(cart.def)),"Cart identity survives stack-limit mods");
            cart.def.stackLimit=originalStackLimit;
            Check(worker.GetGizmos().OfType<Command_Toggle>().Any(g=>g.defaultLabel=="LERT_WB_Use".Translate()),"Personal toggle exists");
            Check(!State.Enabled(worker),"Automation initially disabled");
            float native=worker.GetStatValue(StatDefOf.MoveSpeed,true,0);
            float originalInventory=MassUtility.GearAndInventoryMass(worker);
            foreach(var c in new[]{cart,steel,plasteel})
            {
                Check(State.TryAttach(worker,c),"Attach "+c.Kind);
                Check(!State.TryAttach(other,c),"Exclusive owner "+c.Kind);
                float speed=worker.GetStatValue(StatDefOf.MoveSpeed,true,0);
                Check(Math.Abs(speed-LogisticsRules.Speed(native,c.Kind))<0.01f,"Native MoveSpeed "+c.Kind+" = "+speed);
                Check(Math.Abs(MassUtility.GearAndInventoryMass(worker)-originalInventory)<0.001f,"No inventory mass injection "+c.Kind);
                State.Detach(worker,anchor+new IntVec3(-8-(int)c.Kind,0,0));
            }
            Check(Math.Abs(worker.GetStatValue(StatDefOf.MoveSpeed,true,0)-native)<0.01f,"Speed restored after detach");
            food=Stock("WB food",anchor+new IntVec3(7,0,0),"Hay","RawCorn");
            materials=Stock("WB materials",anchor+new IntVec3(13,0,2),"ComponentIndustrial","Cloth","Silver","Gold","Jade","Chemfuel","MedicineHerbal","SmokeleafLeaves","Steel");
            furniture=Stock("WB furniture",anchor+new IntVec3(20,0,0),"MinifiedThing");
            // Specific minified inner filter must allow the furniture itself as native filters inspect the inner thing.
            furniture.settings.filter.SetAllow(ThingDef.Named("DiningChair"),true);
            Spawn("Hay",50,anchor);Spawn("ComponentIndustrial",4,anchor+new IntVec3(1,0,0));
            Spawn("RawCorn",75,anchor+new IntVec3(0,0,1));Spawn("RawCorn",25,anchor+new IntVec3(1,0,1));
            var mini=(MinifiedThing)ThingMaker.MakeThing(ThingDefOf.MinifiedThing);
            mini.InnerThing=ThingMaker.MakeThing(ThingDef.Named("DiningChair"),ThingDefOf.WoodLog);GenSpawn.Spawn(mini,anchor+new IntVec3(-1,0,0),map);
            var extra=new[]{"Cloth","Silver","Gold","Jade","Chemfuel","MedicineHerbal","SmokeleafLeaves","Steel"};
            for(int i=0;i<extra.Length;i++)Spawn(extra[i],1,anchor+new IntVec3(i%4-3,0,i/4-2));
            // Resource with no configured store, opportunistically loaded and later dumped at parking.
            Spawn("Uranium",2,anchor+new IntVec3(2,0,0));
            State.SetEnabled(worker,true);
            var inventoryLoad=ThingMaker.MakeThing(ThingDefOf.Steel);inventoryLoad.stackCount=600;worker.inventory.innerContainer.TryAdd(inventoryLoad);
            var heavyJob=new WorkGiver_HaulGeneral().JobOnThing(worker,anchor.GetFirstThing(Map,ThingDef.Named("Hay")));
            Check(heavyJob!=null && heavyJob.def!=WBDefOf.LERT_WB_Transport,"No empty cart trip when no cart can fit one unit");
            worker.inventory.innerContainer.Remove(inventoryLoad);inventoryLoad.Destroy(DestroyMode.Vanish);
            before=Counts();
            Check(HaulPlanner.ShouldUseCart(worker,anchor.GetFirstThing(Map,ThingDef.Named("Hay"))),"Mixed load triggers cart");
            var initial=anchor.GetFirstThing(Map,ThingDef.Named("Hay"));
            var auto=new WorkGiver_HaulGeneral().JobOnThing(worker,initial);
            Check(auto?.def==WBDefOf.LERT_WB_Transport,"Native haul job replaced by cart job");
            worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,false,initial));
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
            startTick=stageTick=Find.TickManager.TicksGame;stage=1;
            CameraJumper.TryJumpAndSelect(worker);
        }
        private void Run()
        {
            worker.needs.food.CurLevelPercentage=1;worker.needs.rest.CurLevelPercentage=1;
            int now=Find.TickManager.TicksGame;
            if(now-startTick>18000) {Check(false,"Timeout stage "+stage+" job "+worker.CurJob+" cargo "+cart.Cargo.ContentsString);finished=true;return;}
            if(stage==1 && (worker.jobs.curDriver as JobDriver_Wheelbarrow)?.Phase==TransportPhase.Deliver && cart.Cargo.Count>=11)
            {
                Check(cart.CargoMass<=HaulPlanner.Budget(worker,cart)+0.001f,"Cargo respects mass budget");
                Check(cart.Cargo.Any(t=>t is MinifiedThing),"Minified chair loaded");
                Check(cart.Cargo.Count>=11,"At least 11 distinct cargo stacks");
                worker.drafter.Drafted=true;
                Check(cart.Spawned && cart.User==null,"Draft immediately detaches cart");
                lastCargo=cart.Cargo.Sum(t=>t.stackCount);
                State.SetEnabled(worker,false);worker.drafter.Drafted=false;
                Check(WheelbarrowJobs.Unavailable(worker,cart)==null,"Manual resume available with automation off");
                worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,false));
                stage=2;stageTick=now;
            }
            if(stage==2)
            {
                if(!activeSaved && State.ActiveCart(worker)==cart)
                {
                    activeSaved=true;
                    GameDataSaveLoader.SaveGame("WB_active_roundtrip");
                    File.WriteAllLines(Path.Combine(outDir,"expected-counts.txt"),before.Select(x=>x.Key+"="+x.Value));
                    File.WriteAllText(Path.Combine(outDir,"active-save-written.txt"),"WB_active_roundtrip");
                    Check(true,"Saved active attached cart with cargo");
                }
                int count=cart.Cargo.Sum(t=>t.stackCount);
                if(count<lastCargo)
                {
                    int zone=Map.zoneManager.ZoneAt(worker.Position)?.ID??-1;
                    if(deliveryOrder.Count==0 || deliveryOrder.Last()!=zone)deliveryOrder.Add(zone);
                    lastCargo=count;
                }
                if(cart.Spawned && !cart.Cargo.Any && worker.jobs.curDriver is not JobDriver_Wheelbarrow)
                {
                    Check(cart.Cargo.Count==0,"Completed cart empty");
                    Check(Map.zoneManager.ZoneAt(cart.Position)==parking,"Returned to prioritized parking");
                    Check(Counts().OrderBy(x=>x.Key).SequenceEqual(before.OrderBy(x=>x.Key)),"Exact global item counts conserved");
                    Check(food.AllContainedThings.Any(t=>t.def.defName=="RawCorn") && food.AllContainedThings.Any(t=>t.def.defName=="Hay"),"Hay and corn at food storage");
                    Check(furniture.AllContainedThings.Any(t=>t is MinifiedThing),"Chair at furniture storage");
                    var uranium=Map.listerThings.ThingsOfDef(ThingDef.Named("Uranium")).FirstOrDefault(t=>(t.Position-cart.Position).LengthHorizontalSquared<100);
                    Check(uranium!=null && State.IsLeftover(uranium),"Unstored cargo dumped and tracked at parking");
                    Check(deliveryOrder.Count>0 && deliveryOrder[0]==food.ID,"Nearest food storage visited first");
                    Check(HaulPlanner.NextPickup(worker,cart,new HashSet<int>())==null,"No endless pickup of unstored leftovers");
                    Check(!State.Enabled(worker),"Manual command did not enable automation");
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir,"completed.png"));
                    GameDataSaveLoader.SaveGame("WB_completed_roundtrip");
                    stage=3;stageTick=now;
                }
            }
            if(stage==3 && now-stageTick>300)
            {
                Check(!cart.Cargo.Any && cart.Spawned,"Cart stays empty after 300 ticks");
                parking.Priority=StoragePriority.Low;
                highParking=new Zone_WheelbarrowParking(Map.zoneManager){label="WB priority parking",Priority=StoragePriority.Critical};Map.zoneManager.RegisterZone(highParking);
                foreach(var c in CellRect.CenteredOn(anchor+new IntVec3(-20,0,0),1))highParking.AddCell(c);
                var best=HaulPlanner.FindParking(worker,cart);
                Check(best.HasValue && Map.zoneManager.ZoneAt(best.Value)==highParking,"Parking priority beats proximity");
                IntVec3 plantCell=highParking.Cells[0];
                var previousPlant=plantCell.GetPlant(Map);previousPlant?.Destroy(DestroyMode.Vanish);
                var grass=(Plant)ThingMaker.MakeThing(ThingDef.Named("Plant_Grass"));GenSpawn.Spawn(grass,plantCell,Map);
                highParking.ClearPlants=true;highParking.DesignatePlants();
                Log.Message("[WBTEST] Grass fixture intended="+plantCell+" actual="+grass.Position+" spawned="+grass.Spawned+" selected="+plantCell.GetPlant(Map)+" target="+grass);
                Check(Map.designationManager.DesignationOn(grass,DesignationDefOf.CutPlant)!=null,"Vegetation creates native cut designation");
                cart.DeSpawn();GenSpawn.Spawn(cart,anchor,Map);
                var silver=ThingMaker.MakeThing(ThingDef.Named("Silver"));silver.stackCount=3;cart.Cargo.TryAdd(silver);
                var uranium=ThingMaker.MakeThing(ThingDef.Named("Uranium"));uranium.stackCount=2;cart.Cargo.TryAdd(uranium);
                untouched=Spawn("Gold",7,anchor+new IntVec3(1,0,0));
                before=Counts();worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,true));
                stage=4;stageTick=now;
            }
            if(stage==4 && now-stageTick>10 && cart.Spawned && !cart.Cargo.Any && worker.jobs.curDriver is not JobDriver_Wheelbarrow)
            {
                Check(untouched.Spawned && untouched.stackCount==7,"Finish does not collect new nearby cargo");
                Check(Map.zoneManager.ZoneAt(cart.Position)==highParking,"Finish uses priority parking");
                Check(Counts().OrderBy(x=>x.Key).SequenceEqual(before.OrderBy(x=>x.Key)),"Finish preserves all item quantities");
                food.settings.filter.SetDisallowAll();materials.settings.filter.SetDisallowAll();furniture.settings.filter.SetDisallowAll();
                var steel=ThingMaker.MakeThing(ThingDefOf.Steel);steel.stackCount=3;cart.Cargo.TryAdd(steel);
                before=Counts();worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,false));
                stage=5;stageTick=now;
            }
            if(stage==5 && now-stageTick>10 && cart.Spawned && !cart.Cargo.Any && worker.jobs.curDriver is not JobDriver_Wheelbarrow)
            {
                Check(true,"Continue with no accepting storage finishes empty");
                Check(Counts().OrderBy(x=>x.Key).SequenceEqual(before.OrderBy(x=>x.Key)),"No-storage fallback preserves all items");
                materials.settings.filter.SetAllow(ThingDefOf.Steel,true);
                cart.DeSpawn();GenSpawn.Spawn(cart,anchor,Map);
                var steel=ThingMaker.MakeThing(ThingDefOf.Steel);steel.stackCount=10;cart.Cargo.TryAdd(steel);
                worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,true));
                stage=6;stageTick=now;faultInjected=false;
            }
            if(stage==6)
            {
                var driver=worker.jobs.curDriver as JobDriver_Wheelbarrow;
                if(!faultInjected && driver?.Phase==TransportPhase.Deliver && worker.pather.Moving)
                {
                    fireCell=worker.pather.Destination.Cell;
                    testFire=(Fire)ThingMaker.MakeThing(ThingDefOf.Fire);testFire.fireSize=0.1f;GenSpawn.Spawn(testFire,fireCell,Map);
                    // Exercise ignition in the arrival tick before the normal danger AI can mask delivery revalidation.
                    worker.Position=fireCell;worker.pather.StopDead();driver.Notify_PatherArrived();
                    faultInjected=true;stageTick=now;
                }
                if(faultInjected && now-stageTick>=3)
                {
                    Check(!fireCell.GetThingList(Map).Any(t=>t.def==ThingDefOf.Steel),"Burning delivery cell rejected on arrival");
                    testFire.Destroy(DestroyMode.Vanish);worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    cart.DeSpawn();GenSpawn.Spawn(cart,anchor,Map);
                    worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,true));
                    stage=7;stageTick=now;faultInjected=false;
                }
            }
            if(stage==7)
            {
                var driver=worker.jobs.curDriver as JobDriver_Wheelbarrow;
                if(!faultInjected && driver?.Phase==TransportPhase.Park && worker.pather.Moving)
                {
                    IntVec3 berth=worker.pather.Destination.Cell;
                    var zone=Map.zoneManager.ZoneAt(berth);
                    if(zone!=null)zone.Delete(false);
                    worker.Position=berth;worker.pather.StopDead();driver.Notify_PatherArrived();
                    faultInjected=true;stageTick=now;
                }
                if(faultInjected && (cart.Spawned && worker.jobs.curDriver is not JobDriver_Wheelbarrow || now-stageTick>600))
                {
                    Check(cart.Spawned && Map.zoneManager.ZoneAt(cart.Position) is Zone_WheelbarrowParking z && z.Allows(cart.Kind),"Deleted parking berth is replanned");
                    worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    cart.DeSpawn();GenSpawn.Spawn(cart,anchor+new IntVec3(1,0,0),Map);worker.Position=anchor;
                    Map.areaManager.TryMakeNewAllowed(out restricted);
                    restricted[anchor]=true;restricted[anchor+new IntVec3(1,0,0)]=true;
                    worker.playerSettings.AreaRestrictionInPawnCurrentMap=restricted;
                    worker.jobs.TryTakeOrderedJob(WheelbarrowJobs.Create(worker,cart,true));
                    stage=8;stageTick=now;faultInjected=false;
                }
            }
            if(stage==8)
            {
                if(!faultInjected && State.ActiveCart(worker)==cart)
                {
                    restricted[anchor]=false;restricted[anchor+new IntVec3(1,0,0)]=false;
                    restricted[anchor+new IntVec3(-25,0,-15)]=true;
                    faultInjected=true;stageTick=now;
                }
                if(faultInjected && now-stageTick>300)
                {
                    Check(cart.Spawned && worker.jobs.curDriver is not JobDriver_Wheelbarrow,"Forbidden current cell with no parking terminates safely");
                    worker.playerSettings.AreaRestrictionInPawnCurrentMap=null;
                    worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    Check(true,"RUN COMPLETE");finished=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                File.WriteAllText(Path.Combine(outDir,"done.txt"),"complete");
                }
            }
        }
    }
}
#endif
