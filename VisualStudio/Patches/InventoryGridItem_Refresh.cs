namespace StackManager.Patches
{
	/// <summary>
	/// Drops the unit count from a grid entry that already prints how much of it is left.
	/// </summary>
	/// <remarks>
	/// A container of liquid shows its amount in one corner of the icon ("3.00 L"). Making that container stackable
	/// gives it a unit count as well, and the game prints that in the same corner - the two end up on top of each
	/// other and neither can be read. The amount says more than the count does for a liquid, so the count goes.
	/// Only text is cleared, and only text that is nothing but a number: the panel writes the label again on every
	/// refresh, so an entry reused for another item shows its own number again without anything restored here.
	/// </remarks>
	[HarmonyPatch(typeof(InventoryGridItem), nameof(InventoryGridItem.Refresh))]
	internal class InventoryGridItem_Refresh
	{
		/// <summary>One line per prefab, so a full inventory does not fill the log with the same note</summary>
		private static readonly HashSet<string> s_Reported = new();

		private static bool s_Failed;

		public static void Postfix(InventoryGridItem __instance, GearItem __0)
		{
			try
			{
				HideRedundantCount(__instance, __0);
			}
			catch (Exception e)
			{
				if (s_Failed) return;

				s_Failed = true;
				DebugLog.Write($"UI: the stack count could not be hidden ({e.Message})");
			}
		}

		private static void HideRedundantCount(InventoryGridItem? grid, GearItem? gi)
		{
			if (grid == null || gi == null) return;

			// Only a container that holds something has an amount to show
			if (gi.m_LiquidItem == null) return;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null || stack.m_Units < 2) return;

			UILabel? volume = grid.m_LiquidLabel;
			if (volume == null || string.IsNullOrEmpty(volume.text)) return;

			UILabel? count = grid.m_StackLabel;
			UILabel? unit = grid.m_UnitLabel;

			string countText = TextOf(count);
			string unitText = TextOf(unit);

			bool hidCount = ClearIfCount(count, countText);
			bool hidUnit = ClearIfCount(unit, unitText);

			if (!hidCount && !hidUnit) return;

			string name = CommonUtilities.NormalizeName(gi.name) ?? gi.name ?? string.Empty;
			if (!s_Reported.Add(name)) return;

			DebugLog.Write($"UI: {name} holds {stack.m_Units} units and prints {volume.text}, so the count is hidden "
				+ $"(stack label \"{countText}\", unit label \"{unitText}\") - both sat in the same corner of the icon");
		}

		/// <summary>Clears the label when it holds nothing but a count, and reports whether it did</summary>
		private static bool ClearIfCount(UILabel? label, string text)
		{
			if (label == null) return false;
			if (string.IsNullOrWhiteSpace(text)) return false;
			if (!IsCount(text)) return false;

			label.text = string.Empty;

			return true;
		}

		/// <summary>True for "4", "x4" and "x 4", which is how a unit count is written</summary>
		private static bool IsCount(string text)
		{
			string trimmed = text.Trim();

			if (trimmed.Length > 0 && (trimmed[0] == 'x' || trimmed[0] == 'X' || trimmed[0] == '\u00D7'))
			{
				trimmed = trimmed.Substring(1).Trim();
			}

			if (trimmed.Length == 0) return false;

			for (int i = 0; i < trimmed.Length; i++)
			{
				if (!char.IsDigit(trimmed[i])) return false;
			}

			return true;
		}

		private static string TextOf(UILabel? label)
		{
			if (label == null) return string.Empty;

			return label.text ?? string.Empty;
		}
	}
}
