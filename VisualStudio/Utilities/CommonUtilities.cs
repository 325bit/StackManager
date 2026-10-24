using Il2CppSystem.Text.RegularExpressions;

namespace StackManager.Utilities
{
	internal class CommonUtilities
	{
		// NOTE: These "Get" methods are volitile. Ensure it is always up to date as otherwise it can break anything tied to GearItem
		// This is used to load prefab info of a GearItem
		[return: NotNullIfNotNull(nameof(name))]
		public static GearItem GetGearItemPrefab(string name) => GearItem.LoadGearItemPrefab(name);
		[return: NotNullIfNotNull(nameof(name))]
		public static ToolsItem GetToolItemPrefab(string name) => GearItem.LoadGearItemPrefab(name).GetComponent<ToolsItem>();
		[return: NotNullIfNotNull(nameof(name))]
		public static ClothingItem GetClothingItemPrefab(string name) => GearItem.LoadGearItemPrefab(name).GetComponent<ClothingItem>();

		/// <summary>Names that have already been through the clean up, keyed by the name as the game gave it</summary>
		private static readonly Dictionary<string, string> s_Normalized = new(StringComparer.Ordinal);

		/// <summary>Ceiling on the cache; it is emptied when reached, which costs one re-fill and never grows</summary>
		private const int CacheLimit = 4096;

		/// <summary>
		/// Normalizes the name given to remove extra bits using regex for most of the changes
		/// </summary>
		/// <remarks>
		/// Called for every gear item several times over - on spawn, on every merge candidate, twice per inventory
		/// sweep - so the two regex calls are the most expensive thing this mod does per item. A name that carries
		/// nothing the clean up would remove is therefore returned as it stands, without allocating anything, and the
		/// names that do need work are remembered.
		/// The regex path itself is kept untouched: it decides which items the mod recognises, so it is not the place
		/// to trade exactness for speed.
		/// </remarks>
		/// <param name="name">The name of the thing to normalize</param>
		/// <returns>Normalized name without <c>(Clone)</c> or any numbers appended</returns>
		[return: NotNullIfNotNull(nameof(name))]
		public static string? NormalizeName(string name)
		{
			if (name == null) return null;

			if (IsClean(name)) return name;

			if (s_Normalized.TryGetValue(name, out string? cached)) return cached;

			string normalized = Clean(name);

			if (s_Normalized.Count >= CacheLimit) s_Normalized.Clear();

			s_Normalized[name] = normalized;

			return normalized;
		}

		/// <summary>
		/// True when the clean up would hand the name back unchanged: no parentheses to strip, no NUL, no whitespace.
		/// </summary>
		/// <remarks>
		/// The whitespace pattern is <c>\s\d{0,}</c>, so it matches a whitespace character even when no digit follows
		/// it, which means every whitespace character in a name is removed. A closing parenthesis on its own is kept
		/// by the clean up as well, since the pattern only starts at an opening one.
		/// </remarks>
		private static bool IsClean(string name)
		{
			for (int i = 0; i < name.Length; i++)
			{
				char c = name[i];

				if (c == '(' || c == '\0') return false;
				if (char.IsWhiteSpace(c)) return false;
			}

			return true;
		}

		/// <summary>
		/// Strips the suffixes an instance name carries, leaving the prefab name.
		/// </summary>
		private static string Clean(string name)
		{
			string name0 = Regex.Replace(name, @"(?:\(\d{0,}\))", string.Empty);
			string name1 = Regex.Replace(name0, @"(?:\s\d{0,})", string.Empty);
			string name2 = name1.Replace("(Clone)", string.Empty, StringComparison.InvariantCultureIgnoreCase);
			string name3 = name2.Replace("\0", string.Empty);
			return name3.Trim();
		}

		/// <summary>
		/// Checks if the player is currently involved in anything that would make modded actions unwanted
		/// </summary>
		/// <param name="PlayerManagerComponent">The current instance of the PlayerManager component, use <see cref="GameManager.GetPlayerManagerComponent()"/></param>
		/// <returns><c>true</c> if the player isnt dead, in a conversation, locked or in a cinematic</returns>
		public static bool IsPlayerAvailable(PlayerManager PlayerManagerComponent)
		{
			if (PlayerManagerComponent == null) return false;

			// The four modes are mutually exclusive, so this has to be a set of negations.
			// Chaining them with && could never be true.
			PlayerControlMode mode = PlayerManagerComponent.m_ControlMode;

			bool first      = mode != PlayerControlMode.Dead;
			bool second     = mode != PlayerControlMode.InConversation;
			bool third      = mode != PlayerControlMode.Locked;
			bool fourth     = mode != PlayerControlMode.InFPCinematic;

			return first && second && third && fourth;
		}

		/// <summary>
		/// Gets the GearItem PDID
		/// </summary>
		/// <param name="gi"></param>
		/// <returns></returns>
		public static string? GetGearItemPDID(GearItem gi)
		{
			return gi.GetComponent<ObjectGuid>().PDID;
		}
	}
}
