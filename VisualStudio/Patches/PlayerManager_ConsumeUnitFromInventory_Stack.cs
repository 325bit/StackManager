namespace StackManager.Patches
{
	/// <summary>
	/// Removes exactly one unit of a stack this mod created when the fire panel consumes its fuel.
	/// </summary>
	/// <remarks>
	/// The vanilla method writes StackableItem.m_Units directly in native code, so neither the unit-count setter nor
	/// Inventory.RemoveUnits ever sees the change. Doing the removal here keeps the contents in step with the count.
	/// It also keeps the last unit out of the way: with a single unit left the original method destroys the gear
	/// item, which is the correct outcome.
	/// </remarks>
	[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ConsumeUnitFromInventory))]
	internal class PlayerManager_ConsumeUnitFromInventory_Stack
	{
		public static bool Prefix(GameObject __0)
		{
			if (!FuelStacking.InFeedFire) return true;
			if (__0 == null) return true;

			GearItem? gi = FuelStacking.OwnStack(__0.GetComponent<GearItem>());
			if (gi == null) return true;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null || stack.m_Units <= 1) return true;

			int before = stack.m_Units;

			// Resize moves the amount that belongs to the removed unit as well as the count. Plain gear has no
			// amount to move, so its count is simply lowered.
			if (!StackContentSync.Resize(gi, before - 1, "fuel burned"))
			{
				stack.m_Units = before - 1;
			}

			DebugLog.Write($"Fire: burned one unit of {gi.name}, {before} -> {stack.m_Units}");

			return false;
		}
	}
}
