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