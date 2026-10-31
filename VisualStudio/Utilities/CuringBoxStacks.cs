namespace StackManager.Utilities
{
	/// <summary>
	/// Carries a stack's unit count through a curing box, which has nowhere of its own to keep one.
	/// </summary>
	/// <remarks>
	/// A curing box saves a piece as a prefab, a condition and a remaining fraction - there is no unit count among
	/// them, so a stack of twenty pieces was written as one piece and came back as one, and the other nineteen were
	/// gone. The fraction is the only number the box writes and reads back for itself, and a stack here only ever
	/// holds whole units, so it carries the count: N units are stored as N - 1 + fraction, which reads back as N
	/// units holding that fraction each. Writing a value that has already been written leaves it exactly as it is,
	/// so the number survives a save being written a second time.
	/// </remarks>
	internal static class CuringBoxStacks
	{
		/// <summary>A stored value may sit this far above a whole unit before it counts as more than one</summary>
		private const float Snap = 1e-4f;

		/// <summary>
		/// Writes the unit count of the item that was just put into a box into the fraction the box saves for it.
		/// </summary>
		/// <remarks>
		/// Done as the item goes in, because the box rebuilds its contents from that fraction twice: once when a save
		/// is loaded, and once more when a piece finishes curing and is replaced by the cured one. A count that only
		/// lived in memory would be lost at both, and the second one costs the player every extra piece.
		/// </remarks>
		public static void Write(CuringStorageState? state, GearItem? gear)
		{
			if (state == null || gear == null) return;

			StackableItem? stack = gear.m_StackableItem;
			if (stack == null || stack.m_Units < 2) return;

			Il2CppSystem.Collections.Generic.List<CuringStorageState.CuringItem>? items = state.m_CuringItems;
			if (items == null) return;

			int id = gear.GetInstanceID();

			for (int i = items.Count - 1; i >= 0; i--)
			{
				CuringStorageState.CuringItem? entry = items[i];
				if (entry == null) continue;

				GearItem? stored = entry.m_InstantiatedItem;
				if (stored == null) continue;
				if (stored.GetInstanceID() != id) continue;

				float fraction = Mathf.Clamp01(entry.m_RemainingFraction);

				entry.m_RemainingFraction = stack.m_Units - 1 + fraction;

				DebugLog.Write($"Curing box: {gear.name} stored as {stack.m_Units} units of {fraction:P0}");

				return;
			}
		}

		/// <summary>
		/// Turns a stored fraction back into a unit count, and narrows the fraction to what a single unit holds.
		/// </summary>
		/// <remarks>
		/// A box saved before this build carries a plain fraction of one or less, which reads as the single unit it
		/// was; that is also what a fraction computed by the box itself looks like, so nothing has to be versioned.
		/// </remarks>
		public static int Read(ref float fraction)
		{
			if (float.IsNaN(fraction)) fraction = 1f;

			if (fraction <= 1f + Snap)
			{
				fraction = Mathf.Clamp(fraction, 0f, 1f);

				return 1;
			}

			int units = Mathf.CeilToInt(fraction - Snap);
			if (units < 1) units = 1;

			float perUnit = fraction - (units - 1);

			fraction = Mathf.Clamp(perUnit, 0.01f, 1f);

			return units;
		}

		/// <summary>Gives an item the box has just rebuilt the unit count it was saved with.</summary>
		/// <remarks>
		/// Nothing else has to be scaled: food carries the amount of one unit and the game multiplies that by this
		/// count itself, so setting the count is the whole repair.
		/// </remarks>
		public static void Restore(GearItem? gear, int units)
		{
			if (gear == null || units < 2) return;

			StackableItem? stack = gear.m_StackableItem;

			if (stack == null)
			{
				// Only reachable for an item this mod does not stack, which means the box had nothing to carry
				// either; reported rather than dropped silently, so a wrong assumption shows up in the log
				Main.Logger.Log($"Curing box: {gear.name} was saved with {units} units but carries no stackable "
					+ "component, the extra units cannot be restored", FlaggedLoggingLevel.Always);

				return;
			}

			stack.m_Units = units;

			DebugLog.Write($"Curing box: {gear.name} rebuilt as {units} units");
		}
	}
}
