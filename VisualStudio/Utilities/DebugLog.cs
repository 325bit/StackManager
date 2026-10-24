namespace StackManager.Utilities
{
	/// <summary>
	/// Writes the repair and diagnostic lines, but only while the Debug Logging setting is on.
	/// </summary>
	/// <remarks>
	/// The mod reports every split, every merge and every correction it makes, which is exactly what is needed to
	/// trace a problem and far more than a normal session should carry: a bag full of stacks produces a few hundred
	/// lines a minute, and the game log is the only place they can go.
	/// Anything a player has to see - a repair that failed, a leftover the inventory refused - is written at
	/// Always level elsewhere and is not affected by this switch.
	/// </remarks>
	internal static class DebugLog
	{
		public static bool Enabled => Settings.Instance.DebugMessages;

		public static void Write(string message)
		{
			if (!Enabled) return;

			Main.Logger.Log(message, FlaggedLoggingLevel.Debug);
		}
	}
}
