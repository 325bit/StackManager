using StackManager.Utilities;

namespace StackManager.Patches
{
	/* Logic:
		The game calls this when an item is about to be merged into a stack that already lives in the player inventory.
	*/
	[HarmonyPatch(typeof(PlayerManager))]
	[HarmonyPatch(nameof(PlayerManager.TryAddToExistingStackable))]
	[HarmonyPatch([typeof(GearItem), typeof(float), typeof(int), typeof(GearItem)], [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out])]
	public class PlayerManager_TryAddToExistingStackable
	{
		/// <summary>
		/// The fourth parameter is <c>out GearItem</c> in the game's signature, not <c>ref</c>, so it is flagged as
		/// <see cref="ArgumentType.Out"/>. Declaring it as Ref made Harmony read a value that the caller never wrote.
		/// </summary>
		public static bool Prefix(ref GearItem gearToAdd, float normalizedCondition, int numUnits, ref GearItem? existingGearItem, out StackContentSync.MergePart __state, ref bool __result)
		{
			__state = default;

			if (GameManager.IsMainMenuActive()) return true;
			if (gearToAdd == null) return true;

			// A craft creates a temporary item to carry its progress and destroys that item when the craft finishes.
			// When a prefab merges into an existing stack, the game returns the whole stack as the item it just
			// created, so the temporary object and the player's stack become the same thing and the whole stack is
			// destroyed. Keeping the preview as a separate instance costs one short-lived object and leaves the
			// player's stack intact; the real result is still free to merge from inside the success path.
			if (CraftDeliveryGuard.IsPreviewDelivery)
			{
				existingGearItem = null;
				__result = false;
				return false;
			}

			// A partially eaten unit has to stay on its own, see FoodStackingRules for why
			if (FoodStackingRules.KeepsOutOfStack(gearToAdd))
			{
				// No stack accepted it, so the caller falls back to adding it as a new stack
				existingGearItem = null;
				__result = false;
				return false;
			}

			if (gearToAdd.gameObject.GetComponent<StackableItem>() != null)
			{
				// Vanilla path. The game will raise the unit count of whichever stack it picks and leave the amount
				// where it was, so what this item carries is read now and poured in by the Postfix below.
				__state = StackContentSync.CaptureMerge(gearToAdd, numUnits);

				return true;
			}

			bool runVanilla = StackingUtilities.Do(gearToAdd, normalizedCondition, numUnits, ref existingGearItem);

			// Skipping the original leaves __result at default(bool) = false, which tells the caller the item was not
			// stacked even though it was. Report the success this call handled instead.
			if (!runVanilla) __result = true;

			return runVanilla;
		}

		/// <summary>
		/// Brings the content of the stack that accepted the item back in line with its unit count.
		/// </summary>
		/// <remarks>
		/// A vanilla merge only adds to <c>StackableItem.m_Units</c>; nothing moves the amount across. For food and
		/// plain gear that is invisible, because one unit's worth is multiplied by the count, but a powder or a liquid
		/// holds the total of the stack in one field, so the amount the joining item carried has to be added here.
		/// Without it the receiving stack kept the amount it had before the merge, and the repair that reads the count
		/// back out of that amount scaled it by the new count instead - a bag with two thirds of its salt left that
		/// took a stack of seven came out holding five and a third bags' worth instead of seven and two thirds.
		/// </remarks>
		public static void Postfix(ref GearItem? __3, StackContentSync.MergePart __state)
		{
			try
			{
				StackContentSync.ApplyMerge(__3, __state);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Stack content: merged amount not added, {e.Message}");
			}

			StackContentSync.EnsureSynced(__3, "inventory merge");

			// A stack that was a part of a unit when it took these units in can only describe them by lowering its
			// count again: the merge raised the count and left the amount where it was
			StackContentSync.Settle(__3, "units merged into a part");
		}
	}
}
