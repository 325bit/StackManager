using System.Text.RegularExpressions;

using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;

namespace StackManager.Patches
{
	/// <summary>
	/// Makes a saved stack load with both its unit count and the contents those units stand for.
	/// </summary>
	/// <remarks>
	/// The unit count of every gear item lives in <c>m_StackableItemSerialized</c>, while a powder or a liquid keeps
	/// the amount it holds in its own proxy. The two are written independently, so a save read back without care can
	/// end up with a count of three units and the weight of one, which is exactly how cooking ends up consuming the
	/// wrong amount.
	/// The prefix below records what the prefab shipped with and marks the load as in progress, so the unit count setter
	/// does not rescale the content a second time. The postfix then restores a missing component, expands a stack saved
	/// by an older build (whose content still described a single unit) and remembers the count the content belongs to.
	/// </remarks>
	[HarmonyPatch(typeof(GearItem), nameof(GearItem.Deserialize), [typeof(GearItemSaveDataProxy), typeof(bool)])]
	public class GearItem_Deserialize
	{
		/// <summary>Matches the unit count in the serialized proxy, whatever escaping depth the save used</summary>
		private static readonly Regex s_UnitsPattern = new("m_UnitsProxy\\W{0,8}(\\d+)", RegexOptions.Compiled);

		/// <summary>Amount the prefab itself holds, read before the save overwrites it</summary>
		private static float s_PrefabPowderKg;
		private static bool s_HadPrefabPowder;

		private static float s_PrefabLiquidLiters;
		private static bool s_HadPrefabLiquid;

		public static void Prefix(GearItem __instance)
		{
			StackContentSync.BeginLoad();

			s_HadPrefabPowder = false;
			s_HadPrefabLiquid = false;

			if (__instance == null) return;

			PowderItem? powder = __instance.m_PowderItem;
			if (powder != null)
			{
				s_PrefabPowderKg = StackContentSync.ToKilograms(powder.m_Weight);
				s_HadPrefabPowder = true;
			}

			LiquidItem? liquid = __instance.m_LiquidItem;
			if (liquid != null)
			{
				s_PrefabLiquidLiters = StackContentSync.ToLiters(liquid.m_Liquid);
				s_HadPrefabLiquid = true;
			}
		}

		public static void Postfix(GearItem __instance, GearItemSaveDataProxy __0)
		{
			try
			{
				if (__instance == null) return;

				int savedUnits = __0 == null ? 0 : ReadUnits(__0.m_StackableItemSerialized);

				StackableItem? stack = __instance.m_StackableItem;

				// Only a saved stack needs its units restored; a single unit has nothing to restore but may still need to
				// be cleaned up below, so the early return must not swallow that case.
				if (savedUnits > 1 && stack == null)
				{
					stack = __instance.gameObject.GetComponent<StackableItem>();

					if (stack == null)
					{
						stack = __instance.gameObject.AddComponent<StackableItem>();

						stack.m_DefaultUnitsInItem = 1;
						stack.m_StackConditionDifferenceConstraint = 100f;
						stack.m_StackSpriteName = string.Empty;
						stack.m_ShareStackWithGear = Array.Empty<StackableItem>();

						AddedStackables.Register(stack);

						DebugLog.Write($"Restored a missing stackable component on {__instance.name}, the saved count of "
							+ $"{savedUnits} units had nothing to load into");
					}

					__instance.m_StackableItem = stack;
				}

				if (stack == null) return;

				if (savedUnits > 1 && stack.m_Units != savedUnits)
				{
					DebugLog.Write($"Restored {savedUnits} units on {__instance.name} (was {stack.m_Units})");

					stack.m_Units = savedUnits;
				}

				// Saves written before contents were rescaled hold the amount of a single unit, so a stack of three
				// bags would load as three units weighing one bag
				ExpandLegacyContent(__instance, stack);

				// The container limits are not saved, so a stack of N units comes back with the room of one. The game
				// then believes the item is over full and refuses to add to it, so the limits are widened to match.
				EnsureCapacity(__instance, stack);

				StripFromPartialUnit(__instance, stack);

				// What was just read is the total for this stack, so it must not be scaled again
				StackContentSync.MarkLoaded(__instance);
			}
			finally
			{
				StackContentSync.EndLoad();
			}
		}

		/// <summary>
		/// Turns a stack saved with a single unit's content into one that holds the amount of every unit in it.
		/// </summary>
		/// <remarks>
		/// The content is only expanded when it still matches the prefab exactly. Anything else has already been
		/// rescaled by this build, or has been partly used, and must be left alone.
		/// </remarks>
		private static void ExpandLegacyContent(GearItem gi, StackableItem stack)
		{
			if (stack.m_Units <= 1) return;
			if (!AddedStackables.Contains(stack)) return;

			int units = stack.m_Units;

			if (s_HadPrefabPowder && gi.m_PowderItem != null)
			{
				float stored = StackContentSync.ToKilograms(gi.m_PowderItem.m_Weight);

				if (Math.Abs(stored - s_PrefabPowderKg) > 1e-4f) return;

				float total = stored * units;

				gi.m_PowderItem.m_Weight = StackContentSync.FromKilograms(total);

				if (StackContentSync.ToKilograms(gi.m_PowderItem.m_WeightLimit) < total)
				{
					gi.m_PowderItem.m_WeightLimit = StackContentSync.FromKilograms(total);
				}

				Main.Logger.Log($"Stack content: {gi.name} loaded {units} units with a single unit of powder, "
					+ $"restored {stored:F3} -> {total:F3} kg", FlaggedLoggingLevel.Always);

				return;
			}

			if (s_HadPrefabLiquid && gi.m_LiquidItem != null)
			{
				float stored = StackContentSync.ToLiters(gi.m_LiquidItem.m_Liquid);

				// Only a stack that still holds exactly one unit's worth needs expanding
				if (Math.Abs(stored - s_PrefabLiquidLiters) > 1e-4f) return;

				float total = stored * units;

				gi.m_LiquidItem.m_Liquid = ItemLiquidVolume.FromLiters(total);

				if (StackContentSync.ToLiters(gi.m_LiquidItem.m_LiquidCapacity) < total)
				{
					gi.m_LiquidItem.m_LiquidCapacity = ItemLiquidVolume.FromLiters(total);
				}

				Main.Logger.Log($"Stack content: {gi.name} loaded {units} units with a single unit of liquid, "
					+ $"restored {stored:F2} -> {total:F2} L", FlaggedLoggingLevel.Always);
			}
		}

		/// <summary>
		/// Widens the powder weight limit or liquid capacity to fit what the stack actually holds.
		/// </summary>
		/// <remarks>
		/// Neither limit is part of the save: they come from the prefab and therefore describe a single unit. A stack
		/// restored with the content of several units would look over full to the game, which then refuses to use or
		/// extend it.
		/// </remarks>
		private static void EnsureCapacity(GearItem gi, StackableItem stack)
		{
			if (stack.m_Units <= 1) return;
			if (!AddedStackables.Contains(stack)) return;

			PowderItem? powder = gi.m_PowderItem;
			if (powder != null)
			{
				float content = StackContentSync.ToKilograms(powder.m_Weight);

				if (StackContentSync.ToKilograms(powder.m_WeightLimit) < content)
				{
					powder.m_WeightLimit = StackContentSync.FromKilograms(content);
				}
			}

			LiquidItem? liquid = gi.m_LiquidItem;
			if (liquid != null)
			{
				float volume = StackContentSync.ToLiters(liquid.m_Liquid);

				if (StackContentSync.ToLiters(liquid.m_LiquidCapacity) < volume)
				{
					liquid.m_LiquidCapacity = ItemLiquidVolume.FromLiters(volume);
				}
			}
		}

		/// <summary>
		/// Removes the component from a single unit that is no longer complete.
		/// </summary>
		/// <remarks>
		/// The component is only added to complete units, but an item can decay below that point while it is already in
		/// a stack of one. Leaving it attached changes how the crafting system consumes the item, so the unit goes back
		/// to the vanilla path the next time it is loaded.
		/// </remarks>
		private static void StripFromPartialUnit(GearItem gi, StackableItem stack)
		{
			if (stack.m_Units > 1) return;
			if (!AddedStackables.Contains(stack)) return;
			if (FoodStackingRules.IsFullUnit(gi)) return;

			DropComponent(gi, stack);

			DebugLog.Write($"Unstackable {gi.name}: it is no longer a complete unit");
		}

		private static void DropComponent(GearItem gi, StackableItem stack)
		{
			AddedStackables.TryUnregister(stack);
			StackContentSync.Forget(stack);

			gi.m_StackableItem = null;
			UnityEngine.Object.Destroy(stack);
		}

		private static int ReadUnits(string? serialized)
		{
			if (string.IsNullOrWhiteSpace(serialized)) return 0;

			Match match = s_UnitsPattern.Match(serialized);

			if (!match.Success) return 0;
			if (!int.TryParse(match.Groups[1].Value, out int units)) return 0;

			return units;
		}
	}
}
