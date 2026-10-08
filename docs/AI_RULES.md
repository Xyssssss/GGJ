# AI_RULES.md — 项目速览与铁律（人人先读这一份）

> **这是最短的一份，开工前花 10 分钟读完。**
> 各人给 AI 的完整提示词在 `ai_prompts/` 下四个文件里，**整段复制**贴给自己的 AI。
> 分工与任务清单见 `05_任务分派与AI提示词.md`。

---

## 1. 项目速览

| 项 | 值 |
|---|---|
| 引擎 / 语言 | Godot 4.5.1 **Mono** / C#；命名空间统一 `GGJ` |
| 路径 | `D:\global-game-jam` |
| 画面 | 1280×720 **单屏固定镜头**，2D，无重力无跳跃 |
| 玩法 | 8 向自由移动，**只能向正右方开炮**（不可瞄准） |
| 循环 | 赢了 → 三选一强化**敌人** → 下一轮；输了 → 三选一强化**自己** → **重打本轮** |
| 终局 | 打满 8 轮结算，评级按**总失败次数** |
| 编译 | VS Code `Ctrl+Shift+B` → 回 Godot → **F5** 运行 |

**当前状态**：骨架已生成并验证通过（0 警告 0 错误，主循环跑得通）。
你们要在**各自目录里填内容**，不是从零搭。

---

## 2. 三条设计铁律（AI 最爱"顺手优化"掉这三条）

| # | 铁律 | 为什么 |
|---|---|---|
| 1 | **只有胜利才推进轮次**；失败重打本轮 | 玩家天然想赢，不会故意送死 |
| 2 | **评级按总失败次数** | 抑制"故意送死刷 buff"的**唯一锁扣**，去掉游戏就塌 |
| 3 | **词条是平铺单池**：无前置、无解锁、无依赖、无顺序 | GDD 里的"链"只是策划的设计辅助，**不进游戏** |

> 任何 AI 建议你加 `Prerequisite`、加解锁树、把评级改成按用时 —— **一律拒绝**。

---

## 3. 文件所有权（红线，不是建议）

**判断标准只有一条：你只改自己"独占"列里的文件。**
觉得别人的代码有 bug → **不要动手**，在群里说"需要 XXX 改 `src/Y/Z.cs` 的 [位置]，原因是……"。

| 角色 | 独占 | 绝不能碰 |
|---|---|---|
| **P1 守门人** | `Scenes/**`（唯一 `.tscn` 创建者）、`src/UI/**`、`src/VFX/**` | 其他目录的 `.cs` |
| **P2 战斗手** | `src/Player/**`、`src/Bullets/**`、`src/Enemies/**`、`src/Arena/**`、`src/Combat/**` | 任何 `.tscn` / `.tres` |
| **P0 策划 · 逻辑手** | `data/**`、`src/Data/**`、`src/Upgrades/**`、`src/Run/**`、`docs/**` | 别人的 `.cs`、任何 `.tscn` |
| **A1 美术 / 音频** | `art/**`、`audio/**`、（可选）`src/Audio/**` | 任何 `.tscn` / `.tres` / 别人的 `.cs` |

> **`.tscn` 归 P1，`data/**/*.tres` 归 P0** —— 这样策划配数值不必等守门人，互不阻塞。

---

## 4. 技术约定（照做，别另起一套）

| 主题 | 约定 |
|---|---|
| 跨模块通信 | **一律 `Bus`**：`Bus.Sub<T>(this, 处理器)` / `Bus.Pub(new T(…))`。事件是 `readonly struct`，定义在各模块自己的 `Events.cs` 里。**不要互相 `GetNode`** |
| 判空 | `GodotObject.IsInstanceValid(node)`。**Godot C# 里被销毁的节点不等于 `null`** |
| 生命周期 | 移动 / 碰撞 → `_PhysicsProcess`；UI / 动画 / 输入 → `_Process` |
| 数值 | 一律从 `StatBlock`（`GameManager.I.PlayerStats/EnemyStats`）或 `.tres` 读，**不写魔法数字** |
| 场地边界 | 一律走 `ArenaBounds`（`Center` / `Inside` / `Reflect` / `RandomPointOnEdge`），**不写死 1280×720** |
| 随机 | 一律走 `Rng`（`Range` / `RangeInt` / `Chance` / `Direction`），**不用 `new Random()`** |
| 伤害 | **唯一入口** `DamageSystem.Deal(ref HitInfo)`，不在别处直接扣血 |
| 文案 | **唯一来源** `GameManager.I.T("key")` → `data/strings.tres`。代码和场景里**不许出现任何面向玩家的文字** |
| 特效扩展 | 新效果实现为 `IModifier` 或 `HitInfo.Canceled`，**不写进 `DamageSystem` 主体** |

---

## 5. 六条通用红线

| # | 红线 | 后果 / 正确做法 |
|---|---|---|
| 1 | ❌ AI 顺手重构整个项目 | 提示词里的**文件边界**每次开新对话都要重新贴一次。这是防冲突最有效的手段 |
| 2 | ❌ 用 `node == null` 判空 | 用 `GodotObject.IsInstanceValid(node)` |
| 3 | ❌ 移动逻辑放 `_Process` | 会随帧率变速 / 碰撞穿透 → 放 `_PhysicsProcess` |
| 4 | ❌ `ProcessMode` 设错 | `WhenPaused` = **只在暂停时跑**。`Hud` 已是 `Always`（边打边刷新），`UpgradePicker` 用 `WhenPaused`（只在暂停时用）。照抄现有写法 |
| 5 | ❌ 开着 Godot 的 C# 热重载 | 编辑器 → .NET → 取消「自动重载项目」。统一 `Ctrl+Shift+B` 编译 → F5 |
| 6 | ❌ 提交前不自检 | `git status` 只看到自己的文件 → 编译通过 → 跑一遍没崩 → **pitch 一遍再提交**（全员在 `main`，不开分支） |

---

## 6. 提交信息格式

```
<类型>(<模块>): <一句话>

类型：feat 新功能 / fix 修 bug / tune 数值 / art 素材 / audio 音频 / docs 文档
示例：
  feat(Player): 激光词条，发射方向顺时针环绕
  fix(Enemies): Configure 里重置反弹计数，修复池复用残留
  tune(data): 第 5 轮波次间隔 4.0 → 4.5
```
