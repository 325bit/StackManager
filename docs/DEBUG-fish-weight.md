# Debug: stacked fish loses weight when "place all" is used in the trader box

Round: **P140** (diagnostic build, behaviour unchanged). Status: static evidence complete, waiting for one
reproduction log. Version under test: 1.3.9.

## Symptom

One stack of fish, `m_Units = 7`, total weight 34 kg in the player's bag. Placing it into the trader exchange
box with **Place All** leaves the count at 7 but the reported weight becomes 19 kg. Taking the fish out and
putting it back **one unit at a time** restores 34 kg at 7 units. The player performed no other action on the
fish. 34 -> 19 is a factor of 0.5588; the unit count is untouched.

## Why this round needs a probe

The existing log covers the session (13:41:12 - 13:50:43, StackManager v1.3.9, debug logging on), but the
placement window is almost silent: between the exchange box opening at 13:47:37 and 13:48:46 there is exactly
one StackManager line (`AddStack FOOD: GEAR_RawSmallMouthBass cal=1465/1465 units=1` at 13:47:52). No
`Stack content:`, `cut back`, `now holds`, `Stack split` or `filled to` line refers to a fish in that window.
The save written at 13:50:11 (scene change CoastalRegion -> QuonsetGasStation) contains no 7-unit fish stack.
So neither the field writers nor the object identity can be recovered from what is already recorded.

## Static evidence

| # | Evidence |
|---|---|
| E1 | `Latest.log` 13:41:12 - 13:50:43, StackManager v1.3.9, debug logging enabled. 3160 `AddStack FOOD`, 1587 `Unstackable`, 165 `FoodDiag`, but only **one** `Stack content:` line in the whole session. |
| E2 | The only weight repair line is `13:46:36 GEAR_RawRainbowTrout carried 0.608 kg while still holding every calorie of a whole unit - put back to 1.000 kg`. This is `StackContentSync.RepairFoodWeight` (`weighedAsPart` branch) and it proves fish weight is an **instance** value, not a fixed prefab value. |
| E3 | The only `Stack split:` line is for `GEAR_BurdockPrepared`, not a fish. The `Normalize` recount that caused the 1.3.7 salt-bag loss did **not** run for the fish this session. |
| E4 | Save `sandbox3` (17.9 MB LZF) decompresses to 190 scene entries. `m_UnitsProxy: 7` records exist for whisky, burdock, bones, canned corn etc., but no fish. Fish records carry per-instance `m_WeightKG`. |
| E5 | `Panel_ExchangeBox.HandleOnStackableTransfer(int units)` at `0x1807F3312` compares `units` with `StackableItem.m_Units` (`+0x20`). Equal -> `0x1807F3613` -> `HandleOnItemTransfer()` (**whole object, no clone**). Different -> `ExchangeBox.TryAddToExistingStackable` -> `GearItem.InstantiateStackable(units)` -> `ExchangeBox.AddItem`. **Place All and one-at-a-time are two different branches.** |
| E6 | `HandleOnItemTransfer` calls `ExchangeBox.TryAddToExistingStackable`, `Inventory.RemoveGear`, `Inventory.DestroyGear`, `ExchangeBox.AddItem`, `PlayerManager.AddItemToPlayerInventory`, `ExchangeBox.UpdateExchanged`. |
| E7 | Our `ExchangeBox_TryAddToExistingStackable` postfix calls `StackContentSync.Settle(existingItem)`, which can reach `Normalize` -> `SetWholeUnits`. |
| E8 | Field writers in `StackContentSync`: `Clamp` (`RepairFoodWeight` + `SetWholeUnits`), `SetWholeUnits` (food: `m_CaloriesRemaining = baseline.UnitContent`, then `WeightKG = baseline.UnitWeightKg`), `ScaleTo`, `SetPartialUnit`, `RepairFoodWeight`, `RestoreContainerWeight`, `ApplyUnitsChange`. All of them are read-only observable with the probe. |
| E9 | `TryGetBaseline` food branch: `UnitContent = m_CaloriesTotal`, `UnitWeightKg = UnitWeightOf(gi)` = `GetSingleItemWeightKG(false) / calRatio` (prefab `BaseWeightKG` when the piece is nearly empty). `RepairFoodWeight` compares against `PrefabWeightOf` = `m_GearItemData.BaseWeightKG`. |
| E10 | Weight semantics (project notes): `WeightKG` is the weight of **one** unit; the game multiplies by `m_Units` and by `m_CaloriesRemaining / m_CaloriesTotal`. A change in the calorie ratio alone changes the displayed total without touching `WeightKG`. |
| E11 | `GearItem.InstantiateStackable` calls `StackableItem.FindBestFitInShareStackForUnits` and `GearItem.InstantiateGearItem`; the copy's field provenance needs a runtime snapshot. |
| E12 | `ExpandedTradingFramework.dll` only patches `Panel_ExchangeBox.ShowExchangeBox` / `RefreshTables` (reward normalisation); it does not rewrite the placement chain. "Place All" is the vanilla `Panel_PickUnits` full-count path. |
| E13 | Every probe target was checked with MetaPeek against the 2.36.0 compile face: `Panel_ExchangeBox.MoveItem`, `HandleOnStackableTransfer`, `HandleOnItemTransfer`, both `ExchangeBox.TryAddToExistingStackable` overloads, `AddItem`, `RemoveItem`, `GetMissingAmount`, `InitializeExchangeBox`, `GearItem.InstantiateStackable`, `Inventory.RemoveGear` / `DestroyGear` / `AddGear`, `GearItem.GetItemWeightKG`, `GetSingleItemWeightKG`, `GearItemData.BaseWeightKG`. |

## Hypotheses

| ID | Hypothesis | Evidence state |
|---|---|---|
| H1 | `Settle` / `Normalize` / `Clamp` on the whole-object branch rewrites `m_CaloriesRemaining` or `WeightKG` to a single-unit baseline, scaling the displayed weight down. | Likely, unverified (E7-E10) |
| H2 | `HandleOnItemTransfer`'s merge into an existing box stack leaves the count but resets the content. | Plausible, unverified (E6, E7) |
| H3 | `RepairFoodWeight` stamps the instance weight with the prefab `BaseWeightKG`, giving 7 x prefab weight = 19 kg. | Plausible, unverified (E2, E9) |
| H4 | `m_CaloriesTotal` changes during the move, so the calorie ratio falls to ~0.5588 and the game scales 34 kg to 19 kg without any weight write. | Plausible, unverified (E10) |
| H5 | `InstantiateStackable` rebuilds from the prefab and loses the instance weight. | Weak: it only runs on the one-at-a-time branch, which the player says is **correct** (E5, E11) |
| H6 | Display-only fault: the fields never changed, the UI reads a different number. | Plausible, unverified (E1, E2) |
| H7 | Another mod writes the weight. | Low: ETF excluded (E12); no other mod logs near the window |
| H8 | `IdleCheck` / `SyncInventory` reconciliation writes during the transfer window. | Plausible, unverified (E8) |

## Probe matrix (all log-only, `[P140]` prefix, `#if P140_DIAG`)

Watched items: every gear item with a `FoodItem` and `m_Units >= 2`, in the bag and in the exchange box. No
fish name is hardcoded, so the probe works whatever species was stacked.

| # | Observation | What it distinguishes |
|---|---|---|
| 1 | `Tick` 0.15 s change detector (`DIFF NEW/GONE/CHG`) over id, units, `own` (WeightKG), `single`, `item` (GetItemWeightKG), `base` (prefab weight), `calR/calT/ratio`, location, registered, track | Catches native writes; identifies the exact field that changed |
| 2 | `SNAP` tagged snapshots at every boundary | Full state before/after each step |
| 3 | `WRITER` before/after every `StackContentSync` field writer (RepairFoodWeight, RestoreContainerWeight, SetWholeUnits calories+weight, ScaleTo, SetPartialUnit) | H1, H3, H8 - names the writer |
| 4 | `Panel_ExchangeBox.MoveItem` | Placement entry |
| 5 | `HandleOnStackableTransfer(int)` | One-at-a-time branch (E5) |
| 6 | `HandleOnItemTransfer()` | Place-All branch (E5) |
| 7 | `ExchangeBox.TryAddToExistingStackable` 4-arg, prefix + result/target | H2, merge decision |
| 8 | `ExchangeBox.TryAddToExistingStackable` 3-arg | The other merge overload |
| 9 | `ExchangeBox.AddItem` / `RemoveItem` | Box side effects |
| 10 | `ExchangeBox.GetMissingAmount` | Requirement-driven unit count |
| 11 | `ExchangeBox.InitializeExchangeBox` | Captures the box so its contents can be sampled |
| 12 | `GearItem.InstantiateStackable` src/dst fields | H5 |
| 13 | `PlayerManager.InstantiateItemInPlayerInventory` (4-arg) src/dst | Take-back path |
| 14 | `Inventory.RemoveGear` (both overloads), `DestroyGear(GearItem)`, `AddGear` | Object identity changes |
| 15 | `StackableItem.set_m_Units` | Managed vs native unit writes |
| 16 | `InventoryGridItem.Refresh` stack/unit labels | H6 |
| 17 | `PROBE READY` + first `SNAP` | Baseline / confirms the build is live |

## Reproduction protocol (one session only)

1. Restart the game so the new DLL loads; keep **Debug Logging** on.
2. Load the save and confirm the fish stack is 7 units / 34 kg.
3. **Baseline**: do nothing for 3 seconds. Expect `PROBE READY` plus stable `SNAP` lines and no `DIFF`.
4. **A - Place All**: open the trader box, select the fish, use the full-count / place-all option. Wait 3 seconds.
   Note the displayed weight.
5. **B - Take all back**: take the whole stack back into the bag in one action. Wait 3 seconds. Note the weight.
6. **C - One at a time**: put the fish back one unit at a time (7 placements). Wait 3 seconds. Note the weight.
7. **Save the game**, quit, and send `MelonLoader/Latest.log`.

If the baseline already shows `DIFF`, stop at step 3 and send the log - the fault is in loading, not placing.

## Result table

| Log outcome | Conclusion | Next action |
|---|---|---|
| A shows `DIFF CHG ... own=4.857 -> 2.714` with an adjacent `WRITER site=SetWholeUnits.weight` | H1/H3, our own writer | Fix that writer's food branch |
| A shows `DIFF CHG ... calR` or `calT` changed, `own` unchanged | H4 / calorie writer | Fix the calorie value or the ratio source |
| A shows `DIFF CHG ... item` only, all fields unchanged | H6 or a game-side recomputation | Adjust the display path, not the data |
| A shows `DIFF NEW` with a different id and `INSTANTSTACK` | H5 / object replaced | Restore the instance weight on the clone |
| No `DIFF` at all in A but `UI` prints 19 kg | H6 | Display-only fix |
| `MERGE4 result=True` with a `WRITER` right after it | H1/H2 | Guard the exchange-box merge |
| B restores 34 kg without any `WRITER` | Data never broke; the box computes a different number | Fix the box-side calculation |
| C restores 34 kg and shows `STACKTRANSFER` while A shows `ITEMTRANSFER` | Confirms the branch split (E5) | Fix only the whole-object branch |
| A shows `WRITER site=RepairFoodWeight` with `base=2.714` | H3 confirmed | Stop stamping prefab weight onto fish |

## Cleanup contract (must be done together with the fix, not before)

- Back up `VisualStudio/Diagnostics/FishWeightProbe.cs` to `docs/diagnostics/FishWeightProbe.P140.cs.txt` and
  to `D:/code/WorkBuddy_WorkSpace/The_Long_Dark/analysis/FishWeightProbe.P140.cs.bak`.
- Remove `VisualStudio/Diagnostics/`, the `P140_DIAG` line in `StackManager.csproj`, and the `OnUpdate` hook.
- Remove every `#if P140_DIAG ... #endif` block injected into `Utilities/StackContentSync.cs`
  (14 blocks; `docs/diagnostics/remove_p140_injections.py` does this mechanically).
- Rebuild and confirm the shipped DLL contains **zero** `P140`, `PROBE READY`, `FishWeightProbe` and
  `STACKTRANSFER begin` strings (UTF-16LE and ASCII).
- The probe removal and the actual fix are two separate changes, so the diagnostic build stays reversible.

## Commitment

Except for the extreme case where the change appears with no adjacent hook at all (a native write with no
managed boundary), this log is enough to name the writer and the field, so the next message delivers the fix
without asking for another reproduction.

---

# Resolution (P140 closed, v1.3.10)

## What the log actually captured

The session in `MelonLoader/Latest.log` (17:28:24 - 17:50:32, P140 build, 7090 `[P140]` lines) contains the
**split** case, eight times, not the original "place all" case. The player's stack was one
`GEAR_RawCohoSalmon` with `units=25`, `WeightKG=4.211 kg`, `m_CaloriesTotal=1263.2`, total 105.270 kg. Taking
13 units out produced two stacks that no longer add up:

| Step | Evidence |
|---|---|
| Before (17:48:52.076) | `UI x25 ... u=25 own=4.211 single=4.210 item=105.270 base=1.000 calR=1263.2 calT=1263.2` |
| Split starts (17:48:54.359) | `INSTANTBAG begin units=13 src=... u=12 own=4.211 item=50.530 calT=1263.2 track=25/1263.2` - the source stack has already been lowered to 12 units, correctly leaving 12 x 4.211 = 50.530 kg |
| Copy wakes up (17:48:54.360) | `AddStack FOOD: GEAR_RawCohoSalmon (cal=1064/1064 units=1)` - **the wrong calories are already on the instance at Awake**, before any code of this mod can write |
| Copy enters the bag (17:48:54.361) | `ADDGEAR begin ... u=1 own=3.547 single=3.550 item=3.550 calT=1064.1` |
| Split ends (17:48:54.364) | `INSTANTBAG end dst=... u=13 own=3.547 item=46.110 calT=1064.1 track=13/1064.1` |
| Net | 50.530 + 46.110 = 96.640 kg instead of 105.270 kg; the 13 moved fish lost 13 x (4.211 - 3.547) = 8.63 kg |

All eight splits show the same constant regardless of the source: sources of 4.211 kg / 1263.2 cal and sources
of 3.547 kg / 1064.1 cal both produce a copy of **3.547 kg / 1064.1 cal**. Three different Coho Salmon weights
exist in this save (3.547, 4.211, 10.760 kg), and calories are exactly 300 per kg in all three, so the copy is
not being randomised - it is being stamped with one fixed value.

## Root cause

`PlayerManager.InstantiateItemInPlayerInventory(GearItem, Int32, Single, InventoryInstantiateFlags)` does not
clone the stack it was given. Its disassembly calls `AssetHelper.SafeLoadAssetAsync`,
`TemporaryPrefabAsset.TryGetPrefab` and `UnityEngine.Object.Instantiate` (RVA `0xCEB490`, calls at
`0x180CEB61C`, `0x180CEB6BA`, `0x180CEBB27`). `GearItem.InstantiateStackable` (RVA `0x847200`) reaches
`GearItem.InstantiateGearItem`, which likewise calls `AssetHelper.TryInstantiateAsset`. Both split paths
therefore build the copy from the **prefab**, whose `WeightKG` is 3.547 kg and `m_CaloriesTotal` is 1064.1 cal,
and only afterwards write the unit count onto it. A fish caught in the world carries a weight chosen at catch
time; that instance value is discarded.

Vanilla fish have no `StackableItem`, so the game never had to split a fish stack and this path stayed invisible.
Making food stackable is what exposes it. Fixed-weight food (potato 0.200, yeast 0.200, scrap metal 0.200) is
unaffected because its prefab value equals its instance value - the other `INSTANTBAG` samples in this log all
keep their weight.

## Ruled out

- **H1 / H3 - a writer inside this mod**: zero `[P140] WRITER` lines in the entire log. `RepairFoodWeight` reads
  `PrefabWeightOf` = `m_GearItemData.BaseWeightKG` = 1.000 kg for fish, so both of its guards
  (`carriesShare`, `weighedAsPart` requiring `current < whole * 0.9`) fail for a 3.547 kg or 4.211 kg fish and it
  returns without writing. `Clamp` returns because `content == baseline.UnitContent`, and `FillToUnits` returns
  immediately for `ContentKind.Food`.
- **H2 - calories ratio only**: `own` (`GearItem.WeightKG`) and `calT` changed together, while `ratio` stayed
  1.000. This is a real field replacement, not a display calculation.
- **H4 - display only**: `UI`, `SNAP` and `DIFF` all agree on `u=13 own=3.547 item=46.110`.
- **H5 - the recipe or a merge ate the difference**: no `Stack content`, `Stack split`, `now holds`, `cut back` or
  `filled to` line involves fish anywhere in this session, and the source stack's 12 remaining units keep their
  4.211 kg exactly.
- **H8 - `ExpandedTradingFramework`**: it patches only `Panel_ExchangeBox.ShowExchangeBox` / `RefreshTables`.

## Is this the same bug as "place all" (34 kg -> 19 kg)?

**Same family, and this log is the breakthrough, but the original case is not directly proven here.** Both are a
stack of caught fish losing weight during a container/stack operation, and both are only possible because food was
made stackable. This session's log contains no "place all" event on fish - the 40 `ITEMTRANSFER` lines at
17:44:21 carry no fish - so the whole-object branch (`HandleOnItemTransfer`) is not sampled here. The constant
prefab value explains the split case completely; whether the whole-object transfer reaches one of these two
instantiation methods, or instead merges into a stack that already carries prefab values, still needs the
regression check below. The two new patches cover both instantiation entry points, so a single regression run
answers it.

## Minimal fix

- `Utilities/SplitInstanceValues.cs` - copies `WeightKG`, `m_CaloriesTotal` and `m_CaloriesRemaining` from the
  source onto the copy, then calls `StackContentSync.Adopt` so the track is rebuilt from the corrected values.
  It only runs on a stack whose `StackableItem` this mod registered, and only when a value actually differs, so
  prefab-based craft deliveries and vanilla stackable gear are untouched. Idempotent by construction.
- `Patches/GearItem_InstantiateStackable_Transfer.cs` - postfix on the container-side split.
- `Patches/PlayerManager_InstantiateItemInPlayerInventory_Transfer.cs` - postfix on the bag-side split
  (`TargetMethod()` selects the four-argument `GearItem` overload, the one the P140 probe saw fire).

`StackContentSync.Adopt` is safe after the copy: `Clamp` sees `content == baseline.UnitContent` and returns,
`RepairFoodWeight` cannot match its guards against a fish heavier than `BaseWeightKG`, and `FillToUnits`
skips food.

## Regression check

1. Load a save with one stack of 25 whole Coho Salmon / 105 kg. Split out 13 units into the bag. Expected:
   12 units / 50.530 kg in the box and 13 units / 54.743 kg in the bag (105.270 kg total).
2. Repeat with 1, 12 and 24 units, and split into a container as well as into the bag.
3. Re-run the original "place all" operation on a 7-unit / 34 kg fish stack. Expected: still 34 kg in the box.
   If it still drops to 19 kg, that branch uses a different mechanism and should be traced separately.
4. Craft a salt bag and split a fixed-weight food stack (potatoes, yeast) to confirm delivery and ordinary
   splitting are unchanged.
5. In the log, each affected split prints `Split copy: GEAR_RawCohoSalmon kept 4.211 kg / 1263.2 cal instead of
   the prefab's 3.547 kg / 1064.1 cal`; fixed-weight food should print nothing.

## Probe teardown

- `VisualStudio/Diagnostics/FishWeightProbe.cs` backed up to `docs/diagnostics/FishWeightProbe.P140.cs.txt`
  and `D:/code/WorkBuddy_WorkSpace/The_Long_Dark/analysis/FishWeightProbe.P140.cs.bak`.
- The injected `StackContentSync.cs` kept at `analysis/StackContentSync.P140.injected.cs.bak`; all 14
  `#if P140_DIAG` blocks removed by `docs/diagnostics/remove_p140_injections.py`.
- `Diagnostics/` removed, `P140_DIAG` removed from the project, `OnUpdate` hook removed. A clean baseline build
  succeeded before the fix was added, keeping probe removal and fix as two separate changes.
