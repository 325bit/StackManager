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