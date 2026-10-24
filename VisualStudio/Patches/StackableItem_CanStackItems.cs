namespace StackManager.Patches
{
	/* Logic:
		A stack stores a single weight, condition and calorie value for all of its units, so every unit in it has to be
		identical for the reported total to stay true. Meat pieces drift apart as they age, and merging a 0.47 kg piece
		into a 0.50 kg one reports 1.00 kg for what is really 0.97 kg. The containers mod flags that mismatch, the cooking
		panel reads that same total, and the crafting system turns it back into whole units - so a merge that is not
		strictly identical is refused here.

		Only components this mod created are checked. Gear the game already ships as stackable keeps whatever rules the
		game gave it, which keeps this from changing vanilla behaviour.
	*/
	[HarmonyPatch(typeof(StackableItem), nameof(StackableItem.CanStackItems))]
	public class StackableItem_CanStackItems
	{
		/// <summary>One line per item name, so a repeated refusal does not flood the log</summary>
		private static readonly HashSet<string> s_Reported = new();

		public static void Postfix(GearItem gearStack, GearItem gearToAdd, ref bool __result)
		{
			if (!__result) return;
			if (gearStack == null || gearToAdd == null) return;

			StackableItem? stack = gearStack.m_StackableItem;
			if (stack == null || !AddedStackables.Contains(stack)) return;

			if (FoodStackingRules.AreInterchangeable(gearToAdd, gearStack)) return;

			__result = false;

			string key = gearToAdd.name ?? string.Empty;
			if (!s_Reported.Add(key)) return;

			DebugLog.Write($"Merge refused: {gearToAdd.name} is not the same unit as the stack it was matched with "
				+ $"(weight and condition must match exactly, otherwise the stack total is wrong)");
		}
	}
}
