# Stack Manager v1.3.12

## Fixed

- **A large fuel stack could not be added to a fire at all.** v1.3.11 stopped the fire from receiving the whole
  stack and made the consumption remove one unit (log: `AddFuel` added exactly 7200 s, bag went `3 -> 2 -> 1`),
  but the panel's 12-hour cap check runs *before* `Fire.AddFuel` and measures the burn time of the fuel with the
  unit count still at its full value - a stack of three fat measured 6.00 h, two units measured 4.00 h, so a stack
  whose whole burn time plus the fire's remaining time crossed 12 hours was refused outright. The scaling with the
  unit count is inlined inside `FuelSourceItem.GetModifiedBurnDurationHours` (no call to `get_StackMultiplier` in
  the disassembly). Inside the feed-fire window the result is now divided by the unit count, so the cap check sees
  one piece. The window is deliberately narrow: `Container.GetHoursFuel` totals the burn time of everything in a
  container and there the whole stack is the correct amount, and `Fire.TurnOn` /
  `Panel_FireStart.RefreshEstimatedDurationLabel` keep the vanilla behaviour as well.

---

# Stack Manager v1.3.11

## Fixed

- **Feeding a stack to a fire burned the entire stack at once.** Selecting a stack this mod created (for example six
  pieces of animal fat, 6 x 1.0 kg) and pressing "add fuel" handed the whole stack to the fire, which could push the
  burn time past the 12 hour cap and keep the fire going for days. The feed panel calls `Fire.AddFuel` with the
  selected gear item *before* it consumes a unit, so the fire sees a stack that still holds every unit, and the
  consume call writes `StackableItem.m_Units` directly in native code - neither the unit-count setter nor
  `Inventory.RemoveUnits` sees the change, leaving this mod with no way to keep the stack in step. The unit count is
  now flattened to one for the duration of `AddFuel`, put straight back afterwards, and exactly one unit is removed
  together with its contents. Stacks the game or another mod created are untouched, and the last unit still destroys
  the item as before.

---

# Stack Manager v1.3.10

## Fixed

- **Splitting a stack of fish made the fish lose weight.** Taking 13 fish out of a 25-fish stack that weighed
  105 kg left 12 fish at 50 kg in the box and 13 at 46 kg in the bag: about 9 kg had disappeared. Splitting does
  not clone the stack - the game loads the prefab and instantiates that, then writes the new unit count onto the
  copy. A caught fish carries a weight chosen at the moment it was caught, together with calories derived from
  that weight, and both were replaced by the prefab's fixed values (3.547 kg / 1064 cal instead of 4.211 kg /
  1263 cal). Food with a fixed weight never showed this because its prefab and instance values are the same, and
  vanilla fish cannot stack at all. Copies now inherit the original's weight and calories on both split paths,
  and their bookkeeping is rebuilt afterwards so later merges scale against the right unit size.

---

# Stack Manager v1.3.9

## Fixed

- **Crafting one more salt bag wiped out the whole stack that was already in the bag.**
  A craft creates its result twice: a temporary item that carries the crafting progress, and the real reward once
  the craft succeeds. The game destroys the temporary item as soon as the progress completes. A crafted result is
  built from a prefab, and the game first tries to merge that prefab into a matching stack already in the bag;
  when the merge succeeds, it returns the *existing stack* as the item it just created. On a stackable bag that
  made the player's entire stack the temporary object, so finishing the craft destroyed every bag in it and the
  reward arrived as one fresh bag instead. The temporary item is now kept as a separate short-lived instance,
  while the real result from the success path still merges normally, so a craft adds one bag to the stack instead
  of replacing it.

---

# Stack Manager v1.3.8

## Fixed

- **A crafted salt bag sometimes vanished instead of joining the stack, while the materials were still taken.**
  Every bag this mod makes stackable was meant to leave its first moments with a record of what one unit of it
  holds. The record was written by the code that takes over copied items, but the food rule had already claimed
  the component a moment earlier in the same wake-up, and the takeover bowed out on seeing that - so a freshly
  crafted bag carried no record at all. The first merge that landed on such a bag had nothing to compare
  against: the game had raised the count by one bag without moving the salt, the repair that would have scaled
  the salt to the new count never fired, and the check that follows a merge read the extra unit as a mistake
  and recounted the stack back down. The bag was gone, the cash was gone, and the stack showed nothing for it.
  Whether a bag was hit depended on a race with the periodic sweep, which is why it only happened sometimes.
  The takeover no longer bows out: a stack gets its record the moment it is born, and a merge into it now
  scales the contents the way it always should have.

---

# Stack Manager v1.3.7

## Fixed

- **A drink taken from the flask left the flask printing the weight it had before.** The drinking itself was
  never wrong: one unit of the stack went out and the flask genuinely held less. But the flask works its own
  weight and volume out only when an item enters or leaves it or its panel is opened, and nothing in the
  drinking tells it that its contents just changed - so it went on reporting the weight of every cup it used
  to hold until the player happened to open it. A finished drink now asks the flask holding it to recount,
  which is also what wakes the redraw of the bag and the description.

---

# Stack Manager v1.3.6

## Fixed

- **The flask still refused a stack, because the split knew bottles and the drinks are cups.** What the flask
  accepts is not a bottle at all: a cup of tea, coffee or soup in this build carries its drink in a DrinkItem,
  which works the volume out from the weight of the item - a cup is as many litres as it is kilograms. The split
  only read LiquidItem, so it saw no volume in a stack of cups, stepped aside, and the flask went on turning the
  whole stack away for weighing more than the room it had left. A drink stack is now measured the same way the
  flask measures it, cut to what fits, and the rest stays in the bag; a drink needs no volume written on the
  leftover, because its units settle what it weighs.
- **A redraw that never shows itself cannot be told apart from one that never ran.** The refresh of the flask's
  counters hangs on a hook and a frame delay, and when the numbers stay stale there was nothing in the log to say
  whether the refresh ran and the panels ignored it or the refresh never happened. Every redraw that actually
  goes out is now written to the debug log.

---

# Stack Manager v1.3.5

## Fixed

- **The flask fix of 1.3.4 did not fire for stacks that had come out of a save.** The split only touched stacks this
  mod had registered, and a stack loaded from a save carries a component the game rebuilds on its own, which the
  registry never sees. Every stack of liquid in a save was therefore invisible to the split, and the flask went on
  refusing all of it. A stack is now recognised by its name on the list of items this mod stacks, which is how the
  rest of the mod already tells its stacks apart, so a stack does not have to have been created in this session to
  be taken apart.
- **A drink left the flask printing its old weight, and there was nothing in the log to say why.** The redraw of
  the panels hangs on one hook, and a hook that failed to attach looks exactly like a hook that never had anything
  to do. The first recalculation a flask makes is now written to the log on purpose, so an install that did not
  take shows itself instead of hiding behind the game.

---

# Stack Manager v1.3.4

## Fixed

- **A stack of liquid could not go into an insulated flask at all.** The flask adds up its own volume and the
  volume of the item it is offered, and refuses the item outright when the sum passes what it holds. That is the
  same shape of check a curing box makes, and it goes wrong the same way: a stack of ours carries every unit in it
  at once, so five bottles of cooking oil were five bottles to a one litre flask and not one of them went in. The
  bottles that fit are taken and the rest stays in the bag as its own stack, exactly as a curing box now takes the
  pieces of a stack that fit.
- **A flask kept printing the weight and the amount it had before a drink.** Drinking from a flask takes a bottle
  out, drinks part of it and puts what is left back, and each of those steps makes the flask work out its volume
  and its weight again - but nothing told the panels, so the bag went on showing the old weight and the list inside
  the flask went on showing the units it used to hold. Both are asked to redraw the moment the flask recalculates
  itself, which is the one place every one of those steps passes through.

---

# Stack Manager v1.3.3

## Fixed

- **A stack of sixty seven pieces of meat in the trader's exchange box weighed 0.67 kg.** A part of a unit - a
  piece with a bite taken out of it - was allowed to be the stack that other units merged into. Every merge in the
  game works the same way: it finds the closest matching stack, adds the incoming count to it, and moves no content
  across. Two stacks of whole pieces come out right, because a whole piece is what the count is multiplied by. A
  part does not: sixty six whole pieces merged into the piece with a bite out of it gave sixty seven units carrying
  the calories of that one piece, and food is weighed by the calories it holds, so the whole stack weighed a
  hundredth of a piece each. A part is now answered with "no match", which every caller already handles by adding
  the item as a stack of its own. The bag, a container and the exchange box all go through the same call, so all
  three are covered, and a stack that still ends up holding more units than it has content for is read back out of
  that content on the spot.
- **A weight written from a piece that was almost gone followed the item forever.** How heavy one whole unit is was
  read from the item itself, scaled back up by the calories that were left. On a piece with a few calories in it
  that number lands anywhere, and it was then used to weigh whole ones: a stack that was recounted against such a
  piece was stamped with a hundredth of a kilo and stayed like that, because the value is saved with the item.
  Below five percent of its calories the prefab is used instead, which is the only weight that cannot have been
  written over.
- **A whole piece could be saved weighing a fraction of itself.** The same value is what the weight of a piece is
  repaired against, so a piece that had already been written down to a fraction agreed with itself and was never
  corrected. A piece that still holds every calorie of a whole unit but weighs less than nine tenths of it is now
  put back to the weight the prefab gives it.

---

# Stack Manager v1.3.2

## Fixed

- **Salt went missing every time a bag was put into a curing box and taken out again.** Merging one item into a
  stack is the game's job, and all it does is add the unit count of the one to the other: it never moves the amount
  across. That is invisible for food and plain gear, which describe a single unit and are multiplied by the count,
  and it is right for two stacks of full bags - seven full ones that take an eighth hold eight. A bag the box has
  already taken salt out of is not a full bag, though. Two thirds of a bag that took a stack of seven kept the salt
  of the two thirds it was, the repair that reads the count back out of the amount scaled that two thirds by eight,
  and the bag came back holding five and a third bags worth instead of seven and two thirds. Each round trip through
  the box therefore took roughly half of what was left. The amount the joining item carries is now added to the stack
  that takes it, before anything compares the count against the amount. Any liquid container that stacks is
  repaired by the same change, which it needed for the same reason.

---

# Stack Manager v1.3.1

## Fixed

- **Interacting with a curing box emptied the stack count off the salt bags.** A curing box does not keep salt as
  bags: it holds each bag aside and, when the lid closes, pours what it carries into its own salt store, subtracting
  from the bag and destroying it once nothing is left. The unit count is never touched, because in the game every bag
  is one unit. A stack of twenty bags is one item carrying the salt of twenty, so the box took one bag's worth out of
  the middle of it and handed it back still claiming twenty - and the count on the bag was the number the player saw.
  The count is now read back out of the salt that is left, on the way out of the box. Bags already written off this
  way come back right the next time they pass through a box.
- **Powder handed back as an amount came back as a single bag.** A recipe, a harvest and a full container all give
  salt and flour back as a weight, and the game answers that by building one bag from the prefab and pouring the whole
  amount into it - six and two thirds of a kilo of salt became one bag holding all of it. The count is now read out of
  the amount, so the same six and two thirds becomes six bags and one that is two thirds full.
- **A bag of salt was measured against the box's meat space.** The box skips its own capacity check for anything that
  carries a powder, because salt is not stored as meat, but the piece-by-piece fit added in 1.3.0 did not: a stack of
  salt bags was cut at the line of the free meat space and the part that fitted went into the box. Salt bags are now
  left alone by that fit, exactly as the box leaves them alone.

---

# Stack Manager v1.3.0

## Fixed

- **A stack heavier than the free space in a curing box could not go in at all.** The box weighs an item as a whole
  before it takes any of it, and a stack of ours reports every unit at once, so twenty one pieces of kilo meat were
  twenty one kilos to that check and not one of them entered an empty twenty kilo box. The box now takes the pieces
  that fit and hands the rest back as its own stack in the backpack. A move the box refuses for any other reason puts
  the whole stack back the way it was.

---

# Stack Manager v1.2.2

## Fixed

- **A stack put into a curing box came back as one piece.** The box saves a piece as a prefab, a condition and a
  remaining fraction, with no field for a unit count, so twenty pieces were written as one and the other nineteen
  were gone the moment the save was loaded. The count now travels in that fraction and is read back when the box
  rebuilds the piece, both when a save loads and when a piece finishes curing and is replaced by the cured one.
  Pieces lost to a save written before this version cannot be recovered.

---

# Stack Manager v1.2.1

## Fixed

- **Moving cooking oil between a container and the backpack left the two sides disagreeing.** Taking a unit out of a
  container stack lowered the count but left the volume of the whole stack behind, so the container kept reporting the
  weight of the bottle that had just left, and moving a whole stack to the bag handed the copy the volume of a single
  bottle instead of the stack. Both directions are now corrected at the moment of the transfer, which also covers the
  same move for flour and the gunpowder can.
- The repair threshold was measured against the total of a stack, so a stack of a hundred bags could lose a whole bag
  and sit inside the one percent it allowed. It is now measured against a single unit.

---

# Stack Manager v1.2.0

## Added

- **`Mods/StackManager_AddItems.txt`**, a plain text list for items this mod does not stack yet. It is created on the
  first start: a name under `#非液体` gets ordinary stacking, a name under `#液体/重量可变` gets whole-portion
  stacking for liquids and for anything whose weight per unit can change. Entries apply from the next start, and the
  file is only ever read, so deleting a line takes the item out again.
- The two headers are also accepted as `#non-liquid` and `#liquid`, and a name may keep the quotes and trailing comma
  it had when it was copied out of `config.json`.
- A name that ends up under no header, or that is spelled in a different case from an entry already in the list, is
  reported in `MelonLoader/Latest.log` instead of disappearing silently.

---

# Stack Manager v1.1.1

## Added

- **Beehives, yeast and tape rolls stack**, following the normal rule: any two of them merge and the stack keeps the
  better condition.
- **The gunpowder can stacks under the whole-portion rule.** Full cans merge, and a can a recipe has taken powder out
  of stays on its own. A recipe also draws from the can holding the least gunpowder first.

## Changed

- **Updates no longer replace `config.json`.** New entries are appended to the lists already in the file and nothing
  else is touched, so an item removed on purpose stays removed. The previous file is still kept as
  `config.v<version>.<timestamp>.json`.

---

# Stack Manager v1.1.0

## Added

- **Food stacks.** Whole portions of the same food now merge into one inventory entry.
- **Cooking ingredients stack:** flour, salt, salt bag, sugar, oats tin and cooking oil.
- **Crafting draws from the lightest stack first**, so a recipe empties the smallest pieces before touching a big stack.
- **New setting: Max Weight Food Stackable.** Only whole portions of food and ingredients may join a stack — a half
  eaten piece always stays on its own.
- **New setting: Debug Logging.** Writes every merge, split and correction to `MelonLoader/Latest.log` for problem
  reports. Warnings and errors are written either way, so the default is off.
- Handling for saves written by this version: a stack whose unit count and contents disagree is corrected when the
  save loads.

## Fixed

- A single item taken out of a stack kept reporting the weight of the whole stack. Dropping three 1.5 kg bags of flour
  one at a time gave two 1.5 kg bags and one 4.5 kg bag.
- Weights drifted apart after cooking: the stack and its surroundings disagreed about how much was left.
- A recipe consumed twice what it asked for. Crafting a 0.5 kg pie out of a 10 kg stack of meat left 9 kg plus
  0.25 kg instead of 9 kg plus 0.5 kg. Flour and cooking oil had the same fault.
- Food made in a cooking session could stay at the calorie value it spawned with and be treated as a partly eaten
  portion, which kept it from ever stacking.
- A stacked liquid container printed its unit count on top of its volume in the inventory grid, making both unreadable.
  The count is now hidden and the volume is shown alone.
- An unreadable `config.json` no longer disables the mod. The broken file is backed up with a timestamp and a fresh
  default is written.

## Changed

- Splitting, dropping and merging now update the weight of every affected item immediately, not on the next visit to
  a container.
- Stacks that no longer hold whole portions are separated into whole units plus one partial piece instead of
  reporting a total nothing matches.
- `config.json` gained the `MaxWeightStackItems` list for cooking ingredients. Existing files are regenerated on first
  launch, with the old one kept as `config.v1.0.5.<timestamp>.json`.

## Notes

- Food that was already stored with the wrong calorie value by an older build keeps it; it cannot be told apart from a
  portion that was eaten on purpose.
- Turning **Max Weight Food Stackable** off leaves existing stacks alone. They keep working, but new food no longer
  stacks.

---

# Stack Manager v1.0.0

This is the initial release. Contains the same functionality of the [Better Stacking](<https://github.com/ds5678/Better-Stacking>) mod by ds5678 as well as new functionality.
The new functionality is the addition of stacking to the following items:
- Carrots
- Potatoes
- Stump Remover
- Recycled Cans

In addition, the following items are locked at 100% durability:
- Coffee Tins
- Green Tea Packages

The addition of stack functionality is enabled by default but can be disabled (Scene change required to update)