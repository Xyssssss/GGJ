using System;

namespace GGJ;

public enum ModifierOp
{
    Add,        // Base + ΣAdd
    Mul,        // (Base + ΣAdd) × (1 + ΣMul)
    Override,   // 最后生效，直接定为 Value
}

public interface IModifier
{
    Enum Stat { get; }
    ModifierOp Op { get; }
    float Value { get; }
}

/// <summary>通用修饰器。词条选中后由 UpgradeService 生成并塞进 StatBlock。</summary>
public sealed class StatModifier : IModifier
{
    public Enum Stat { get; set; }
    public ModifierOp Op { get; set; }
    public float Value { get; set; }

    public StatModifier(Enum stat, ModifierOp op, float value)
    {
        Stat = stat; Op = op; Value = value;
    }
}
