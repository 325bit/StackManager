namespace StackManager.Patches
{
	/// <summary>
	/// Repairs a stack right after a unit was taken out of it and dropped.
	/// </summary>
	/// <remarks>
	/// Dropping one bag out of a stack of three lowers the count and leaves the amount untouched, so the stack keeps
	/// reporting the weight of all three bags. Dropping the rest one at a time then ends with a single bag that still
	/// claims 4.5 kg.
	/// The dropped copy is adopted here as well: the game splits a stack by cloning the gear item, which clones the
	/// stackable component too, and nothing else has ever seen that copy.
	/// </remarks>
	[HarmonyPatch(typeof(GearItem), nameof(GearItem.Drop), [typeof(int), typeof(bool), typeof(bool), typeof(bool)])]
	public class GearItem_Drop
	{
		public static void Postfix(GearItem __instance, GearItem __result)
		{
			try
			{
				StackContentSync.EnsureSynced(__instance, "dropped");

				// The copy is looked at again on the next tick: it is built by cloning the stack and then having its
				// unit count lowered, so the amount it inherits is only wrong once that count is final
				StackContentSync.Adopt(__result, "dropped copy");
				StackContentSync.Watch(__result);
			}
			catch (Exception e)
			{
				// A drop that lands on a gear item the game already destroyed throws; the idle check picks it up
				DebugLog.Write($"Drop check failed: {e.Message}");
			}
		}
	}
}
