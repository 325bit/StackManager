using Il2CppTLD.Trader;
using StackManager.Utilities;

namespace StackManager.Patches
{
	/* Logic:
		The trader's exchange box keeps its own merge instead of the one the bag and a container use, and that one is
		the same shape: it takes the stack the closest match returns and raises its count, leaving the amount where it
		was. Anything that ends up in the box is outside the player's inventory, so the sweep that repairs stacks never
		reaches it - the count has to be read back out of the amount right here, or not at all.
	*/
	[HarmonyPatch(typeof(ExchangeBox), nameof(ExchangeBox.TryAddToExistingStackable))]
	[HarmonyPatch([typeof(int), typeof(GearItem), typeof(int), typeof(GearItem)], [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out])]
	internal class ExchangeBox_TryAddToExistingStackable
	{
		public static void Postfix(ref GearItem? __3)
		{
			try
			{
				StackContentSync.Settle(__3, "units merged into a part in the exchange box");
			}
			catch (Exception e)
			{
				DebugLog.Write($"Stack content: exchange box merge not settled, {e.Message}");
			}
		}
	}
}
