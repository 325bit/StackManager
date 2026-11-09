using StackManager.Utilities;

namespace StackManager.Patches
{
	/// <summary>Restores the original's instance values on the copy made when units are split into a container.</summary>
	/// <remarks>
	/// The copy is instantiated from the prefab, so it arrives with the prefab's weight and calories. See
	/// <see cref="SplitInstanceValues"/> for why that loses weight on a stack of caught fish.
	/// </remarks>
	[HarmonyPatch(typeof(GearItem), nameof(GearItem.InstantiateStackable))]
	internal class GearItem_InstantiateStackable_Transfer
	{
		public static void Postfix(GearItem __instance, GearItem __result)
		{
			SplitInstanceValues.Carry(__instance, __result, "split into container");
		}
	}
}
