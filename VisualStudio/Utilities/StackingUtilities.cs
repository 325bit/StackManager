namespace StackManager
{
	public class StackingUtilities
	{
		[Obsolete("Use StackingUtilities.SetNormalizedCondition instead")]
		public static float MaybeSetCondition(GearItem gi)
		{
			return gi.m_GearItemData.MaxHP
						* ((gi.m_StackableItem.IsAStackOfItems) ? gi.m_StackableItem.StackMultiplier : 1);
		}

		public static float SetNormalizedCondition(GearItem gi, GearItem target)
		{
			return Math.Max(gi.GetNormalizedCondition(), target.GetNormalizedCondition()); 
		}

		/// <summary>
		/// Adds <paramref name="units"/> to <paramref name="targetStack"/> and gives the stack a single shared condition
		/// </summary>
		/// <param name="useMaxCondition">When set, the merged stack is pinned to 100% instead of the highest condition of the two</param>
		public static void MergeIntoStack(GearItem targetStack, GearItem gearToAdd, int units, bool useMaxCondition = false)
		{
			if (gearToAdd == null) return;
			if (targetStack == null) return;
			if (targetStack.m_StackableItem == null) return;
			if (targetStack.m_GearItemData == null) return;

			string name = CommonUtilities.NormalizeName(gearToAdd.name) ?? string.Empty;

			// Items in the Advanced list always keep a full condition stack, so the whole stack never splits on durability.
			// This has to feed the same value the clamp below uses, otherwise the assignment would be discarded.
			float targetCondition = (useMaxCondition || Main.Config.Advanced.Contains(name))
				? 1f
				: SetNormalizedCondition(gearToAdd, targetStack);

			DebugLog.Write($"TargetCondition:{targetCondition}");

			float maxHP = targetStack.m_GearItemData.MaxHP;
			if (maxHP > 0f)
			{
				targetStack.CurrentHP = Mathf.Clamp(targetCondition * maxHP, 0f, maxHP);
			}

			// Align the stored content with the count before it moves, so the step below scales from a known state
			StackContentSync.EnsureSynced(targetStack, "before merge");

			targetStack.m_StackableItem.m_Units += units;

			// Flour and cooking oil keep the content of the whole stack in a single field, so that amount has to grow
			// with the count or every recipe keeps seeing what a single unit holds
			StackContentSync.EnsureSynced(targetStack, "merge");

			if (targetStack.m_StackableItem.m_ShareStackWithGear == null)
			{
				targetStack.m_StackableItem.m_ShareStackWithGear = Array.Empty<StackableItem>();
			}
		}

		public static bool CanBeMerged(GearItem? target, GearItem? item)
		{
			return target != null && item != null;
		}

		/// <summary>
		/// Returns <c>true</c> if the vanilla stacking should be used for this item
		/// </summary>
		/// <remarks>
		/// Instance names carry a "(Clone)" or " (1)" suffix, so they must be normalized before being compared against the config.
		/// Comparing the raw name never matches and silently disables everything downstream of this check.
		/// </remarks>
		public static bool UseDefaultStacking(GearItem gearItem)
		{
			if (gearItem == null) return true;

			string name = CommonUtilities.NormalizeName(gearItem.name) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name)) return true;

			return !Main.Config.STACK_MERGE.Contains(name);
		}

		public static bool CanOperate(GearItem? gi, float condition)
		{
			return !(gi == null) && condition > 0 && !(UseDefaultStacking(gi));
		}

		public static bool Do(GearItem gearToAdd, float normalizedCondition, int numUnits)
		{
			if (gearToAdd == null) return true;

			// Checked before anything is looked up: the candidate scan below walks the whole inventory, and this
			// method is entered for every item the game stacks, most of which this mod does not handle at all
			if (!CanOperate(gearToAdd, normalizedCondition)) return true;

			Inventory? inventory = GameManager.GetInventoryComponent();
			if (inventory == null)
			{
				DebugLog.Write("Custom merge skipped: inventory component is not available, vanilla stacking continues");
				return true;
			}

			GearItem? targetStack = StackableItem.GetClosestMatchStackable(inventory.m_Items, gearToAdd, normalizedCondition);

			//if (ComplexLogger.Main.CurrentLevel.HasFlag(FlaggedLoggingLevel.Debug)) LogEverthing(gearToAdd, targetStack, normalizedCondition, numUnits);

			if (targetStack == null)
			{
				DebugLog.Write($"Custom merge skipped: no matching stack for {gearToAdd.name}, vanilla stacking continues");
				return true;
			}

			if (!CanBeMerged(targetStack, gearToAdd))
			{
				DebugLog.Write($"Custom merge skipped: {gearToAdd.name} cannot merge into {targetStack.name}, "
					+ $"vanilla stacking continues");
				return true;
			}

			if (targetStack.m_StackableItem != null)
			{
				MergeIntoStack(targetStack, gearToAdd, numUnits, Settings.Instance.UseMaxHP);

				//LogEverthing(gearToAdd, targetStack, normalizedCondition, numUnits);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Handles the <see cref="PlayerManager.TryAddToExistingStackable(GearItem, float, int, out GearItem)"/> patch
		/// </summary>
		/// <returns><c>true</c> when the vanilla method should still run, <c>false</c> when this call handled it</returns>
		/// <remarks>
		/// <paramref name="existingGearItem"/> maps to an <c>out</c> parameter of the patched method, so it has to be assigned
		/// on every path. Skipping an out parameter would hand the caller whatever happened to be on the stack.
		/// </remarks>
		public static bool Do(GearItem gearToAdd, float normalizedCondition, int numUnits, ref GearItem? existingGearItem)
		{
			// Assign first, so every early return below still leaves the caller with a well defined value
			existingGearItem = null;

			if (gearToAdd == null) return true;
			if (!CanOperate(gearToAdd, normalizedCondition)) return true;

			Inventory inventory = GameManager.GetInventoryComponent();
			if (inventory == null)
			{
				DebugLog.Write("Custom merge skipped: inventory component is not available, vanilla stacking continues");
				return true;
			}

			// A lit match must never be merged into a stack of unlit ones
			if (gearToAdd.IsLitMatch()) return true;

			// Read anything needed for logging up front: stacking consumes the gear item,
			// so touching it after TryStackingItem can hit a destroyed object
			string displayName = gearToAdd.DisplayName;
			string name = CommonUtilities.NormalizeName(gearToAdd.name) ?? string.Empty;

			GearItem? candidate = StackableItem.GetClosestMatchStackable(inventory.m_Items, gearToAdd, normalizedCondition);

			if (!inventory.TryStackingItem(gearToAdd))
			{
				// Not an error: the vanilla method right after this prefix still runs and does the real merge
				DebugLog.Write($"Custom merge skipped for {displayName}, vanilla stacking continues");
				return true;
			}

			existingGearItem = candidate;

			DebugLog.Write($"Custom merge took x{numUnits} {displayName} ({name}) into "
				+ $"{(candidate == null ? "null" : candidate.name)}");

			return false;
		}

		public static void LogEverthing(GearItem? gearToAdd, GearItem? targetStack, float normalizedCondition, int units)
		{
			System.Text.StringBuilder sb = new();

			//if (gearToAdd == null && targetStack == null)
			//{
			//	Main.Logger.Log("gearToAdd:null", FlaggedLoggingLevel.Always);
			//	Main.Logger.Log("targetStack:null", FlaggedLoggingLevel.Always);
			//	return;
			//}
			if (gearToAdd == null)
			{
				sb.AppendLine("gearToAdd:null");
				//Main.Logger.Log("gearToAdd:null", FlaggedLoggingLevel.Always);
				//return;
			}
			if (targetStack == null)
			{
				sb.AppendLine("targetStack:null");
				//Main.Logger.Log("targetStack:null", FlaggedLoggingLevel.Always);
				//return;
			}

			float difference = 0f;
			object? gearToAddUID = null;
			object? targetStackUID = null;
			string gearToAddName = string.Empty;
			string targetStackName = string.Empty;

			if (!(gearToAdd == null) && !(targetStack == null))
			{
				difference = Math.Abs(gearToAdd.CurrentHP - targetStack.CurrentHP);
				gearToAddUID = CommonUtilities.GetGearItemPDID(gearToAdd);
				targetStackUID = CommonUtilities.GetGearItemPDID(targetStack);
				gearToAddName = string.IsNullOrWhiteSpace(gearToAdd.name) ? "NULL" : gearToAdd.name;
				targetStackName = string.IsNullOrWhiteSpace(targetStack.name) ? "NULL" : targetStack.name;
			}

			sb.AppendLine("Gear to add: ");
			sb.Append('x');
			sb.Append(units);
			sb.Append(' ');
			sb.Append(gearToAddName);
			sb.Append(' ');
			sb.Append(gearToAddUID);
			sb.AppendLine("Target stack: ");
			sb.Append(targetStackName);
			sb.Append(' ');
			sb.Append(targetStackUID);
			sb.AppendLine("Health Difference: ");
			sb.Append(difference);

			Main.Logger.Log("Begin Detailed Log", FlaggedLoggingLevel.Always, LoggingSubType.IntraSeparator);
			//Main.Logger.Log($"LogEverthing({gearToAddName}, {targetStackName}, {normalizedCondition}, {units}) Difference in HP:{difference}", FlaggedLoggingLevel.Always);

			//Main.Logger.Log($"{gearToAddName}:{gearToAddUID}", FlaggedLoggingLevel.Always);
			//Main.Logger.Log($"\tCurrentHP:{gearToAdd.CurrentHP}, Normalized:{gearToAdd.GetNormalizedCondition()}", FlaggedLoggingLevel.Always);

			//Main.Logger.Log($"{targetStackName}:{targetStackUID}", FlaggedLoggingLevel.Always);
			//Main.Logger.Log($"\tCurrentHP:{targetStack.CurrentHP}, Normalized:{targetStack.GetNormalizedCondition()}, m_StackableItem.m_Units:{targetStack.m_StackableItem.m_Units}", FlaggedLoggingLevel.Always);

			Main.Logger.Log(sb.ToString(), FlaggedLoggingLevel.Always);

			Main.Logger.Log("End Detailed Log", FlaggedLoggingLevel.Always, LoggingSubType.IntraSeparator);
		}
	}
}
