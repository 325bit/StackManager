using StackManager.Utilities;
using StackManager.Utilities.Exceptions;

namespace StackManager.Patches
{
	/// <summary>Restores the original's instance values on the copy made when units are taken back into the bag.</summary>
	/// <remarks>
	/// This overload is the one the pick-units panel uses to hand a chosen number of units to the player. It loads
	/// the prefab through the addressable asset loader rather than cloning the stack, so the copy starts out with
	/// the prefab's weight and calories. See <see cref="SplitInstanceValues"/>.
	/// </remarks>
	[HarmonyPatch]
	internal class PlayerManager_InstantiateItemInPlayerInventory_Transfer
	{
		public static MethodBase TargetMethod()
		{
			MethodInfo? target = typeof(PlayerManager).GetMethods()
				.FirstOrDefault(method => method.Name == nameof(PlayerManager.InstantiateItemInPlayerInventory)
					&& method.GetParameters().Length == 4
					&& method.GetParameters()[0].ParameterType == typeof(GearItem));

			return target ?? throw new BadMemeException("InstantiateItemInPlayerInventory(GearItem,...) not found.");
		}

		public static void Postfix(GearItem __0, GearItem __result)
		{
			SplitInstanceValues.Carry(__0, __result, "split into bag");
		}
	}
}
