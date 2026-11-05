namespace StackManager.Utilities
{
	/// <summary>
	/// Redraws every place a flask is shown after its contents have changed.
	/// </summary>
	/// <remarks>
	/// The flask recalculates its own volume and its own weight the moment a bottle goes in or comes out, but
	/// nothing tells the panels that the numbers they are printing are stale. A drink takes a bottle out, puts what
	/// is left of it back, and the flask holds a different weight and a different amount while the bag still shows
	/// the old ones - it only catches up on the next redraw of its own.
	/// Three surfaces print those numbers and they are not redrawn together: the list of items inside the flask,
	/// the entry in the bag, and the description of the item being looked at. The last one is what the player reads
	/// while pressing the drink button, and it belongs to the bag panel rather than to the flask panel, so it is
	/// reached through the bag.
	/// The redraw is deferred to the next frame rather than done on the spot: the flask is recalculated from inside
	/// the panel's own update, which is the middle of the list it would be asked to rebuild.
	/// </remarks>
	internal static class FlaskView
	{
		private static bool s_Pending;

		/// <summary>Only the first failure is reported, and only the first one is logged</summary>
		private static bool s_Reported;

		/// <summary>Asks for a redraw on the next frame</summary>
		public static void Request()
		{
			s_Pending = true;
		}

		/// <summary>Runs the redraw that was asked for, once per frame at most</summary>
		public static void Tick()
		{
			if (!s_Pending) return;

			s_Pending = false;

			try
			{
				Panel_InsulatedFlask? flask = InterfaceManager.GetPanel<Panel_InsulatedFlask>();

				if (flask != null && flask.isActiveAndEnabled) flask.RefreshTables();

				// The bag prints the weight of the flask itself, which is the item every drink changes
				Panel_Inventory? inventory = InterfaceManager.GetPanel<Panel_Inventory>();

				if (inventory == null) return;

				if (inventory.isActiveAndEnabled) inventory.RefreshTable();

				RefreshDescription(inventory);

				// Written so a stale counter can be told apart from a refresh that never ran: the next test only
				// means anything once this line is known to follow the drink in the log
				DebugLog.Write("Flask: the flask and the bag were redrawn after a flask recalculated itself");
			}
			catch (Exception e)
			{
				if (s_Reported) return;

				s_Reported = true;

				DebugLog.Write($"Flask: the counters could not be redrawn ({e.Message})");
			}
		}

		/// <summary>
		/// Rewrites the weight, the unit count and the flask indicators of whatever item is being looked at.
		/// </summary>
		/// <remarks>
		/// Redrawing the bag is not enough for these: the description is built once when the panel is opened and
		/// only rebuilt when another item is selected, so a flask that changed underneath it keeps printing the
		/// weight and the amount it had before the drink.
		/// </remarks>
		private static void RefreshDescription(Panel_Inventory inventory)
		{
			ItemDescriptionPage? page = inventory.m_ItemDescriptionPage;
			if (page == null) return;

			Panel_Inventory_Examine? examine = InterfaceManager.GetPanel<Panel_Inventory_Examine>();

			GearItem? shown = examine == null ? null : examine.GearItem;
			if (shown == null) return;

			page.UpdateWeightDisplay(shown);
			page.UpdateStackStatus(shown);

			Il2CppTLD.Gear.InsulatedFlask? flask = shown.gameObject.GetComponent<Il2CppTLD.Gear.InsulatedFlask>();

			if (flask != null) page.UpdateInsulatedFlaskIndicators(flask);
		}
	}
}
