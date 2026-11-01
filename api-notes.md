# Installed RimWorld 1.6 API audit

Inspected read-only using local ilspycmd 9.1.0.7988 against installed Assembly-CSharp.dll. net472 mod compilation succeeded with zero warnings/errors.

- `Map : IThingHolder`; `Map.GetChildHolders` enumerates map components implementing IThingHolder, supporting save/reference discovery of attached carts.
- `ThingOwner<T>(IThingHolder owner)` is unlimited-stack by default. `TryDrop` preserves ownership on placement failure; `TryTransferToContainer` is native transfer.
- `ThingOwner<T>` implements two generic enumerable interfaces; use `InnerListForReading` for LINQ on non-Thing generic types to avoid ambiguity.
- `MassUtility.Capacity(Pawn,StringBuilder=null)` is BodySize*35, and `GearAndInventoryMass` is the native occupied mass.
- `MinifiedThing` is in **RimWorld**, has `InnerThing`; inner furniture mass must be read explicitly.
- `Map.pathFinder.FindPathNow(IntVec3,LocalTargetInfo,TraverseParms,PathFinderCostTuning?=null,PathEndMode=OnCell,...)` replaces old FindPath. Dispose pooled PawnPath after reading Found/TotalCost.
- `StoreUtility.TryFindBestBetterStorageFor`, `IsGoodStoreCell`, storage settings and AllHaulDestinationsListInPriorityOrder are available.
- `JobDriver.Notify_PatherArrived/Failed` are virtual; an ongoing Never toil may drive a serialized state machine with native pather callbacks.
- `WorkGiver_HaulGeneral.JobOnThing` is overridden on its own type; narrow postfix can replace only native HaulToCell jobs.
- `StatWorker.FinalizeValue(StatRequest,ref float,bool)` plus protected `stat` allows a material-specific MoveSpeed postfix without recursive stat requests.
- `Zone(string,ZoneManager)`, `Designator_ZoneAdd`, `GetZoneAddGizmos`, GetGizmos and normal plant designations are available.
- `Thing.GetFloatMenuOptions(Pawn)` supplies manual cart actions without replacing the game's global float-menu provider.
- Game supports `-savedatafolder=ABSOLUTE_PATH` and `-quicktest`; both verified in GenFilePaths and QuickStarter. Test profiles can remain separate from user's actual data.
- Source folder bundled with game has only 43 files; it is not a full source checkout.

Recipe defs checked: CraftingSpot; FueledSmithy/ElectricSmithy with Smithing; TableMachining with Machining.
