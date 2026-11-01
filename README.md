LERT'S WHEELBARROWS — PRACTICAL HAULING FOR RIMWORLD 1.6
Version 0.3.0

Update 0.3.0: loaded graphics for any nonempty cargo, custom icons, Allow Tool urgent hauling support, group parking for selected wheelbarrows, a dedicated category, and stock at suitable traders. Base market values: wooden 100, steel 250, plasteel 820 silver. Enable wheelbarrows on the pawn for urgent hauling; these trips collect only urgent designated items with available storage. Group parking requires available haulers and free allowed parking cells.

During hauling, click the wheelbarrow and open its Cargo tab to see the load, individual stacks, and their weights. Unload drops the selected stack onto the ground; Unload all drops everything. The colonist then delivers any remaining cargo and returns the wheelbarrow to parking. Parking is 75% transparent when selected and 95% transparent otherwise. Separate side sprites are used for east/west travel.

Fewer trips with individual stacks. More cargo in each run.

Lert's Wheelbarrows adds three practical hauling tools to RimWorld: wooden, steel, and plasteel wheelbarrows. Colonists can automatically borrow an available wheelbarrow, collect a mixed load, and deliver its contents to the appropriate storage locations.

Each wheelbarrow has its own cargo container. Hay, components, corn, a minified chair, and other eligible items can travel together. There is no separate limit on the number of different cargo types; available carrying mass is what matters.


FEATURES

- Three wheelbarrow types with different capacity bonuses and movement behavior.
- An individual automatic-use toggle for each colonist.
- Mixed cargo collected from multiple nearby stacks.
- Support for empty minified furniture.
- Deliveries to multiple storage locations using their filters, priorities, and availability.
- A dedicated parking zone with wheelbarrow-type filters and return priority.
- Optional vegetation clearing in parking zones.
- Manual Continue work and Finish work commands for interrupted runs.
- Saved wheelbarrows, cargo, and settings.


THE THREE WHEELBARROWS

WOODEN WHEELBARROW
Capacity bonus: +100 kg.
Movement: multiplies the colonist's current movement speed by 0.80, making them 20% slower while using it.
Recipe: 80 wood at a crafting spot. Available from the start.

STEEL WHEELBARROW
Capacity bonus: +150 kg.
Movement: caps the colonist's current movement speed at 4.6 cells per second, the base speed of an ordinary adult human. Faster colonists are capped; injured or otherwise slower colonists are not sped up.
Recipe: 70 steel, 10 wood, and 1 component at a fueled or electric smithy. Requires Smithing research.

PLASTEEL WHEELBARROW
Capacity bonus: +200 kg.
Movement: leaves the colonist's current movement speed unchanged, preserving their normal bonuses and penalties.
Recipe: 20 steel, 50 plasteel, 10 wood, and 2 components at a machining table. Requires Machining research.

Capacity figures are bonuses, not fixed total container limits. The cargo budget is:

the colonist's native mass capacity + the wheelbarrow bonus - their equipment and inventory mass.

An ordinary human has 35 kg of native capacity before equipment is accounted for. A wooden wheelbarrow therefore provides a 135 kg budget before existing carried mass is subtracted. The wheelbarrow itself is not added to the colonist's backpack.

Movement modifiers apply only while the wheelbarrow is in use, including the empty return trip. Normal terrain, door, weather, and other movement conditions still apply. This description uses RimWorld's in-game units: kilograms and cells per second.


HOW HAULING WORKS

1. Select an adult colonist and enable Use wheelbarrows. Automatic use is off by default.

2. When a normal storage-hauling job involves a load that needs multiple ordinary trips, the colonist looks for the nearest available, reachable wheelbarrow that can carry at least part of the initial target. Distance is based on a usable route. If no suitable cart is available, normal hauling remains in place.

3. After fetching the wheelbarrow, the colonist heads to the initial hauling target and collects nearby eligible items. There is no fixed pickup radius. Colonists search the map for the nearest eligible items, respecting allowed areas, forbidden items, reservations, and reachable paths.

4. Cargo is loaded according to its actual mass. Part of a stack can be taken when the whole stack will not fit. Forbidden, inaccessible, or otherwise reserved items are not collected.

5. The colonist visits the nearest eligible storage locations. Filters, storage priorities, and available space determine where each item may go. After unloading, the next eligible destination is chosen from the colonist's new position.

6. The wheelbarrow is returned to the highest-priority available parking zone. Among equally prioritized options, the nearest available space is preferred.

For example, 50 hay, 4 components, 100 corn, and a minified chair can share a run if their combined mass fits. Hay and corn can go to the appropriate food stockpile, components to a materials stockpile, and the chair to furniture storage.

WHAT HAPPENS TO LEFTOVER CARGO?

Additional items may be collected even when no storage location currently accepts them. If no valid destination is found by the end of the run, those items are brought back and unloaded onto the ground next to the parking space. The same applies when a destination fills up or becomes unavailable during the trip.

A normally completed wheelbarrow run leaves the cart empty. If no parking is available, the cart and remaining cargo are left near the completion location. If safe physical placement is impossible, cargo is retained in the container for another attempt.


WHEELBARROW PARKING

Create a Wheelbarrow parking zone from the zone menu. Draw and adjust it like a normal zone.

Each zone lets you:
- allow or disallow wooden, steel, and plasteel wheelbarrows;
- set its return priority;
- enable Clear vegetation.

Vegetation clearing is off by default. When enabled, plants and trees receive normal cutting designations. Colonists perform that work according to their work priorities; vegetation is not removed instantly.

Parking filters control wheelbarrow types, not cargo types. Cargo destinations use your normal storage settings. A wheelbarrow does not have to be parked inside a zone before it can be borrowed.


INTERRUPTED RUNS AND RIGHT-CLICK COMMANDS

Drafting immediately interrupts the run. The colonist leaves the wheelbarrow and its cargo behind and follows the combat order.

To handle the abandoned cart, undraft and select a capable colonist, then right-click the wheelbarrow:

CONTINUE WORK
Resume delivery of the existing cargo. If there is nothing to deliver or no eligible storage, proceed to finishing the run. An empty cart may begin collecting a new nearby load when suitable work is available.

FINISH WORK
Stop collecting new items. Deliver eligible cargo already on board, return the cart to parking, and unload any remaining cargo onto the ground nearby.

Manual commands work even when automatic wheelbarrow use is disabled, and they do not turn that toggle on. The colonist must still be able to perform the work. Forbidden status, other colonists' reservations, and path access still matter.


INSTALLATION AND REQUIREMENTS

Requires RimWorld 1.6 and Harmony. No other mod is required.

Manual installation:
1. Extract the LertWheelbarrows folder into your game's Mods folder.
2. Enable Lert's Wheelbarrows after Harmony and Core.
3. If you use Common Sense or Deep Stack, also place this mod after them.
4. Restart the game, craft a wheelbarrow, and enable automatic use for the colonists you want to use it.

Ukrainian, English, and Russian text is included. Command names depend on the selected game language; other languages use English fallback text.


COMPATIBILITY AND VERSION 0.3.0 LIMITS

The new 0.3.0 features have not been tested in-game; this update received Release compilation and code review only. Earlier 0.2.0 hauling logic was tested on RimWorld 1.6.4633 rev1246 with Core and Harmony, and in a selected setup containing HugsLib, Common Sense, Allow Tool, Deep Stack, and Show Me Your Hands. Active and empty wheelbarrow save/load scenarios were also tested.

This is an early version of the mod. Full compatibility with arbitrary modpacks and the normal visual interface has not been confirmed. Including Show Me Your Hands in the tested setup establishes a logic test, not complete visual compatibility. Full integration with RimHUD and Vanilla Expanded Framework has not been confirmed either. If you use a large modpack, test it on a copy of your save.

Current limits:
- Adult human colonists only. Children, animals, and mechanoids are not supported.
- The mod hauls existing items. It does not automatically mine ore, cut trees for cargo, or harvest crops.
- Ingredient delivery, construction, refueling, feeding, pawn carrying, and corpse hauling are not replaced.
- Containers with nested cargo are excluded. Empty minified furniture is allowed.
- No caravan capacity bonus, animal-drawn carts, saddles, or automatic cross-map transport of an attached wheelbarrow.
- A wheelbarrow occupies one cell and follows the colonist's normal path. There is no separate wheel-physics system.
- Two small compatible stacks can occasionally trigger an unnecessary wheelbarrow-fetching trip even when ordinary hauling could combine them.

For your first test, use a new colony or a copy of a save. Back up before removing the mod; safe removal of its items from an existing save is not guaranteed.


FUTURE DEVELOPMENT AND FEEDBACK

Animal-drawn carts and specialized saddles are directions for future development. They are not included in version 0.3.0.

If you encounter a problem, include your RimWorld version, mod list and load order, wheelbarrow type, what the colonist was doing, and steps to reproduce it. A relevant Player.log excerpt is helpful when available.

Thanks to everyone who tries the mod and helps improve colony hauling!
