global using ComplexLogger;
global using StackManager.config;
global using StackManager.Utilities;
global using StackManager.Utilities.Exceptions;
global using StackManager.Utilities.JSON;

global using MelonLoader.Utils;

namespace StackManager
{
	public class Main : MelonMod
	{
		public static ComplexLogger<Main> Logger { get; } = new();
		#region Config File
		public static Config Config { get; set; } = new();
		public static string ConfigDirectory { get; } = Path.Combine(MelonEnvironment.ModsDirectory, "StackManager");
		public static string ConfigFile { get; } = Path.Combine(ConfigDirectory, "config.json");
		/// <summary>
		/// Schema version of config.json, not the mod version: a saved file carrying any other value is backed up and
		/// brought up to date from <see cref="BuildDefaultConfig"/>, so this moves whenever the file gains entries an
		/// existing install has to be given - a new list, or a default item that was not in the old list.
		/// </summary>
		public static Version CurrentVersion { get; } = new(1, 0, 6);
		#endregion
		#region MelonMod Methods
		public override void OnInitializeMelon()
		{
			Settings.OnLoad();

			// Installed before any early return below, so a first run still gets it
			PatchUnitCountHook();

			if (!Directory.Exists(ConfigDirectory))
			{
				DebugLog.Write("Directory does not exist, making");
				Directory.CreateDirectory(ConfigDirectory);
			}

			if (!File.Exists(ConfigFile))
			{
				DebugLog.Write("file does not exist, making");
				SetupDefaultConfig();
				JsonFile.Save<Config>(ConfigFile, Config);
			}
			else
			{
				// NOTE: Config is initialised to a fresh instance, so it is never null here.
				// A null-coalescing Load would short circuit and the file would never be read.
				Config = LoadConfig();

				if (Config.ConfigurationVersion != CurrentVersion)
				{
					UpdateConfig();
				}
				else
				{
					DebugLog.Write($"Loaded {ConfigFile} (v{Config.ConfigurationVersion}) - "
						+ $"{Config.STACK_MERGE.Count} merge, {Config.Advanced.Count} advanced, "
						+ $"{Config.AddStackableComponent.Count} stackable, {Config.MaxWeightStackItems.Count} food stack");
				}
			}

			// Read last, so the names it adds to the config lists can never be written back into config.json above
			AddItemsFile.Apply();
		}

		/// <summary>
		/// Installs the unit count watcher, which keeps powder and liquid contents in step with the stack.
		/// </summary>
		/// <remarks>
		/// Patched by hand rather than through the attribute, so a game build that no longer exposes the setter cannot
		/// stop the rest of the patches from being applied - it just loses the automatic resync until the next merge
		/// entry point runs.
		/// </remarks>
		private static void PatchUnitCountHook()
		{
			try
			{
				PropertyInfo? property = typeof(StackableItem).GetProperty("m_Units");
				MethodInfo? setter = property?.GetSetMethod();
				MethodInfo? postfix = typeof(Patches.StackableItem_UnitsChanged).GetMethod(nameof(Patches.StackableItem_UnitsChanged.Postfix));

				if (setter == null || postfix == null)
				{
					Logger.Log("Unit count hook skipped: StackableItem.m_Units has no setter in this build", FlaggedLoggingLevel.Always);
					return;
				}

				new HarmonyLib.Harmony("StackManager.Units").Patch(setter, postfix: new HarmonyMethod(postfix));

				DebugLog.Write("Unit count hook installed");
			}
			catch (Exception e)
			{
				Logger.Log($"Unit count hook not installed: {e.Message}", FlaggedLoggingLevel.Always);
			}
		}
		#endregion

		/// <summary>
		/// Sweeps the inventory for stacks whose amount no longer matches their unit count.
		/// </summary>
		/// <remarks>
		/// Eating, a drop, a recipe and a container transfer each reach the inventory through a different method, and
		/// not all of them can be hooked. A sweep every half second costs two reads per item and catches the rest,
		/// including anything another mod does to a stack.
		/// </remarks>
		public override void OnUpdate()
		{
			if (Time.unscaledTime < s_NextIdleCheck) return;

			s_NextIdleCheck = Time.unscaledTime + IdleCheckInterval;

			try
			{
				StackContentSync.IdleCheck();

				s_IdleCheckFailed = false;
			}
			catch (Exception e)
			{
				// One line per run is enough to notice it is broken; this must never fill the log every half second
				// and it must not switch the check off for good, because a call during boot can fail once
				if (s_IdleCheckFailed) return;

				s_IdleCheckFailed = true;
				Logger.Log($"Idle stack check failed: {e}", FlaggedLoggingLevel.Always);
			}
		}

		private const float IdleCheckInterval = 0.5f;

		private static float s_NextIdleCheck;
		private static bool s_IdleCheckFailed;

		/// <summary>
		/// Reads config.json. Falls back to the built in defaults if the file cant be parsed, keeping a copy of the broken file.
		/// </summary>
		public static Config LoadConfig()
		{
			try
			{
				Config? loaded = JsonFile.Load<Config>(ConfigFile);
				if (loaded == null) throw new BadMemeException($"Config deserialized to null, {ConfigFile}");
				return loaded;
			}
			catch (Exception e)
			{
				Logger.Log($"Failed to load {ConfigFile}, falling back to defaults", FlaggedLoggingLevel.Always);
				Logger.Log(e.ToString(), FlaggedLoggingLevel.Always);

				BackupConfig("unreadable");
				SetupDefaultConfig();
				JsonFile.Save<Config>(ConfigFile, Config);
				return Config;
			}
		}

		/// <summary>
		/// Copies the current config.json next to itself, so a regeneration never silently destroys it
		/// </summary>
		public static void BackupConfig(string reason)
		{
			try
			{
				if (!File.Exists(ConfigFile)) return;

				string backup = Path.Combine(ConfigDirectory, $"config.{reason}.{DateTime.Now:yyyyMMdd-HHmmss}.json");
				File.Copy(ConfigFile, backup, true);
				Logger.Log($"Old config backed up to {backup}", FlaggedLoggingLevel.Always);
			}
			catch (Exception e)
			{
				Logger.Log($"Failed to back up {ConfigFile}: {e.Message}", FlaggedLoggingLevel.Always);
			}
		}

		public static bool UpdateConfig()
		{
			Logger.Log($"Config version {Config.ConfigurationVersion} does not match {CurrentVersion}, updating", FlaggedLoggingLevel.Always);

			BackupConfig($"v{Config.ConfigurationVersion}");

			Config defaults = BuildDefaultConfig();

			// A hand edited file can lose a list entirely, and every reader below assumes the lists exist
			Config.STACK_MERGE ??= new();
			Config.Advanced ??= new();
			Config.AddStackableComponent ??= new();
			Config.MaxWeightStackItems ??= new();

			// Merged rather than replaced. An update only ever introduces new entries, and a player who took a name
			// out of a list on purpose must not find it back afterwards, so nothing already in the file is touched.
			int added = MergeMissing(Config.STACK_MERGE, defaults.STACK_MERGE)
				+ MergeMissing(Config.Advanced, defaults.Advanced)
				+ MergeMissing(Config.AddStackableComponent, defaults.AddStackableComponent)
				+ MergeMissing(Config.MaxWeightStackItems, defaults.MaxWeightStackItems);

			Config.ConfigurationVersion = CurrentVersion;

			JsonFile.Save<Config>(ConfigFile, Config);

			Logger.Log($"Config updated to v{CurrentVersion}: {added} new entries added, everything else kept", FlaggedLoggingLevel.Always);

			return true;
		}

		/// <summary>
		/// Appends every default entry the target list does not carry yet.
		/// </summary>
		/// <remarks>
		/// Case is ignored, so a name a player typed by hand in a different case still counts as present and is not
		/// added a second time.
		/// </remarks>
		private static int MergeMissing(List<string>? target, List<string>? defaults)
		{
			if (target == null || defaults == null) return 0;

			int added = 0;

			foreach (string name in defaults)
			{
				if (string.IsNullOrWhiteSpace(name)) continue;
				if (target.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) continue;

				target.Add(name);
				added++;

				DebugLog.Write($"Config: added {name}");
			}

			return added;
		}

		public static bool SetupDefaultConfig()
		{
			DebugLog.Write("Making default config");
			Config = BuildDefaultConfig();
			return true;
		}

		/// <summary>
		/// The config a fresh install starts with, and the source of every entry an update adds.
		/// </summary>
		private static Config BuildDefaultConfig()
		{
			Config config = new() { ConfigurationVersion = CurrentVersion };

			config.STACK_MERGE = new()
			{
				"GEAR_BirchSaplingDried",
				"GEAR_BearHideDried",
				"GEAR_BeeHive",
				"GEAR_BottleAntibiotics",
				"GEAR_BottlePainKillers",
				"GEAR_Carrot",
				"GEAR_CoffeeTin",
				"GEAR_GreenTeaPackage",
				"GEAR_GreyWolfPelt",
				"GEAR_GutDried",
				"GEAR_HerbLeavesDried",
				"GEAR_LeatherDried",
				"GEAR_LeatherHideDried",
				"GEAR_MapleSaplingDried",
				"GEAR_MooseHideDried",
				"GEAR_PackMatches",
				"GEAR_Potato",
				"GEAR_RabbitPeltDried",
				//"GEAR_RecycledCan",
				"GEAR_StumpRemover",
				"GEAR_TapeRoll",
				"GEAR_WolfPeltDried",
				"GEAR_WoodMatches",
				"GEAR_Yeast"
			};

			config.Advanced = new()
			{
				"GEAR_CoffeeTin",
				"GEAR_GreenTeaPackage",
				//"GEAR_RecycledCan",
				"GEAR_Potato"
			};

			config.AddStackableComponent = new()
			{
				"GEAR_BeeHive",
				"GEAR_GreyWolfPelt",
				"GEAR_HerbLeavesDried",
				"GEAR_Potato",
				//"GEAR_RecycledCan",
				"GEAR_StumpRemover",
				"GEAR_TapeRoll",
				"GEAR_Yeast"
			};

			// Whole units only. Cooking ingredients are sorted under food in the inventory but cannot be eaten, so
			// the calorie based partial check does not apply to them; the whole unit is used up by a recipe.
			// Powders (flour, salt, sugar, oats, gunpowder) and liquid containers (cooking oil) both belong in this
			// list: what a stack of them holds is kept in step with its unit count, so the crafting panel and the
			// recipes see the total of the stack rather than the contents of a single container.
			config.MaxWeightStackItems = new()
			{
				"GEAR_CookingOil",
				"GEAR_Flour",
				"GEAR_GunpowderCan",
				"GEAR_OatsTin",
				"GEAR_Salt",
				"GEAR_SaltBag",
				"GEAR_SugarA"
			};

			return config;
		}
	}
}