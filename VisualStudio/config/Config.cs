using System.Text.Json.Serialization;

namespace StackManager.config
{
	public class Config
	{
		[JsonInclude]
		public Version ConfigurationVersion { get; set; } = new();
		[JsonInclude]
		public List<string> STACK_MERGE { get; set; } = new();
		[JsonInclude]
		public List<string> Advanced { get; set; } = new();
		[JsonInclude]
		public List<string> AddStackableComponent { get; set; } = new();
		/// <summary>
		/// Cooking ingredients that belong to the food category but cannot be eaten. They get the same full weight
		/// stacking rule as food, see <see cref="Utilities.FoodStackingRules"/>.
		/// Powders and liquid containers are both listed here: a stack of them holds the content of every unit in it,
		/// which is kept in step with the unit count rather than being fixed to one container.
		/// </summary>
		[JsonInclude]
		public List<string> MaxWeightStackItems { get; set; } = new();
	}
}
