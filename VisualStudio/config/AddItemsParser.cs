namespace StackManager.config
{
	/// <summary>Which rule the names listed under a header follow</summary>
	internal enum AddItemsRule
	{
		/// <summary>No header seen yet, or a line that is only a note: names below it are skipped</summary>
		None,
		/// <summary>Ordinary stacking: whole units merge and the better condition is kept</summary>
		Plain,
		/// <summary>Whole portion stacking, for liquids and anything whose weight per unit can change</summary>
		WeightVariable
	}

	/// <summary>What an add-items file asked for</summary>
	internal sealed class AddItemsResult
	{
		public List<string> Plain { get; } = new();
		public List<string> WeightVariable { get; } = new();

		/// <summary>One based line numbers of names that sit above the first header, so they can be reported</summary>
		public List<int> OrphanLines { get; } = new();
	}

	/// <summary>
	/// Turns the lines of <c>Mods/StackManager_AddItems.txt</c> into the two lists of names it describes.
	/// </summary>
	/// <remarks>
	/// Holds no game or config type, so the format can be exercised without a running game and the file can be read
	/// before anything else is loaded.
	/// </remarks>
	internal static class AddItemsParser
	{
		/// <summary>English wording accepted for a header, compared with spaces and slashes removed</summary>
		private static readonly string[] s_PlainKeys = { "nonliquid", "solid" };

		/// <summary>English wording accepted for a header, compared with spaces and slashes removed</summary>
		private static readonly string[] s_WeightKeys = { "liquid", "weightvariable", "variableweight", "liquidweightvariable" };

		public static AddItemsResult Parse(IEnumerable<string> lines)
		{
			AddItemsResult result = new();

			AddItemsRule rule = AddItemsRule.None;
			int number = 0;

			foreach (string raw in lines)
			{
				number++;

				string line = raw.Trim();
				if (line.Length == 0) continue;

				if (line[0] == '#')
				{
					AddItemsRule header = ClassifyHeader(line);

					// A line that is not a header is a note, and a note never ends a block, so a remark written
					// between two names leaves the names below it with the rule they already had
					if (header != AddItemsRule.None) rule = header;

					continue;
				}

				string name = ReadName(line);
				if (name.Length == 0) continue;

				switch (rule)
				{
					case AddItemsRule.Plain:
						AddOnce(result.Plain, name);
						break;

					case AddItemsRule.WeightVariable:
						AddOnce(result.WeightVariable, name);
						break;

					default:
						result.OrphanLines.Add(number);
						break;
				}
			}

			return result;
		}

		/// <summary>
		/// Reads the rule out of a line that starts with <c>#</c>, returning <see cref="AddItemsRule.None"/> for a note.
		/// </summary>
		/// <remarks>
		/// A header has to start with its keyword, so a note is free to mention one further along without changing the
		/// rule of the names below it. The Chinese keywords are unambiguous; the English ones are only taken as an
		/// exact match, because those words do turn up inside ordinary notes.
		/// </remarks>
		private static AddItemsRule ClassifyHeader(string line)
		{
			string text = line.TrimStart('#').Trim();

			if (text.StartsWith("非液体", StringComparison.Ordinal)) return AddItemsRule.Plain;
			if (text.StartsWith("液体", StringComparison.Ordinal)) return AddItemsRule.WeightVariable;
			if (text.StartsWith("重量可变", StringComparison.Ordinal)) return AddItemsRule.WeightVariable;

			string key = text.Replace(" ", string.Empty)
				.Replace("/", string.Empty)
				.Replace("-", string.Empty)
				.Replace("_", string.Empty)
				.ToLowerInvariant();

			if (Array.IndexOf(s_PlainKeys, key) >= 0) return AddItemsRule.Plain;
			if (Array.IndexOf(s_WeightKeys, key) >= 0) return AddItemsRule.WeightVariable;

			return AddItemsRule.None;
		}

		/// <summary>
		/// Takes the name out of a line, dropping what it may have picked up from a copy of config.json or a note.
		/// </summary>
		/// <remarks>
		/// Quotes and a trailing comma are stripped because pasting a line out of config.json is the obvious way to
		/// spell an item, and an entry nothing recognises looks exactly like one that was never read.
		/// </remarks>
		private static string ReadName(string line)
		{
			string name = line;

			int note = name.IndexOf('#');
			if (note >= 0) name = name[..note];

			note = name.IndexOf("//", StringComparison.Ordinal);
			if (note >= 0) name = name[..note];

			name = name.Trim().TrimEnd(',').Trim();

			if (name.Length >= 2 && (name[0] == '"' || name[0] == '\'') && name[^1] == name[0]) name = name[1..^1];

			return name.Trim();
		}

		private static void AddOnce(List<string> target, string name)
		{
			if (target.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) return;

			target.Add(name);
		}
	}
}
