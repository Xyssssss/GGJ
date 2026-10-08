using Godot;

namespace GGJ;

public struct SpawnEnemyRequest
{
    public Vector2 Position;
    public Vector2 Direction;
    public float SpeedMul;
    public int HP;
    public float Scale;
    public bool IsTracker;
    /// <summary>
    /// 死亡时是否允许分裂（配合分裂词条刷出的独立马蜂窝个体）。
    /// 只有马蜂窝 = true；普通怪/精英/追踪怪/分裂出的小怪均 = false。
    /// struct 字段默认 false，所以漏传时安全地表现为"不可分裂"。
    /// </summary>
    public bool CanSplit;

    /// <summary>
    /// 皮肤类别：决定敌人从哪个贴图池随机换皮。
    /// - Normal：普通池（鸟/虫/甲虫随机）
    /// - Elite：精英池（鸽子；考拉已移作追踪怪造型）
    /// - Hive：马蜂窝（分裂词条开启后每波额外刷出的独立母体，死亡裂出马蜂）
    /// - Bee：马蜂（马蜂窝死亡裂出的子怪造型）
    /// - Tracker：追踪怪（固定考拉，不再随机普通皮）
    /// struct 默认值 = Normal，漏传时安全落到普通随机皮。
    /// </summary>
    public EnemySkinKind SkinKind;
}

public readonly struct EnemySpawned
{
    public readonly Node? Enemy;
    public EnemySpawned(Node? e) { Enemy = e; }
}

public readonly struct EnemyDespawned
{
    public readonly Node? Enemy;
    public EnemyDespawned(Node? e) { Enemy = e; }
}
