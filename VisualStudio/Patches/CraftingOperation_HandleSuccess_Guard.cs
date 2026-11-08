using StackManager.Utilities;

namespace StackManager.Patches
{
	/// <summary>Marks the window in which a craft hands out its real result.</summary>
	/// <remarks>
	/// Everything the success path creates is safe to merge: by then the work-in-progress host has already been
	/// destroyed, so nothing later will treat the returned item as a temporary object.
	/// </remarks>
	[HarmonyPatch(typeof(Il2CppTLD.Gear.CraftingOperation), "HandleSuccess")]
	internal class CraftingOperation_HandleSuccess_Guard
	{
		public static void Prefix() => CraftDeliveryGuard.EnterSuccess();

		public static void Postfix() => CraftDeliveryGuard.ExitSuccess();
	}
}
