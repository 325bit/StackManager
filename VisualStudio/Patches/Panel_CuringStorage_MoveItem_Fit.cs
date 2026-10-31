namespace StackManager.Patches
{
	/// <summary>
	/// Lets a stack into a curing box piece by piece instead of all at once or not at all.
	/// </summary>
	/// <remarks>
	/// The box weighs an item as a whole before it takes any of it, so a stack that is heavier than the space left
	/// is refused outright - and a stack of ours reports every unit at once, which is what made twenty one pieces of
	/// kilo meat unable to enter an empty twenty kilo box. <see cref="CuringBoxFit"/> cuts the stack down to the
	/// units that fit before the game's own check runs, so the check passes for those and the rest stays in the bag.
	/// Only the bag to box direction is handled: the check this works around does not exist for the other one, and a
	/// move out of the box must keep taking the whole stack it was asked for.
	/// </remarks>
	[HarmonyPatch(typeof(Panel_CuringStorage), nameof(Panel_CuringStorage.MoveItem))]
	internal class Panel_CuringStorage_MoveItem_Fit
	{
		/// <remarks>
		/// Everything is inside a try: the count is changed here, and a throw halfway would leave the stack short of
		/// units with no copy holding them.
		/// </remarks>
		public static void Prefix(Panel_CuringStorage __instance, out CuringBoxFit.Pending __state)
		{
			__state = default;

			try
			{
				if (__instance == null) return;

				// The capacity check only runs for a move out of the bag, so this never touches the other direction
				if (!__instance.IsInventoryTableSelected) return;

				__state = CuringBoxFit.Prepare(__instance.m_CuringStorage, __instance.GetSelectedGearItem());
			}
			catch (Exception e)
			{
				__state = default;

				DebugLog.Write($"Curing box fit: split skipped, {e.Message}");
			}
		}

		/// <remarks>
		/// The leftover is only handed back here, once the game has actually put the stack in the box. Handing it
		/// over before the move would put it back into the bag it came from, where it would merge with the original.
		/// </remarks>
		public static void Postfix(Panel_CuringStorage __instance, CuringBoxFit.Pending __state)
		{
			try
			{
				if (!CuringBoxFit.Finish(__state)) return;
				if (__instance == null) return;

				// The bags on screen were built from the stack that just left, so the leftover needs a redraw to show
				__instance.ForceRefreshTables();
			}
			catch (Exception e)
			{
				DebugLog.Write($"Curing box fit: leftover not handed back, {e.Message}");
			}
		}
	}
}
