using Godot;

namespace GGJ;

/// <summary>一轮的刷怪参数。放在 data/waves/ 下，一轮一个 .tres。</summary>
[GlobalClass]
public partial class WaveConfig : Resource
{
    [Export] public int WaveCount { get; set; } = 3;
    [Export] public int EnemiesPerWave { get; set; } = 3;
    [Export] public float WaveInterval { get; set; } = 5f;

    public static WaveConfig Create(int waves, int perWave, float interval) => new()
    {
        WaveCount = waves, EnemiesPerWave = perWave, WaveInterval = interval
    };
}
