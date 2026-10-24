namespace StackManager.Patches
{
	/// <summary>
	/// Catches every unit count change made from managed code and rescales the stack contents with it.
	/// </summary>
	/// <remarks>
	/// A stack of flour or cooking oil keeps the content of the whole stack in one field, so that content has to follow
	/// every unit count change - otherwise the stack reports what one unit holds no matter how many it carries.
	/// Merges that run through managed code - this mod's own merge, and the container sorting mods - assign
	/// <see cref="StackableItem.m_Units"/> directly, which is what this hook watches. Merges performed inside the game
	/// never call this setter, so the merges, a drop and a recipe each bring the contents back in step on their own.
	/// This class carries no <see cref="HarmonyLib.HarmonyPatchAttribute"/> on purpose: it is installed by hand from
	/// <see cref="Main.OnInitializeMelon"/>, so a game build without the setter cannot break the other patches.
	/// </remarks>
	internal class StackableItem_UnitsChanged
	{
		public static void Postfix(StackableItem __instance)
		{
			if (__instance == null) return;
			if (StackContentSync.IsLoading) return;

			StackContentSync.EnsureSynced(__instance.GetComponent<GearItem>(), "unit count changed");
		}
	}
}
