using StackManager.Utilities;

namespace StackManager.Patches
{
	/// <summary>
	/// Redraws the flask and the bag whenever the flask has worked out a new volume or weight.
	/// </summary>
	/// <remarks>
	/// A drink takes a bottle out of the flask and hands back what is left of it, and each of those two steps makes
	/// the flask recalculate itself - but neither of them tells the panels. The result was a flask that had lost a
	/// portion of its contents while the bag still printed the weight it had before, and a list inside the flask
	/// that still showed the units it used to hold. The one place both steps pass through is this recalculation.
	/// The first call is written to the log on purpose: everything drawn from this hook depends on it being
	/// installed, and a hook that failed to attach looks exactly like a hook that never had anything to do.
	/// </remarks>
	[HarmonyPatch(typeof(Il2CppTLD.Gear.InsulatedFlask), "UpdateCachedValues")]
	internal class InsulatedFlask_UpdateCachedValues_Refresh
	{
		private static bool s_Reported;

		public static void Postfix()
		{
			if (!s_Reported)
			{
				s_Reported = true;

				Main.Logger.Log("Flask: a flask recalculated itself, the redraw hook is in place",
					FlaggedLoggingLevel.Always);
			}

			FlaskView.Request();
		}
	}
}
