namespace StackManager.Utilities
{
	/// <summary>
	/// Keeps track of every <see cref="StackableItem"/> this mod created, so nothing else ever touches one it did not make.
	/// </summary>
	/// <remarks>
	/// Two separate decisions rely on this registry:
	/// the rollback in <see cref="StackManager.Patches.DisableDecay"/> may only destroy components listed here, and
	/// <see cref="FoodStackingRules"/> may only refuse a merge for a stack built by this mod. Treating a component the
	/// game or another mod added as our own would change behaviour we do not own.
	/// </remarks>
	public static class AddedStackables
	{
		private static readonly HashSet<int> s_InstanceIds = new();

		public static void Register(StackableItem? item)
		{
			if (item == null) return;

			s_InstanceIds.Add(item.GetInstanceID());
		}

		/// <summary>Returns <c>true</c> when the component was created by this mod and the entry could be removed</summary>
		public static bool TryUnregister(StackableItem? item)
		{
			if (item == null) return false;

			return s_InstanceIds.Remove(item.GetInstanceID());
		}

		public static bool Contains(StackableItem? item)
		{
			if (item == null) return false;

			return s_InstanceIds.Contains(item.GetInstanceID());
		}
	}
}
