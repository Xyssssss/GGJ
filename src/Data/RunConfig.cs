using Godot;

namespace GGJ;

/// <summary>
/// 全局数值配置。策划改 data/run_config.tres 即可，不需要改代码。
/// .tres 缺失或损坏时 CreateDefault() 兜底，保证游戏永远跑得起来。
/// </summary>
[GlobalClass]
public partial class RunConfig : Resource
{
    [Export] public int TotalRounds { get; set; } = 8;
    [Export] public int PlayerMaxHP { get; set; } = 3;
    [Export] public float PlayerMoveSpeed { get; set; } = 220f;
    [Export] public float FireCooldown { get; set; } = 0.6f;
    [Export] public float BulletSpeed { get; set; } = 500f;
    [Export] public int EnemyHP { get; set; } = 1;
    [Export] public float EnemyMoveSpeed { get; set; } = 120f;

    // ---- 玩家侧机制默认值（P2-17：策划在 Inspector 里直接调）----
    [Export] public float RicochetChance { get; set; } = 0.5f;        // 跳弹概率
    [Export] public float DashRange { get; set; } = 120f;             // 闪现距离 px
    [Export] public float DashCooldown { get; set; } = 3f;            // 闪现 CD 秒
    [Export] public float DeathbladeWindow { get; set; } = 8f;        // 名刀窗口秒
    [Export] public float ExtraShotInterval { get; set; } = 0.08f;    // 连发补射间隔秒
    [Export] public float LaserSpinDegPerSec { get; set; } = 90f;     // 激光环绕角速度

    // ---- 敌人侧机制默认值 ----
    [Export] public float EliteChance { get; set; } = 0.20f;          // 精英出生概率
    [Export] public float EliteHPMul { get; set; } = 5f;              // 精英血量倍率
    [Export] public float EliteSpeedMul { get; set; } = 0.4f;         // 精英移速倍率
    [Export] public float EliteScaleMul { get; set; } = 2f;           // 精英体积倍率
    [Export] public int TrackerCount { get; set; } = 2;               // 追踪怪数量
    [Export] public float TrackerSpeed { get; set; } = 40f;           // 追踪怪速度
    [Export] public int TrackerHP { get; set; } = 1;                  // 追踪怪血量（待实测）
    [Export] public float AccelBase { get; set; } = 0.20f;            // 加速反弹基础增量
    [Export] public float AccelDecay { get; set; } = 0.50f;           // 加速反弹衰减系数
    [Export] public float AccelCap { get; set; } = 0.50f;             // 加速反弹硬顶
    [Export] public int SplitHP { get; set; } = 1;                    // 分裂小怪血量
    [Export] public float SplitSpeedMul { get; set; } = 1.5f;         // 分裂小怪速度倍率
    [Export] public float SplitScale { get; set; } = 0.5f;            // 分裂小怪体积
    [Export] public int SplitHivePerWave { get; set; } = 2;           // 每波额外刷出的马蜂窝（分裂母体）数量
    [Export] public float SplitHiveHPMul { get; set; } = 2f;          // 马蜂窝血量倍率（基础 HP×该值，母体比普通怪耐打）
    [Export] public float SplitHiveSpeedMul { get; set; } = 0.6f;     // 马蜂窝移速倍率（巢慢速漂浮，快起来不像巢）

    // ---- 吸血与成长（撞击流两条腿）默认值 ----
    [Export] public int LifestealKills { get; set; } = 4;        // 吸血：击杀多少个回一次血
    [Export] public int LifestealAmount { get; set; } = 1;      // 吸血：每次回多少血
    [Export] public int GrowthKills { get; set; } = 10;         // 成长：击杀多少个生命上限 +1
    [Export] public int GrowthAmount { get; set; } = 1;         // 成长：每次生命上限 +多少

    [Export] public Godot.Collections.Array<WaveConfig> Waves { get; set; } = new();

    [Export] public PackedScene? PlayerScene { get; set; }
    [Export] public PackedScene? EnemyScene { get; set; }
    [Export] public PackedScene? BulletScene { get; set; }

    /// <summary>按轮次查表（1-based）。表不够长就用最后一轮的参数。</summary>
    public WaveConfig GetWave(int round)
    {
        if (Waves == null || Waves.Count == 0) return WaveConfig.Create(3, 3, 5f);
        int i = Mathf.Clamp(round - 1, 0, Waves.Count - 1);
        return Waves[i] ?? WaveConfig.Create(3, 3, 5f);
    }

    public static RunConfig CreateDefault() => new()
    {
        TotalRounds = 8,
        PlayerMaxHP = 3,
        PlayerMoveSpeed = 220f,
        FireCooldown = 0.6f,
        BulletSpeed = 500f,
        EnemyHP = 1,
        EnemyMoveSpeed = 120f,
        LifestealKills = 4,
        LifestealAmount = 1,
        GrowthKills = 10,
        GrowthAmount = 1,
        RicochetChance = 0.5f,
        DashRange = 120f,
        DashCooldown = 3f,
        DeathbladeWindow = 8f,
        ExtraShotInterval = 0.08f,
        LaserSpinDegPerSec = 90f,
        EliteChance = 0.20f,
        EliteHPMul = 5f,
        EliteSpeedMul = 0.4f,
        EliteScaleMul = 2f,
        TrackerCount = 2,
        TrackerSpeed = 40f,
        TrackerHP = 1,
        AccelBase = 0.20f,
        AccelDecay = 0.50f,
        AccelCap = 0.50f,
        SplitHP = 1,
        SplitSpeedMul = 1.5f,
        SplitScale = 0.5f,
        SplitHivePerWave = 2,
        SplitHiveHPMul = 2f,
        SplitHiveSpeedMul = 0.6f,
        Waves = new Godot.Collections.Array<WaveConfig>
        {
            WaveConfig.Create(3, 3, 5.0f), WaveConfig.Create(3, 4, 5.0f),
            WaveConfig.Create(4, 4, 4.5f), WaveConfig.Create(4, 5, 4.5f),
            WaveConfig.Create(5, 5, 4.0f), WaveConfig.Create(5, 6, 4.0f),
            WaveConfig.Create(6, 6, 3.5f), WaveConfig.Create(6, 7, 3.5f),
        },
        PlayerScene = Res.Packed("res://" + ScenesPath.Player),
        EnemyScene  = Res.Packed("res://" + ScenesPath.Enemy),
        BulletScene = Res.Packed("res://" + ScenesPath.Bullet),
    };
}

/// <summary>场景路径常量。散落在各处写字符串最容易拼错，集中在这里。</summary>
public static class ScenesPath
{
    public const string Player = "Scenes/entities/player.tscn";
    public const string Enemy  = "Scenes/entities/enemy_base.tscn";
    public const string Bullet = "Scenes/entities/bullet.tscn";
    public const string Hud    = "Scenes/ui/hud.tscn";
    public const string Picker = "Scenes/ui/upgrade_picker.tscn";
    public const string Result = "Scenes/ui/result_screen.tscn";
}
