using Il2CppTLD.IntBackedUnit;

namespace StackManager.Utilities
{
	/// <summary>
	/// Decides which food and cooking ingredient units are allowed to join a stack.
	/// </summary>
	/// <remarks>
	/// Only full units may merge. Food stores how much is left in <see cref="FoodItem.m_CaloriesRemaining"/>, drinks
	/// store it in <see cref="LiquidItem.m_Liquid"/>, and both are what the displayed weight is derived from. Merging a
	/// half eaten unit into full ones would spread the missing amount over the whole stack, and the stack would end up
	/// heavier than the sum of its parts the moment it is split again.
	/// Merging is refused instead, which leaves the partial unit as its own single unit stack.
	/// Everything here is inert while the Max Weight Food Stackable setting is off.
	/// </remarks>
	public static class FoodStackingRules
	{
		/// <summary>Calories and volumes are floats, so exact equality would never match</summary>
		private const float ToleranceFraction = 0.01f;

		public static bool IsEnabled => Settings.Instance.FoodStackable;

		/// <summary>
		/// True when this mod should give the item a <see cref="StackableItem"/> so full units of it can stack.
		/// </summary>
		/// <remarks>
		/// Cooking ingredients sit in the same inventory category as food but cannot be eaten, so they are listed in the
		/// config by name instead of being detected through a component. Powders and liquid containers are allowed as
		/// well, and a stack of them shares the content of every unit in it, which <see cref="StackContentSync"/> keeps
		/// in step with the unit count.
		/// </remarks>
		public static bool CanAddComponent(GearItem? gi, string name)
		{
			if (gi == null) return false;

			bool configured = IsConfiguredIngredient(name);
			bool edible = gi.m_FoodItem != null;

			return configured || edible;
		}

		public static bool CanAddComponent(GearItem? gi)
		{
			if (gi == null) return false;

			return CanAddComponent(gi, CommonUtilities.NormalizeName(gi.name) ?? string.Empty);
		}

		/// <summary>
		/// True when this item must not take part in a stack merge.
		/// </summary>
		public static bool KeepsOutOfStack(GearItem? gi)
		{
			if (!IsEnabled) return false;
			if (gi == null) return false;

			StackableItem? stack = gi.m_StackableItem;

			// Items without a component cannot be merged by the vanilla entry points anyway, and a component this mod
			// did not create follows whatever rules its owner set up
			if (stack == null) return false;
			if (!AddedStackables.Contains(stack)) return false;

			if (!IsPartialUnit(gi)) return false;

			DebugLog.Write($"FoodStack: {Describe(gi)} is partial, keeping it out of the stack");

			return true;
		}

		/// <summary>
		/// True when this unit is not the full weight it was created with.
		/// </summary>
		/// <remarks>
		/// The game does not use one convention for every food: some items keep calories per unit while others keep the
		/// total for the whole stack. Rather than guessing which one a given item uses, a unit counts as full when the
		/// ratio matches either one. A genuinely eaten item matches neither.
		/// </remarks>
		public static bool IsPartialUnit(GearItem? gi)
		{
			if (gi == null) return false;

			LiquidItem? liquid = gi.m_LiquidItem;
			if (liquid != null && !IsFullContainer(liquid)) return true;

			// A powder keeps the weight of the whole stack in one place, so what has to be whole is the weight of a
			// single unit - a bag a recipe has already taken something out of no longer matches a full one
			if (gi.m_PowderItem != null) return StackContentSync.UnitFraction(gi) < 1f - ToleranceFraction;

			FoodItem? food = gi.m_FoodItem;
			if (food == null) return false;
			if (food.m_CaloriesTotal <= 0f) return false;

			float ratio = food.m_CaloriesRemaining / food.m_CaloriesTotal;

			int units = 1;
			StackableItem? stack = gi.m_StackableItem;

			if (stack != null && stack.m_Units > 1) units = stack.m_Units;

			return !Nearly(ratio, 1f) && !Nearly(ratio, units);
		}

		/// <summary>
		/// True when this unit still holds everything it was created with, which is the only state a stack can describe.
		/// </summary>
		/// <remarks>
		/// A partially used unit no longer matches the prefab: its weight is below the nominal one, and the crafting
		/// system turns a weight requirement into a number of whole units. Giving a half eaten unit a stackable
		/// component is what let one pie eat two rabbit meats, so partial units are left exactly as the game ships them.
		/// </remarks>
		public static bool IsFullUnit(GearItem? gi)
		{
			if (gi == null) return false;

			return !IsPartialUnit(gi);
		}

		/// <summary>
		/// True when the two units are the same thing, so combining them cannot make the stack lie about its contents.
		/// </summary>
		/// <remarks>
		/// A stack stores one weight, one condition and one calorie value for all of its units, so the units have to be
		/// identical for the total to stay correct. Meat pieces drift apart as they age (a stack of a 0.47 kg and a
		/// 0.50 kg piece reports 1.00 kg), and the total is what the container list and the crafting requirements read.
		/// </remarks>
		public static bool AreInterchangeable(GearItem? added, GearItem? existing)
		{
			if (added == null || existing == null) return false;

			string? addedName = CommonUtilities.NormalizeName(added.name);
			string? existingName = CommonUtilities.NormalizeName(existing.name);

			if (!string.Equals(addedName, existingName, StringComparison.OrdinalIgnoreCase)) return false;

			// A powder stack stores its whole weight in one place, so what has to match is the weight per unit, not the
			// total: after three bags were merged the stack weighs three bags and would never match a fourth one.
			if (added.m_PowderItem != null || existing.m_PowderItem != null)
			{
				if (added.m_PowderItem == null || existing.m_PowderItem == null) return false;

				float addedPerUnit = StackContentSync.ToKilograms(added.m_PowderItem.m_Weight) / UnitsOf(added);
				float existingPerUnit = StackContentSync.ToKilograms(existing.m_PowderItem.m_Weight) / UnitsOf(existing);

				return Nearly(addedPerUnit, existingPerUnit);
			}

			// A liquid stack stores one shared volume, so the fill level is what has to match
			if (added.m_LiquidItem != null || existing.m_LiquidItem != null)
			{
				if (added.m_LiquidItem == null || existing.m_LiquidItem == null) return false;

				return Nearly(FillRatio(added.m_LiquidItem), FillRatio(existing.m_LiquidItem));
			}

			if (added.GetSingleItemWeightKG(false) != existing.GetSingleItemWeightKG(false)) return false;

			FoodItem? addedFood = added.m_FoodItem;
			FoodItem? existingFood = existing.m_FoodItem;

			if (addedFood == null && existingFood == null) return true;
			if (addedFood == null || existingFood == null) return false;

			return Nearly(addedFood.m_CaloriesRemaining, existingFood.m_CaloriesRemaining);
		}

		/// <summary>
		/// How much of the container is still filled, 1 meaning untouched. Only used for logging.
		/// </summary>
		public static float FillRatio(LiquidItem? liquid)
		{
			if (liquid == null) return 1f;
			if (liquid.m_LiquidCapacity == ItemLiquidVolume.Zero) return 1f;

			return liquid.m_Liquid / liquid.m_LiquidCapacity;
		}

		private static bool IsFullContainer(LiquidItem liquid)
		{
			// A prefab without a capacity is not a measurable container, so the unit is whole by definition
			if (liquid.m_LiquidCapacity == ItemLiquidVolume.Zero) return true;

			return liquid.m_Liquid >= liquid.m_LiquidCapacity;
		}

		private static bool IsConfiguredIngredient(string name)
		{
			if (Main.Config == null) return false;
			if (string.IsNullOrWhiteSpace(name)) return false;

			return Main.Config.MaxWeightStackItems.Contains(name);
		}

		private static float UnitsOf(GearItem gi)
		{
			StackableItem? stack = gi.m_StackableItem;

			return stack == null ? 1 : stack.m_Units;
		}

		private static string Describe(GearItem gi)
		{
			LiquidItem? liquid = gi.m_LiquidItem;
			FoodItem? food = gi.m_FoodItem;

			if (food != null)
			{
				return $"{gi.name} (calories {food.m_CaloriesRemaining:F1}/{food.m_CaloriesTotal:F1}, units {UnitsOf(gi)})";
			}

			if (liquid != null)
			{
				return $"{gi.name} (filled {FillRatio(liquid):P0}, units {UnitsOf(gi)})";
			}

			return gi.name;
		}

		private static bool Nearly(float value, float target)
		{
			return Math.Abs(value - target) <= Math.Abs(target) * ToleranceFraction;
		}
	}
}
