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
		public static bool Prefix(ref GearItem gearToAdd, float normalizedCondition, int numUnits, ref GearItem? existingGearItem, ref bool __result)
		{
			if (GameManager.IsMainMenuActive()) return true;
			if (gearToAdd == null) return true;

			// A partially eaten unit has to stay on its own, see FoodStackingRules for why
			if (FoodStackingRules.KeepsOutOfStack(gearToAdd))
			{
				// No stack accepted it, so the caller falls back to adding it as a new stack
				existingGearItem = null;
				__result = false;
				return false;
			}

			if (gearToAdd.gameObject.GetComponent<StackableItem>() != null) return true;

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
		/// A vanilla merge raises the count inside the game without calling anything managed, so the content the stack
		/// shares would stay where it was: a stack of three bags would still weigh one bag.
		/// </remarks>
		public static void Postfix(ref GearItem? __3)
		{
			StackContentSync.EnsureSynced(__3, "inventory merge");
		}
	}
}
