using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;

namespace StackManager.Patches
{
	/// <summary>
	/// Keeps the reported weight of a stack equal to the amount it actually holds.
	/// </summary>
	/// <remarks>
	/// Powder stacks now store the total of every unit in <see cref="PowderItem.m_Weight"/>. If the game's own weight
	/// calculation also multiplies by the unit count, that total would be reported once per unit - a stack of three
	/// bags weighing 4.5 kg would claim 13.5 kg, and every container it sits in would look full.
	/// The check only fires when the reported value is clearly larger than the stored content, so it stays out of the
	/// way on builds where the weight is already correct, and it only ever touches components this mod created.
	/// </remarks>
	[HarmonyPatch(typeof(GearItem), nameof(GearItem.GetItemWeightKG), [typeof(bool)])]
	public class GearItem_WeightReport
	{
		/// <summary>A factor this large can only be the unit count being applied twice</summary>
		private const float DoubleCountFactor = 1.5f;

		public static void Postfix(GearItem __instance, ref ItemWeight __result)
		{
			if (__instance == null) return;

			StackableItem? stack = __instance.m_StackableItem;
			if (stack == null || stack.m_Units <= 1) return;
			if (!AddedStackables.Contains(stack)) return;

			PowderItem? powder = __instance.m_PowderItem;
			if (powder == null) return;

			float content = StackContentSync.ToKilograms(powder.m_Weight);
			if (content <= 0f) return;

			float reported = StackContentSync.ToKilograms(__result);
			if (reported <= content * DoubleCountFactor) return;

			__result = StackContentSync.FromKilograms(content);
		}
	}
}
