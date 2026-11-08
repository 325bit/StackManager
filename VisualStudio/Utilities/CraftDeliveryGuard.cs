namespace StackManager.Utilities
{
	/// <summary>
	/// Tells the item a craft shows while it is in progress apart from the one it hands out on success.
	/// </summary>
	/// <remarks>
	/// A craft instantiates its result twice: once before the progress starts, as a host for the work-in-progress
	/// component, and once from inside the success path as the real reward. The game destroys the first host as
	/// soon as the progress completes. Handing it a stack from the player's bag is fatal, because destroying the
	/// host then destroys every unit in that stack, see <c>docs/DEBUG-craft-saltbag.md</c>.
	/// Both marks expire on their own, so a craft that ends through an exception cannot leave a stale mark behind.
	/// </remarks>
	internal static class CraftDeliveryGuard
	{
		private const float StaleSeconds = 30f;

		private static int s_SuccessDepth;
		private static float s_SuccessStamp;
		private static int s_DeliveryDepth;
		private static float s_DeliveryStamp;

		public static void EnterSuccess()
		{
			s_SuccessDepth++;
			s_SuccessStamp = UnityEngine.Time.unscaledTime;
		}

		public static void ExitSuccess()
		{
			if (s_SuccessDepth > 0) s_SuccessDepth--;
		}

		public static void EnterDelivery()
		{
			s_DeliveryDepth++;
			s_DeliveryStamp = UnityEngine.Time.unscaledTime;
		}

		public static void ExitDelivery()
		{
			if (s_DeliveryDepth > 0) s_DeliveryDepth--;
		}

		/// <summary>True while a craft is creating its work-in-progress host, before the success path runs.</summary>
		public static bool IsPreviewDelivery
		{
			get
			{
				float now = UnityEngine.Time.unscaledTime;

				bool delivering = s_DeliveryDepth > 0 && now - s_DeliveryStamp < StaleSeconds;
				bool succeeded = s_SuccessDepth > 0 && now - s_SuccessStamp < StaleSeconds;

				return delivering && !succeeded;
			}
		}
	}
}
