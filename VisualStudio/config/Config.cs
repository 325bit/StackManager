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
		/// Items that stack only as whole units, the same rule food follows. Cooking ingredients that belong to the
		/// food category but cannot be eaten are listed here, see <see cref="Utilities.FoodStackingRules"/>, and so
		/// is the gunpowder can, which a recipe takes part of.
		/// Powders and liquid containers are both allowed: a stack of them holds the content of every unit in it,
		/// which is kept in step with the unit count rather than being fixed to one container.
		/// </summary>
		[JsonInclude]
		public List<string> MaxWeightStackItems { get; set; } = new();
	}
}
