using Il2CppTLD.IntBackedUnit;

namespace StackManager.Utilities
{
	/// <summary>
	/// Puts as much of a stack into a curing box as the box can still hold.
	/// </summary>
	/// <remarks>
	/// The box refuses an item outright when its own total plus that item would pass its capacity, and it weighs the
	/// item the way the game reports it - which for a stack of ours is every unit in it at once. Twenty one pieces of
	/// kilo meat are twenty one kilos to that check, so not one of them could go into an empty twenty kilo box, even
	/// though twenty fit exactly. The units that fit are moved and the rest is handed back as its own stack, which is
	/// what the player is asking for when the piece count and the box size disagree.
	/// The shrink happens before the game's own move and the leftover is only handed over once that move has gone
	/// through, because anything that stops the move in between would otherwise take the extra units with it.
	/// </remarks>
	internal static class CuringBoxFit
	{
		/// <summary>What one move has to carry from its prefix to its postfix</summary>
		internal struct Pending
		{
			/// <summary>The stack that is being moved, already cut down to the units that fit</summary>
			public GearItem? Lead;

			/// <summary>The units that did not fit, waiting to be handed back</summary>
			public GearItem? Leftover;

			/// <summary>The unit count the stack had before it was cut</summary>
			public int Units;
		}

		/// <summary>
		/// Cuts a stack down to what a box can take, and prepares the rest to be handed back.
		/// </summary>
		/// <returns>An empty <see cref="Pending"/> when the move is to be left alone</returns>
		public static Pending Prepare(CuringStorage? box, GearItem? gear)
		{
			Pending none = default;

			if (box == null || gear == null) return none;

			StackableItem? stack = gear.m_StackableItem;
			if (stack == null) return none;

			int units = stack.m_Units;
			if (units < 2) return none;
			if (!AddedStackables.Contains(stack)) return none;

			// A powder never reaches the check this works around: the box skips its capacity test for any item that
			// carries a PowderItem, because salt is not stored as meat. Cutting one here is worse than useless - a
			// bag of salt would be measured against the meat space left, split at that line, and the part that fits
			// would be poured into the salt store while the rest went back with a count nobody asked for.
			if (gear.m_PowderItem != null) return none;

			int whole = Grams(gear.GetItemWeightKG(false));
			if (whole <= 0) return none;

			// Rounded up, so a stack whose pieces are not all the same weight never fits one piece too many
			int perUnit = (whole + units - 1) / units;
			if (perUnit <= 0) return none;

			int free = Grams(box.Capacity) - Grams(box.TotalWeight);
			if (free <= 0) return none;

			// The game takes it as it is, which is the case this must not touch
			if (whole <= free) return none;

			int fit = free / perUnit;
			if (fit < 1 || fit >= units) return none;

			GearItem? leftover = null;

			try
			{
				// Built while the source still holds every unit, so the copy can be cut down from a known count
				leftover = gear.InstantiateStackable(units - fit);
			}
			catch (Exception e)
			{
				DebugLog.Write($"Curing box fit: no copy could be made for the leftover of {gear.name}, "
					+ $"the whole stack stays outside ({e.Message})");

				return none;
			}

			if (leftover == null || leftover.m_StackableItem == null)
			{
				if (leftover != null) UnityEngine.Object.Destroy(leftover.gameObject);

				DebugLog.Write($"Curing box fit: no copy was made for the leftover of {gear.name}, "
					+ "the whole stack stays outside");

				return none;
			}

			// The copy carries a copy of the component and the amount of the whole stack; adopting it clamps that
			// amount to the units it now claims, and registers the component as one of ours
			leftover.m_StackableItem.m_Units = units - fit;
			StackContentSync.Adopt(leftover, "curing box leftover");

			if (!StackContentSync.Resize(gear, fit, "curing box fit"))
			{
				UnityEngine.Object.Destroy(leftover.gameObject);

				DebugLog.Write($"Curing box fit: {gear.name} could not be cut down, the whole stack stays outside");

				return none;
			}

			// Handed back before anything else runs: from here the stack is short of units and this copy is the only
			// thing holding them, so the caller has to hear about it whatever the log does
			Pending pending = new() { Lead = gear, Leftover = leftover, Units = units };

			try
			{
				DebugLog.Write($"Curing box fit: {gear.name} holds {units} units ({whole / 1000f:F2} kg) and {fit} of "
					+ $"them fit into {free / 1000f:F2} kg of free space, {units - fit} left behind");
			}
			catch
			{
				// A log line is not worth losing the units over
			}

			return pending;
		}

		/// <summary>
		/// Hands the units that did not fit back, or puts the count back when the move did not happen after all.
		/// </summary>
		/// <returns><c>true</c> when a leftover stack was put into the bag, so the panel has something new to show</returns>
		public static bool Finish(Pending pending)
		{
			GearItem? lead = pending.Lead;
			GearItem? leftover = pending.Leftover;

			if (lead == null || leftover == null) return false;

			// Still in the bag is the one thing that says the move never happened: every other outcome means the box
			// took the stack, so the units that stayed behind belong to the player
			if (StillInBag(lead))
			{
				StackContentSync.Resize(lead, pending.Units, "curing box move refused");
				UnityEngine.Object.Destroy(leftover.gameObject);

				DebugLog.Write($"Curing box fit: {lead.name} did not go in, all {pending.Units} of its units were put back");

				return false;
			}

			StackContentSync.PlaceInInventory(leftover, lead);

			return true;
		}

		/// <summary>Whether the item is still listed in the player's inventory</summary>
		private static bool StillInBag(GearItem gear)
		{
			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null) return false;

			return StackContentSync.Holds(inventory, gear);
		}

		/// <summary>Whole grams, which is the unit the capacity check itself works in</summary>
		private static int Grams(ItemWeight weight)
		{
			return Mathf.RoundToInt(StackContentSync.ToKilograms(weight) * 1000f);
		}
	}
}
