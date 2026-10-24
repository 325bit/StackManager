namespace StackManager
{
	public class Settings : JsonModSettings
	{
		internal static Settings Instance { get; } = new();

		[Name("Add Stackable Behaviour")]
		[Description("If enabled, the mod will add stackable behaviour to items defined in the config (this is not something you can add to). Turning it off only takes effect for items that awake afterwards, so reload the game to fully revert")]
		public bool AddStack = true;

		[Name("Set to max condition")]
		[Description("If enabled, stacking always uses the highest condition of the items being merged")]
		public bool UseMaxHP = false;

		[Name("Max Weight Food Stackable")]
		[Description("Yes: food, cooking ingredients and the gunpowder can only stack with other full units, so a 0.5kg piece of a 1kg pie never joins a stack. No: the mod stops adding stacking to them, and existing stacks stay as they are")]
		[Choice("No", "Yes")]
		public int MaxWeightFoodStackable = 1;

		[Name("Debug Logging")]
		[Description("Yes: writes every stack split, merge and weight correction to MelonLoader/Latest.log. Warnings and errors are always written. Turn this on when you report a problem")]
		[Choice("No", "Yes")]
		public int DebugLogging = 0;

		/// <summary>
		/// Readable form of <see cref="DebugLogging"/>. Any non zero value counts as Yes, so an edited settings file
		/// can never land on a value that quietly turns the log off again.
		/// </summary>
		internal bool DebugMessages => DebugLogging != 0;

		/// <summary>
		/// Readable form of <see cref="MaxWeightFoodStackable"/>. Any non zero value counts as Yes, so an edited
		/// settings file can never land on a value that quietly disables the feature.
		/// </summary>
		internal bool FoodStackable => MaxWeightFoodStackable != 0;

		// This is used to load the settings
		internal static void OnLoad()
		{
			Instance.AddToModSettings(BuildInfo.GUIName);
			Instance.RefreshGUI();
		}
	}
}
