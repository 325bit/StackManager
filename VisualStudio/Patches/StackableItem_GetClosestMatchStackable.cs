using StackManager.Utilities;

namespace StackManager.Patches
{
	/* Logic:
		Every stack merge in the game starts here: the bag, a container and the trader's exchange box all keep their
		own merge, and all three ask this for the stack to join. None of them then moves any content across - they add
		the incoming count to the one that was found and nothing else. That is fine when the stack found is made of
		whole units, and wrong when it is a part: the count grows while the amount stays at one part's worth.
		A part is answered with "no match", which every caller already handles by adding the item on its own.
	*/
	[HarmonyPatch(typeof(StackableItem), nameof(StackableItem.GetClosestMatchStackable))]
	internal class StackableItem_GetClosestMatchStackable
	{
		public static void Postfix(ref GearItem? __result)
		{
			try
			{
				if (__result == null) return;
				if (!FoodStackingRules.RefusesToTakeUnits(__result)) return;

				__result = null;
			}
			catch (Exception e)
			{
				DebugLog.Write($"Stack merge: a partial stack was left open to a merge, {e.Message}");
			}
		}
	}
}
