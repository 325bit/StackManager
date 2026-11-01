using Il2CppTLD.IntBackedUnit;

namespace StackManager.Patches
{
	/// <summary>
	/// Keeps the unit count of powder that comes back into the pack as an amount.
	/// </summary>
	/// <remarks>
	/// A salt tank, a recipe and a harvest all hand salt back as a number of kilos, and the game answers that by
	/// building a bag from the prefab and pouring the whole amount into it. A bag built that way claims one unit,
	/// which is how a stack of salt bags came back as a single bag: the amount survived and the count did not.
	/// The count is read back out of the amount here, so six and two thirds of a kilo of salt becomes six bags and
	/// one that is two thirds full instead of one bag holding all of it.
	/// </remarks>
	[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.AddPowderToInventory))]
	internal class PlayerManager_AddPowderToInventory_Units
	{
		/// <remarks>
		/// Everything is inside a try: this runs in the middle of whatever asked for the powder, and a throw here
		/// would cost the caller the result it is about to use.
		/// </remarks>
		public static void Postfix(GearItem __result, ItemWeight __0)
		{
			try
			{
				StackContentSync.RebuildFromAmount(__result, __0, "powder put back into the bag");
			}
			catch (Exception e)
			{
				DebugLog.Write($"Powder count not restored: {e.Message}");
			}
		}
	}
}
