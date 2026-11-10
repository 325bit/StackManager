namespace StackManager.Utilities
{
	/// <summary>
	/// Coordinates the hooks that make a stack created by this mod behave as one piece of fuel.
	/// </summary>
	/// <remarks>
	/// Panel_FeedFire.OnFeedFire hands the selected GearItem to Fire.AddFuel while it still carries every unit, and
	/// only afterwards asks PlayerManager.ConsumeUnitFromInventory to remove one. The hooks flatten the unit count
	/// for the duration of AddFuel and take one unit away themselves, so the fire never sees a whole stack and the
	/// item that fed it is never destroyed. Nothing here changes an item the game or another mod made stackable.
	/// </remarks>
	internal static class FuelStacking
	{
		/// <summary>True from the start of OnFeedFire until it returns.</summary>
		internal static bool InFeedFire { get; set; }

		/// <summary>
		/// True while the count is flattened, so the unit-count hook does not repair the stack in the middle of it.
		/// </summary>
		internal static bool IsFlattening { get; set; }

		/// <summary>Returns the gear item only when its stackable component was created by this mod.</summary>
		internal static GearItem? OwnStack(GearItem? gi)
		{
			if (gi == null) return null;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null) return null;
			if (!AddedStackables.Contains(stack)) return null;

			return gi;
		}

		/// <summary>One structured line describing the fuel the game is about to burn.</summary>
		internal static string Describe(GearItem gi)
		{
			StackableItem? stack = gi.m_StackableItem;
			FuelSourceItem? fuel = gi.m_FuelSourceItem;
			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? "?";
			float condition = gi.GetNormalizedCondition();

			float own = StackContentSync.ToKilograms(gi.WeightKG);
			float reported = StackContentSync.ToKilograms(gi.GetItemWeightKG(false));
			float burn = fuel == null ? 0f : fuel.m_BurnDurationHours;
			float modified = fuel == null ? 0f : fuel.GetModifiedBurnDurationHours(condition);

			return $"{name} units={stack?.m_Units ?? 1} default={stack?.m_DefaultUnitsInItem ?? 1} "
				+ $"own={own:F2}kg reported={reported:F2}kg condition={condition:F2} "
				+ $"burn={burn:F2}h modified={modified:F2}h";
		}
	}
}
