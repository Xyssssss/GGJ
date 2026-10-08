using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 全局单例（Autoload: GameManager）。只存状态和引用，不写玩法逻辑。
/// 谁都可读；只有 Main 和 RoundDirector 可写。
/// </summary>
public partial class GameManager : Node
{
    public static GameManager I { get; private set; } = null!;

    // ---- 两条独立的成长线 ----
    public StatBlock PlayerStats { get; } = new();
    public StatBlock EnemyStats { get; } = new();

    // ---- 配置（Main 启动时注入，.tres 坏了也有代码默认值兜底）----
    public RunConfig Cfg { get; set; } = null!;
    public FeelConfig Feel { get; set; } = null!;
    public StringsData Strings { get; set; } = null!;

    // ---- 运行时状态 ----
    public Player? Player { get; set; }
    public int Round { get; set; } = 1;
    public int TotalDeaths { get; set; }
    public int TotalKills { get; set; }
    public float RunTime { get; set; }

    /// <summary>名刀窗口：玩家不死，但敌人照常被击杀（GDD §4.1.1，是 §3.4 的显式例外）。</summary>
    public bool DeathbladeActive { get; set; }
    public bool DeathbladeConsumed { get; set; }
    public bool HasDeathblade => !DeathbladeConsumed && PlayerStats.HasFlag(PlayerStat.FlagDeathblade);

    public List<PlayerUpgradeData> PlayerUpgrades { get; } = new();
    public List<EnemyUpgradeData> EnemyUpgrades { get; } = new();

    /// <summary>当前这一关"开始前"的玩家 buff 快照（DisplayName，可重复=层数）。用于存档。</summary>
    public List<string> RoundStartPlayerBuffs { get; } = new();

    /// <summary>当前这一关"开始前"的敌人 buff 快照。</summary>
    public List<string> RoundStartEnemyBuffs { get; } = new();

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;   // 暂停时仍需计时
    }

    public override void _Process(double delta)
    {
        // 三选一等暂停阶段不计入挑战用时
        if (!GetTree().Paused) RunTime += (float)delta;
    }

    public string T(string key) => Strings.Get(key);

    public void ResetRun()
    {
        Round = 1;
        TotalDeaths = 0;
        TotalKills = 0;
        RunTime = 0f;
        DeathbladeActive = false;
        DeathbladeConsumed = false;
        PlayerUpgrades.Clear();
        EnemyUpgrades.Clear();
        RoundStartPlayerBuffs.Clear();
        RoundStartEnemyBuffs.Clear();
        PlayerStats.ClearModifiers();   // 清空数值加成，避免跨局残留
        EnemyStats.ClearModifiers();
    }

    /// <summary>快照当前 buff 到 RoundStart（每关开始时调用，存档时保存"前 n-1 关的 buff"）。</summary>
    public void SnapshotRoundStartBuffs()
    {
        RoundStartPlayerBuffs.Clear();
        foreach (var u in PlayerUpgrades) if (u != null) RoundStartPlayerBuffs.Add(u.DisplayName);
        RoundStartEnemyBuffs.Clear();
        foreach (var u in EnemyUpgrades) if (u != null) RoundStartEnemyBuffs.Add(u.DisplayName);
    }
}
