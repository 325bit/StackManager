using Il2CppTLD.Gear;

namespace StackManager.Utilities
{
	/// <summary>
	/// Hands the lightest matching ingredient to a recipe first.
	/// </summary>
	/// <remarks>
	/// A weight based requirement is taken from whichever matching item the inventory offers first, and on a save
	/// with stacks that is almost always the biggest one: a pie that needs 0.5 kg of meat takes it out of a ten
	/// kilo stack, which then has to be split into whole units again, while the half kilo piece already sitting in
	/// the bag is never touched.
	/// The item list is sorted by weight for the length of the crafting call and put back afterwards. Sorting the
	/// list is enough to change which item the game reaches for without taking over how it takes what it needs, so
	/// the amount consumed stays exactly what the recipe asks for.
	/// </remarks>
	internal static class CraftConsumptionOrder
	{
		/// <summary>The entries that were reordered, and what sat where before</summary>
		private sealed class SavedOrder
		{
			public readonly List<int> Slots = new();
			public readonly List<GearItemObject> Values = new();
			public readonly List<Pick> Picks = new();
			public int Count;
		}

		/// <summary>The candidates of one ingredient, lightest first, as they stood before the craft</summary>
		private sealed class Pick
		{
			public string Label = string.Empty;
			public readonly List<GearItem> Items = new();
			public readonly List<float> Amounts = new();
		}

		private static readonly HashSet<string> s_Reported = new();

		private static SavedOrder? s_Saved;

		/// <summary>Sorts the ingredients a blueprint needs so that the lightest one comes first</summary>
		public static void Apply(BlueprintData? blueprint)
		{
			// A previous call that threw before its postfix ran would otherwise leave the sorted order behind
			Restore();

			if (blueprint == null) return;

			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null) return;

			Il2CppSystem.Collections.Generic.List<GearItemObject>? items = inventory.m_Items;
			if (items == null) return;

			Dictionary<string, List<int>> groups = Group(items, blueprint);
			if (groups.Count == 0) return;

			SavedOrder saved = new() { Count = items.Count };
			List<string> order = new();

			foreach (KeyValuePair<string, List<int>> group in groups)
			{
				List<int> slots = group.Value;
				if (slots.Count < 2) continue;

				List<GearItemObject> sorted = new(slots.Count);

				foreach (int slot in slots)
				{
					GearItemObject? entry = items[slot];
					if (entry != null) sorted.Add(entry);
				}

				if (sorted.Count < 2) continue;

				sorted.Sort((left, right) => WeightOf(left).CompareTo(WeightOf(right)));

				for (int i = 0; i < slots.Count; i++)
				{
					saved.Slots.Add(slots[i]);
					saved.Values.Add(items[slots[i]]);

					items[slots[i]] = sorted[i];
				}

				order.Add($"{group.Key} {string.Join(" + ", sorted.ConvertAll(entry => WeightOf(entry).ToString("F2")))}");

				Pick pick = new() { Label = group.Key };

				foreach (GearItemObject entry in sorted)
				{
					GearItem? item = entry.m_GearItem;
					if (item == null) continue;

					pick.Items.Add(item);
					pick.Amounts.Add(AmountOf(item));
				}

				if (pick.Items.Count >= 2) saved.Picks.Add(pick);
			}

			if (saved.Slots.Count == 0) return;

			s_Saved = saved;

			DebugLog.Write($"Craft order: lightest first, {string.Join(" | ", order)} kg");
		}

		/// <summary>Puts the item list back the way the game had it</summary>
		public static void Restore()
		{
			SavedOrder? saved = s_Saved;
			s_Saved = null;

			if (saved == null) return;

			Verify(saved);

			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null) return;

			Il2CppSystem.Collections.Generic.List<GearItemObject>? items = inventory.m_Items;
			if (items == null) return;

			// A craft that consumed a whole item shortens the list, and writing the old entries back by position
			// would then copy one item over another. The order is cosmetic, so a list that no longer holds the
			// same entries is left exactly as it is.
			if (items.Count != saved.Count) return;

			HashSet<int> known = new();

			foreach (GearItemObject value in saved.Values)
			{
				int id = InstanceIdOf(value);
				if (id != 0) known.Add(id);
			}

			for (int i = 0; i < saved.Slots.Count; i++)
			{
				int slot = saved.Slots[i];
				if (slot >= items.Count) return;
				if (!known.Contains(InstanceIdOf(items[slot]))) return;
			}

			for (int i = 0; i < saved.Slots.Count; i++)
			{
				items[saved.Slots[i]] = saved.Values[i];
			}
		}

		/// <summary>
		/// Notes when the recipe took from a heavier item than the lightest one it was offered.
		/// </summary>
		/// <remarks>
		/// The game picks its own ingredient, so handing it the list in the right order is a request rather than a
		/// command. This says whether the request was honoured, which is the difference between "the ingredient was
		/// used up" and "the picker inside the game never looks at this list".
		/// </remarks>
		private static void Verify(SavedOrder saved)
		{
			foreach (Pick pick in saved.Picks)
			{
				if (pick.Items.Count < 2) continue;

				int paid = -1;

				for (int i = 0; i < pick.Items.Count; i++)
				{
					float now;

					try
					{
						now = AmountOf(pick.Items[i]);
					}
					catch
					{
						// A candidate that was used up is gone by now, which is exactly what a payment looks like
						now = -1f;
					}

					if (now < 0f || !Nearly(now, pick.Amounts[i]))
					{
						paid = i;
						break;
					}
				}

				if (paid <= 0) continue;
				if (!s_Reported.Add(pick.Label)) continue;

				DebugLog.Write($"Craft order: {pick.Label} was offered lightest first, but the recipe took from "
					+ $"candidate {paid + 1} of {pick.Items.Count} - the game picks by its own rule");
			}
		}

		/// <summary>The amount an item holds, in whatever unit it keeps</summary>
		private static float AmountOf(GearItem item)
		{
			PowderItem? powder = item.m_PowderItem;
			if (powder != null) return StackContentSync.ToKilograms(powder.m_Weight);

			LiquidItem? liquid = item.m_LiquidItem;
			if (liquid != null) return StackContentSync.ToLiters(liquid.m_Liquid);

			FoodItem? food = item.m_FoodItem;
			if (food != null) return food.m_CaloriesRemaining;

			return StackContentSync.ToKilograms(item.GetItemWeightKG(false));
		}

		private static bool Nearly(float a, float b)
		{
			float scale = Math.Max(1f, Math.Max(Math.Abs(a), Math.Abs(b)));

			return Math.Abs(a - b) <= 0.001f * scale;
		}

		/// <summary>
		/// Collects the slots of every item the blueprint asks for, keyed by prefab name.
		/// </summary>
		/// <remarks>
		/// Powders and liquids are required by type rather than by gear item, because a bag of flour and a bag of
		/// salt are the same prefab family; they are matched on their type instead of their name.
		/// </remarks>
		private static Dictionary<string, List<int>> Group(Il2CppSystem.Collections.Generic.List<GearItemObject> items, BlueprintData blueprint)
		{
			Dictionary<string, List<int>> groups = new(StringComparer.OrdinalIgnoreCase);

			HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
			HashSet<int> powders = new();
			HashSet<int> liquids = new();

			BlueprintData.RequiredGearItem[]? gear = blueprint.m_RequiredGear;

			if (gear != null)
			{
				foreach (BlueprintData.RequiredGearItem? requirement in gear)
				{
					GearItem? prefab = requirement?.m_Item;
					if (prefab == null) continue;

					string? name = CommonUtilities.NormalizeName(prefab.name);
					if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
				}
			}

			BlueprintData.RequiredPowder[]? requiredPowder = blueprint.m_RequiredPowder;

			if (requiredPowder != null)
			{
				foreach (BlueprintData.RequiredPowder? requirement in requiredPowder)
				{
					PowderType? type = requirement?.m_Powder;
					if (type == null) continue;

					powders.Add(type.GetInstanceID());
				}
			}

			BlueprintData.RequiredLiquid[]? requiredLiquid = blueprint.m_RequiredLiquid;

			if (requiredLiquid != null)
			{
				foreach (BlueprintData.RequiredLiquid? requirement in requiredLiquid)
				{
					LiquidType? type = requirement?.m_Liquid;
					if (type == null) continue;

					liquids.Add(type.GetInstanceID());
				}
			}

			int count = items.Count;

			for (int i = 0; i < count; i++)
			{
				GearItem? item = items[i]?.m_GearItem;
				if (item == null) continue;

				string? key = KeyOf(item, names, powders, liquids);
				if (key == null) continue;

				if (!groups.TryGetValue(key, out List<int>? slots)) groups[key] = slots = new List<int>();

				slots.Add(i);
			}

			return groups;
		}

		/// <summary>The group an item belongs to, or <c>null</c> when the blueprint does not ask for it</summary>
		private static string? KeyOf(GearItem item, HashSet<string> names, HashSet<int> powders, HashSet<int> liquids)
		{
			string name = CommonUtilities.NormalizeName(item.name) ?? string.Empty;

			if (names.Contains(name)) return name;

			PowderItem? powder = item.m_PowderItem;

			if (powder != null && powders.Contains(powder.m_Type.GetInstanceID())) return name;

			LiquidItem? liquid = item.m_LiquidItem;

			if (liquid != null && liquids.Contains(liquid.m_LiquidType.GetInstanceID())) return name;

			return null;
		}

		/// <summary>What the game reports for the whole item, which is what a requirement is measured in</summary>
		private static float WeightOf(GearItemObject? entry)
		{
			GearItem? item = entry?.m_GearItem;
			if (item == null) return float.MaxValue;

			return StackContentSync.ToKilograms(item.GetItemWeightKG(false));
		}

		private static int InstanceIdOf(GearItemObject? entry)
		{
			GearItem? item = entry?.m_GearItem;
			if (item == null) return 0;

			return item.m_InstanceID;
		}
	}
}
