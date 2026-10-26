# StackManager 性能评估

对象：本仓 `VisualStudio/`（v1.0.5 + 内容对账/合成顺序/日志开关这些改动之后的构建）
环境：TLD 2.55 / Unity 6 / MelonLoader 0.7.3，实机 275 个 mod
数据来源：① 代码逐钩子调用路径统计；② 真实存档规模；③ `D:\code\Csharp\PerfBench` 微基准

---

## 一、结论

**稳态开销可以忽略**：游戏在跑、玩家在走、背包开着看 HUD 时，这个 mod 大约占 **0.05% 的帧预算**（每 0.5 秒一次约 0.4 ms 的巡检）。

**代价集中在三类"事件"上，且都是一次性/短时尖峰**：

| 场景 | 额外耗时（估计） | 观感 |
|---|---|---|
| 进入新区域（`GearItem.Awake` × 6000–8000） | 15–40 ms | 沉在几秒的读图里，基本无感 |
| 批量转移（"全部存放" 20–30 件） | 10–45 ms | 可能有一次微顿 |
| 做一次菜（`ConsumeMaterialsUsedForCrafting`） | 3–8 ms，批量做 20 次 = 60–160 ms | 可感知，主要在配方界面的产出结算上 |
| 普通合堆 / 丢一个 / 拿一个 | 3–6 µs | 完全无感 |
| 常驻内存 | < 1 MB（`s_Tracks` 会缓慢增长，见第四节） | 可忽略 |

**没有每帧补丁**（没有任何 `Update`/`LateUpdate` 钩子），这是最重要的一点：稳态没有每帧固定成本。

两个"白花钱"的地方，属于纯浪费、与功能无关：

1. **`NormalizeName` 用两次正则**（`Il2CppSystem.Text.RegularExpressions.Regex.Replace`），每次调用约 0.5–1.5 µs，是这个 mod 里被调用最多的函数；
2. **`DebugLog.Write($"...")` 在日志开关关闭时仍然先把字符串拼好**（C# 求值顺序如此），每次 250 B 垃圾 + 0.5 µs。

这两个都可以在不改变任何行为的前提下消掉，收益见第六节。

---

## 二、测量方法

### 2.1 真实规模（来自实机存档）

| 指标 | 数值 |
|---|---|
| 背包物品数 | **512** |
| 其中带 `StackableItem` | 45（其中 42 个是 `units > 1` 的堆） |
| 其中食物 | 198 |
| 粉末 / 液体 | 1 / 3 |
| QuonsetGasStation 场景内的 gear 引用 | **8097** |
| CoastalRegion 场景内的 gear 引用 | **6315** |
| 存档场景总数 | 183 |

### 2.2 微基准（.NET 8，托管侧实测）

复刻 `CommonUtilities.NormalizeName` 与日志插值，样本是真实 prefab 名（含 `(Clone)`、` (12)` 变体）：

| 项目 | 每次耗时 | 每次分配 |
|---|---|---|
| `NormalizeName`（当前：2×`Regex.Replace` + 2×`Replace` + `Trim`） | **524 ns** | 59 B |
| 同上，改成单次扫描（无正则） | 124 ns | 134 B |
| 同上，加 `Dictionary<string,string>` 缓存 | **21 ns** | 0 B |
| 一行日志的字符串插值（不管开关） | **475 ns** | 252 B |
| 同上，先判断开关再拼 | 2 ns | 0 B |

> 游戏里还要叠一层 Il2Cpp 互操作：`Regex` 来自 `Il2CppSystem`，每次 `Replace` 都是一次托管→原生→托管往返。所以下文把托管数字按 **1.5–3×** 折算成游戏内估计；这也解释了为什么"消掉正则"的收益比基准表看上去更大。

---

## 三、钩子清单与开销

按对帧的影响排序。所有数字为"游戏内估计"，括号里是算法。

| # | 钩子 | 触发频率 | 每次开销 | 典型累计 |
|---|---|---|---|---|
| 1 | `GearItem.Awake`（`DisableDecay` Prefix + Postfix） | 每个 gear 生成一次 | 2–4 µs（2×`NormalizeName`、4–5×`GetComponent`、2×`List.Contains`、名称 switch） | **进图 15–40 ms**（8097 件 ≈ 8000 × 3 µs） |
| 2 | `CraftingOperation.ConsumeMaterialsUsedForCrafting` 的 **Trace** 补丁 | 每次做菜，前后各一遍 | 需求数 × 512 件，每件一次 `NormalizeName`；命中件还要拼 2 行字 | **3–8 ms/次 + 约 0.3 MB 垃圾** |
| 3 | `StackableItem.CanStackItems` Postfix | 每次合并 × 候选数（最坏 O(n)=512） | 2–3 µs（`AreInterchangeable` 里 2×`NormalizeName`） | 单次合并 1–1.5 ms；"全部存放" 30 件 **30–45 ms** |
| 4 | `Container.AddToExistingStackable` Postfix → `SyncContainer` | 每次合并都**扫整个容器** | 非堆 0.2 µs / 本 mod 的堆 2 µs | 20 件入 90 件容器 ≈ 0.5–4 ms |
| 5 | `OnUpdate` → `IdleCheck` | 2 Hz，每次扫 512 件 | 非堆 0.05 µs；带组件 1–2 µs | **0.4 ms/次 → 0.8 ms/s**（约 0.005% CPU） |
| 6 | `GearItem.GetItemWeightKG(bool)` Postfix | 极频繁（HUD、容器重量、配方面板） | 未堆叠物品 4 个分支即返回（<20 ns） | 开面板时仍 <0.1 ms/帧 |
| 7 | `Inventory.RemoveUnits` / `GearItem.Drop` Postfix | 用户动作 | 3–6 µs | 每次 <0.01 ms |
| 8 | `StackableItem.m_Units` setter Postfix | 每次托管写 units | 2–4 µs + 1 次 `GetComponent<GearItem>` | 每次合并 2 次调用 |
| 9 | `CraftingOperation` Order 补丁 | 每次做菜 | 几次排序 + 字典 + 一次 `WeightOf`/件 | ~0.2 ms |
| 10 | `CraftingOperation` → `SyncInventory` | 每次做菜 | 512 件对账 | ~0.5 ms |
| 11 | `GearItem.Deserialize` | 读档时每件一次 | 编译正则匹配 + 少量判断 | 512 件 ≈ 1 ms（一次性） |
| 12 | `InventoryGridItem.Refresh` | 面板每次刷新每格 | 非液体 3 个判断即返回 | 可忽略 |

### 3.1 值得展开的两点

**（1）`Do(GearItem, float, int)` 先扫候选、后判配置**

```csharp
GearItem? targetStack = StackableItem.GetClosestMatchStackable(inventory.m_Items, gearToAdd, normalizedCondition);
if (!CanOperate(gearToAdd, normalizedCondition)) return true;   // 配置里没有这个物品 → 走原版
```

对**所有**物品（包括和这个 mod 无关的 90% 物品）都会先做一次跨背包候选扫描，扫完才发现"不归我管"。这是纯浪费，把 `CanOperate` 提前一行即可。

**（2）Trace 补丁在日志关闭时仍然工作**

```csharp
lines.Add($"#{instances} id={item.m_InstanceID} units={...} ...");
```

`DebugLog.Write` 内部才判断开关，但字符串已经拼好了、`matches()` 也已经对 512 件逐个做了 `NormalizeName`。日志默认是关的，所以这段开销 100% 是白付。

---

## 四、内存

| 结构 | 规模 | 说明 |
|---|---|---|
| `s_Baselines`（单件内容量，按 prefab 名） | 数百条 × ~60 B ≈ 几十 KB | 有界，永不释放，正常 |
| `s_Tracks`（按 `StackableItem` 实例） | 每个堆一条 ~48 B | **只在 >256 条且 `Gear == null` 时清理**；长时间游玩、频繁生成/销毁物品会缓慢积累到几千条 |
| `s_Reported` / `s_DiagnosedFoods` | 按名字去重，有界 | 正常 |
| `CraftConsumptionOrder` 的临时表 | 每次做菜几十 KB | 立刻回收，正常 |
| `s_Watch` | 每次 tick 清空 | 正常 |

`s_Tracks` 是唯一有"缓慢增长"性质的结构：它持有 `GearItem` 包装对象，而 `PruneTracks` 的判据依赖 Unity 的"已销毁 == null"。如果这层语义在 2.55 的 interop 上不成立（需要实测确认），条目就只会增加。**建议加一个硬上限**（比如 4096，超了就整体清空重建），代价是零。

峰值方面：进图 8000 次 `Awake` 只给真正带组件、真正是食物的物品建基线，不产生与数量成正比的长期分配。

---

## 五、分级结论

- **稳态（走路、开背包、看 HUD、看容器）**：约 **0.05%** 帧预算，无每帧钩子 → 可以认为"没有开销"。
- **进图**：+15–40 ms 一次性，淹没在几秒的加载里。
- **批量存箱**：+10–45 ms，属于"能感觉到但不会卡"的量级，且随背包物品数线性增长（512 件已经偏大，装到 800 件会更明显）。
- **做菜**：+3–8 ms/次，批量合成会叠加；这也是唯一一个"玩家会连续触发"的重路径。
- **内存**：常驻 < 1 MB；`s_Tracks` 需加上界。

---

## 六、优化建议（按性价比）

> 状态：下表各项已按此顺序处理，P0 / P1 全部落地，P2 做了一半，实测见第八节。

### P0 · 零行为风险，先做这两个 ✅

1. ✅ **Trace 补丁开头加 `if (!DebugLog.Enabled) return;`**
   做菜 3–8 ms/次 → ~0.2 ms/次，垃圾从 ~0.3 MB/次 → 0。
2. ✅ **`StackingUtilities.Do(3 参)` 把 `CanOperate` 提到 `GetClosestMatchStackable` 之前**
   与本 mod 无关的物品合堆时不再白扫背包。

### P1 · 低风险，收益最大 ✅

3. ✅ **`NormalizeName` 加名字缓存 + 快速路径**
   - 名字里没有 `(`、`\0`、空白时直接返回原串（零分配、无正则），这覆盖了全部 prefab 名；
   - 需要处理的名字（实例名）走原正则一次，结果进 `Dictionary` 缓存，上限 4096 条；
   - **正则本身没有改**，所以"哪些物品被识别"这件事的判定完全不变。实测 496 → 29 ns，见 8.1。
4. ✅ **`SyncContainer` 只对"单位数真的动了的那一件"做完整对账**
   判据是 `s_Tracks` 里记录的上次单位数，两次读取 + 一次查表 ≈ 0.1 µs；
   没变的一律跳过，未登记过的本 mod 堆仍会走完整对账（否则首次合堆会漏）。

### P2 · 可选

5. ⏭ `DebugLog` 改成惰性：**未做**。剩下的插值全部在冷路径（每次修复一行、每个 prefab 一次、拒绝合并一次），
   关闭状态下的浪费已经随第 1、3 项消失，再改要动 40 多个调用点，不值。
6. ✅ `TryGetBaseline` 的食物分支不再每次 `new Baseline`：改成一个复用的实例填充后返回；
   每 prefab 仍会补一条"名字登记"（`KnowsPrefab` 依赖它），但只写一次。
7. ✅ `s_Tracks` 加上限 4096，超了整体清空重建（清空的代价只是重新登记一次，不影响正确性）。
8. ⏭ 巡检自适应降频：**未做**。收益 < 0.05% 帧预算，却要在"内容有没有变"上做判断，风险倒挂。

---

## 七、未验证项

本文的游戏内毫秒数是"托管实测 × 互操作系数"的估算，不是游戏内计时。要拿到真实数字，最省事的办法是加一段临时采样：在 `IdleCheck` 和 `ConsumeMaterials` 里用 `Stopwatch` 统计，每 60 次打一行 `Debug` 日志，跑一趟图 + 做几次菜就有实测值。需要的话我可以加上（默认关闭，不影响正常游玩）。

复现基准：`cd D:\code\Csharp\PerfBench && dotnet run -c Release`。

## 八、优化实施记录（v1.1.0）

> 第三节的钩子开销表是**优化前**的数字，保留作为对照；本节记录改了什么、实测多少。

### 8.1 微基准（`D:/code/Csharp/PerfBench`，.NET 8 托管侧）

`NormalizeName`，语料 286 条（16 个真实 prefab 名 × 17 种实例后缀，含 `\0`、`(Clone) (3)`、`((1))` 等边角）：

| 实现 | 每次耗时 | 每次分配 |
|---|---|---|
| 旧：2×`Regex.Replace` + 2×`Replace` + `Trim` | **496 ns** | 92 B |
| 新：快速路径（名字干净，直接原样返回） | **29 ns** | **0 B** |
| 新：需要处理的名字（首次走正则，之后命中缓存） | **24 ns** | **0 B** |

等价性：对同一语料逐条比对旧正则返回值与新实现返回值，**286/286 完全一致**。
新实现只加了一条前置判断和一层缓存，真正做清洗的仍然是原来那四步，所以"哪些物品被识别"的判定没有任何变化。

### 8.2 五处改动

| 文件 | 改动 | 直接效果 |
|---|---|---|
| `Patches/CraftingOperation_ConsumeMaterialsTrace.cs` | `Dump()` 开头 `if (!DebugLog.Enabled) return;` | 日志关闭时做菜不再遍历配方需求 × 背包 |
| `Utilities/StackingUtilities.cs` | `Do(GearItem, float, int)`：`CanOperate` 提到候选扫描之前 | 与本 mod 无关的 90% 物品不再白扫背包；顺带补了 `inventory == null` 判断 |
| `Utilities/CommonUtilities.cs` | `NormalizeName` 快速路径 + 上限 4096 的缓存 | 见 8.1，单次调用便宜 17 倍且零分配 |
| `Utilities/StackContentSync.cs` | `SyncContainer` 只对单位数变化过的堆做完整对账（`NeedsContainerSync`） | 一次合并从"扫整个容器"变成"扫列表 + 修一件" |
| `Utilities/StackContentSync.cs` | `TryGetBaseline` 食物分支复用实例；`s_Tracks` 加 4096 上限 | 巡检不再每秒 new 数百个 `Baseline`；追踪表有界 |

### 8.3 预期收益（按第三节的算法折算）

| 场景 | 优化前 | 预期优化后 | 依据 |
|---|---|---|---|
| 进图（`GearItem.Awake` × 6000–8000） | 15–40 ms | **5–10 ms** | 每次 Awake 2 次 `NormalizeName`：1 µs → 0.06 µs |
| `CanStackItems` 单次合并（O(n) 候选） | 1–1.5 ms | **0.1–0.2 ms** | 每个候选 `AreInterchangeable` 里 2 次 `NormalizeName` |
| 批量存箱（20–30 件） | 30–45 ms | **10–15 ms** | 只对真正变化的堆做完整对账，其余 0.1 µs/件 |
| 做菜（每次） | 3–8 ms + 0.3 MB 垃圾 | **~0.2 ms + 0 垃圾** | Trace 直接返回 |
| 稳态巡检（2 Hz × 512 件，其中 198 食物） | 0.4 ms/次 | **~0.1 ms/次** | 名字提速 + 食物基线不再分配 |

以上是"托管实测 × 1.5–3× 互操作系数"的估算，仍未在游戏内计时（见第七节）。

### 8.4 正确性说明

- `SyncContainer` 的收窄有明确边界：只跳过"不属于本 mod 且单位数没变"的条目。
  属于本 mod 但**还没有追踪记录**的堆一律放行，所以"首次合堆"不会漏——这一点比单纯比对单位数更重要。
- 食物名字的登记（`RegisterFoodName`）保留了原来的副作用：`KnowsPrefab` 仍能答出食物 prefab，
  否则读档后的堆会在 `Adopt` / `ExpandLegacyContent` 处被当成陌生组件跳过。
- `s_Tracks` 清空是安全的：没有追踪记录的堆在下次被看到时按"首次见到"重新登记，
  最坏结果是一次多余的登记，而不是一次错误的拆分。
