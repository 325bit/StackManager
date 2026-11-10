namespace StackManager.Patches
{
	/// <summary>
	/// Reports the burn time of one unit instead of the whole stack while a feed-fire action is running.
	/// </summary>
	/// <remarks>
	/// Measured on 2.55 with the v1.3.11 diagnostics: a stack of three animal fat (prefab burn 1.00 h, condition
	/// 1.00) returned 6.00 h from this method, two units returned 4.00 h, and Fire.AddFuel - which sees the count
	/// already flattened to one - added exactly 7200 s. The multiplier that grows with StackableItem.m_Units is
	/// inlined here, which is why the disassembly shows no call to get_StackMultiplier.
	/// The panel checks its 12-hour cap against this value before Fire.AddFuel is ever reached, so a large stack
	/// was refused outright even though feeding it one unit at a time is legal.
	/// The normalisation is limited to the feed-fire window on purpose: Container.GetHoursFuel also calls this
	/// method and there the whole stack really is what the total should be. Fire.TurnOn and
	/// Panel_FireStart.RefreshEstimatedDurationLabel stay outside the window and keep the vanilla behaviour.
	/// </remarks>
	[HarmonyPatch(typeof(FuelSourceItem), nameof(FuelSourceItem.GetModifiedBurnDurationHours))]
	internal class FuelSourceItem_BurnDuration_Stack
	{
		public static void Postfix(FuelSourceItem __instance, ref float __result)
		{
			if (!FuelStacking.InFeedFire) return;

			try
			{
				GearItem? gi = __instance.GetComponent<GearItem>();

				StackableItem? stack = gi?.m_StackableItem;
				if (stack == null || !AddedStackables.Contains(stack)) return;

				int units = stack.m_Units;
				if (units <= 1) return;

				float whole = __result;
				__result /= units;

				DebugLog.Write($"Fire: burn duration {whole:F2}h covered {units} units, reporting {__result:F2}h");
			}
			catch (Exception e)
			{
				DebugLog.Write($"Fire: burn duration normalisation skipped, {e.Message}");
			}
		}
	}
}
