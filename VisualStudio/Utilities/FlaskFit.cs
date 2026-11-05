using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;

namespace StackManager.Utilities
{
	/// <summary>
	/// Puts as much of a liquid stack into an insulated flask as the flask can still hold.
	/// </summary>
	/// <remarks>
	/// A flask turns an item away when its own volume plus that item would pass its capacity, and it reads the item
	/// the way the game reports it - for a stack of ours that is the volume of every unit in it at once. Five
	/// bottles of cooking oil are five bottles to that check, so not one of them goes into a one litre flask, even
	/// though one of them fits with room to spare. The units that fit are moved and the rest is handed back as its
	/// own stack, which is what the player is asking for when the bottle count and the flask size disagree.
	/// Bottles carry their volume in LiquidItem; the cups of 2.55 - tea, coffee, soup, soda - carry it in DrinkItem,
	/// which works the volume out from the weight of the item, so a stack of cups is measured by what all of them
	/// weigh. Both are read here, and a drink needs no volume written on the leftover: its units settle it.
	/// The shrink happens before the game's own move and the leftover is only handed over once that move has gone
	/// through, because anything that stops the move in between would otherwise take the extra units with it.
	/// </remarks>
	internal static class FlaskFit
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

		/// <summary>How the flask compares volumes, and how this file compares them to match</summary>
		private const float MillilitresPerLitre = 1000f;

		/// <summary>
		/// Cuts a stack down to what a flask can take, and prepares the rest to be handed back.
		/// </summary>
		/// <returns>An empty <see cref="Pending"/> when the move is to be left alone</returns>
		public static Pending Prepare(Il2CppTLD.Gear.InsulatedFlask? flask, GearItem? gear)
		{
			Pending none = default;

			if (flask == null || gear == null) return none;
			if (StackContentSync.IsLoading) return none;

			StackableItem? stack = gear.m_StackableItem;
			if (stack == null) return none;

			int units = stack.m_Units;
			if (units < 2) return none;

			// A stack read back from a save carries a component the game rebuilt, so the registry never saw it -
			// the configured name is what tells it apart from a stack this mod did not create
			if (!AddedStackables.Contains(stack) && !FoodStackingRules.IsManagedItem(gear))
			{
				DebugLog.Write($"Flask fit: {gear.name} carries {units} units but the stack is not one of this mod's, "
					+ "so the flask has to take it whole");

				return none;
			}

			// A flask holds what answers for a liquid. Bottles carry theirs in LiquidItem; every cup - tea, coffee,
			// soup, soda - carries it in DrinkItem instead, and a drink works its volume out from its own weight,
			// so a stack of cups reads as the litres all of them would fill
			LiquidItem? liquid = gear.m_LiquidItem;
			DrinkItem? drink = null;

			if (liquid == null)
			{
				drink = gear.GetComponent<DrinkItem>();

				if (drink == null)
				{
					DebugLog.Write($"Flask fit: {gear.name} carries neither a liquid nor a drink, "
						+ "so the flask has to judge it alone");

					return none;
				}
			}

			int whole = Millilitres(StackContentSync.ToLiters(liquid != null ? liquid.m_Liquid : drink!.GetVolumeLitres()));
			if (whole <= 0) return none;

			// Rounded up, so a stack whose bottles are not all equally full never fits one bottle too many
			int perUnit = (whole + units - 1) / units;
			if (perUnit <= 0) return none;

			int free = Millilitres(StackContentSync.ToLiters(flask.GetCapacityLitres()))
				- Millilitres(StackContentSync.ToLiters(flask.GetVolumeLitres()));

			if (free <= 0) return none;

			// The flask takes it as it is, which is the case this must not touch
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
				DebugLog.Write($"Flask fit: no copy could be made for the leftover of {gear.name}, "
					+ $"the whole stack stays outside ({e.Message})");

				return none;
			}

			if (!StackContentSync.Resize(gear, fit, "flask fit"))
			{
				if (leftover != null) UnityEngine.Object.Destroy(leftover.gameObject);

				DebugLog.Write($"Flask fit: {gear.name} could not be cut down, the whole stack stays outside");

				return none;
			}

			// What the stack that goes in holds now, read back rather than assumed, so the two halves add up to the
			// volume the player had however the split rounded
			int kept = Millilitres(StackContentSync.ToLiters(liquid != null ? liquid.m_Liquid : drink!.GetVolumeLitres()));
			int rest = whole - kept;

			if (rest <= 0 || !CutDown(leftover, units - fit, rest, liquid))
			{
				StackContentSync.Resize(gear, units, "flask fit undone");
				if (leftover != null) UnityEngine.Object.Destroy(leftover.gameObject);

				DebugLog.Write($"Flask fit: no copy was made for the leftover of {gear.name}, "
					+ "the whole stack stays outside");

				return none;
			}

			// Handed back before anything else runs: from here the stack is short of units and this copy is the only
			// thing holding them, so the caller has to hear about it whatever the log does
			Pending pending = new() { Lead = gear, Leftover = leftover, Units = units };

			try
			{
				DebugLog.Write($"Flask fit: {gear.name} holds {units} units ({whole / MillilitresPerLitre:F2} L) and "
					+ $"{fit} of them fit into {free / MillilitresPerLitre:F2} L of free space, "
					+ $"{units - fit} left behind");
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
		/// <param name="added">
		/// What the flask itself answered. The bag cannot be asked: the caller only takes the stack out of it after
		/// this returns, so a stack that went in is still listed there at this point.
		/// </param>
		/// <returns><c>true</c> when a leftover stack was put into the bag, so the panels have something new to show</returns>
		public static bool Finish(Pending pending, bool added)
		{
			GearItem? lead = pending.Lead;
			GearItem? leftover = pending.Leftover;

			if (lead == null || leftover == null) return false;

			if (!added)
			{
				StackContentSync.Resize(lead, pending.Units, "flask move refused");
				UnityEngine.Object.Destroy(leftover.gameObject);

				DebugLog.Write($"Flask fit: {lead.name} did not go in, all {pending.Units} of its units were put back");

				return false;
			}

			StackContentSync.PlaceInInventory(leftover, lead);

			return true;
		}

		/// <summary>
		/// Turns the copy of a stack into the stack that stays behind: its own unit count and its own share.
		/// </summary>
		/// <remarks>
		/// The amount is written here rather than left to <see cref="StackContentSync.Adopt"/>: a copy is clamped
		/// against what one whole bottle of the prefab holds, and a stack of half full bottles would be filled back
		/// up by that. A drink needs none of it - its volume is worked out from its weight, which the unit count
		/// already settles - so only its count is written.
		/// </remarks>
		private static bool CutDown(GearItem? leftover, int units, int millilitres, LiquidItem? liquid)
		{
			if (leftover == null) return false;
			if (leftover.m_StackableItem == null) return false;

			leftover.m_StackableItem.m_Units = units;

			if (liquid != null)
			{
				if (leftover.m_LiquidItem == null) return false;

				leftover.m_LiquidItem.m_Liquid = ItemLiquidVolume.FromLiters(millilitres / MillilitresPerLitre);
			}

			// The copy carries a copy of the component, and without this every rule in this mod would skip it
			AddedStackables.Register(leftover.m_StackableItem);

			return true;
		}

		/// <summary>Millilitres, which is the unit the flask's capacity check itself works in</summary>
		private static int Millilitres(float litres)
		{
			return Mathf.RoundToInt(litres * MillilitresPerLitre);
		}
	}
}
