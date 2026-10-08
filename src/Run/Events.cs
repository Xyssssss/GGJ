namespace GGJ;

public readonly struct RoundStarted
{
    public readonly int Round;
    public RoundStarted(int round) { Round = round; }
}

public readonly struct RoundWon
{
    public readonly int Round;
    public RoundWon(int round) { Round = round; }
}

public readonly struct RoundLost
{
    public readonly int Round;
    public readonly int TotalDeaths;
    public RoundLost(int round, int totalDeaths) { Round = round; TotalDeaths = totalDeaths; }
}

/// <summary>弹出三选一。ForPlayer = true 是玩家线（失败），false 是敌人线（胜利）。</summary>
public readonly struct UpgradeOffered
{
    public readonly bool ForPlayer;
    public UpgradeOffered(bool forPlayer) { ForPlayer = forPlayer; }
}

/// <summary>三选一选完之后由 UI 发出。</summary>
public readonly struct UpgradeChosen
{
    public readonly bool ForPlayer;
    public UpgradeChosen(bool forPlayer) { ForPlayer = forPlayer; }
}

public readonly struct RunFinished
{
    public readonly int TotalDeaths;
    public readonly float Time;
    public readonly string Rank;
    public RunFinished(int totalDeaths, float time, string rank)
    {
        TotalDeaths = totalDeaths; Time = time; Rank = rank;
    }
}

/// <summary>名刀窗口开始。</summary>
public readonly struct DeathbladeStarted
{
    public readonly float Duration;
    public DeathbladeStarted(float duration) { Duration = duration; }
}

/// <summary>
/// 新一轮即将开始（在刷第一波之前）。用于清空上一轮残留的瞬态实体
/// （目前在途子弹）。⚠️ 必须在 spawner.BeginRound 之前发布——
/// 否则被三选一暂停冻结在出生点的遗留子弹会当帧秒杀新轮第 1 波。
/// Bus.Pub 是同步的：订阅者在此事件里清场，会在 RoundDirector 继续往下
/// 执行 BeginRound 的刷怪之前完成。
/// </summary>
public readonly struct RoundClearing
{
    public readonly int Round;
    public RoundClearing(int round) { Round = round; }
}
