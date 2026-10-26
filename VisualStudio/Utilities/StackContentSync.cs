using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;

namespace StackManager.Utilities
{
	/// <summary>
	/// Keeps what a stack physically holds in step with the number of units it claims to hold.
	/// </summary>
	/// <remarks>
	/// Two different kinds of amount live inside a single gear item.
	/// A powder keeps its weight in <see cref="PowderItem.m_Weight"/> and a liquid its volume in
	/// <see cref="LiquidItem.m_Liquid"/>; both describe the stack as a whole.
	/// Food and plain gear keep one unit's worth in <see cref="FoodItem.m_CaloriesRemaining"/> and
	/// <see cref="GearItem.WeightKG"/>, and the game multiplies that by the unit count on its own.
	/// Two things go wrong with that, and both are repaired here:
	/// a unit count that moves while the amount stays put - dropping one bag out of a stack of three left the stack
	/// still claiming the weight of all three - and an amount that moves while the count stays put - a recipe taking
	/// 0.5 kg out of a stack of five 1 kg pieces left it holding four and a half units while still calling itself five.
	/// The first is repaired by scaling the amount with the count, the second by splitting the stack into whole units
	/// plus a separate partial unit, so a stack never holds anything that is not a full unit.
	/// </remarks>
	public static class StackContentSync
	{
		/// <summary>Amounts are floats, so this is the residue that still counts as nothing left over</summary>
		private const float Epsilon = 0.002f;

		/// <summary>
		/// A deficit smaller than this many units is treated as a whole unit.
		/// </summary>
		/// <remarks>
		/// Floating point adds and subtracts leave residues of a few grams on a kilo. Splitting those off would
		/// produce a never ending stream of 0.999 unit leftovers that immediately want to merge back.
		/// </remarks>
		private const float SnapFraction = 0.02f;

		/// <summary>How long the amount in a stack has to sit still before the idle check splits it</summary>
		private const float SettleSeconds = 0.8f;

		/// <summary>Ticks are held back this long after a save was applied, while the inventory is still filling up</summary>
		private const float LoadHoldSeconds = 6f;

		/// <summary>
		/// How far past what its unit count can hold a stack may sit before the excess is cut back, in units.
		/// </summary>
		/// <remarks>
		/// Used only for the upper bound. Rounding never makes an amount grow, so anything above a single unit's
		/// worth has come from one item being handed the amount of the stack it was split off from.
		/// Measured against the amount of one unit and not against the total: a stack of a hundred bags can lose a
		/// whole bag and still sit within one percent of its total, which is how a container stack kept the content of
		/// the unit that had just been taken out of it.
		/// </remarks>
		private const float ClampTolerance = 0.01f;

		private enum ContentKind
		{
			None,
			Powder,
			Liquid,
			Food
		}

		/// <summary>What one full unit of a prefab holds, recorded from the prefab itself</summary>
		private sealed class Baseline
		{
			public ContentKind Kind;
			public float UnitContent;    // kg for a powder, litres for a liquid, calories for food
			public float UnitWeightKg;   // what a single full unit weighs, container included
			public float UnitLimit;      // powder weight limit or liquid capacity of one unit
		}

		/// <summary>The amount a stack was last seen in step with</summary>
		private sealed class Track
		{
			public GearItem Gear = null!;
			public int Units;
			public float Content;
			public float PendingContent;
			public float PendingSince;
		}

		private static readonly Dictionary<string, Baseline> s_Baselines = new();
		/// <summary>Reused by the food branch of <see cref="TryGetBaseline"/>, whose result is read and dropped at once</summary>
		private static readonly Baseline s_FoodBaseline = new();
		private static readonly Dictionary<int, Track> s_Tracks = new();

		/// <summary>Ceiling on tracked stacks; the table is emptied when it is passed, see <see cref="PruneTracks"/></summary>
		private const int TrackLimit = 4096;

		private static readonly HashSet<string> s_Reported = new();

		/// <summary>
		/// Gear items that left the inventory and have to be looked at on the next tick.
		/// </summary>
		/// <remarks>
		/// A dropped item sits in the world, not in the inventory, so the sweep never reaches it. It is also the item
		/// that most needs the check: the game builds it by cloning the whole stack and then lowering its unit count,
		/// which leaves one unit carrying the amount of all of them - a single bag of flour that reports 4.5 kg.
		/// </remarks>
		private static readonly List<GearItem> s_Watch = new();

		private static bool s_Loading;
		private static bool s_Busy;
		private static float s_LastLoadActivity = -1000f;

		/// <summary>True while a save is being applied, where contents must be taken at face value</summary>
		public static bool IsLoading => s_Loading;

		public static void BeginLoad()
		{
			s_Loading = true;
		}

		public static void EndLoad()
		{
			s_Loading = false;
			s_LastLoadActivity = Time.unscaledTime;
		}

		/// <summary>
		/// Records what a single unit of this prefab holds, so a stack can be rebuilt from an amount later on.
		/// </summary>
		/// <remarks>
		/// Called the moment the component is added, which happens in <see cref="GearItem.Awake"/> on a freshly
		/// spawned prefab: at that point the item still carries exactly one unit worth of everything.
		/// Food is the exception - see <see cref="UnitContentOf"/> - and is never read back from here.
		/// </remarks>
		public static void RecordBaseline(GearItem? gi)
		{
			if (gi == null) return;

			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name)) return;
			if (s_Baselines.ContainsKey(name)) return;

			ContentKind kind = ReadContent(gi, out float content);
			if (kind == ContentKind.None) return;

			s_Baselines[name] = new Baseline
			{
				Kind = kind,
				UnitContent = UnitContentOf(gi, kind, content),
				UnitWeightKg = UnitWeightOf(gi, kind),
				UnitLimit = ReadLimit(gi, kind)
			};
		}

		/// <summary>
		/// How much one whole unit of this item holds.
		/// </summary>
		/// <remarks>
		/// A food item states its own ceiling in <see cref="FoodItem.m_CaloriesTotal"/>, and that number is not final
		/// when the item appears: a cooked pie is built from the raw one and only picks up its cooked calorie value a
		/// moment later. Reading the calories at that point records the raw value as if it were the size of a whole
		/// pie, and every later repair then trims a full pie back down to it - which is why freshly made pies never
		/// stacked. The ceiling the item reports is the only value that follows it through that change.
		/// </remarks>
		private static float UnitContentOf(GearItem gi, ContentKind kind, float content)
		{
			if (kind != ContentKind.Food) return content;

			FoodItem? food = gi.m_FoodItem;

			return food == null || food.m_CaloriesTotal <= 0f ? content : food.m_CaloriesTotal;
		}

		/// <summary>True when this mod is the one that made the prefab with that name stackable</summary>
		public static bool KnowsPrefab(string name)
		{
			return !string.IsNullOrWhiteSpace(name) && s_Baselines.ContainsKey(name);
		}

		/// <summary>
		/// Takes over a component that came along with a copied gear item.
		/// </summary>
		/// <remarks>
		/// The game splits a stack by cloning the whole gear item, which clones the <see cref="StackableItem"/> with it.
		/// That clone is a different component instance, so without this every rule in this mod would quietly skip it:
		/// a dropped bag would still merge with full ones and would never have its amount repaired.
		/// A copy is also clamped to whole units, because a copy that inherited the total of the stack it came from
		/// would hold several units worth of flour while calling itself a single bag.
		/// </remarks>
		public static void Adopt(GearItem? gi, string reason)
		{
			if (gi == null) return;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null) return;
			if (AddedStackables.Contains(stack)) return;

			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;
			if (!KnowsPrefab(name)) return;

			AddedStackables.Register(stack);

			// A copy is clamped to what its own unit count can hold, because a copy that inherited the amount of the
			// stack it came from would hold several units worth of flour while calling itself a single bag.
			// This cannot be done here alone: the game raises the original, clones it, and only then lowers the count
			// on the clone, so at this point the clone usually still claims every unit of the stack. The watch list
			// below finishes the job once the count has settled.
			Clamp(gi, reason);

			// The other direction: a copy built from the prefab is handed a unit count but only the content of one
			FillToUnits(gi, reason);

			ReadContent(gi, out float content);

			s_Tracks[stack.GetInstanceID()] = new Track { Gear = gi, Units = Mathf.Max(1, stack.m_Units), Content = content };
		}

		/// <summary>Queues a gear item that just left the inventory for a check on the next tick</summary>
		public static void Watch(GearItem? gi)
		{
			if (gi == null) return;
			if (gi.m_StackableItem == null) return;

			s_Watch.Add(gi);
		}

		/// <summary>
		/// Cuts an amount back to what the unit count of the item can hold.
		/// </summary>
		/// <remarks>
		/// Every split the game does - dropping one unit out of a stack, moving units into another container, handing
		/// back the leftovers of a recipe - produces an item that started life as a copy of the whole stack. The copy
		/// keeps the shared amount, so a single unit ends up holding the amount of all of them. Nothing else in this
		/// class repairs that, because a stack of one is a legitimate state (it is what a partly used unit looks like),
		/// so the only way to tell a copy apart from a part is that a copy claims more than one unit can hold.
		/// </remarks>
		public static void Clamp(GearItem? gi, string reason)
		{
			if (gi == null) return;
			if (s_Loading) return;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null) return;
			if (!AddedStackables.Contains(stack)) return;

			ContentKind kind = ReadContent(gi, out float content);
			if (kind == ContentKind.None) return;

			if (!TryGetBaseline(gi, kind, out Baseline? baseline))
			{
				// Nothing was ever recorded for this prefab, which happens on a save that already carries the
				// component. The stack is the only description of a unit left, and it is only trusted while the
				// amount still fits inside the container's own limit - a copy holding several units at once would
				// otherwise be taken as the shape of a single unit and become impossible to correct.
				float limit = ReadLimit(gi, kind);
				if (limit <= 0f) return;
				if (content > limit * Mathf.Max(1, stack.m_Units) * (1f + ClampTolerance)) return;

				baseline = EnsureBaseline(gi, stack, kind, content);
			}

			if (baseline == null || baseline.UnitContent <= 0f) return;

			int units = Mathf.Max(1, stack.m_Units);

			// Both kinds of item state their weight in a way the game scales again on its own, so a weight of our
			// own has to be put back. A powder or a liquid carries its amount in the weight field, so anything
			// stamped on top of it is added; a food carries one whole unit and is scaled by the calories, so the
			// share of a part must not be written into it.
			if (kind == ContentKind.Food) RepairFoodWeight(gi, baseline);
			else RestoreContainerWeight(gi, baseline);

			// Food states the amount of one unit, powder and liquid state the amount of the whole stack
			float expected = kind == ContentKind.Food ? baseline.UnitContent : baseline.UnitContent * units;
			if (expected <= 0f) return;

			// Allowance is one unit's worth times this fraction, not a fraction of the whole total, see ClampTolerance
			if (content <= expected + baseline.UnitContent * ClampTolerance) return;

			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? string.Empty;

			SetWholeUnits(gi, kind, baseline, units);

			DebugLog.Write($"Stack content: {name} held {content:F3} for {units} units, cut back to {expected:F3} "
				+ $"({reason}) [{Audit(gi, kind)}]");
		}

		/// <summary>
		/// Grows a stack that was built from the prefab with more than one unit, which leaves it holding one unit's worth.
		/// </summary>
		/// <remarks>
		/// The game hands an item to a container or the bag by instantiating the prefab and then writing the unit count
		/// onto the copy, so a copy of three bottles carries the volume of one - the amount is never taken from the
		/// stack it was split off. A stack of ours always holds the amount of every unit in it, so an amount of exactly
		/// one unit on a higher count can only be that copy.
		/// Only call this at the moment such a copy is handed over. A stack a recipe has just taken something out of
		/// can look the same - three bags with two of them consumed also hold exactly one bag - and raising that one
		/// would hand the consumed amount back, which is why the sweep never does this.
		/// </remarks>
		public static void FillToUnits(GearItem? gi, string reason)
		{
			if (gi == null) return;
			if (s_Loading) return;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null) return;
			if (!AddedStackables.Contains(stack)) return;

			int units = stack.m_Units;
			if (units <= 1) return;

			ContentKind kind = ReadContent(gi, out float content);

			// Food and plain gear describe a single unit already, and the game multiplies that by the count itself
			if (kind != ContentKind.Powder && kind != ContentKind.Liquid) return;

			if (!TryGetBaseline(gi, kind, out Baseline? baseline)) return;
			if (baseline == null || baseline.UnitContent <= 0f) return;

			// Only the signature of a copy made from the prefab, so a stack something took a part out of is left alone
			if (!Nearly(content, baseline.UnitContent)) return;

			SetWholeUnits(gi, kind, baseline, units);

			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? string.Empty;
			float expected = baseline.UnitContent * units;

			DebugLog.Write($"Stack content: {name} was handed {units} units holding one unit's worth, "
				+ $"filled to {expected:F3} ({reason}) [{Audit(gi, kind)}]");
		}

		/// <summary>
		/// Declares the amount a loaded stack carries to be the amount it should carry.
		/// </summary>
		/// <remarks>
		/// A save written by this build stores the total of every unit, so scaling it again on load would multiply a
		/// stack by its own unit count every time the game is started.
		/// A save written before that, or one left behind by a build that did not repair partial stacks, can still
		/// hold a count that does not match the amount. Those are marked as out of step - by storing a content value
		/// that never compares equal - so the next idle check splits them back into whole units.
		/// </remarks>
		public static void MarkLoaded(GearItem? gi)
		{
			StackableItem? stack = gi?.m_StackableItem;
			if (stack == null || gi == null) return;

			ContentKind kind = ReadContent(gi, out float content);

			s_Tracks[stack.GetInstanceID()] = new Track
			{
				Gear = gi,
				Units = stack.m_Units,
				Content = IsInStep(gi, stack, kind, content) ? content : float.NaN
			};
		}

		/// <summary>Forgets a component that is about to be destroyed</summary>
		public static void Forget(StackableItem? stack)
		{
			if (stack == null) return;

			s_Tracks.Remove(stack.GetInstanceID());
		}

		/// <summary>
		/// Brings one stack back in step, acting on a change that has just been made.
		/// </summary>
		public static void EnsureSynced(GearItem? gi, string reason)
		{
			Reconcile(gi, reason, true);
		}

		/// <summary>
		/// Compares a stack against the amount it was last seen with and repairs whichever of the two moved.
		/// </summary>
		/// <param name="immediate">
		/// When set, an amount that moved is repaired straight away. Used by the hooks that know the action is over.
		/// When cleared the repair waits for the amount to stop moving, which is what the idle check uses - it must not
		/// split a stack out from under an eat or a craft that is still running.
		/// </param>
		public static void Reconcile(GearItem? gi, string reason, bool immediate)
		{
			if (s_Busy) return;

			s_Busy = true;

			try
			{
				ReconcileStack(gi, reason, immediate);
			}
			finally
			{
				s_Busy = false;
			}
		}

		/// <summary>
		/// Body of <see cref="Reconcile"/>, kept separate so the guard above covers every exit.
		/// </summary>
		/// <remarks>
		/// Writing the unit count back goes through the property setter, which is patched and would re-enter here in
		/// the middle of a repair. The guard above makes that re-entry a no-op instead of a second split.
		/// </remarks>
		private static void ReconcileStack(GearItem? gi, string reason, bool immediate)
		{
			if (s_Loading) return;
			if (gi == null) return;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null) return;
			if (!AddedStackables.Contains(stack)) return;

			int units = stack.m_Units;
			if (units <= 0) return;

			// Anything holding more than its unit count can account for is a copy of a stack, not a stack: repair that
			// first, so every comparison below is made against a sane amount
			Clamp(gi, reason);

			ContentKind kind = ReadContent(gi, out float content);
			int id = stack.GetInstanceID();

			if (!s_Tracks.TryGetValue(id, out Track? track) || track.Gear != gi)
			{
				s_Tracks[id] = new Track { Gear = gi, Units = units, Content = content };
				return;
			}

			// A unit count that moved on its own: the amount has to follow it, or a stack of two keeps the weight of
			// the three it used to hold
			if (units != track.Units && Nearly(content, track.Content))
			{
				ApplyUnitsChange(gi, kind, track.Units, units, reason);

				ReadContent(gi, out content);
			}

			track.Units = units;

			if (Nearly(content, track.Content))
			{
				track.PendingContent = content;
				track.PendingSince = 0f;
				return;
			}

			// An amount that moved on its own: something took a part out of the stack
			if (!immediate)
			{
				if (!Nearly(content, track.PendingContent))
				{
					track.PendingContent = content;
					track.PendingSince = Time.unscaledTime;
					return;
				}

				if (Time.unscaledTime - track.PendingSince < SettleSeconds) return;
			}

			if (!Normalize(gi, stack, kind, content, reason)) return;

			ReadContent(gi, out content);

			track.Units = stack.m_Units;
			track.Content = content;
			track.PendingContent = content;
			track.PendingSince = 0f;
		}

		/// <summary>Syncs the stacks a container merge has just changed, used after the game merged something into it</summary>
		/// <remarks>
		/// Only the stack that received the units needs looking at. Every other stack in the container was already in
		/// step when it got there, and asking the store-all button to move thirty items used to run the full repair
		/// over the whole container thirty times. The check that keeps this narrow is two reads and a lookup, so it
		/// costs about as much as walking the list.
		/// </remarks>
		public static void SyncContainer(Container? container)
		{
			if (s_Loading) return;
			if (container == null) return;

			Il2CppSystem.Collections.Generic.List<GearItemObject>? items = container.m_Items;
			if (items == null) return;

			int count = items.Count;

			for (int i = 0; i < count; i++)
			{
				GearItemObject? entry = items[i];
				if (entry == null) continue;

				GearItem? gi = entry.m_GearItem;
				if (gi == null) continue;

				if (!NeedsContainerSync(gi)) continue;

				Reconcile(gi, "container merge", true);
			}
		}

		/// <summary>
		/// True when this item is one of ours whose unit count no longer matches what was last repaired.
		/// </summary>
		/// <remarks>
		/// An untracked stack of ours counts as changed: it was never repaired under a track, so the merge that put it
		/// there may be the first thing that gave it a unit count above one. Anything the game or another mod made
		/// stackable has no content of ours to keep in step and is skipped.
		/// </remarks>
		private static bool NeedsContainerSync(GearItem gi)
		{
			StackableItem? stack = gi.m_StackableItem;
			if (stack == null) return false;
			if (!AddedStackables.Contains(stack)) return false;

			if (!s_Tracks.TryGetValue(stack.GetInstanceID(), out Track? track)) return true;
			if (track.Gear != gi) return true;

			return track.Units != stack.m_Units;
		}

		/// <summary>Syncs every stack in the player's inventory</summary>
		public static void SyncInventory(string reason)
		{
			if (s_Loading) return;

			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null) return;

			Il2CppSystem.Collections.Generic.List<GearItemObject>? items = inventory.m_Items;
			if (items == null) return;

			int count = items.Count;

			for (int i = 0; i < count; i++)
			{
				GearItemObject? entry = items[i];
				if (entry == null) continue;

				Reconcile(entry.m_GearItem, reason, true);
			}
		}

		/// <summary>
		/// Periodic sweep over the inventory, which catches every path that changes a stack without telling anyone.
		/// </summary>
		/// <remarks>
		/// Eating, a recipe, a container transfer and a drop all reach the inventory through a different method, and
		/// only some of them can be hooked reliably. Comparing each stack against the amount it was last seen with is
		/// cheap - two reads and a dictionary lookup - and does not depend on knowing which method did it.
		/// The repair itself waits for the amount to settle, so a stack is never split while it is being eaten from.
		/// </remarks>
		public static void IdleCheck()
		{
			if (s_Loading) return;
			if (Time.unscaledTime - s_LastLoadActivity < LoadHoldSeconds) return;
			if (GameManager.IsMainMenuActive()) return;

			CheckWatched();

			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null) return;

			Il2CppSystem.Collections.Generic.List<GearItemObject>? items = inventory.m_Items;
			if (items == null) return;

			int count = items.Count;

			for (int i = 0; i < count; i++)
			{
				GearItemObject? entry = items[i];
				if (entry == null) continue;

				Reconcile(entry.m_GearItem, "idle check", false);
			}

			PruneTracks();
		}

		/// <summary>
		/// How much of one unit this stack still holds, 1 meaning untouched.
		/// </summary>
		/// <remarks>
		/// Used by the merge rules: a unit that is not whole must not join a stack, because everything the stack
		/// reports is stored once and shared by every unit in it.
		/// </remarks>
		public static float UnitFraction(GearItem? gi)
		{
			if (gi == null) return 1f;

			ContentKind kind = ReadContent(gi, out float content);
			if (kind == ContentKind.None) return 1f;

			if (!TryGetBaseline(gi, kind, out Baseline? baseline)) return 1f;
			if (baseline == null || baseline.UnitContent <= 0f) return 1f;

			float perUnit = content;

			// Powder and liquid keep the total of the stack, food keeps the amount of a single unit
			if (kind != ContentKind.Food)
			{
				int units = Mathf.Max(1, gi.m_StackableItem?.m_Units ?? 1);
				perUnit = content / units;
			}

			return Mathf.Clamp01(perUnit / baseline.UnitContent);
		}

		public static float ToKilograms(ItemWeight weight)
		{
			return weight / ItemWeight.FromKilograms(1f);
		}

		public static ItemWeight FromKilograms(float kilograms)
		{
			return ItemWeight.FromKilograms(kilograms);
		}

		public static float ToLiters(ItemLiquidVolume volume)
		{
			return volume / ItemLiquidVolume.FromLiters(1f);
		}

		/// <summary>How much of the container is still filled, 1 meaning untouched</summary>
		public static float FillRatio(LiquidItem? liquid)
		{
			if (liquid == null) return 1f;
			if (liquid.m_LiquidCapacity == ItemLiquidVolume.Zero) return 1f;

			return liquid.m_Liquid / liquid.m_LiquidCapacity;
		}

		/// <summary>Reads the amount a stack holds, whichever component carries it</summary>
		private static ContentKind ReadContent(GearItem gi, out float content)
		{
			content = 0f;

			PowderItem? powder = gi.m_PowderItem;
			if (powder != null)
			{
				content = ToKilograms(powder.m_Weight);
				return ContentKind.Powder;
			}

			LiquidItem? liquid = gi.m_LiquidItem;
			if (liquid != null)
			{
				content = ToLiters(liquid.m_Liquid);
				return ContentKind.Liquid;
			}

			FoodItem? food = gi.m_FoodItem;
			if (food != null)
			{
				content = food.m_CaloriesRemaining;
				return ContentKind.Food;
			}

			return ContentKind.None;
		}

		private static float ReadLimit(GearItem gi, ContentKind kind)
		{
			switch (kind)
			{
				case ContentKind.Powder:
					return gi.m_PowderItem == null ? 0f : ToKilograms(gi.m_PowderItem.m_WeightLimit);

				case ContentKind.Liquid:
					return gi.m_LiquidItem == null ? 0f : ToLiters(gi.m_LiquidItem.m_LiquidCapacity);

				default:
					return 0f;
			}
		}

		/// <summary>
		/// True when the amount in the stack is exactly what its unit count says it should be.
		/// </summary>
		private static bool IsInStep(GearItem gi, StackableItem stack, ContentKind kind, float content)
		{
			if (kind == ContentKind.None) return true;

			if (!TryGetBaseline(gi, kind, out Baseline? baseline)) return true;
			if (baseline == null || baseline.UnitContent <= 0f) return true;

			int units = Mathf.Max(1, stack.m_Units);

			// Food keeps one unit's worth, so a stack is in step as long as that one unit is whole
			float expected = kind == ContentKind.Food ? baseline.UnitContent : baseline.UnitContent * units;

			return Nearly(content, expected);
		}

		/// <summary>
		/// Scales the amount of a stack to a new unit count.
		/// </summary>
		/// <remarks>
		/// Only powder and liquid are scaled: they are the two components that hold the total of the stack.
		/// Food and plain gear already describe a single unit, and the game multiplies that by the count itself.
		/// </remarks>
		private static void ApplyUnitsChange(GearItem gi, ContentKind kind, int fromUnits, int toUnits, string reason)
		{
			if (fromUnits <= 0) return;

			float ratio = (float)toUnits / fromUnits;
			if (Mathf.Abs(ratio - 1f) < 0.001f) return;

			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? string.Empty;
			float before = 0f;
			float after = 0f;

			switch (kind)
			{
				case ContentKind.Powder:
				{
					PowderItem? powder = gi.m_PowderItem;
					if (powder == null) return;

					before = ToKilograms(powder.m_Weight);
					after = before * ratio;

					powder.m_Weight = FromKilograms(after);
					ResizeLimit(gi, kind, toUnits, after);

					break;
				}

				case ContentKind.Liquid:
				{
					LiquidItem? liquid = gi.m_LiquidItem;
					if (liquid == null) return;

					before = ToLiters(liquid.m_Liquid);
					after = before * ratio;

					liquid.m_Liquid = ItemLiquidVolume.FromLiters(after);
					ResizeLimit(gi, kind, toUnits, after);

					break;
				}

				default:
					// Food and plain gear hold a single unit's worth, so there is nothing to scale
					return;
			}

			DebugLog.Write($"Stack content: {name} {fromUnits} -> {toUnits} units, "
				+ $"{Describe(before, after, kind)} ({reason}) [{Audit(gi, kind)}]");
		}

		/// <summary>
		/// Shrinks or grows the powder limit or liquid capacity to match a new unit count.
		/// </summary>
		/// <remarks>
		/// Both limits describe one unit and are not saved, so they have to be rebuilt from the recorded baseline.
		/// Leaving a raised capacity behind after the units that needed it are gone makes the game see a half empty
		/// container, and a half empty container is refused as a merge target.
		/// </remarks>
		private static void ResizeLimit(GearItem gi, ContentKind kind, int units, float content)
		{
			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;
			if (!s_Baselines.TryGetValue(name, out Baseline? baseline)) return;
			if (baseline.UnitLimit <= 0f) return;

			float limit = Mathf.Max(baseline.UnitLimit * units, content);

			if (kind == ContentKind.Powder && gi.m_PowderItem != null)
			{
				gi.m_PowderItem.m_WeightLimit = FromKilograms(limit);
			}
			else if (kind == ContentKind.Liquid && gi.m_LiquidItem != null)
			{
				gi.m_LiquidItem.m_LiquidCapacity = ItemLiquidVolume.FromLiters(limit);
			}
		}

		/// <summary>
		/// Rebuilds a stack from the amount it holds: whole units stay in it, whatever is left over becomes its own unit.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the stack is in a state the caller can record, <c>false</c> when the repair has to be
		/// retried later - a stack that is not in the player's inventory cannot be split, because there is nowhere
		/// to put the leftover.
		/// </returns>
		private static bool Normalize(GearItem gi, StackableItem stack, ContentKind kind, float content, string reason)
		{
			// A stack of one is allowed to be a part: that is exactly what the game itself hands out after a bite
			if (stack.m_Units < 2) return true;
			if (kind == ContentKind.None) return true;

			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;

			if (!TryGetBaseline(gi, kind, out Baseline? baseline))
			{
				baseline = EnsureBaseline(gi, stack, kind, content);
			}

			if (baseline == null || baseline.UnitContent <= 0f) return true;

			// Food states the amount of a single unit, the other two state the total of the stack
			float totalUnits = kind == ContentKind.Food
				? (stack.m_Units - 1) + content / baseline.UnitContent
				: content / baseline.UnitContent;

			if (totalUnits <= Epsilon) return true;

			int whole = Mathf.FloorToInt(totalUnits + 0.001f);
			float partial = totalUnits - whole;

			// Anything that is off by less than a rounding error counts as a whole unit
			if (partial >= 1f - SnapFraction)
			{
				whole++;
				partial = 0f;
			}
			else if (partial <= SnapFraction)
			{
				// The last mouthful is not worth a piece of its own
				partial = 0f;
			}

			if (partial <= Epsilon)
			{
				// Nothing whole left in it: the stack is one partly used unit, but the count is left exactly as the
				// game has it so a broken save cannot be turned into a missing item here
				if (whole < 1) return true;
				if (whole == stack.m_Units) return true;

				// Whole units, just not the number the stack claims. Nothing has to be split, only recounted.
				SetWholeUnits(gi, kind, baseline, whole);
				stack.m_Units = whole;

				DebugLog.Write($"Stack content: {name} now holds {whole} whole units instead of {totalUnits:F2} "
					+ $"({reason})");

				return true;
			}

			if (whole < 1) return true;

			// The leftover has to go somewhere; a stack that is not in the bag has nowhere to put it, so it is left
			// exactly as it is and retried once the player picks it up
			if (!gi.m_InPlayerInventory && !gi.m_IsInSatchel)
			{
				ReportOnce($"away:{name}", $"Stack split: {name} holds {totalUnits:F2} units but is not in the player "
					+ "inventory, the split waits until it is");

				return false;
			}

			GearItem? part = null;

			try
			{
				part = gi.InstantiateStackable(1);
			}
			catch (Exception e)
			{
				ReportOnce($"split-error:{name}", $"Stack split failed for {name}: {e.Message}");
				return true;
			}

			if (part == null || part.m_StackableItem == null)
			{
				if (part != null) UnityEngine.Object.Destroy(part.gameObject);

				ReportOnce($"split-null:{name}", $"Stack split failed for {name}: the game returned no copy");
				return true;
			}

			StackableItem partStack = part.m_StackableItem;

			// The copy carries a copy of the component, which nothing has registered yet
			if (!AddedStackables.Contains(partStack)) AddedStackables.Register(partStack);

			partStack.m_Units = 1;
			SetPartialUnit(part, kind, baseline, partial);

			SetWholeUnits(gi, kind, baseline, whole);
			stack.m_Units = whole;

			ReadContent(part, out float partContent);
			s_Tracks[partStack.GetInstanceID()] = new Track { Gear = part, Units = 1, Content = partContent };

			PlaceInInventory(part, gi);

			DebugLog.Write($"Stack split: {name} held {totalUnits:F2} units worth, now {whole} full units plus one "
				+ $"{DescribePartial(partial, baseline, kind)} piece ({reason}) [{Audit(gi, kind)}]");

			return true;
		}

		/// <summary>Fills a stack with the amount every unit in it needs</summary>
		/// <remarks>
		/// Only food gets its weight written here. A powder or a liquid derives its weight from the amount it holds, so
		/// stamping the nominal weight of a bag onto it would add that weight on top of the amount - a stack of three
		/// bags of flour would report three bags of flour plus three kilos of tins. Saving would keep that number too.
		/// </remarks>
		private static void SetWholeUnits(GearItem gi, ContentKind kind, Baseline baseline, int units)
		{
			// Powder and liquid store the total of the stack, food stores the amount of a single unit
			float amount = kind == ContentKind.Food ? baseline.UnitContent : baseline.UnitContent * units;

			switch (kind)
			{
				case ContentKind.Powder:
					if (gi.m_PowderItem != null)
					{
						gi.m_PowderItem.m_Weight = FromKilograms(amount);

						if (baseline.UnitLimit > 0f)
						{
							gi.m_PowderItem.m_WeightLimit = FromKilograms(Mathf.Max(baseline.UnitLimit * units, amount));
						}
					}
					return;

				case ContentKind.Liquid:
					if (gi.m_LiquidItem != null)
					{
						gi.m_LiquidItem.m_Liquid = ItemLiquidVolume.FromLiters(amount);

						if (baseline.UnitLimit > 0f)
						{
							gi.m_LiquidItem.m_LiquidCapacity = ItemLiquidVolume.FromLiters(Mathf.Max(baseline.UnitLimit * units, amount));
						}
					}
					return;

				case ContentKind.Food:
					if (gi.m_FoodItem != null) gi.m_FoodItem.m_CaloriesRemaining = amount;
					break;
			}

			// A plain gear item states the weight of one unit, and the game multiplies it by the unit count itself
			gi.WeightKG = FromKilograms(baseline.UnitWeightKg);
		}

		/// <summary>Fills a single, partly used unit</summary>
		/// <remarks>
		/// The capacity and limit stay at one full unit: that is what makes the game - and the merge rules here - see
		/// a container that is only partly full and keep it out of a stack of full ones.
		/// </remarks>
		private static void SetPartialUnit(GearItem gi, ContentKind kind, Baseline baseline, float fraction)
		{
			float amount = baseline.UnitContent * fraction;

			switch (kind)
			{
				case ContentKind.Powder:
					if (gi.m_PowderItem != null)
					{
						gi.m_PowderItem.m_Weight = FromKilograms(amount);

						if (baseline.UnitLimit > 0f) gi.m_PowderItem.m_WeightLimit = FromKilograms(baseline.UnitLimit);
					}
					return;

				case ContentKind.Liquid:
					if (gi.m_LiquidItem != null)
					{
						gi.m_LiquidItem.m_Liquid = ItemLiquidVolume.FromLiters(amount);

						if (baseline.UnitLimit > 0f) gi.m_LiquidItem.m_LiquidCapacity = ItemLiquidVolume.FromLiters(baseline.UnitLimit);
					}
					return;

				case ContentKind.Food:
					if (gi.m_FoodItem != null) gi.m_FoodItem.m_CaloriesRemaining = amount;

					// A food item always carries the weight of one whole unit: the game scales it by the share of
					// the calories that are left. Writing that share in here as well scales it twice, which is how
					// a pie that should have left 0.5 kg of meat left 0.25 kg instead.
					gi.WeightKG = FromKilograms(baseline.UnitWeightKg);
					return;
			}

			gi.WeightKG = FromKilograms(baseline.UnitWeightKg * fraction);
		}

		/// <summary>
		/// Puts the weight of a food item back to the weight of one whole unit.
		/// </summary>
		/// <remarks>
		/// A food item stores the weight of a whole unit and the game scales it by the calories that are left, so a
		/// half eaten piece is shown at half its weight without anything being written. A build before this one
		/// wrote the calorie share into the weight as well, which the game then scaled again - a 0.5 kg piece came
		/// out as 0.25 kg, and the number is saved along with the item so it would never correct itself.
		/// Only a value that is exactly the old mistake is touched, so a weight another mod chose on purpose stays.
		/// </remarks>
		private static void RepairFoodWeight(GearItem gi, Baseline baseline)
		{
			if (baseline.UnitWeightKg <= 0f) return;

			float current = ToKilograms(gi.WeightKG);
			if (current <= 0f) return;
			if (Nearly(current, baseline.UnitWeightKg)) return;

			FoodItem? food = gi.m_FoodItem;
			if (food == null || food.m_CaloriesTotal <= 0f) return;

			float ratio = Mathf.Clamp01(food.m_CaloriesRemaining / food.m_CaloriesTotal);
			if (ratio >= 1f - Epsilon) return;
			if (!Nearly(current, baseline.UnitWeightKg * ratio)) return;

			gi.WeightKG = FromKilograms(baseline.UnitWeightKg);

			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? string.Empty;

			ReportOnce($"food-weight:{name}", $"Stack content: {name} carried {current:F3} kg for a piece holding "
				+ $"{ratio:P0} of its calories, which the game weighs again - put back to {baseline.UnitWeightKg:F3} kg");
		}

		/// <summary>
		/// The weight of one whole unit, read from an item that may already be partly used.
		/// </summary>
		/// <remarks>
		/// Food reports what is left of it, scaled by the calories, so a half eaten piece says half a kilo. A
		/// baseline has to describe a whole unit: taken from a piece it would shrink a little more every time one
		/// is split off, and every later split would be weighed against the wrong number.
		/// </remarks>
		private static float UnitWeightOf(GearItem gi, ContentKind kind)
		{
			float single = ToKilograms(gi.GetSingleItemWeightKG(false));

			if (kind != ContentKind.Food) return single;

			FoodItem? food = gi.m_FoodItem;
			if (food == null || food.m_CaloriesTotal <= 0f) return single;

			float ratio = Mathf.Clamp01(food.m_CaloriesRemaining / food.m_CaloriesTotal);
			if (ratio < 0.05f) return single;

			return single / ratio;
		}

		/// <summary>
		/// Puts the weight of a powder or a liquid back to the weight of its empty container.
		/// </summary>
		/// <remarks>
		/// Both kinds of item derive their weight from the amount they hold, so any weight of their own is added to it.
		/// The value a stack should carry is the one in its gear data; anything else was written by an earlier build of
		/// this mod and has since been saved, so it would follow the item forever.
		/// </remarks>
		private static void RestoreContainerWeight(GearItem gi, Baseline baseline)
		{
			if (gi.m_GearItemData == null) return;

			float current = ToKilograms(gi.WeightKG);
			if (current <= 0f) return;

			float baseWeight = ToKilograms(gi.m_GearItemData.BaseWeightKG);

			// Only values this mod could have written are touched: a full bag, a part of one, or a whole stack of them
			bool writtenByUs = Nearly(current, baseline.UnitWeightKg)
				|| Nearly(current, baseline.UnitWeightKg * Mathf.Max(1, gi.m_StackableItem?.m_Units ?? 1));

			if (!writtenByUs) return;
			if (Nearly(current, baseWeight)) return;

			gi.WeightKG = gi.m_GearItemData.BaseWeightKG;

			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? string.Empty;

			ReportOnce($"weight:{name}", $"Stack content: {name} carried {current:F3} kg of its own on top of its amount, "
				+ $"put back to {baseWeight:F3} kg");
		}

		/// <summary>
		/// Reads the recorded amount of one unit, or what the item itself says a full unit is.
		/// </summary>
		/// <remarks>
		/// A food item states its own ceiling in <see cref="FoodItem.m_CaloriesTotal"/>, which needs no baseline, is
		/// exact, and is the only value that survives the item being cooked - so it is read fresh every time and a
		/// recorded value is never trusted for food. Powders and liquids have no such field - their limit is raised
		/// along with the stack - so they are only ever measured against the value recorded when the item was whole.
		/// </remarks>
		private static bool TryGetBaseline(GearItem gi, ContentKind kind, out Baseline? baseline)
		{
			baseline = null;

			if (kind == ContentKind.Food)
			{
				FoodItem? food = gi.m_FoodItem;
				if (food == null || food.m_CaloriesTotal <= 0f) return false;

				// A food baseline is only ever read straight away, so the same instance is filled in again instead of
				// building one per call: the sweep asks this for every food item twice a second.
				s_FoodBaseline.Kind = kind;
				s_FoodBaseline.UnitContent = food.m_CaloriesTotal;
				s_FoodBaseline.UnitWeightKg = UnitWeightOf(gi, kind);
				s_FoodBaseline.UnitLimit = 0f;

				baseline = s_FoodBaseline;

				RegisterFoodName(gi, food, kind);

				return true;
			}

			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;

			if (!string.IsNullOrWhiteSpace(name) && s_Baselines.TryGetValue(name, out Baseline? known))
			{
				baseline = known;
				return baseline.UnitContent > 0f;
			}

			return false;
		}

		/// <summary>
		/// Makes a food prefab known by name, so <see cref="KnowsPrefab"/> answers for it once the item has been seen.
		/// </summary>
		/// <remarks>
		/// The entry is a marker, not a measurement: food is always compared against the calorie total the item reports
		/// at that moment, never against a recorded number. Written once per prefab, which is what keeps the sweep from
		/// allocating a baseline per food item per tick.
		/// </remarks>
		private static void RegisterFoodName(GearItem gi, FoodItem food, ContentKind kind)
		{
			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name)) return;
			if (s_Baselines.ContainsKey(name)) return;

			s_Baselines[name] = new Baseline
			{
				Kind = kind,
				UnitContent = food.m_CaloriesTotal,
				UnitWeightKg = s_FoodBaseline.UnitWeightKg,
				UnitLimit = 0f
			};
		}

		private static Baseline? EnsureBaseline(GearItem gi, StackableItem stack, ContentKind kind, float content)
		{
			string name = CommonUtilities.NormalizeName(gi.name) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name)) return null;

			if (s_Baselines.TryGetValue(name, out Baseline? known)) return known;

			// No baseline was recorded, which means this prefab was never seen while it was whole. The stack itself
			// is the best remaining description of a single unit: it is only ever built out of whole ones.
			int units = Mathf.Max(1, stack.m_Units);

			Baseline baseline = new()
			{
				Kind = kind,
				UnitContent = content / units,
				UnitWeightKg = UnitWeightOf(gi, kind),
				UnitLimit = kind == ContentKind.Food ? 0f : ReadLimit(gi, kind) / units
			};

			if (baseline.UnitContent <= 0f) return null;

			s_Baselines[name] = baseline;

			DebugLog.Write($"Stack content: no recorded unit for {name}, taking {baseline.UnitContent:F3} from the stack");

			return baseline;
		}

		/// <summary>Puts the leftover unit into the player's inventory, unless the game already put it there</summary>
		private static void PlaceInInventory(GearItem part, GearItem source)
		{
			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null) return;

			if (Holds(inventory, part)) return;

			part.m_InPlayerInventory = true;
			part.m_BeenInPlayerInventory = true;

			inventory.AddGear(part, false);

			if (!Holds(inventory, part))
			{
				Main.Logger.Log($"Stack split: the inventory did not take the leftover of {source.name}", FlaggedLoggingLevel.Always);
			}
		}

		private static bool Holds(Inventory inventory, GearItem gi)
		{
			Il2CppSystem.Collections.Generic.List<GearItemObject>? items = inventory.m_Items;
			if (items == null) return false;

			int id = gi.m_InstanceID;
			int count = items.Count;

			for (int i = 0; i < count; i++)
			{
				GearItemObject? entry = items[i];
				if (entry == null) continue;

				GearItem? item = entry.m_GearItem;
				if (item == null) continue;
				if (item.m_InstanceID == id) return true;
			}

			return false;
		}

		/// <summary>Looks at the items that have left the inventory since the last tick</summary>
		/// <remarks>
		/// A dropped item is out of the inventory and out of reach of the sweep, but it is the one that most often
		/// carries the amount of the stack it was split from. Each entry is handled once - the item stays in the world
		/// and must not be touched again from here, because it is no longer ours to maintain.
		/// </remarks>
		private static void CheckWatched()
		{
			if (s_Watch.Count == 0) return;

			for (int i = 0; i < s_Watch.Count; i++)
			{
				GearItem? gi = s_Watch[i];
				if (gi == null) continue;

				try
				{
					Clamp(gi, "left the inventory");
				}
				catch (Exception e)
				{
					DebugLog.Write($"Dropped item check failed: {e.Message}");
				}
			}

			s_Watch.Clear();
		}

		/// <summary>Drops trackers whose gear item no longer exists, so the table does not grow all session</summary>
		/// <remarks>
		/// The sweep relies on a destroyed Unity object comparing equal to null, which does not have to hold through
		/// interop. The ceiling makes the table bounded either way: emptying it only costs one repair pass, because a
		/// stack without a track is simply registered again the next time it is looked at.
		/// </remarks>
		private static void PruneTracks()
		{
			if (s_Tracks.Count > TrackLimit)
			{
				ReportOnce("track table", $"Stack tracking table reached {s_Tracks.Count} entries, starting over");

				s_Tracks.Clear();

				return;
			}

			if (s_Tracks.Count < 256) return;

			List<int>? dead = null;

			foreach (KeyValuePair<int, Track> pair in s_Tracks)
			{
				if (pair.Value.Gear != null) continue;

				(dead ??= new List<int>()).Add(pair.Key);
			}

			if (dead == null) return;

			foreach (int id in dead) s_Tracks.Remove(id);
		}

		private static void ReportOnce(string key, string message)
		{
			if (!s_Reported.Add(key)) return;

			DebugLog.Write(message);
		}

		private static string Describe(float before, float after, ContentKind kind)
		{
			return kind == ContentKind.Powder
				? $"content {before:F3} -> {after:F3} kg"
				: $"volume {before:F2} -> {after:F2} L";
		}

		private static string DescribePartial(float fraction, Baseline baseline, ContentKind kind)
		{
			float amount = baseline.UnitContent * fraction;

			return kind switch
			{
				ContentKind.Powder => $"{amount:F3} kg",
				ContentKind.Liquid => $"{amount:F2} L",
				_ => $"{amount:F0} cal"
			};
		}

		/// <summary>
		/// The numbers the game itself works with, printed next to every repair.
		/// </summary>
		/// <remarks>
		/// A wrong weight on screen can come from the amount, from the item's own weight, or from the way the game
		/// multiplies the two, and the three look identical from the outside. Printing all of them together means one
		/// log line is enough to tell which one moved.
		/// </remarks>
		private static string Audit(GearItem gi, ContentKind kind)
		{
			string amount;

			switch (kind)
			{
				case ContentKind.Powder:
					amount = $"powder={ToKilograms(gi.m_PowderItem == null ? ItemWeight.Zero : gi.m_PowderItem.m_Weight):F3}kg";
					break;

				case ContentKind.Liquid:
					amount = $"liquid={ToLiters(gi.m_LiquidItem == null ? ItemLiquidVolume.Zero : gi.m_LiquidItem.m_Liquid):F2}L";
					break;

				default:
					amount = $"cal={gi.m_FoodItem?.m_CaloriesRemaining ?? 0f:F0}/{gi.m_FoodItem?.m_CaloriesTotal ?? 0f:F0}";
					break;
			}

			float own = ToKilograms(gi.WeightKG);
			float reported = ToKilograms(gi.GetItemWeightKG(false));
			float single = ToKilograms(gi.GetSingleItemWeightKG(false));

			return $"units={gi.m_StackableItem?.m_Units ?? 1} {amount} own={own:F3} item={reported:F3} single={single:F3}";
		}

		private static bool Nearly(float a, float b)
		{
			if (float.IsNaN(a) || float.IsNaN(b)) return false;

			float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));

			return Mathf.Abs(a - b) <= Epsilon * scale;
		}
	}
}
