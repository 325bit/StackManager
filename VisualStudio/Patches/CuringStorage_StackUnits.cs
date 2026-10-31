namespace StackManager.Patches
{
	/// <summary>
	/// Records the unit count of a stack that is put into a curing box.
	/// </summary>
	/// <remarks>
	/// The box saves its contents as a prefab, a condition and a remaining fraction, with no room for a unit count,
	/// so twenty pieces went in as a stack and came back as one. <see cref="CuringBoxStacks"/> keeps the count in
	/// the fraction instead; this is where it is written, as the piece enters the box.
	/// </remarks>
	[HarmonyPatch(typeof(CuringStorage), nameof(CuringStorage.AddItem))]
	internal class CuringStorage_AddItem_Units
	{
		/// <remarks>
		/// A positional parameter is used because the name the interop stub carries is not the one the game used.
		/// Everything is inside a try: this runs in the middle of a transfer the game is driving, and a throw here
		/// would leave the piece attached to the box without an entry, or the other way round.
		/// </remarks>
		public static void Postfix(CuringStorage __instance, GearItem __0)
		{
			try
			{
				CuringBoxStacks.Write(__instance?.m_State, __0);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Curing box: unit count not recorded, {e.Message}");
			}
		}
	}

	/// <summary>
	/// Gives a piece a curing box rebuilds the unit count it was saved with.
	/// </summary>
	/// <remarks>
	/// Every rebuild goes through this method with the stored fraction - the first one after a save is loaded, and
	/// the one that replaces a piece with its cured version. The count is read back and the fraction narrowed in the
	/// prefix, before the item exists: reading it afterwards would mean repairing the calories and the weight the
	/// game had already computed from the enlarged fraction.
	/// </remarks>
	[HarmonyPatch]
	internal class CuringStorage_Instantiate_Units
	{
		public static MethodBase TargetMethod()
		{
			// Three parameters, the last two being the condition and the remaining fraction
			MethodInfo? target = typeof(CuringStorage).GetMethods()
				.FirstOrDefault(method => method.Name == "InstantiateGearItem"
					&& method.GetParameters().Length == 3
					&& method.GetParameters()[1].ParameterType == typeof(float)
					&& method.GetParameters()[2].ParameterType == typeof(float));

			return target ?? throw new BadMemeException(
				"CuringStorage.InstantiateGearItem(AssetReference, float, float) not found for patch.");
		}

		public static void Prefix(ref float __2, out int __state)
		{
			__state = 1;

			try
			{
				__state = CuringBoxStacks.Read(ref __2);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Curing box: stored unit count not read, {e.Message}");
			}
		}

		public static void Postfix(GearItem __result, int __state)
		{
			try
			{
				CuringBoxStacks.Restore(__result, __state);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Curing box: saved unit count not restored, {e.Message}");
			}
		}
	}
}
