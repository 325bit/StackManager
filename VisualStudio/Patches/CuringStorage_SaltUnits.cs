namespace StackManager.Patches
{
	/// <summary>
	/// Measures a bag of salt as it goes into a curing box, while it still agrees with its own unit count.
	/// </summary>
	/// <remarks>
	/// The box keeps salt as a weight in its own reservoir and hands back whatever it could not take, so a bag whose
	/// count was several units comes back with the salt of fewer and the count untouched. What one bag holds is only
	/// readable before the box touches it, which is exactly here - see <see cref="CuringSaltStacks"/>.
	/// This is the same entry point <see cref="CuringStorage_AddItem_Units"/> uses for the piece count, and the two do
	/// not overlap: that one only records an item the box keeps, this one only looks at a powder.
	/// </remarks>
	[HarmonyPatch(typeof(CuringStorage), nameof(CuringStorage.AddItem))]
	internal class CuringStorage_AddItem_SaltUnits
	{
		/// <remarks>
		/// A positional parameter is used because the name the interop stub carries is not the one the game used.
		/// Everything is inside a try: this runs in the middle of a transfer, and a throw here would leave the bag
		/// attached to the box with no record of what it holds.
		/// </remarks>
		public static void Postfix(GearItem __0)
		{
			try
			{
				CuringSaltStacks.Record(__0);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Salt bag not measured, its count may drift: {e.Message}");
			}
		}
	}

	/// <summary>
	/// Gives every bag the box hands back the count its remaining salt stands for.
	/// </summary>
	/// <remarks>
	/// The lid closing is when the box pours the bags it is holding into its reservoir, subtracts what it took from
	/// each one and throws the empty ones away. The bags that survive are handed back to the backpack right after, so
	/// this is the last moment the count can be put right before the player sees it.
	/// </remarks>
	[HarmonyPatch(typeof(CuringStorage), nameof(CuringStorage.CloseCuringStorage))]
	internal class CuringStorage_Close_SaltUnits
	{
		public static void Postfix(CuringStorage __instance)
		{
			try
			{
				CuringSaltStacks.Settle(__instance);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Salt bag counts not checked when a curing box closed: {e.Message}");
			}
		}
	}
}
