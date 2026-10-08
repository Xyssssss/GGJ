using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 词条抽取与生效。
///
/// P2-18 落地清单（照文件顶部 TODO 逐条实现）：
///   ✔ _Ready 时按硬编码路径加载全部词条 .tres（导出后 DirAccess 枚举不可靠），加载失败容错跳过
///   ✔ 抽 3 个不重复；过滤已满层的（对照 GameManager 已有层数 vs MaxStack）
///   ✔ 保底：池里还有机制类时，3 个选项至少 1 个机制类
///   ✔ 名刀特例：DeathbladeConsumed == true 后永久移出池
///   ✔ 四向 × 追踪互斥：GDD 标注"待实测"，先按不互斥实现（TODO 见 PickChoices）
/// </summary>
public partial class UpgradeService : Node
{
    private readonly List<PlayerUpgradeData> _playerPool = new();
    private readonly List<EnemyUpgradeData> _enemyPool = new();

    public override void _Ready()
    {
        LoadPools();
    }

    // ==================== 词条池加载 ====================

    /// <summary>
    /// 扫描两个词条目录加载全部 .tres。
    /// ⚠️ 单个文件加载失败必须容错跳过（缺一张卡不能崩整局）。
    /// </summary>
    // 词条路径清单：导出后 DirAccess 枚举 res:// 目录不可靠（Godot 会把 .tres 转换/重映射，
    // 导致 GetFiles() 返回的文件名不再是 .tres），改为硬编码路径 + ResourceLoader.Load 直接加载。
    // ResourceLoader 能正确处理导出后的资源重映射，编辑器和导出后都可靠。
    // ⚠️ 新增词条 .tres 时，在对应数组里补一行路径即可。
    private static readonly string[] PlayerUpgradePaths =
    {
        "res://data/player_upgrades/dash.tres",
        "res://data/player_upgrades/deathblade.tres",
        "res://data/player_upgrades/growth.tres",
        "res://data/player_upgrades/laser.tres",
        "res://data/player_upgrades/lifesteal.tres",
        "res://data/player_upgrades/multishot.tres",
        "res://data/player_upgrades/petite.tres",
        "res://data/player_upgrades/rapid_fire.tres",
        "res://data/player_upgrades/ricochet.tres",
        "res://data/player_upgrades/swift.tres",
        "res://data/player_upgrades/swift_bullet.tres",
    };

    private static readonly string[] EnemyUpgradePaths =
    {
        "res://data/enemy_upgrades/bounce_accel.tres",
        "res://data/enemy_upgrades/bulky.tres",
        "res://data/enemy_upgrades/dense.tres",
        "res://data/enemy_upgrades/elite.tres",
        "res://data/enemy_upgrades/four_sides.tres",
        "res://data/enemy_upgrades/split.tres",
        "res://data/enemy_upgrades/tough.tres",
        "res://data/enemy_upgrades/tracker.tres",
    };

    private void LoadPools()
    {
        foreach (var path in PlayerUpgradePaths)
        {
            var res = ResourceLoader.Load<PlayerUpgradeData>(path);
            if (res != null) _playerPool.Add(res);
            else GD.PushWarning($"[UpgradeService] 词条加载失败（跳过）：{path}");
        }

        foreach (var path in EnemyUpgradePaths)
        {
            var res = ResourceLoader.Load<EnemyUpgradeData>(path);
            if (res != null) _enemyPool.Add(res);
            else GD.PushWarning($"[UpgradeService] 词条加载失败（跳过）：{path}");
        }
    }

    // ==================== 抽取 ====================

    /// <summary>
    /// 抽 count 个不重复词条。
    /// 规则：满层过滤 → 名刀消耗过滤 → 平铺随机抽 → 保底至少 1 个机制类。
    /// </summary>
    public IReadOnlyList<Resource> PickChoices(bool forPlayer, int count = 3)
    {
        var result = new List<Resource>();

        if (forPlayer)
        {
            // 候选池：过滤满层（同名卡在 GameManager 已有层数 vs MaxStack）
            var candidates = new List<PlayerUpgradeData>();
            foreach (var c in _playerPool)
            {
                // 名刀特例：触发即消耗，永久移出池
                if (c.Stat == PlayerStat.FlagDeathblade && GameManager.I.DeathbladeConsumed)
                    continue;

                int owned = CountOwned(GameManager.I.PlayerUpgrades, c.DisplayName);
                if (owned >= c.MaxStack) continue;   // 已满层，不再出现

                candidates.Add(c);
            }

            Shuffle(candidates);

            // 抽 count 个
            for (int i = 0; i < candidates.Count && result.Count < count; i++)
                result.Add(candidates[i]);

            // 保底：池里还有机制类时，3 个选项至少 1 个机制类
            EnsureMechanic(candidates, result);
        }
        else
        {
            var candidates = new List<EnemyUpgradeData>();
            foreach (var c in _enemyPool)
            {
                int owned = CountOwned(GameManager.I.EnemyUpgrades, c.DisplayName);
                if (owned >= c.MaxStack) continue;

                candidates.Add(c);
            }

            Shuffle(candidates);

            for (int i = 0; i < candidates.Count && result.Count < count; i++)
                result.Add(candidates[i]);

            EnsureMechanic(candidates, result);
        }

        // TODO(词条调优)：「四向」×「追踪怪」互斥 —— GDD 标注"待实测，由策划定"，
        // 当前按不互斥实现。策划拍板后在这里加互斥过滤（同批选项中二选一）。

        return result;
    }

    /// <summary>数一下玩家/敌人已拥有列表里同名卡出现几次（即当前层数）。</summary>
    private static int CountOwned(List<PlayerUpgradeData> owned, string displayName)
    {
        int n = 0;
        foreach (var o in owned) if (o.DisplayName == displayName) n++;
        return n;
    }

    private static int CountOwned(List<EnemyUpgradeData> owned, string displayName)
    {
        int n = 0;
        foreach (var o in owned) if (o.DisplayName == displayName) n++;
        return n;
    }

    /// <summary>
    /// 保底规则（GDD §4.3）：池中仍有机制类时，三个选项至少 1 个是机制类。
    /// 实现：结果里没有机制类时，从候选里找出机制类换掉结果中的一个非机制类。
    /// </summary>
    private static void EnsureMechanic<T>(List<T> candidates, List<Resource> result) where T : Resource
    {
        bool hasMechanic = false;
        foreach (var r in result)
            if (IsMechanic(r)) { hasMechanic = true; break; }
        if (hasMechanic) return;

        // 结果全非机制 → 从候选（未进结果的）里找一个机制类换进来
        var inResult = new HashSet<Resource>(result);
        foreach (var c in candidates)
        {
            Resource res = c;
            if (inResult.Contains(res)) continue;
            if (!IsMechanic(res)) continue;

            // 换掉结果里的第一个非机制类
            for (int i = 0; i < result.Count; i++)
            {
                if (!IsMechanic(result[i])) { result[i] = res; return; }
            }
            result.Add(res);   // 结果不足 count 时直接补
            return;
        }
    }

    private static bool IsMechanic(Resource r) => r switch
    {
        PlayerUpgradeData p => p.IsMechanic,
        EnemyUpgradeData e => e.IsMechanic,
        _ => false,
    };

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Index(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ==================== 生效 ====================

    /// <summary>把选中的词条应用到对应 StatBlock（数值立即生效）。</summary>
    public void Apply(Resource upgrade)
    {
        if (upgrade is PlayerUpgradeData p)
        {
            GameManager.I.PlayerStats.AddModifier(new StatModifier(p.Stat, p.Op, p.Value));
            GameManager.I.PlayerUpgrades.Add(p);
        }
        else if (upgrade is EnemyUpgradeData en)
        {
            GameManager.I.EnemyStats.AddModifier(new StatModifier(en.Stat, en.Op, en.Value));
            GameManager.I.EnemyUpgrades.Add(en);
        }
    }

    /// <summary>按 DisplayName 在词条池里找一张卡（存档恢复 buff 用）。</summary>
    public Resource? FindByName(string displayName, bool forPlayer)
    {
        if (forPlayer)
        {
            foreach (var c in _playerPool) if (c.DisplayName == displayName) return c;
        }
        else
        {
            foreach (var c in _enemyPool) if (c.DisplayName == displayName) return c;
        }
        return null;
    }
}
