# Feed-fire panel does not recognise a stack added by this mod

## Symptom

A stack created by this mod (six pieces of `GEAR_AnimalFat`, `6 x 1.0 kg`, reported as `6.0 kg` in the bag) is
selected in the *Add Fuel* panel. One press of the button puts the whole stack into the fire at once. The burn time
can pass the 12 hour soft cap and keep the fire alive for two or three days. Native stacks (for example reclaimed
wood showing `1 / 29`) behave normally.

## Static evidence (TLD 2.55, `%TEMP%\dump255\dump.cs` + `tld_disasm.py`)

| Step | Finding | Evidence |
|---|---|---|
| Fuel list | `Panel_FeedFire.RefreshFuelSources` walks the inventory `GearItem`s and keeps only those with a `m_FuelSourceItem` (`GearItem+0x158`) whose `m_IsTinder` (`FuelSourceItem+0x3C`) is false. It never reads `m_StackableItem` (`+0x230`). | RVA `0xA0D7A0`; `0x180A0DC0D cmp BYTE PTR [rdi+0x3C],0` |
| Count label | `GearItemListEntry.UpdateColors` reads `m_StackableItem` from the **cached field** `GearItem+0x230`, and only when it is a live Unity object and `m_Units (+0x20) > 0` does it format the count into the item label. This is where `29` in `1 / 29` comes from. | RVA `0x676EB0`; `0x180677113 mov rdi,[rax+0x230]`, `0x1806771A6 Int32.ToString` |
| Action | `Panel_FeedFire.OnFeedFire` calls `Fire.AddFuel(GearItem, bool)` **once**, then `PlayerManager.ConsumeUnitFromInventory(GameObject)` **once**. | RVA `0xA0C1B0`; `0x180A0C8B7`, `0x180A0C8F5` |
| Burn time | `FuelSourceItem.GetModifiedBurnDurationHours(condition)` returns `m_BurnDurationHours (+0x28) * firestartingSkillMultiplier * condition`. `Fire.AddFuel` adds that one value to the remaining lifetime and then clamps to the cap. No direct read of `m_Units`. | RVA `0x836850`, `0x7D38F0`; `0x1807D3B68`, `0x1807D3C55` |
| Consumption | `ConsumeUnitFromInventory` does `dec DWORD PTR [StackableItem+0x20]` **directly in native code**, then destroys the whole gear item when the count reaches zero. | RVA `0xCE1040`; `0x180CE115F`, `0x180CE1349 Inventory.DestroyGear` |
| This mod | Its only two sync entry points are the `m_Units` **setter** (`PatchUnitCountHook`) and `Inventory.RemoveUnits` (`Inventory_RemoveUnits`). The feed-fire path bypasses both. | `StackManager.cs`, `Patches/StackableItem_UnitsChanged.cs`, `Patches/Inventory_RemoveUnits.cs` |

`StackableItem.get_StackMultiplier` is `m_Units / m_DefaultUnitsInItem` (RVA `0x8707C0`). The mod sets
`m_DefaultUnitsInItem = 1`, so a six unit stack reports a multiplier of six and `GetItemWeightKG()` reports the
whole `6.0 kg`. The game multiplies the per-unit weight on its own; see `StackContentSync.SetWholeUnits`.

## Causal chain

The panel hands the selected gear item to the fire **before** it takes a unit away, so every part of the fuel
calculation sees a stack that still holds all six units. The consume call then changes the count in native code
without telling any managed hook, leaving the contents out of step, and on a single-unit stack it destroys the
whole item. That is the only per-unit consumption path in the game that this mod did not own.

Ruled out:

- The fuel filter (`FilterItemFuelSource`) does not reject the item - it appears in the list and can be selected.
- `FuelSourceItem.Start` is an empty stub (RVA `0x407D80`, first instruction `ret`); it does not scale the burn time.
- `GetModifiedBurnDurationHours` does not multiply by `StackMultiplier`; its only inputs are the prefab burn hours,
  the firestarting skill multiplier and the item condition.
- `Fire.AddFuel` has no loop and no direct `m_StackableItem` read; it is called exactly once per click.

What remains open is **where the multiplier is applied** - inside `AddFuel` through an inlined helper, or by the
panel adding the fuel more than once. The structured log lines below settle that in one run.

## Measured result (v1.3.11 diagnostics, live session 2026-11-10)

```
Fire: AddFuel sees GEAR_AnimalFat units=3 default=1 own=1.00kg reported=3.00kg condition=1.00 burn=1.00h modified=6.00h lifeBefore=15688s
Fire: AddFuel restored units=3 lifeAfter=22888s delta=7200s
Fire: burned one unit of GEAR_AnimalFat, 3 -> 2
Fire: AddFuel sees GEAR_AnimalFat units=2 ... modified=4.00h ...
Fire: AddFuel restored units=2 lifeAfter=30079s delta=7200s
Fire: burned one unit of GEAR_AnimalFat, 2 -> 1
```

This settles the open question from the table above: the multiplier **is** applied inside the fuel-duration
calculation. `GetModifiedBurnDurationHours` returned `2.00 h x units` (6.00 h for three, 4.00 h for two) although
the prefab burn time is 1.00 h and condition is 1.00 - the scaling with `m_Units` is inlined there, which is why
the disassembly shows no call to `get_StackMultiplier`. The v1.3.11 flatten worked exactly as designed: `AddFuel`
received a count of one and added precisely 7200 s (one unit), and the bag went `3 -> 2 -> 1`.

The reported symptom after v1.3.11 - a stack can be fed one by one while its whole burn time plus the fire's
remaining time stays under 12 h, and cannot be fed at all once it crosses - is the panel's cap check
(`0x180A0C4F8 comiss xmm6,[rax+0x48]`): it runs **before** `Fire.AddFuel`, against the still-unflattened count,
so it measured the whole stack and refused everything once that crossed the cap.

## Fix (v1.3.12): normalise the burn time inside the feed-fire window

`find_callers.py` on RVA `0x836850` (`GetModifiedBurnDurationHours`) finds exactly six call sites:

| Caller | Meaning | Whole stack correct here? |
|---|---|---|
| `Fire.AddFuel` (`0x7D3B68`) | how much the fire gains | no - one unit is fed |
| `Panel_FeedFire.OnFeedFire` (`0xA0C493`) | the 12-hour cap check | no - one unit is fed |
| `Fire.TurnOn` (`0x7D5890`) | starting a fire with this item | out of scope, untouched |
| `Container.GetHoursFuel` (`0x8E8430`, twice) | total burn hours of everything in a container | **yes** - the stack is really that much fuel |
| `Panel_FireStart.RefreshEstimatedDurationLabel` (`0xA12610`) | firestarting panel estimate | out of scope, untouched |

`Patches/FuelSourceItem_BurnDuration_Stack.cs` divides the result by the unit count, but only while the feed-fire
window flag is up, so the cap check now sees one piece while `Container.GetHoursFuel` keeps totalling the whole
stack. Together with the v1.3.11 flatten (which already makes `AddFuel` itself see one) both call sites inside the
window report the same single-unit time, whatever the flattening does.

Known boundary: starting a fire with a mod stack (`Fire.TurnOn`) and the firestarting panel estimate still use the
whole-stack value; no symptom was reported there and changing them would need the same evidence first.

## Fix (v1.3.11)

All changes are limited to stacks registered in `AddedStackables`; vanilla stacks and other mods' stacks keep the
original behaviour.

1. `Patches/Panel_FeedFire_OnFeedFire_Stack.cs` - brackets one feed-fire action with a window flag, so the two
   hooks below only act during a click of the fire panel.
2. `Patches/Fire_AddFuel_Stack.cs` - flattens `m_Units` to `1` for the duration of `Fire.AddFuel`, restores the
   original count immediately afterwards, and records the remaining burn time before and after. While flattening,
   `StackableItem_UnitsChanged` is suppressed so the contents are not rescaled to a single unit.
3. `Patches/PlayerManager_ConsumeUnitFromInventory_Stack.cs` - takes exactly one unit away with
   `StackContentSync.Resize` (which moves the contents as well) and skips the native method, so the whole gear item
   is never destroyed. A stack down to its last unit still goes through the original method and is destroyed.
4. `Patches/StackableItem_UnitsChanged.cs` - ignores the temporary flattening.

## Diagnostic lines (Debug Logging on) and how to read them

```
Fire: AddFuel sees GEAR_AnimalFat units=6 default=1 own=1.00kg reported=6.00kg condition=1.00 burn=...h modified=...h lifeBefore=...s
Fire: AddFuel restored units=6 lifeAfter=...s delta=...s
Fire: burned one unit of GEAR_AnimalFat(Clone), 6 -> 5
```

| Observation | Conclusion | Next step |
|---|---|---|
| All three lines present, `delta` equals one unit of burn time, bag shows `5 x 1.0 kg` | Fixed | Close the issue |
| All three lines present, `delta` is roughly six units | The multiplier is applied inside `AddFuel` by an inlined helper; flattening `m_Units` there is not enough | Extend the flatten window to the whole `OnFeedFire`, or normalise `GetModifiedBurnDurationHours` by `StackMultiplier` |
| `Fire: AddFuel sees` missing | The selected fuel is not a stack this mod registered - `m_StackableItem` is null or the component was never claimed | Repair the cached `m_StackableItem` field when the fuel list is built |
| `Fire: burned one unit` missing | `ConsumeUnitFromInventory` did not run through this path | Check whether the panel took a different exit (research item, forge, torch) |
| Several `AddFuel sees` lines for one click | The panel is calling the action repeatedly | Normalise the burn time contribution instead of the unit count |

## Regression check

1. Load the save with `6 x 1.0 kg` animal fat.
2. Open *Add Fuel*, select the fat, press the button once.
3. Expected: burn time rises by one piece, the bag shows `5 x 1.0 kg`, and no 12 hour overflow.
4. Repeat until one piece remains; the last press must still remove the item entirely.
5. Control: feed a native stack (reclaimed wood `1 / 29`) once - behaviour must be unchanged.
6. Control: eat, drop and craft from a mod stack - those paths must be unchanged (the window flag is off).

## Forensics index

- `tld_dump.py type Panel_FeedFire / GearItem / GearItemListEntry / FuelSourceItem / StackableItem / Fire / PlayerManager`
- `tld_disasm.py Panel_FeedFire RefreshFuelSources / OnFeedFire / FilterItemFuelSource`
- `tld_disasm.py GearItemListEntry UpdateColors`
- `tld_disasm.py PlayerManager ConsumeUnitFromInventory`
- `tld_disasm.py Fire AddFuel`
- `tld_disasm.py FuelSourceItem GetModifiedBurnDurationHours --len 0x400`
- `tld_disasm.py StackableItem get_StackMultiplier`
- Deployed DLL md5 `fb72ac55932f229c9afe6e9ddd5e7638` (v1.3.12, identical in `bin/Release`, `Release/` and `Mods/`; v1.3.11 was `5f0dd460916ea7e48283dee8c697d8ee`)
