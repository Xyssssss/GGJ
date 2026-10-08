using Godot;

namespace GGJ;

public enum Rarity { Common, Rare, Epic }

/// <summary>
/// 玩家线词条数据。策划在编辑器里：右键 → 新建资源 → 存成 .tres，全程零代码。
///
/// ⚠️ 故意**没有** Prerequisite 字段：抽取池是平铺单池，
///    无前置、无解锁、无依赖、无先后顺序。「链」只是策划的设计辅助，不进游戏。
/// </summary>
[GlobalClass]
public partial class PlayerUpgradeData : Resource
{
    [Export] public string DisplayName { get; set; } = "未命名";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Texture2D? Icon { get; set; }
    [Export] public Rarity Rarity { get; set; } = Rarity.Common;
    [Export] public int MaxStack { get; set; } = 1;
    [Export] public bool IsMechanic { get; set; }      // 机制类（三选一保底用）

    [Export] public PlayerStat Stat { get; set; } = PlayerStat.MaxHP;
    [Export] public ModifierOp Op { get; set; } = ModifierOp.Add;
    [Export] public float Value { get; set; }
}
