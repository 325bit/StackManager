namespace StackManager.Patches
{
	/// <summary>
	/// Makes the flask recount itself the moment a drink taken from it has finished.
	/// </summary>
	/// <remarks>
	/// A drink ends in the player manager, which lowers the unit count of the stack it drank from by one. The
	/// flask never hears about that: it works its own weight and volume out only when an item enters or leaves
	/// it or its panel is opened, so after the drink it keeps reporting the weight of every cup it used to hold
	/// until the player happens to open it. Asking it to recount here also wakes the redraw that follows every
	/// recount, so the bag and the description catch up at once instead of on the next panel open.
	/// The stack itself is already right at this point - the unit it lost was taken off inside the call above -
	/// so this only has to make the counters and the panels agree with it.
	/// </remarks>
	[HarmonyPatch(typeof(PlayerManager), "EatingComplete_Internal")]
	internal class PlayerManager_EatingComplete_FlaskRefresh
	{
		public static void Postfix(PlayerManager __instance)
		{
			try
			{
				GearItem? eaten = __instance.m_FoodItemEaten;

				// Already cleared when the drinking was refused, which needs no refresh
				if (eaten == null) return;

				Il2CppTLD.Gear.InsulatedFlask? flask = eaten.m_InsulatedFlask;
				if (flask == null) return;

				// Written so a drink that never reaches its flask can be told apart from a recount that changed nothing
				DebugLog.Write("Flask: the drink finished, the flask holding it was asked to recount");

				// The recount is what the redraw hook listens for, so no second path is needed
				flask.UpdateCachedValues();
			}
			catch (Exception e)
			{
				DebugLog.Write($"Flask: a finished drink could not ask its flask to recount ({e.Message})");
			}
		}
	}
}
