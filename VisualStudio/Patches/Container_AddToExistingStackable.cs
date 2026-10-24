namespace StackManager.Patches
{
	/* Logic:
		The game calls this when an item is about to be merged into a stack that already lives in a container.
		Vanilla handles everything unless the item has no StackableItem of its own yet, in which case the
		custom merge below takes a shot at it.
	*/
	[HarmonyPatch(typeof(Container))]
	[HarmonyPatch(nameof(Container.AddToExistingStackable))]
	[HarmonyPatch([typeof(GearItem), typeof(float), typeof(int)])]
	public class Container_AddToExistingStackable
	{
		public static bool Prefix(ref GearItem gearToAdd, float normalizedCondition, int numUnits, ref bool __result)
		{
			// A partially eaten unit has to stay on its own, see FoodStackingRules for why
			if (FoodStackingRules.KeepsOutOfStack(gearToAdd))
			{
				// Reporting false means "no existing stack accepted this item", so the caller adds it as a new stack
				__result = false;
				return false;
			}

			if (gearToAdd.gameObject.GetComponent<StackableItem>() != null) return true;
			return StackingUtilities.Do(gearToAdd, normalizedCondition, numUnits);
		}

		/// <summary>
		/// Brings the content of every stack in this container back in line with its unit count.
		/// </summary>
		/// <remarks>
		/// The merge itself happens inside the game, which raises the unit count without calling anything managed, so a
		/// postfix on the entry point is the only moment where this mod learns that a stack grew.
		/// </remarks>
		public static void Postfix(Container __instance)
		{
			StackContentSync.SyncContainer(__instance);
		}
	}
}
