namespace StackManager.Patches
{
	/// <summary>
	/// Shows Fire.AddFuel a single unit of a stack this mod created.
	/// </summary>
	/// <remarks>
	/// The panel calls this with the selected GearItem before it consumes a unit, so the whole stack is still live
	/// while the fire works out how much fuel it was handed. Flattening the count for that one call keeps every
	/// calculation that scales with the unit count at one piece; the count is put back immediately afterwards and
	/// the consumption hook removes exactly one unit with its contents.
	/// </remarks>
	[HarmonyPatch(typeof(Fire), nameof(Fire.AddFuel))]
	internal class Fire_AddFuel_Stack
	{
		internal sealed class State
		{
			internal GearItem? Gear;
			internal StackableItem? Stack;
			internal int Units;
			internal float LifeBefore;
		}

		public static void Prefix(Fire __instance, GearItem __0, out State __state)
		{
			__state = new State();

			if (!FuelStacking.InFeedFire) return;

			GearItem? gi = FuelStacking.OwnStack(__0);
			if (gi == null) return;

			StackableItem? stack = gi.m_StackableItem;
			if (stack == null || stack.m_Units <= 1) return;

			__state.Gear = gi;
			__state.Stack = stack;
			__state.Units = stack.m_Units;

			if (DebugLog.Enabled)
			{
				// Diagnostics only: a broken read must never stop the fire from being fed.
				try
				{
					__state.LifeBefore = SafeLife(__instance);
					DebugLog.Write($"Fire: AddFuel sees {FuelStacking.Describe(gi)} lifeBefore={__state.LifeBefore:F0}s");
				}
				catch (Exception e)
				{
					DebugLog.Write($"Fire: fuel description skipped, {e.Message}");
				}
			}

			// The unit-count hook would read this as a real change and rescale the contents; the flag makes it a no-op.
			FuelStacking.IsFlattening = true;
			stack.m_Units = 1;
		}

		public static void Postfix(Fire __instance, State __state)
		{
			if (__state.Stack == null) return;

			__state.Stack.m_Units = __state.Units;
			FuelStacking.IsFlattening = false;

			if (DebugLog.Enabled)
			{
				float after = SafeLife(__instance);

				DebugLog.Write($"Fire: AddFuel restored units={__state.Units} lifeAfter={after:F0}s "
					+ $"delta={after - __state.LifeBefore:F0}s");
			}

			StackContentSync.EnsureSynced(__state.Gear, "after feed fire");
		}

		/// <summary>Reading the remaining burn time is for the log only and must never affect the fire itself.</summary>
		private static float SafeLife(Fire fire)
		{
			try
			{
				return fire.GetRemainingLifeTimeSeconds();
			}
			catch
			{
				return 0f;
			}
		}
	}
}
