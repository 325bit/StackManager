namespace StackManager.Patches
{
	/// <summary>
	/// Repairs a stack whenever the game lowers its unit count.
	/// </summary>
	/// <remarks>
	/// The game removes units through this one call on several paths - dropping one out of a stack, consuming a unit,
	/// handing units to another container. All of them leave the amount behind, and this is the earliest point where
	/// the stack can be told how many units it really has.
	/// </remarks>
	[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveUnits), [typeof(GearItem), typeof(int)])]
	public class Inventory_RemoveUnits
	{
		public static void Postfix(GearItem __0)
		{
			try
			{
				StackContentSync.EnsureSynced(__0, "units removed");
			}
			catch (Exception e)
			{
				DebugLog.Write($"RemoveUnits check failed: {e.Message}");
			}
		}
	}
}
