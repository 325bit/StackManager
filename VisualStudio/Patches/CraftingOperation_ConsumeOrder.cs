using Il2CppTLD.Gear;

namespace StackManager.Patches
{
	/// <summary>
	/// Makes a recipe take the lightest matching ingredient first, see <see cref="CraftConsumptionOrder"/>.
	/// </summary>
	/// <remarks>
	/// Runs around the same call the consumption happens in, so the reordered list exists only for the length of
	/// the craft and no other system ever sees it.
	/// </remarks>
	[HarmonyPatch(typeof(CraftingOperation), "ConsumeMaterialsUsedForCrafting")]
	public class CraftingOperation_ConsumeOrder
	{
		public static void Prefix(CraftingOperation __instance)
		{
			try
			{
				CraftConsumptionOrder.Apply(__instance?.Blueprint);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Craft order not applied: {e.Message}");
			}
		}

		public static void Postfix()
		{
			try
			{
				CraftConsumptionOrder.Restore();
			}
			catch (Exception e)
			{
				DebugLog.Write($"Craft order not put back: {e.Message}");
			}
		}
	}
}
