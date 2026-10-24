using Il2CppTLD.Gear;

namespace StackManager.Patches
{
	/* Logic:
		Answers one question with data instead of guesswork: when a recipe hands out its ingredients, which item lost
		what?

		The crafting system consumes by weight and by count, and both land on the same stack, so a stacked ingredient can
		lose more than the recipe asks for - and the difference is only visible on the item that paid, not in the total.
		This writes one line per instance before and after the consumption, for gear as well as for the powders and
		liquids a recipe asks for by type, so a stack that paid for a piece can be told apart from a piece that paid for
		a stack.
	*/
	[HarmonyPatch(typeof(CraftingOperation), "ConsumeMaterialsUsedForCrafting")]
	public class CraftingOperation_ConsumeMaterialsUsedForCrafting
	{
		public static void Prefix(CraftingOperation __instance)
		{
			Dump(__instance, "before");
		}

		public static void Postfix(CraftingOperation __instance)
		{
			Dump(__instance, "after");
		}

		private static void Dump(CraftingOperation operation, string phase)
		{
			// This runs before and after every craft, over every required ingredient and every item in the inventory.
			// It is a diagnostic only, so with Debug Logging off it must not even read the blueprint
			if (!DebugLog.Enabled) return;

			try
			{
				BlueprintData? blueprint = operation?.Blueprint;
				if (blueprint == null) return;

				Inventory? inventory = GameManager.GetInventoryComponent();
				if (inventory == null) return;

				var items = inventory.m_Items;
				if (items == null) return;

				BlueprintData.RequiredGearItem[]? gear = blueprint.m_RequiredGear;

				if (gear != null)
				{
					foreach (BlueprintData.RequiredGearItem requirement in gear)
					{
						GearItem? prefab = requirement?.m_Item;
						if (prefab == null) continue;

						string name = CommonUtilities.NormalizeName(prefab.name) ?? string.Empty;
						if (string.IsNullOrWhiteSpace(name)) continue;

						Trace(items, name, phase, item => SameName(item, name));
					}
				}

				BlueprintData.RequiredPowder[]? powders = blueprint.m_RequiredPowder;

				if (powders != null)
				{
					foreach (BlueprintData.RequiredPowder requirement in powders)
					{
						PowderType? type = requirement?.m_Powder;
						if (type == null) continue;

						int id = type.GetInstanceID();

						Trace(items, $"powder {type.name}", phase, item =>
							item.m_PowderItem != null && item.m_PowderItem.m_Type != null
								&& item.m_PowderItem.m_Type.GetInstanceID() == id);
					}
				}

				BlueprintData.RequiredLiquid[]? liquids = blueprint.m_RequiredLiquid;

				if (liquids != null)
				{
					foreach (BlueprintData.RequiredLiquid requirement in liquids)
					{
						LiquidType? type = requirement?.m_Liquid;
						if (type == null) continue;

						int id = type.GetInstanceID();

						Trace(items, $"liquid {type.name}", phase, item =>
							item.m_LiquidItem != null && item.m_LiquidItem.m_LiquidType != null
								&& item.m_LiquidItem.m_LiquidType.GetInstanceID() == id);
					}
				}
			}
			catch (Exception e)
			{
				DebugLog.Write($"CraftTrace failed ({phase}): {e.Message}");
			}
		}

		/// <summary>Writes the total of one ingredient and then one line per instance that makes it up</summary>
		private static void Trace(
			Il2CppSystem.Collections.Generic.List<GearItemObject> items,
			string label,
			string phase,
			Func<GearItem, bool> matches)
		{
			int instances = 0;
			int units = 0;
			float total = 0f;

			List<string> lines = new();

			int count = items.Count;

			for (int i = 0; i < count; i++)
			{
				GearItem? item = items[i]?.m_GearItem;
				if (item == null) continue;
				if (!matches(item)) continue;

				instances++;
				units += item.m_StackableItem?.m_Units ?? 1;

				float reported = StackContentSync.ToKilograms(item.GetItemWeightKG(false));
				total += reported;

				lines.Add($"#{instances} id={item.m_InstanceID} units={item.m_StackableItem?.m_Units ?? 1} "
					+ $"{Amount(item)} weight={reported:F3}kg");
			}

			if (instances == 0) return;

			DebugLog.Write($"CraftTrace {phase}: {label} items={instances} units={units} weight={total:F3}kg");

			foreach (string line in lines) DebugLog.Write($"CraftTrace {phase}: {label} {line}");
		}

		/// <summary>The amount this item holds, whichever component carries it</summary>
		private static string Amount(GearItem item)
		{
			if (item.m_PowderItem != null)
			{
				return $"powder={StackContentSync.ToKilograms(item.m_PowderItem.m_Weight):F3}kg";
			}

			if (item.m_LiquidItem != null)
			{
				return $"liquid={StackContentSync.ToLiters(item.m_LiquidItem.m_Liquid):F2}L";
			}

			FoodItem? food = item.m_FoodItem;

			if (food != null)
			{
				return $"cal={food.m_CaloriesRemaining:F0}/{food.m_CaloriesTotal:F0}";
			}

			return "amount=none";
		}

		private static bool SameName(GearItem item, string name)
		{
			string itemName = CommonUtilities.NormalizeName(item.name) ?? string.Empty;

			return string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase);
		}
	}
}
