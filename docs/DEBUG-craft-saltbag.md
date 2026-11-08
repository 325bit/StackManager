# Diagnostic plan: crafting one salt bag collapses a two-unit stack

Status: **diagnostic build deployed, waiting for one instrumented reproduction**.
Mod version in the field: 1.3.8 (behaviour unchanged). Diagnostic build md5
`e8f52e3d59bf37ecd2a7465cd283d782`. Probe prefix `[P139]`.

## 1. Stable reproduction (reported by the player)

1. The inventory holds **one** stack of `GEAR_SaltBag`: `m_Units = 2`, 20.000 kg, both units full.
2. Craft exactly one `GEAR_SaltBag` with `ZC8787traderblueprints` / `blueprints/SaltBag.json`
   (`2 x GEAR_CashBundle -> 1 x GEAR_SaltBag`) from the crafting menu.
3. Expected: one stack, `m_Units = 3`, 30.000 kg.
4. Observed: one stack, `m_Units = 1`, 10.000 kg.
5. The player performed no other move, drop, transfer or consumption of a salt bag.

Two whole units and 20 kg disappear. Because the result is still a *single* stack, the fault is
either the old stack being replaced by a different object, or the same object being rewritten in
place. These two cases are what the probe matrix separates.

## 2. Static evidence collected before any experiment

| # | Fact | Source |
|---|---|---|
| E1 | Loaded build really is v1.3.8; Debug Logging is on | `Latest.log:954`, `[DEBUG]` lines throughout |
| E2 | The blueprint requires only `GEAR_CashBundle x2`; no powder, liquid, tool or salt ingredient | unpacked `blueprints/SaltBag.json` |
| E3 | `CraftTrace` shows the cost is paid correctly every time (`CashBundle 8 -> 6 -> 4 -> 2`) | `Latest.log:9351-9354`, `9358-9361` |
| E4 | Every craft instantiates a new `GEAR_SaltBag` (`AddStack FOOD ... units=1`), so the prefab-level merge is *not* the only delivery path | `Latest.log:9350, 9355, 9362` and the 20:11:06-20:11:10 series |
| E5 | When a merge does happen, the data layer scales correctly: `1 -> 2 units, content 10 -> 20`, then `2 -> 3 units, content 20 -> 30` | `Latest.log:6693-6711`, `9356-9357` |
| E6 | **No** `now holds N whole units instead of ...`, **no** `cut back to ...`, **no** `filled to ...` and **no** `Stack split` line exists for `GEAR_SaltBag` anywhere in this session | full-log grep of `Stack content:` / `Stack split:` / `now holds` / `cut back` / `filled to`: 8 lines total, none of them a salt-bag repair |
| E7 | After the last craft (20:11:54.587) the log is completely silent about salt bags until the player exits to menu | `Latest.log:9362-9423` |
| E8 | The session produced no save file (`sandbox3` is still from 19:02), so no terminal-state evidence exists yet | `Survival/sandbox3` mtime |
| E9 | `SaltBag.json` is the only blueprint in that pack mentioning `GEAR_SaltBag` | `grep -rl GEAR_SaltBag` over the unpacked modcomponent |
| E10 | `CraftingOperation_ConsumeMaterials.Postfix` runs `SyncInventory("crafted")` over the whole bag with `immediate: true`, i.e. before the crafted result is handed out | `Patches/CraftingOperation_ConsumeMaterials.cs` |
| E11 | v1.3.8 changed exactly one line of behaviour: `StackContentSync.Adopt` no longer returns early for an already registered component | `git diff -- VisualStudio/Utilities/StackContentSync.cs` |
| E12 | `Adopt` is called only from the Awake postfix, the drop postfix and the curing-box leftover path; no sweep calls it | `grep -rn "Adopt("` |

**Conclusion from E6 + E7:** the transition `(2, 20 kg) -> (1, 10 kg)` is *silent*. None of the
existing repair logs can produce it. The missing evidence is therefore a terminal-state snapshot of
the salt-bag stack, the identity of the object that survives, and the writer of `m_Units` / the
powder amount. This is exactly what one instrumented run must buy back.

## 3. Hypothesis matrix

| ID | Hypothesis | Evidence status | Observable that decides it |
|---|---|---|---|
| H1 | The old stack object is **removed** and a fresh 1-unit bag takes its place | Most likely; explains the silence (removal is unlogged) and the final shape | `SNAP` instance id changes, `REMOVEGEAR` fires for the old id |
| H2 | The **same** stack object is overwritten in place to `units=1, powder=10` | Co-most-likely; also silent if the write is native | `SNAP` id unchanged; `DIFF` shows `u=2/3 -> 1`; `SETUNITS` present or absent tells managed vs native |
| H3 | `Settle` / `Normalize` recounts the extra unit as a mistake (the v1.3.7 mechanism) | **Excluded** by E6 for this session; probe still prints the merge decision points to confirm "not reached" rather than "reached quietly" | any `now holds ...` line, or `SNAP` at `merge4.end` already showing 1 unit |
| H4 | Data is correct (`3 / 30 kg`) and only the inventory **display** is wrong | Unverified | `UI` line prints a count of 1 while `SNAP` says `u=3` |
| H5 | The merge was refused, leaving two separate stacks; the player saw the 1-unit one | Unverified | `SNAP n=2`, `MERGE* result=false` |
| H6 | The craft cost phase consumes the watched stack (blueprint/third-party matching) | Weakly excluded by E2 + E3; confirmed or killed by the consume boundary | `SNAP` at `craft.consume.end` already collapsed |
| H7 | v1.3.8 `Adopt` overwrites the repair track, causing a later `Reconcile` to misread the stack | Plausible regression (E11); no log line covers track writes | `track=none` or a stale `track=1/10.000` on a `u=2/3` stack right before the change |
| H8 | Another mod or native code writes the stack outside every hook | Fallback; no evidence | `DIFF` fires with no adjacent `SETUNITS` and no `REMOVE*` line |

H1 and H2 both explain E4-E7 and are the primary target. H4 would explain the player's report
without any data loss. H3 is kept in the matrix only because "we did not see the line" is weaker
than "we saw the branch was not taken"; the probe closes that gap too.

## 4. Probe matrix (one build, one reproduction)

All probes are in `VisualStudio/Diagnostics/CraftStackProbe.cs`, guarded by `#if P139_DIAG`.
They are **log-only**: no field write, no `__result` change, no `return false`, no timing change
beyond a 0.15 s read-only sample. Every line starts with `[P139]`.

| # | Hook / sampler | What it prints | Hypotheses discriminated |
|---|---|---|---|
| P1 | `Main.OnUpdate` sampler (`Tick`, 0.15 s) | `[P139] PROBE READY`, then `DIFF old=... new=...` on **any** change of the salt-bag instance set, unit count or powder amount, plus a full `SNAP` | H1, H2, H8 (catches native writers no hook can see) |
| P2 | `SNAP tag=...` | For every salt bag in `Inventory.m_Items`: instance id, stackable id, `m_Units`, powder kg, powder limit, reported weight, in-bag / satchel flags, registration, `track=units/content`; plus `n`, `totalU`, `totalKG`, `cash` | H1, H2, H4, H5, H7 |
| P3 | `CraftingOperation.ConsumeMaterialsUsedForCrafting` prefix/postfix | `SNAP` at `craft.consume.begin/end` | H6 (loss before the result exists) |
| P4 | `CraftingOperation.HandleSuccess` prefix/postfix | `SNAP` at `craft.success.begin/end` | H1, H2 (bracket the hand-out) |
| P5 | `PlayerManager.InstantiateCraftedItemInPlayerInventory` prefix/postfix | `SNAP` at `craft.deliver.begin/end` | H1, H2 |
| P6 | `PlayerManager.InstantiateItemInPlayerInventory(GearItem,int,float,flags)` prefix/postfix | `INSTANTIATE begin/end` with source and result identity + `SNAP` | H1, H5 |
| P7 | `PlayerManager.TryAddToExistingStackable` **4-arg** prefix/postfix | `MERGE4 begin/end`: what is being added, `numUnits`, success, target identity + `SNAP` | H1, H3, H5, H7 |
| P8 | `PlayerManager.TryAddToExistingStackable` **2-arg** prefix/postfix | `MERGE2 begin/end` (an unpatched vanilla overload would otherwise hide the result) | H1, H5 |
| P9 | `Inventory.AddGear(GearItem,bool)` prefix/postfix | `ADDGEAR begin` + `SNAP` before/after | H1, H5 |
| P10 | `Inventory.RemoveGear(GameObject)` and `(GameObject,bool)` prefix | `REMOVEGEAR(1/2)` with the removed item's identity + `SNAP` | **H1** (the only other way a stack vanishes) |
| P11 | `Inventory.RemoveUnits(GearItem,int)` prefix/postfix | `REMOVEUNITS begin/end` + `SNAP` | H2, H6 |
| P12 | `StackableItem.set_m_Units` prefix | `SETUNITS sid=... old=... new=...` for salt bags only | **H2 managed vs native** |
| P13 | `InventoryGridItem.Refresh(GearItem,int)` postfix | `UI data=... stackLabel=... unitLabel=... liquidLabel=...` | **H4** (what the player actually saw) |

P1 is the safety net: even a writer that has no Harmony hook at all appears as a `DIFF`, and the
surrounding `SNAP` tags bracket it between two known calls.

### Combined vs separate experiments

Everything in the table runs in **one** reproduction. There is no reason to split it: the probes
are read-only, their cost is one line per event, and the interpretation depends on their relative
order. The only genuinely separate action is saving the game at the end (step 6 of the protocol),
which is not an experiment but a terminal-state artifact.

### Execution order (information gain / cost)

1. Static evidence (done, section 2).
2. Deploy this build (done).
3. One reproduction with the protocol below, including a no-op baseline and a save.
4. Parse the `[P139]` lines in order, apply the interpretation table, then deliver the fix without
   asking for another reproduction.

## 5. Reproduction protocol (one run)

1. **Restart the game** so the new dll loads; keep **Debug Logging on**.
2. Load the save. Confirm in the inventory that the salt bag is one stack of 2 units / 20 kg.
3. **Baseline:** do nothing for ~3 seconds. The log should show `[P139] PROBE READY`, a stable
   `SNAP tag=probe.ready` with `n=1 ... u=2 powder=20.000`, and **no** `DIFF` line.
4. Open the crafting menu and craft **exactly one** `GEAR_SaltBag`.
5. Do **not** move, drop, transfer, eat or re-open any container afterwards. Wait ~5 seconds so
   the sampler and the 0.5 s idle sweep both run to completion. Note what the inventory now shows
   (stack count and total weight).
6. **Save the game** (this session previously produced no save, which is why no terminal state
   could be checked). Then exit to menu / quit.
7. Send `MelonLoader/Latest.log`.

If step 3 already shows a `DIFF` with no craft, stop and send the log anyway: that means the fault
happens on load, which would immediately confirm H7 and remove the need to craft at all.

## 6. Interpretation table

Read the `[P139]` lines in timestamp order. `SNAP` tags give the phase; `DIFF` gives the exact
transition; `id` / `sid` give object identity.

| Result | Conclusion | Next action |
|---|---|---|
| **A** `craft.consume.end` already shows `u=1 powder=10.000` | Loss happens in the cost phase, before any result exists (H6) | Fix the consumption path; inspect `REMOVEUNITS` / `REMOVEGEAR` / `CraftConsumptionOrder` lines that precede it |
| **B** `craft.consume.end` is intact, `craft.success.end` or `craft.deliver.end` shows `u=1 powder=10.000` **and the `id` changed** | H1: the old stack was removed and replaced | Read the `REMOVEGEAR` / `INSTANTIATE` / `ADDGEAR` lines between the two snapshots; the caller that removes the old stack is the fix point |
| **C** Same as B but the **`id` is unchanged**, and `SETUNITS ... new=1` is present | H2, managed writer | The `SETUNITS` line names the count; the `SNAP` before it names the phase; fix that writer |
| **D** Same as C but **no** `SETUNITS` line | H2, native writer (or a direct field write) | `DIFF` brackets it between two tagged `SNAP`s; the enclosing hook is the static fix point; no second reproduction needed |
| **E** `SNAP` stays `u=3 powder=30.000` while `UI ... stackLabel="x1"` (or equivalent) | H4: display-only | Fix the UI path, not the data path |
| **F** `SNAP n=2` (a 3-unit stack plus a 1-unit stack) or `MERGE* result=false` | H5: merge refusal, not data loss | Fix the merge eligibility rule (`GetClosestMatchStackable` / `CanStackItems` / partial-unit filter) |
| **G** `track=none` or `track=1/10.000` on a `u=2` or `u=3` stack just before the change | H7: v1.3.8 still leaves a stack untracked / stale | Revert or rework the `Adopt` change; the `SNAP` series shows exactly when the track was lost |
| **H** A `DIFF` with no adjacent hook line at all | H8: outside every managed path | Disassemble the single caller bracketed by the two `SNAP` tags; done statically, not by asking for another run |

B/C/D are the expected outcomes given E6/E7. **If the result is B, C, D, E, F or G, the next
message contains the fix, not another request to reproduce.** Only result H might need one more
log line, and even then I will first try to close it by disassembling the bracketed caller.

## 7. Cleanup contract

The diagnostic build is physically isolated from any fix:

- Probe code lives only in `VisualStudio/Diagnostics/CraftStackProbe.cs` and is wrapped in
  `#if P139_DIAG`.
- The only edits to shipping files are one `#if P139_DIAG` line in `StackManager.OnUpdate` and one
  `<DefineConstants>` line in the csproj. No behaviour, version number, README or ChangeLog changed.
- After the root cause is confirmed: back up `Diagnostics/CraftStackProbe.cs` into
  `analysis/probe-backups/P139/`, delete the folder, remove both build wires, rebuild, then grep
  the deployed dll for `P139` (expected count: **0**) before shipping the actual fix.
- The fix and the probe removal must be separate changes, never one commit.

## 8. Handoff / evidence index

- Player log used for the static pass: `F:\GameAbouts\The Long Dark\TheLongDark_2.55\MelonLoader\Latest.log`
  (session 20:07:57 - 20:12:19, key lines 3618, 6693-6714, 9350-9362).
- Blueprint: `Mods/ZC8787traderblueprints.modcomponent/blueprints/SaltBag.json`.
- Save slot (unchanged this session, useful for the next one): `Survival/sandbox3`.
- Vanilla hand-out chain (disassembled earlier): `CraftingOperation.HandleSuccess` ->
  `ApplyCraftingProgress` -> `PlayerManager.InstantiateCraftedItemInPlayerInventory` ->
  `InstantiateItemInPlayerInventory(GearItem,int,float,flags)` -> prefab-level
  `TryAddToExistingStackable`, falling back to instantiate + `Inventory.AddGear`.
- `find_callers.py` on 0xCDD760: the 4-arg merge has only three callers game-wide.
- Prior rounds: v1.3.7 fixed the flask recount; v1.3.8 made `Adopt` idempotent for the earlier
  "crafted bag is swallowed" report. Both are in `ChangeLog.md`.

---

# Resolution (P139 closed, v1.3.9)

## Root cause

A craft instantiates its result **twice**: once before the progress starts, as the host for
`CraftingOperation.m_WorkInProgress` (field `+0x58`, an `InProgressCraftItem` component), and once from inside
`CraftingOperation.HandleSuccess -> ApplyCraftingProgress` as the real reward. `ApplyCraftingProgress` destroys
`m_WorkInProgress.gameObject` via `Inventory.DestroyGear` before it hands out the real reward.

`PlayerManager.InstantiateItemInPlayerInventory` (RVA `0xCEB490`) begins with a prefab-level
`TryAddToExistingStackable` call. When it returns true, the method does **not** instantiate anything: at
`0x180CEB74D` it loads the `out GearItem existingGearItem` from the stack slot and returns that object as its own
result. So a successful merge makes **the player's whole stack** the object the craft just "created".

Stack Manager is what makes a salt bag stackable in the first place (`AddStack FOOD: GEAR_SaltBag`). Therefore:

| Step | Evidence |
|---|---|
| Preview delivery merges into the bag's stack | `20:44:12.636 MERGE4 ... result=True target=id=-843210 u=3`; `Stack content: 2 -> 3 units, content 20.000 -> 30.000 kg (inventory merge)` |
| The craft treats the returned stack as its work-in-progress host | `CraftingOperation.m_WorkInProgress @ +0x58`; disassembly `0x1812010DF get_gameObject` -> `0x1812010F6 Inventory.DestroyGear` |
| Finishing the craft destroys the whole stack | `20:44:13.482 REMOVEGEAR(2) item=GEAR_SaltBag id=-843210 u=3 powder=30.000 destroy=False` (`Inventory.DestroyGear` calls `RemoveGear(go, bool)` at `0x180DAD939`) |
| The real reward can no longer merge, so only one fresh bag appears | `20:44:13.484 MERGE4 result=False` -> `ADDGEAR id=-844388 u=1` |
| Net effect | `20:44:13.546 DIFF old=-843210:3:30.000 new=-844388:1:10.000` |

Vanilla is unaffected because a non-stackable bag cannot merge: the preview is always a separate instance and
destroying it costs only that instance. This also explains the "sometimes" behaviour - the loss only happens when
a mergeable stack of the crafted item is present at the moment the preview is created.

## Hypotheses ruled out

- **H1 Normalize recounting the new bag away (v1.3.7 mechanism)**: this session has zero `now holds`, `cut back`,
  `filled to` or `Stack split` lines for `GEAR_SaltBag`; the loss is completely silent.
- **H2 The recipe consumes a salt bag**: `blueprints/SaltBag.json` requires only `2 x GEAR_CashBundle`, with
  `KeroseneLitersRequired = 0`, `GunpowderKGRequired = 0` and no powder/liquid requirements. Cash went 2 -> 0
  correctly.
- **H3 The v1.3.8 `Adopt` change**: `Adopt` only builds bookkeeping records; no `RemoveGear` exists anywhere in
  `StackContentSync`. The destroyed object is identified by instance id, not by a recount.
- **H4 Display-only fault**: `SNAP` and `UI` agree on `u=1 powder=10.000` at the end; the data really is gone.
- **H5 Native field write**: no `SETUNITS` on the destroyed stack; `DIFF` shows the *instance* changed.

## Minimal fix

`CraftDeliveryGuard` marks the two delivery windows. `CraftingOperation_HandleSuccess_Guard` marks the success
path, `PlayerManager_CraftDelivery_Guard` marks both deliveries. While a craft is delivering **outside** the
success path, `PlayerManager_TryAddToExistingStackable` refuses the merge, forcing the game to instantiate a
separate work-in-progress host. Destroying that host can no longer touch the player's stack, while the real
reward from `HandleSuccess` still merges exactly as before.

Both marks are depth-counted and expire after 30 s of unscaled time, so an interrupted craft cannot leave a
stale mark. The guard is read-only with respect to game state: it only makes one merge call return false.

## Regression check

1. Load a save with one stack of 2 whole salt bags (20 kg) and at least 2 `GEAR_CashBundle`.
2. Craft exactly one `GEAR_SaltBag` from the trader blueprint.
3. Expected: one stack of 3 bags / 30 kg, cash reduced by 2, no bag lost.
4. Repeat several times; also craft while no salt bag is present, and craft a non-stackable item, to confirm
   normal delivery is unchanged.
5. In the log, the merge line `2 -> 3 units ... (inventory merge)` should appear only once per craft, after the
   success boundary, and there must be no `REMOVEGEAR` of the player's stack.

## Probe teardown

- Source backed up to `docs/diagnostics/CraftStackProbe.P139.cs.txt` and
  `D:/code/WorkBuddy_WorkSpace/The_Long_Dark/analysis/CraftStackProbe.P139.cs.bak`.
- `VisualStudio/Diagnostics/` removed; `P139_DIAG` removed from the project; `OnUpdate` hook removed.
- The shipped DLL must contain zero `P139` strings (verified after build).
