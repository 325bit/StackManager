using StackManager.Utilities;

namespace StackManager.Patches
{
	/// <summary>Marks both moments at which a craft instantiates its result.</summary>
	/// <remarks>
	/// The mark only says "a craft is handing something out". Whether that something is the temporary host or the
	/// real reward is decided by <see cref="CraftDeliveryGuard.IsPreviewDelivery"/> together with the success mark.
	/// </remarks>
	[HarmonyPatch(typeof(PlayerManager), "InstantiateCraftedItemInPlayerInventory")]
	internal class PlayerManager_CraftDelivery_Guard
	{
		public static void Prefix() => CraftDeliveryGuard.EnterDelivery();

		public static void Postfix() => CraftDeliveryGuard.ExitDelivery();
	}
}
