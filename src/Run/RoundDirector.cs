using Godot;

namespace GGJ;

public enum RoundPhase
{
    Idle,
    Spawning,     // 波次刷怪中
    Fighting,     // 刷完了，等清场
    Upgrading,    // 三选一（游戏完全暂停）
    Deathblade,   // 名刀窗口
    Finished,
}

/// <summary>
/// 轮次状态机 —— 本游戏最重要的一块。
///
/// 胜利（双条件，缺一不可）：所有波次已刷完 &amp;&amp; 场上敌人数 == 0
///   只判断其中一个会提前误判胜利（常见于"最后一波刚刷出、还没加进计数"的那一帧）。
/// 失败：玩家血量归零（名刀窗口除外）。
/// **只有胜利才推进轮次；失败重打本轮，轮次不前进。**
/// </summary>
public partial class RoundDirector : Node
{
    private SpawnDirector _spawner = null!;
    private EnemyService _enemies = null!;

    private RoundPhase _phase = RoundPhase.Idle;
    private float _deathbladeTimer;

    public RoundPhase Phase => _phase;
    public int Round => GameManager.I.Round;
    public int TotalRounds => GameManager.I.Cfg.TotalRounds;

    public void Init(SpawnDirector spawner, EnemyService enemies)
    {
        _spawner = spawner;
        _enemies = enemies;
        Bus.Sub<UpgradeChosen>(this, OnUpgradeChosen);
    }

    public void StartRun()
    {
        GameManager.I.ResetRun();
        BeginRound();
    }

    /// <summary>继续存档：恢复进度与"前 n-1 关"的 buff，然后从第 Round 关重打。</summary>
    public void ResumeRun(SaveData data, UpgradeService upgrades)
    {
        GameManager.I.ResetRun();
        GameManager.I.Round = Mathf.Max(1, data.Round);
        GameManager.I.TotalDeaths = data.TotalDeaths;
        GameManager.I.RunTime = data.RunTime;

        // 恢复前 n-1 关累积的 buff（当前关的 buff 重新选，不恢复）
        foreach (var name in data.PlayerBuffs)
        {
            var r = upgrades.FindByName(name, forPlayer: true);
            if (r != null) upgrades.Apply(r);
        }
        foreach (var name in data.EnemyBuffs)
        {
            var r = upgrades.FindByName(name, forPlayer: false);
            if (r != null) upgrades.Apply(r);
        }

        BeginRound();
    }

    private void BeginRound()
    {
        // ⚠️ 必须在刷第一波之前清空在途子弹：上一轮末尾射出的子弹被三选一
        // 暂停冻结在右边缘（x≈1260，正好是新轮出生点），解除暂停的当帧会把
        // 新轮第 1 波当帧秒杀。Bus.Pub 同步，订阅者（BulletService）会立刻
        // 清完才继续往下执行 BeginRound 的刷怪。
        Bus.Pub(new RoundClearing(GameManager.I.Round));

        GameManager.I.Player?.ResetForRound();
        _enemies.ClearAll();
        GameManager.I.SnapshotRoundStartBuffs();   // 快照这一关开始前的 buff，供存档
        _spawner.BeginRound(GameManager.I.Round);
        _phase = RoundPhase.Spawning;
        Bus.Pub(new RoundStarted(GameManager.I.Round));
    }

    public override void _Process(double delta)
    {
        switch (_phase)
        {
            case RoundPhase.Spawning:
                if (_spawner.AllWavesSpawned) _phase = RoundPhase.Fighting;
                CheckLose();
                CheckWin();
                break;

            case RoundPhase.Fighting:
                CheckLose();
                CheckWin();
                break;

            case RoundPhase.Deathblade:
                _deathbladeTimer -= (float)delta;
                if (_deathbladeTimer <= 0f) EndDeathblade(success: false);
                else if (_enemies.AliveCount == 0) EndDeathblade(success: true);
                break;
        }
    }

    private void CheckWin()
    {
        if (!_spawner.AllWavesSpawned) return;
        if (_enemies.AliveCount != 0) return;

        _phase = RoundPhase.Upgrading;
        GameManager.I.DeathbladeActive = false;
        Bus.Pub(new RoundWon(GameManager.I.Round));

        // 最后一轮胜利 → 直接通关结算，不再弹"选 buff"（第八轮打通即通关）
        if (GameManager.I.Round >= TotalRounds)
        {
            GameManager.I.Round++;   // 推进到 TotalRounds+1，标记已通关（存档按此算总击杀）
            Finish();
            return;
        }

        OfferUpgrade(forPlayer: false);     // 胜利 → 强化敌人
    }

    private void CheckLose()
    {
        var p = GameManager.I.Player;
        if (p == null || !GodotObject.IsInstanceValid(p)) return;

        var hp = p.GetNodeOrNull<Health>("Health");
        if (hp == null || !hp.IsDead) return;

        if (GameManager.I.HasDeathblade)
        {
            StartDeathblade();              // 0 血不立即死，进入名刀窗口
            return;
        }

        _phase = RoundPhase.Upgrading;
        GameManager.I.TotalDeaths++;
        Bus.Pub(new RoundLost(GameManager.I.Round, GameManager.I.TotalDeaths));
        OfferUpgrade(forPlayer: true);      // 失败 → 强化玩家，重打本轮
    }

    // ---------------- 名刀 ----------------

    private void StartDeathblade()
    {
        float dur = GameManager.I.PlayerStats.Get(PlayerStat.DeathbladeWindow);

        _phase = RoundPhase.Deathblade;
        _deathbladeTimer = dur;
        GameManager.I.DeathbladeActive = true;
        GameManager.I.DeathbladeConsumed = true;      // 触发即消耗，无论胜负
        GameManager.I.Player?.GetNodeOrNull<Health>("Health")?.StartInvincible(dur);
        Bus.Pub(new DeathbladeStarted(dur));
    }

    /// <summary>
    /// 名刀窗口结束。成功 = 清空全部敌人（含未刷出的后续波次）→ 回满血、判胜、不计失败次数。
    /// 可选规则（策划未定）：名刀激活时剩余波次立即全部刷出，翻盘更干净。
    /// </summary>
    private void EndDeathblade(bool success)
    {
        GameManager.I.DeathbladeActive = false;
        GameManager.I.Player?.GetNodeOrNull<Health>("Health")?.FullHeal();
        _phase = RoundPhase.Upgrading;

        if (success)
        {
            Bus.Pub(new RoundWon(GameManager.I.Round));

            // 最后一轮名刀翻盘 → 直接通关结算
            if (GameManager.I.Round >= TotalRounds)
            {
                GameManager.I.Round++;
                Finish();
                return;
            }

            OfferUpgrade(forPlayer: false);
        }
        else
        {
            GameManager.I.TotalDeaths++;
            Bus.Pub(new RoundLost(GameManager.I.Round, GameManager.I.TotalDeaths));
            OfferUpgrade(forPlayer: true);
        }
    }

    // ---------------- 三选一 ----------------

    private void OfferUpgrade(bool forPlayer)
    {
        GetTree().Paused = true;            // 三选一时游戏完全暂停
        Bus.Pub(new UpgradeOffered(forPlayer));
    }

    /// <summary>UI 选完后回调。胜利进下一轮，失败重打本轮。</summary>
    private void OnUpgradeChosen(UpgradeChosen e)
    {
        GetTree().Paused = false;

        if (!e.ForPlayer)                   // 打赢了才推进轮次
        {
            GameManager.I.Round++;
            if (GameManager.I.Round > TotalRounds)
            {
                Finish();
                return;
            }
        }

        BeginRound();
    }

    private void Finish()
    {
        _phase = RoundPhase.Finished;
        Bus.Pub(new RunFinished(GameManager.I.TotalDeaths, GameManager.I.RunTime, Rank()));
    }

    /// <summary>评级按总失败次数（GDD §6）。这是抑制刷 buff 的关键锁扣，不要改。</summary>
    private static string Rank()
    {
        int d = GameManager.I.TotalDeaths;
        if (d <= 2)  return "S";
        if (d <= 4)  return "A";
        if (d <= 7)  return "B";
        if (d <= 12) return "C";
        return "D";
    }
}
