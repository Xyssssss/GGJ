# data/ —— 策划专属区

这里全是 `.tres`（Godot 资源文件），**改数值不需要动代码**。

## 怎么配

**方式 A（推荐，已实测）：VS Code 里直接写文本。** `.tres` 就是纯文本文件，
复制下面模板改数值，**不用开 Godot 编辑器、不用重新编译**，F5 直接看效果。

玩家线词条 `player_upgrades/xxx.tres`：

```
[gd_resource type="Resource" script_class="PlayerUpgradeData" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Upgrades/PlayerUpgradeData.cs" id="1_scr"]
a
[resource]
script = ExtResource("1_scr")
DisplayName = "词条名"
Description = "一句话描述"
Rarity = 0
MaxStack = 1
IsMechanic = false
Stat = 0
Op = 0
Value = 1.0
```

敌人线 `enemy_upgrades/xxx.tres`：`script_class` 改成 `EnemyUpgradeData`、
`path` 改成 `res://src/Upgrades/EnemyUpgradeData.cs`，`Stat` 用 `EnemyStat` 的序号。

| 字段 | 含义 |
|---|---|
| `Rarity` | `0`=普通 `1`=稀有 `2`=史诗（**写数字，不要写名字**） |
| `Op` | `0`=Add（Base+ΣAdd）`1`=Mul（(Base+ΣAdd)×(1+ΣMul)）`2`=Override（最后生效，直接定为 Value） |
| `Stat` | 枚举**序号**（从 0 开始数），见 `src/Core/PlayerStat.cs` / `EnemyStat.cs` |
| `IsMechanic` | `true` = 机制类，参与「三选一保底至少 1 个机制类」 |
| `MaxStack` | 可叠几层；满了之后不再出现在池里 |

> 现成范例：`player_upgrades/swift.tres`（迅捷）· `enemy_upgrades/tough.tres`（坚韧）。照抄改数值即可。
**方式 B：Godot 编辑器。** 右键目录 → 新建资源 → 搜类型名
（`RunConfig` / `WaveConfig` / `FeelConfig` / `StringsData` / `PlayerUpgradeData` / `EnemyUpgradeData`）
→ 填表 → 另存为 `.tres`。
拿不准格式时用它生成一个，再照着它的格式用方式 A 批量写。

## 文件说明

| 文件 | 作用 | GDD 出处 |
|---|---|---|
| `run_config.tres` | 总轮数 / 初始血 / CD / 敌人数 / 三个场景引用 | §5 数值总表 |
| `feel_config.tres` | 加速度 / 摩擦 / 无敌时长 / 顿帧 / 震屏 | §5（暂缓调优） |
| `strings.tres` | 全部面向玩家的文案，**换皮只改这里** | §8 |
| `waves/round_N.tres` | 第 N 轮的波次数 / 每波敌人数 / 波次间隔 | §3.7 |
| `player_upgrades/*.tres` | 玩家线词条（失败时三选一） | §4.1 |
| `enemy_upgrades/*.tres` | 敌人线词条（胜利时三选一） | §4.2 |

## ⚠️ 红线

1. ⚠️ **`.tres` 数组字段用方括号**，不是 `Array[...]`：
   正确 `Waves = [ExtResource("w1"), ExtResource("w2")]`
   错误 `Waves = Array[ExtResource("w1")]` ← 会报 `Parse Error: Expected ']'`
2. `.tres` 在 Git 里被标成 binary（见 `.gitattributes`），**冲突了无法自动合并**。
   改之前先 pull，改完立刻 commit + push，别放久了。
3. 所有待调数值都标了 `[待调]`，见 GDD §11 待办表。
4. `data/` 目录**只有策划（P0）能改**，程序要改数值一律 @ P0。
