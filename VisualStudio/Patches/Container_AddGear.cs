namespace StackManager.Patches
{
	/// <summary>
	/// Brings an item back in line with its unit count the moment it is put into a container.
	/// </summary>
	/// <remarks>
	/// Putting part of a stack into a container has the game copy the gear item and then lower the copy's unit count,
	/// which leaves the copy carrying the contents of the whole stack it came out of. Nothing sweeps a container, so
	/// without this the copy would keep that amount until something merged into the same container.
	/// </remarks>
	[HarmonyPatch(typeof(Container), nameof(Container.AddGear), [typeof(GearItem)])]
	internal class Container_AddGear
	{
		public static void Postfix(GearItem __0)
		{
			try
			{
				StackContentSync.FillToUnits(__0, "put into a container");
				StackContentSync.EnsureSynced(__0, "put into a container");
			}
			catch (Exception e)
			{
				// Runs inside the game's transfer, so it must never take the rest of it down with it
				DebugLog.Write($"Container add check failed: {e.Message}");
			}
		}
	}
}
