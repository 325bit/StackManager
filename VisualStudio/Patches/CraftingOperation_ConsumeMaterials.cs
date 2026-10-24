namespace StackManager.Patches
{
	/// <summary>
	/// Puts every stack back in step with its amount once a recipe has taken its ingredients.
	/// </summary>
	/// <remarks>
	/// The crafting system takes what a recipe needs by weight, out of whichever gear item matches. On a stack it
	/// lowers the amount shared by every unit but leaves the unit count alone, so a stack of five 1 kg pieces comes out
	/// of a recipe holding 4.5 units while still calling itself five - and that half unit then travels with the stack
	/// until it is dropped.
	/// This hook splits the leftover off right away, which leaves four whole pieces and one 0.5 kg piece.
	/// </remarks>
	[HarmonyPatch(typeof(Il2CppTLD.Gear.CraftingOperation), "ConsumeMaterialsUsedForCrafting")]
	public class CraftingOperation_ConsumeMaterials
	{
		public static void Postfix()
		{
			try
			{
				StackContentSync.SyncInventory("crafted");
			}
			catch (Exception e)
			{
				DebugLog.Write($"Craft consumption check failed: {e.Message}");
			}
		}
	}
}
