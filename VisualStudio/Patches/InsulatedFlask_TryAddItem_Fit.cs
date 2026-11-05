using StackManager.Utilities;

namespace StackManager.Patches
{
	/// <summary>
	/// Lets a stack of liquid into an insulated flask bottle by bottle instead of all at once or not at all.
	/// </summary>
	/// <remarks>
	/// The flask compares its own volume plus the volume of the item against its capacity before it takes any of
	/// it, so a stack that carries more than the room left is turned away outright - and a stack of ours carries
	/// every unit in it at once, which is what makes five bottles of cooking oil unable to enter a one litre flask
	/// that would take one of them. <see cref="FlaskFit"/> cuts the stack down to the units that fit before the
	/// flask's own check runs, so the check passes for those and the rest stays in the bag.
	/// The counters behind the flask are recalculated by the flask itself as part of taking the item, and the redraw
	/// that shows them is asked for from there, so the split only has to make sure the units that did not fit are
	/// handed back.
	/// </remarks>
	[HarmonyPatch(typeof(Il2CppTLD.Gear.InsulatedFlask), nameof(Il2CppTLD.Gear.InsulatedFlask.TryAddItem))]
	internal class InsulatedFlask_TryAddItem_Fit
	{
		/// <remarks>
		/// Everything is inside a try: the count is changed here, and a throw halfway would leave the stack short of
		/// units with no copy holding them.
		/// </remarks>
		public static void Prefix(Il2CppTLD.Gear.InsulatedFlask __instance, GearItem __0, out FlaskFit.Pending __state)
		{
			__state = default;

			try
			{
				__state = FlaskFit.Prepare(__instance, __0);
			}
			catch (Exception e)
			{
				__state = default;

				DebugLog.Write($"Flask fit: split skipped, {e.Message}");
			}
		}

		/// <remarks>
		/// The leftover is only handed back here, once the flask has actually taken the stack. The bag cannot tell
		/// whether it did - the caller only takes the stack out of it after this returns - so the answer the flask
		/// itself gave is what decides.
		/// </remarks>
		public static void Postfix(bool __result, FlaskFit.Pending __state)
		{
			try
			{
				if (!FlaskFit.Finish(__state, __result)) return;

				// The list on screen was built before the leftover existed, so it needs a redraw to show
				FlaskView.Request();
			}
			catch (Exception e)
			{
				DebugLog.Write($"Flask fit: leftover not handed back, {e.Message}");
			}
		}
	}
}
