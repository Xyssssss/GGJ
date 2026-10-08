using Godot;

namespace GGJ;

/// <summary>敌人线词条数据。与玩家线完全隔离。</summary>
[GlobalClass]
public partial class EnemyUpgradeData : Resource
{
    [Export] public string DisplayName { get; set; } = "未命名";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Texture2D? Icon { get; set; }
    [Export] public Rarity Rarity { get; set; } = Rarity.Common;
    [Export] public int MaxStack { get; set; } = 1;
    [Export] public bool IsMechanic { get; set; }

    [Export] public EnemyStat Stat { get; set; } = EnemyStat.HP;
    [Export] public ModifierOp Op { get; set; } = ModifierOp.Add;
    [Export] public float Value { get; set; }
}
