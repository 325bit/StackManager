namespace StackManager.Patches
{
	/// <summary>
	/// Brings both ends of a unit transfer back in line with their unit counts.
	/// </summary>
	/// <remarks>
	/// Moving part of a stack out of a container takes the units off the source with a direct write to
	/// <see cref="StackableItem.m_Units"/> - the game does not go through anything managed - and then builds the copy
	/// from the prefab, which gives it the unit count but the contents of a single unit.
	/// The source is the half that nothing else can repair: it lives in a container, so the sweep over the inventory
	/// never reaches it, and it keeps the amount of the units that are gone until a merge into that same container
	/// happens to touch it. Repairing both here also makes the transfer correct before the next frame, instead of
	/// showing a weight that is wrong for as long as the sweep takes.
	/// </remarks>
	[HarmonyPatch]
	internal class PlayerManager_InstantiateItem_Transfer
	{
		public static MethodBase TargetMethod()
		{
			// The other overload takes an asset reference; this one carries the gear item that is being split
			MethodInfo? target = typeof(PlayerManager).GetMethods()
				.FirstOrDefault(method => method.Name == nameof(PlayerManager.InstantiateItemInPlayerInventory)
					&& method.GetParameters().Length == 4
					&& method.GetParameters()[0].ParameterType == typeof(GearItem));

			return target ?? throw new BadMemeException(
				"PlayerManager.InstantiateItemInPlayerInventory(GearItem, int, float, flags) not found for patch.");
		}

		/// <summary>
		/// Positional parameters are used on purpose: the names the interop stubs carry are not the ones the game
		/// used, and Harmony refuses a postfix whose parameter it cannot bind.
		/// </summary>
		/// <remarks>
		/// Everything is inside a try: this runs in the middle of a transfer the game is driving, and a throw here
		/// would abandon the rest of it - which is how an item ends up in two places at once.
		/// </remarks>
		public static void Postfix(GearItem __0, GearItem __result)
		{
			try
			{
				StackContentSync.EnsureSynced(__0, "units moved out of it");

				// The copy is built from the prefab, so it was given the count without the contents
				StackContentSync.FillToUnits(__result, "copy moved into the bag");
				StackContentSync.EnsureSynced(__result, "copy moved into the bag");
			}
			catch (Exception e)
			{
				// The sweep over the inventory picks the copy up on its next pass, and the source on the next merge
				DebugLog.Write($"Transfer check failed: {e.Message}");
			}
		}
	}
}
