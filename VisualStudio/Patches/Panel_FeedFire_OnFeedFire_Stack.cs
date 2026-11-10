namespace StackManager.Patches
{
	/// <summary>
	/// Marks the whole of a feed-fire action so the two hooks downstream know they belong to the same click.
	/// </summary>
	/// <remarks>
	/// Fire.AddFuel and PlayerManager.ConsumeUnitFromInventory are general purpose methods. The window keeps the
	/// stack handling limited to the fire panel, so eating, dropping and crafting keep their vanilla behaviour even
	/// when they consume a unit of a stack this mod created.
	/// </remarks>
	[HarmonyPatch(typeof(Panel_FeedFire), nameof(Panel_FeedFire.OnFeedFire))]
	internal class Panel_FeedFire_OnFeedFire_Stack
	{
		public static void Prefix()
		{
			FuelStacking.InFeedFire = true;
		}

		/// <summary>
		/// A finalizer rather than a postfix: the flags must be cleared even when the panel throws, otherwise a later
		/// consumption outside the fire panel would be mistaken for a feed-fire action.
		/// </summary>
		public static void Finalizer()
		{
			FuelStacking.InFeedFire = false;
			FuelStacking.IsFlattening = false;
		}
	}
}
