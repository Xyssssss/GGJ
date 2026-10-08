using Godot;

namespace GGJ;

/// <summary>手感参数。GDD §11 明确暂缓调优，最后统一调。</summary>
[GlobalClass]
public partial class FeelConfig : Resource
{
    [Export] public float Accel { get; set; } = 2000f;
    [Export] public float Friction { get; set; } = 1600f;
    [Export] public float InvincibleTime { get; set; } = 1.5f;
    [Export] public float HitStopTime { get; set; } = 0.05f;
    [Export] public float ShakeStrength { get; set; } = 4f;
    [Export] public float BlinkHz { get; set; } = 12f;

    public static FeelConfig CreateDefault() => new();
}
