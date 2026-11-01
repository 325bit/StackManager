using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;

namespace StackManager.Utilities
{
	/// <summary>
	/// Keeps the unit count of a salt bag in step with the salt a curing box leaves in it.
	/// </summary>
	/// <remarks>
	/// A curing box does not store salt as bags. Every bag handed to it is held aside, and when the lid closes the box
	/// pours what each one carries into its own salt reservoir until that is full: it subtracts from
	/// <see cref="PowderItem.m_Weight"/> and destroys the bag once nothing is left. The unit count is never touched,
	/// because in the game every bag is one unit - a bag that came back part full is still the bag it was.
	/// A stack of bags breaks that assumption. Twenty bags go in as one item carrying the salt of twenty, the box takes
	/// one bag's worth out of the middle of it, and the item comes back claiming twenty while holding nineteen. From
	/// then on the count and the salt disagree, and the number on the bag is the one the player sees.
	/// The bag is measured as it goes in, while it still agrees with itself, and the count is read back out of what is
	/// left once the lid has closed.
	/// </remarks>
	internal static class CuringSaltStacks
	{
		/// <summary>What one bag of a powder held at the moment a stack of them went into a box</summary>
		private sealed class Bag
		{
			public GearItem Item = null!;
			public float PerUnit;
		}

		private static readonly Dictionary<int, Bag> s_Bags = new();

		/// <summary>Ceiling on remembered bags, in case a box is closed without ever being seen to close</summary>
		private const int BagLimit = 512;

		/// <summary>
		/// Remembers how much salt one bag of this stack held, as the stack goes into a curing box.
		/// </summary>
		/// <remarks>
		/// Called before the box has taken anything, which is the only moment the count and the salt still describe
		/// the same thing. A bag added while it is already short of a whole unit is not remembered: there is nothing
		/// to read a per-bag figure from, and the count is already the plain one unit the game expects.
		/// </remarks>
		public static void Record(GearItem? gear)
		{
			if (gear == null) return;

			PowderItem? powder = gear.m_PowderItem;
			if (powder == null) return;

			int id = gear.GetInstanceID();

			int units = gear.m_StackableItem?.m_Units ?? 1;

			if (units < 2)
			{
				s_Bags.Remove(id);

				return;
			}

			float weight = StackContentSync.ToKilograms(powder.m_Weight);
			if (weight <= 0f) return;

			// The prefab knows how much one bag is made with, which is the figure the count has to be read back
			// with: a bag that has already been through a box holds a per unit figure that drifted with it
			float perUnit = StackContentSync.UnitContentOfPrefab(gear);
			if (perUnit <= 0f) perUnit = weight / units;

			// A box that is never closed leaves its entry behind, so the table is emptied rather than grown
			if (s_Bags.Count >= BagLimit) s_Bags.Clear();

			s_Bags[id] = new Bag { Item = gear, PerUnit = perUnit };
		}

		/// <summary>
		/// Gives every bag a curing box is still holding the count that its remaining salt stands for.
		/// </summary>
		/// <remarks>
		/// Runs once the lid has closed and the box has filled its reservoir, which is when the bags it emptied are
		/// already gone and the ones it could not finish are about to be handed back to the backpack. Doing it here
		/// rather than on the next sweep means the count is right before anything can display or merge the bag.
		/// </remarks>
		public static void Settle(CuringStorage? box)
		{
			Il2CppSystem.Collections.Generic.List<GearItem>? bags = box?.AddedSalt;

			if (bags == null)
			{
				s_Bags.Clear();

				return;
			}

			int count = bags.Count;

			for (int i = 0; i < count; i++)
			{
				GearItem? gear = bags[i];
				if (gear == null) continue;

				int id = gear.GetInstanceID();

				if (!s_Bags.TryGetValue(id, out Bag? bag) || bag.Item != gear) continue;

				s_Bags.Remove(id);

				try
				{
					Apply(gear, bag);
				}
				catch (Exception e)
				{
					DebugLog.Write($"Salt bag count not put back: {e.Message}");
				}
			}

			// Anything left over belongs to a box that never closed, and must not be applied to a later one
			s_Bags.Clear();
		}

		/// <summary>Reads the count out of the salt that is left in a bag</summary>
		/// <remarks>
		/// Rounded up, so the bag the box could only take part of still counts as one bag - a part filled bag is a
		/// bag, which is what the game itself hands out and what the rest of the stack is counted by.
		/// A bag the box did not touch at all comes back with the count it went in with, and nothing is written.
		/// </remarks>
		private static void Apply(GearItem gear, Bag bag)
		{
			PowderItem? powder = gear.m_PowderItem;
			StackableItem? stack = gear.m_StackableItem;

			if (powder == null || stack == null) return;
			if (bag.PerUnit <= 0f) return;

			float weight = StackContentSync.ToKilograms(powder.m_Weight);
			if (weight <= 0f) return;

			int units = Mathf.CeilToInt(weight / bag.PerUnit - 0.001f);
			if (units < 1) units = 1;

			int from = stack.m_Units;
			if (units == from) return;

			stack.m_Units = units;

			string name = CommonUtilities.NormalizeName(gear.name) ?? gear.name ?? string.Empty;

			Main.Logger.Log($"Salt bag: {name} came back from a curing box holding {weight:F3} kg, "
				+ $"{from} -> {units} units", FlaggedLoggingLevel.Always);
		}
	}
}
