# Stack Manager

A stacking mod for **The Long Dark**, replacing [Better Stacking](https://github.com/ds5678/Better-Stacking) by ds5678.

Stack Manager makes the game stack the items it normally keeps apart — food above all — and keeps every stack
honest: weights, contents and recipe requirements stay correct after a merge, a split, a drop or a crafting session.

## Features

**Stacking**

- Food stacks. Full portions of the same food merge into a single inventory entry.
- Partly eaten food never joins a stack. A 0.5 kg piece of a 1 kg pie stays on its own, next to a stack of whole 1 kg pies.
- Cooking ingredients stack: flour, salt, salt bag, sugar, oats tin, cooking oil, gunpowder can.
- Dried pelts, hides, guts, leather, saplings and herbs stack.
- Carrots, potatoes, stump remover, matches, painkiller and antibiotic bottles, coffee tins and green tea packages stack.
- Beehives, yeast and tape rolls stack.
- Food only merges with food of the same freshness, and the better of the two is kept.
- Merging keeps the highest condition. Coffee tins, green tea packages and potatoes are held at 100% condition,
  carrots at 50%.

**Correct amounts**

- Every stack shows the weight of what it actually holds, and splitting one hands back pieces that each weigh right.
- Crafting and cooking draw from the lightest stack first.
- A recipe takes what it asks for. A 10 kg stack of meat minus a 0.5 kg recipe leaves 9 kg stacked plus a separate
  0.5 kg piece.
- Stacks saved by an older version are corrected when the save loads.

**Interface**

- A stacked liquid container shows its volume instead of an unreadable unit count on top of it.
- Settings live in the ModSettings menu; the item lists live in `config.json`.

## Requirements

- The Long Dark 2.x (IL2CPP build)
- MelonLoader 0.6.1 or newer
- [ModSettings](https://github.com/dommrogers/ModSettings)
- [ComplexLogger](https://github.com/Arkhorse/ComplexLogger)

## Installation

1. Install MelonLoader and the two dependencies above.
2. Put `StackManager.dll` into `TheLongDark/Mods`.
3. Start the game.

## Settings

| Setting | Default | Effect |
|---|---|---|
| Add Stackable Behaviour | On | Stacking for the items listed in `config.json`. Takes full effect after a restart |
| Set to max condition | Off | Merged stacks are pinned to 100% condition instead of the higher of the two items |
| Max Weight Food Stackable | Yes | Only whole portions of food, ingredients and gunpowder stack. **No** turns that stacking off |
| Debug Logging | No | Writes every merge, split and correction to `MelonLoader/Latest.log` |

## Configuration

`Mods/StackManager/config.json` holds four item lists:

| List | Contents |
|---|---|
| `STACK_MERGE` | Items that stack and keep the best condition |
| `Advanced` | Items whose stacks are always held at 100% condition |
| `AddStackableComponent` | Items that receive stacking from scratch |
| `MaxWeightStackItems` | Food, cooking ingredients and the gunpowder can — whole portions only |

An update only adds entries to these lists. What is already in the file stays as written, and a timestamped copy is
kept next to it, so a name removed on purpose does not come back.

## Version

1.1.1 — see [ChangeLog.md](ChangeLog.md).
