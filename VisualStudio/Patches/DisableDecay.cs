using System.Runtime.CompilerServices;

namespace StackManager.Patches
{
	[HarmonyPatch(typeof(GearItem), nameof(GearItem.Awake))]
	internal class DisableDecay
	{
		// Every StackableItem this mod creates is recorded in AddedStackables, which the rollback below and the merge
		// rules both consult. That registry is what keeps this mod from touching a component it did not create.

		/// <summary>Each food prefab is described once per session, so the log stays readable</summary>
		private static readonly HashSet<string> s_DiagnosedFoods = new();

		/// <summary>
		/// Freshness may only differ by this many percent for two food units to merge.
		/// </summary>
		/// <remarks>
		/// Condition on food is freshness, and the merge keeps the better of the two. Letting a fresh unit merge into a
		/// rotten one would launder the rot, so near identical freshness is required.
		/// </remarks>
		private const float FoodConditionDifferenceConstraint = 10f;

		/// <summary>
		/// Anything without a freshness value is free to merge across condition differences.
		/// </summary>
		private const float NoConditionDifferenceConstraint = 100f;

		public static void Prefix(GearItem __instance)
		{
			if (__instance == null) return;
			if (GameManager.IsMainMenuActive()) return;
			if (string.IsNullOrWhiteSpace(__instance.name)) return;
			if (Main.Config == null) return;

			string name = CommonUtilities.NormalizeName(__instance.name) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name)) return;

			if (!Settings.Instance.AddStack)
			{
				// The food feature adds its own components in the Postfix below and must be allowed to keep doing so
				if (FoodStackingRules.IsEnabled && FoodStackingRules.CanAddComponent(__instance, name)) return;

				// The setting was turned off after the component had already been added, so take it back off.
				// Only items that awake from here on are affected; anything already spawned keeps the component
				// until it is reloaded, because GearItem.Awake only ever runs once per instance.
				// Only the list this setting owns is rolled back, so nothing else is touched by accident.
				if (!Main.Config.AddStackableComponent.Contains(name)) return;

				StackableItem? addedByUs = __instance.gameObject.GetComponent<StackableItem>();

				// A component carrying more than one unit is a live stack: destroying it would drop the extra units,
				// so those stay stacked until the stack is split back to a single unit.
				if (addedByUs != null && addedByUs.m_Units <= 1 && AddedStackables.TryUnregister(addedByUs))
				{
					DebugLog.Write($"AddStack REMOVE: {name}");

					StackContentSync.Forget(addedByUs);

					__instance.m_StackableItem = null;
					UnityEngine.Object.Destroy(addedByUs);
				}

				return;
			}

			if (!Main.Config.AddStackableComponent.Contains(name)) return;
			if (__instance.m_StackableItem != null) return;
			if (__instance.gameObject.GetComponent<StackableItem>() != null) return;

			DebugLog.Write($"AddStack: {name}");

			AddStackable(__instance, 100f);
		}

		public static void Postfix(GearItem __instance)
		{
			if (__instance == null) return;
			if (GameManager.IsMainMenuActive()) return;
			if (string.IsNullOrWhiteSpace(__instance.name)) return;
			if (Main.Config == null) return;

			string name = CommonUtilities.NormalizeName(__instance.name) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name)) return;

			// Runs before the component is added below, so the line reports what the game actually shipped
			DiagnoseFood(__instance, name);

			AddStackableToFoodLike(__instance, name);

			// A gear item the game copied - one unit split off a stack, a container transfer - carries a copy of the
			// component this mod added. That copy is a different component instance which no registry knows, so it is
			// taken over here, otherwise a dropped bag would still merge with full ones.
			StackContentSync.Adopt(__instance, "copied gear item");

			if (Main.Config.STACK_MERGE.Contains(name))
			{
				StackableItem? item = __instance.gameObject.GetComponent<StackableItem>();
				if (item != null) item.m_StackConditionDifferenceConstraint = 100f;
			}

			// These items are pinned to a fixed condition so a stack never splits on durability again
			switch (name)
			{
				case "GEAR_CoffeeTin":			LockCondition(__instance, 1f);		break;
				case "GEAR_GreenTeaPackage":	LockCondition(__instance, 1f);		break;
				case "GEAR_Carrot":				LockCondition(__instance, 0.5f);	break;
				case "GEAR_Potato":				LockCondition(__instance, 1f);		break;
			}
		}

		/// <summary>
		/// Gives food and the configured cooking ingredients a stackable component, so full units of the same item can merge.
		/// </summary>
		/// <remarks>
		/// Most of them ship without a <see cref="StackableItem"/>, which is why the vanilla merge entry points never find
		/// anything to merge and the game silently keeps every portion separate. Adding the component is what actually
		/// makes them stackable; keeping partially used units out is handled separately by <see cref="FoodStackingRules"/>.
		/// </remarks>
		private static void AddStackableToFoodLike(GearItem gi, string name)
		{
			if (!FoodStackingRules.IsEnabled) return;
			if (gi.m_StackableItem != null) return;
			if (gi.gameObject.GetComponent<StackableItem>() != null) return;
			if (!FoodStackingRules.CanAddComponent(gi, name)) return;

			// Only a complete unit may join a stack. Half eaten pieces have a lower weight than the prefab, and the
			// crafting system converts a weight requirement into whole units, so handing them a stackable component
			// makes a recipe eat more pieces than it shows. They keep the vanilla behaviour instead.
			if (!FoodStackingRules.IsFullUnit(gi)) return;

			// Food condition is freshness, so two units only merge when they aged the same. Ingredients carry no
			// freshness worth preserving, so they are free to merge regardless of condition.
			float constraint = gi.m_FoodItem == null ? NoConditionDifferenceConstraint : FoodConditionDifferenceConstraint;

			DebugLog.Write($"AddStack FOOD: {name} ({DescribeContent(gi)})");

			AddStackable(gi, constraint);
		}

		/// <summary>
		/// What the item holds the moment it is given a component, which is the value a baseline used to be taken from.
		/// </summary>
		/// <remarks>
		/// Kept in the log because a cooked item does not hold its final amount yet when it appears: a pie comes out at
		/// the calorie value of the raw one and is raised a moment later. Seeing the spawn value next to the value the
		/// repair reads is what tells those two apart.
		/// </remarks>
		private static string DescribeContent(GearItem gi)
		{
			FoodItem? food = gi.m_FoodItem;
			if (food != null) return $"cal={food.m_CaloriesRemaining:F0}/{food.m_CaloriesTotal:F0} units={UnitsOf(gi)}";

			LiquidItem? liquid = gi.m_LiquidItem;
			if (liquid != null) return $"filled={FoodStackingRules.FillRatio(liquid):P0} units={UnitsOf(gi)}";

			return $"units={UnitsOf(gi)}";
		}

		private static int UnitsOf(GearItem gi)
		{
			StackableItem? stack = gi.m_StackableItem;

			return stack == null ? 1 : stack.m_Units;
		}

		private static void AddStackable(GearItem gi, float conditionDifferenceConstraint)
		{
			StackableItem stack = gi.gameObject.AddComponent<StackableItem>();

			stack.m_DefaultUnitsInItem					= 1;
			stack.m_StackConditionDifferenceConstraint	= conditionDifferenceConstraint;
			stack.m_StackSpriteName						= string.Empty;
			stack.m_ShareStackWithGear					= Array.Empty<StackableItem>();

			if (stack.m_Units == 0) stack.m_Units = 1;

			gi.m_StackableItem = stack;

			AddedStackables.Register(stack);

			// Recorded now, because a gear item that has just awoken still carries exactly one unit worth of
			// everything. That number is what lets a stack be rebuilt after something ate part of it.
			StackContentSync.RecordBaseline(gi);
		}

		/// <summary>
		/// One line per food prefab describing how its data is actually laid out, which decides whether it can stack.
		/// </summary>
		/// <remarks>
		/// Also covers the configured cooking ingredients, which carry no <see cref="FoodItem"/> at all and would
		/// otherwise leave no trace in the log, making a non stacking ingredient impossible to tell apart from one that
		/// was never spawned.
		/// </remarks>
		private static void DiagnoseFood(GearItem gi, string name)
		{
			if (gi.m_FoodItem == null && gi.m_LiquidItem == null && !FoodStackingRules.CanAddComponent(gi, name)) return;
			if (!s_DiagnosedFoods.Add(name)) return;

			FoodItem? food = gi.m_FoodItem;
			LiquidItem? liquid = gi.m_LiquidItem;

			string calories = food == null ? "none" : $"{food.m_CaloriesRemaining:F1}/{food.m_CaloriesTotal:F1}";
			string volume = liquid == null ? "none" : FoodStackingRules.FillRatio(liquid).ToString("P0");
			bool foodWeight = gi.gameObject.GetComponent<FoodWeight>() != null;
			bool stackable = gi.m_StackableItem != null || gi.gameObject.GetComponent<StackableItem>() != null;
			bool consumeAll = food != null && food.m_MustConsumeAll;

			DebugLog.Write($"FoodDiag: {gi.name} calories={calories} volume={volume} foodWeight={foodWeight} "
				+ $"stackable={stackable} consumeAll={consumeAll} partial={FoodStackingRules.IsPartialUnit(gi)}");
		}

		/// <summary>
		/// Halts decay and pins the item at a fraction of its own MaxHP.
		/// </summary>
		/// <remarks>
		/// GearItem.CurrentHP is an absolute value, not a 0-1 ratio (GetNormalizedCondition() divides it by MaxHP).
		/// Hardcoding the absolute numbers locks the item to a value that silently becomes wrong as soon as the
		/// gear data changes, so always derive it from MaxHP.
		/// </remarks>
		private static void LockCondition(GearItem gi, float normalizedCondition)
		{
			if (gi.m_GearItemData == null) return;

			float maxHP = gi.m_GearItemData.MaxHP;

			if (maxHP <= 0f)
			{
				DebugLog.Write($"LockCondition::MaxHP is {maxHP} for {gi.name}, skipping");
				return;
			}

			gi.SetHaltDecay(true);
			gi.CurrentHP = Mathf.Clamp(normalizedCondition * maxHP, 0f, maxHP);
		}
	}

	public static class Extensions
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static LocalizedString CreateLocalizedString(string name)
		{
			return new LocalizedString()
			{
				m_LocalizationID = name
			};
		}
	}
}
