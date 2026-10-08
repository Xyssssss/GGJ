namespace GGJ;

/// <summary>敌人线属性枚举。与玩家线完全隔离，两条线互不相干。</summary>
public enum EnemyStat
{
    // 数值类
    HP,
    MoveSpeed,
    SpawnIntervalReduction, // 密集：刷怪间隔 -X%（0~1 的百分比，不是秒）
    EnemiesPerWaveBonus,
    BodyScale,              // 庞大：体积倍率（含碰撞体积）

    // 加速反弹（带衰减，防后期指数爆炸）
    AccelBase,              // 基础增量，如 0.20 = +20%
    AccelDecay,             // 衰减系数，如 0.50
    AccelCap,               // 硬顶，如 0.50 = 最多 +50%

    // 机制类开关
    FlagElite,              // 精英：每轮每波按概率多刷 1 个大体积慢速单位
    EliteChance,
    EliteHPMul,
    EliteSpeedMul,
    EliteScaleMul,          // 精英体积倍率（P2-16：数据驱动，默认 2f）
    FlagSpawnFourSides,     // 四向：出生点改为整个屏幕边缘
    FlagSplit,              // 分裂：死亡分裂成 2 个小怪
    FlagTracker,            // 追踪怪：每波额外刷若干追踪玩家的单位
    TrackerCount,
    TrackerSpeed,
    TrackerHP,
    FlagAccelOnBounce,
}
