namespace StackManager.Utilities
{
	/// <summary>
	/// Carries the values written onto one caught fish over to the copy a stack split makes.
	/// </summary>
	/// <remarks>
	/// Splitting a stack does not clone the stack. The game loads the prefab through the addressable asset loader,
	/// instantiates that, and only then writes the new unit count onto the copy. Anything the save had written on the
	/// original - the weight a fish was given when it was caught, and the calories derived from that weight - is
	/// replaced by the prefab's fixed values. Food with a fixed weight is unaffected because its prefab and instance
	/// values are identical, so only randomised fish show the loss. Vanilla fish have no StackableItem and never take
	/// this path; making food stackable is what exposes it.
	/// Do not rebuild the copy's bookkeeping before these writes: the track recorded in Awake still describes the
	/// prefab values, and the first merge into the copy would scale against the wrong unit size.
	/// </remarks>
	internal static class SplitInstanceValues
	{
		private const float WeightEpsilon = 0.0005f;
		private const float CalorieEpsilon = 0.01f;

		public static void Carry(GearItem? source, GearItem? copy, string reason)
		{
			if (source == null || copy == null || copy == source) return;
			if (StackContentSync.IsLoading) return;

			// Only stacks this mod created are eligible. A prefab handed to a craft delivery has no registered
			// component, and vanilla stackable gear has no instance-specific weight to preserve.
			StackableItem? sourceStack = source.m_StackableItem;
			if (sourceStack == null || !AddedStackables.Contains(sourceStack)) return;
			if (copy.m_StackableItem == null) return;

			FoodItem? sourceFood = source.m_FoodItem;
			FoodItem? copyFood = copy.m_FoodItem;
			if (sourceFood == null || copyFood == null) return;

			float sourceWeight = StackContentSync.ToKilograms(source.WeightKG);
			float copyWeight = StackContentSync.ToKilograms(copy.WeightKG);
			float sourceTotal = sourceFood.m_CaloriesTotal;
			float sourceRemaining = sourceFood.m_CaloriesRemaining;
			float copyTotal = copyFood.m_CaloriesTotal;

			bool weightDiffers = Mathf.Abs(sourceWeight - copyWeight) > WeightEpsilon;
			bool caloriesDiffer = Mathf.Abs(sourceTotal - copyTotal) > CalorieEpsilon
				|| Mathf.Abs(sourceRemaining - copyFood.m_CaloriesRemaining) > CalorieEpsilon;

			if (!weightDiffers && !caloriesDiffer) return;

			copy.WeightKG = source.WeightKG;
			copyFood.m_CaloriesTotal = sourceTotal;
			copyFood.m_CaloriesRemaining = sourceRemaining;

			// Rebuilds the track from the values that are now on the copy, so the next merge scales correctly.
			StackContentSync.Adopt(copy, reason);

			string name = CommonUtilities.NormalizeName(source.name) ?? source.name ?? "?";
			DebugLog.Write($"Split copy: {name} kept {sourceWeight:F3} kg / {sourceTotal:F1} cal "
				+ $"instead of the prefab's {copyWeight:F3} kg / {copyTotal:F1} cal ({reason})");
		}
	}
}
