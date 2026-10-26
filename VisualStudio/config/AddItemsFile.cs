using System.Text;

namespace StackManager.config
{
	/// <summary>
	/// Reads <c>Mods/StackManager_AddItems.txt</c>, an optional list of extra items that should stack.
	/// </summary>
	/// <remarks>
	/// config.json already describes what stacks, but the mod rewrites that file whenever its shape changes, so a name
	/// put there by hand is at the mercy of an update. This file is only ever read: its names are added to the config
	/// lists in memory after config.json has been written, which is what keeps them out of it, and deleting a line is
	/// all it takes to take the item out again.
	/// </remarks>
	internal static class AddItemsFile
	{
		/// <summary>Sits next to the mod dll rather than in the config folder, so it is easy to find and to copy</summary>
		public static string FilePath { get; } = Path.Combine(MelonEnvironment.ModsDirectory, "StackManager_AddItems.txt");

		/// <summary>Written on first launch, so the file is there to be edited instead of having to be made by hand</summary>
		/// <remarks>
		/// Carries a BOM on purpose: the two headers are Chinese, and an editor that has to guess the encoding of a
		/// plain UTF-8 file may save it back as ANSI, which turns them into text nothing here recognises.
		/// </remarks>
		private const string Template =
@"# Stack Manager - extra stackable items
#
# One item id per line, spelled exactly as the game does (for example GEAR_Charcoal). Case matters.
# Blank lines are ignored, and a line starting with # is only a note.
# 每行一个物品 ID（例如 GEAR_Charcoal），大小写要与游戏内一致；空行会被忽略，# 开头的行只是注释。
#
# ==============================================================================
# Ordinary stacking: whole units of the same item merge and the better condition is kept.
# 普通堆叠：同种物品的完整一份会合并，并保留较好的耐久。
#非液体
#GEAR_ExampleSolid
#
# ==============================================================================
# Whole portion stacking, for liquids and for anything whose weight per unit can change.
# Only a whole unit joins a stack, so a piece a recipe has already used part of stays on its own.
# 整份堆叠：液体 / 重量可变物品。只有完整的一份才会合并，已经被配方取用过的那一份保持独立。
#液体/重量可变
#GEAR_ExampleLiquid
";

		public static void Apply()
		{
			AddItemsResult result;

			try
			{
				CreateIfMissing();
				result = AddItemsParser.Parse(File.ReadAllLines(FilePath));
			}
			catch (Exception e)
			{
				Main.Logger.Log($"AddItems: could not read {FilePath}, no extra items were added: {e}", FlaggedLoggingLevel.Always);
				return;
			}

			ReportOrphans(result);

			if (result.Plain.Count == 0 && result.WeightVariable.Count == 0)
			{
				DebugLog.Write($"AddItems: nothing listed in {FilePath}");
				return;
			}

			Merge(result);
		}

		private static void CreateIfMissing()
		{
			if (File.Exists(FilePath)) return;

			// The literal above carries whatever line endings the source file has; the file handed to the player gets
			// the platform ones, because an editor that shows a bare LF file as a single line makes it unusable
			File.WriteAllText(FilePath, Template.ReplaceLineEndings(), new UTF8Encoding(true));

			Main.Logger.Log($"AddItems: created {FilePath} - items listed in it stack from the next start", FlaggedLoggingLevel.Always);
		}

		/// <summary>
		/// Adds the listed names to the config lists, which is all it takes for the rest of the mod to pick them up.
		/// </summary>
		/// <remarks>
		/// A name can only follow one of the two rules, and the whole portion rule is the only one that keeps a liquid
		/// or a partly used container right, so it wins whenever a name ends up in both.
		/// </remarks>
		private static void Merge(AddItemsResult result)
		{
			// A hand edited config.json can lose a list entirely, and the readers below assume it exists
			Main.Config.STACK_MERGE ??= new();
			Main.Config.AddStackableComponent ??= new();
			Main.Config.MaxWeightStackItems ??= new();

			foreach (string name in result.Plain.FindAll(entry => Contains(result.WeightVariable, entry)))
			{
				Main.Logger.Log($"AddItems: {name} is listed under both headers, using the whole portion rule", FlaggedLoggingLevel.Always);
			}

			result.Plain.RemoveAll(entry => Contains(result.WeightVariable, entry));

			int plain = 0;
			int entries = 0;

			foreach (string name in result.Plain)
			{
				if (Contains(Main.Config.MaxWeightStackItems, name))
				{
					Main.Logger.Log($"AddItems: {name} is already stacked as a whole portion, its #非液体 line does nothing", FlaggedLoggingLevel.Always);
					continue;
				}

				entries += Append(Main.Config.STACK_MERGE, name);
				entries += Append(Main.Config.AddStackableComponent, name);

				plain++;
			}

			foreach (string name in result.WeightVariable)
			{
				entries += Append(Main.Config.MaxWeightStackItems, name);
			}

			Main.Logger.Log($"AddItems: {plain} item(s) for ordinary stacking, {result.WeightVariable.Count} for whole portion "
				+ $"stacking ({entries} new list entries) from {FilePath}", FlaggedLoggingLevel.Always);
		}

		/// <summary>
		/// Adds one name to one list, reporting whether it was missing.
		/// </summary>
		/// <remarks>
		/// The entry is whatever the player typed, and every reader compares it exactly, so a name that only differs in
		/// case from one already in the list would silently match nothing. That case is reported rather than added.
		/// </remarks>
		private static int Append(List<string> target, string name)
		{
			string? known = target.Find(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));

			if (known != null)
			{
				if (!string.Equals(known, name, StringComparison.Ordinal))
				{
					Main.Logger.Log($"AddItems: {name} is spelled {known} in the list - names are matched exactly, "
						+ "so write it that way", FlaggedLoggingLevel.Always);
				}

				return 0;
			}

			target.Add(name);
			DebugLog.Write($"AddItems: added {name}");

			return 1;
		}

		/// <summary>Case is ignored here so a name typed differently is not added a second time</summary>
		private static bool Contains(List<string> list, string name)
		{
			return list.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Reports names that ended up with no rule, which is what a missing header looks like.
		/// </summary>
		/// <remarks>
		/// When nothing at all was recognised, the encoding is the likelier cause: a file saved as ANSI turns the two
		/// Chinese headers into text that cannot match, and the names below them all read as unreached.
		/// </remarks>
		private static void ReportOrphans(AddItemsResult result)
		{
			if (result.OrphanLines.Count == 0) return;

			string hint = result.Plain.Count == 0 && result.WeightVariable.Count == 0
				? " If the headers are there, save the file as UTF-8 (a file saved as ANSI cannot be read)."
				: string.Empty;

			Main.Logger.Log($"AddItems: line(s) {string.Join(", ", result.OrphanLines)} of {FilePath} sit above the first "
				+ "header and were skipped - a name belongs to the header above it (#非液体 or #液体/重量可变)." + hint,
				FlaggedLoggingLevel.Always);
		}
	}
}
