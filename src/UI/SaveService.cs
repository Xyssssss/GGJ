using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace GGJ;

/// <summary>一份存档（存单）的数据。字段对齐策划定的存单内容。</summary>
public class SaveData
{
    public int Version { get; set; } = 2;

    /// <summary>当前第几关（1-based）。未通关=从这关重打；已通关时 Round 已在胜利时 +1。</summary>
    public int Round { get; set; } = 1;

    /// <summary>总失败次数（= 给自己选的 buff 数量）。</summary>
    public int TotalDeaths { get; set; }

    /// <summary>击败敌人数（由第几关按每关固定敌人数累加得出）。</summary>
    public int TotalKills { get; set; }

    /// <summary>挑战已用时（秒）：从开局到结算 / 中途退出。</summary>
    public float RunTime { get; set; }

    /// <summary>结算评级（仅结算后有效；规则待定，先按总失败次数占位）。</summary>
    public string Rank { get; set; } = "";

    /// <summary>是否已通关结算。</summary>
    public bool Finished { get; set; }

    /// <summary>存档时间戳（Unix 秒），用于展示与排序。</summary>
    public long Timestamp { get; set; }

    /// <summary>前 n-1 关累积的玩家 buff（DisplayName 列表，可重复 = 层数）。</summary>
    public List<string> PlayerBuffs { get; set; } = new();

    /// <summary>前 n-1 关累积的敌人 buff（DisplayName 列表，可重复 = 层数）。</summary>
    public List<string> EnemyBuffs { get; set; } = new();
}

/// <summary>
/// 存档服务（建议设为 Autoload，跨场景常驻，顺序放在 GameManager 之后）。
/// 自动存档时机：结算（RunFinished）+ 关窗（含直接关闭后台）。
/// 存单内容：第几关 / 总失败次数 / 击败数 / 用时 / 评级。
/// ⚠️ 恢复进度（从第 N 关起）需要 P0：Main 启动时读 PendingResume、RoundDirector 加 ResumeRun。
/// </summary>
public partial class SaveService : Node
{
    public static SaveService I { get; private set; } = null!;

    public const int SlotCount = 5;

    /// <summary>当前活跃存档位（自动存档写入这里）。</summary>
    public int ActiveSlot { get; set; } = 0;

    /// <summary>菜单点“继续”后写入的待恢复数据；P0 在 Main 启动时消费。</summary>
    public SaveData? PendingResume { get; private set; }

    private bool _runStarted;

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        // 有轮次开始才认为“真的开了一局”，避免在菜单里关窗也把空进度写进存档
        Bus.Sub<RoundStarted>(this, _ => _runStarted = true);
        Bus.Sub<RunFinished>(this, OnRunFinished);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            if (_runStarted) SaveNow();
            GetTree().Quit();
        }
    }

    private void OnRunFinished(RunFinished e) => SaveNow(finished: true);

    // ---------- 菜单动作 ----------

    /// <summary>新开一局：清掉该存档位并标记为当前位。</summary>
    public void StartNew(int slot)
    {
        ActiveSlot = slot;
        PendingResume = null;
        _runStarted = false;
        Delete(slot);
    }

    /// <summary>继续：读档并标记待恢复。返回该位是否有存档。通关存档视为从头开始（不恢复进度）。</summary>
    public bool Continue(int slot)
    {
        var data = Read(slot);
        if (data == null) return false;
        ActiveSlot = slot;
        // 通关存档：下次从头开始新局（成绩比较会保护旧成绩，失败不覆盖）
        PendingResume = data.Finished ? null : data;
        _runStarted = false;
        return true;
    }

    /// <summary>选新游戏用的存档位：优先空位；全满则淘汰评分最低（→用时最久→最早）的。</summary>
    public int PickNewSlot()
    {
        for (int i = 0; i < SlotCount; i++) if (!Has(i)) return i;
        return FindSlotToEvict();
    }

    /// <summary>存档全满时，找出该淘汰的位：评分最低 → 用时最久 → 时间戳最早。</summary>
    public int FindSlotToEvict()
    {
        int worst = 0;
        int worstPriority = int.MaxValue;
        float worstTime = float.MinValue;
        long worstStamp = long.MaxValue;

        for (int i = 0; i < SlotCount; i++)
        {
            var d = Read(i);
            if (d == null) continue;

            int prio = Rating.Priority(d.Rank);
            bool replace =
                prio < worstPriority ||
                (prio == worstPriority && d.RunTime > worstTime) ||
                (prio == worstPriority && d.RunTime == worstTime && d.Timestamp < worstStamp);

            if (replace)
            {
                worst = i;
                worstPriority = prio;
                worstTime = d.RunTime;
                worstStamp = d.Timestamp;
            }
        }
        return worst;
    }

    /// <summary>P0 在 Main 启动时调用：取走待恢复数据（取走即清空，避免重复恢复）。</summary>
    public SaveData? TakePendingResume()
    {
        var d = PendingResume;
        PendingResume = null;
        return d;
    }

    // ---------- 存 / 读 ----------

    public void SaveNow(bool finished = false, string? rank = null)
    {
        var gm = GameManager.I;
        if (gm == null) return;

        int round = Math.Max(1, gm.Round);          // 内部值：通关时 = TotalRounds + 1
        int kills = ComputeKills(round, gm.Cfg);    // 前 round-1 关的击杀（通关 = 总击杀）

        var data = new SaveData
        {
            // 通关存档 Round 展示为最后一关（TotalRounds），而不是内部推进后的 +1
            Round = finished ? Math.Min(round, gm.Cfg.TotalRounds) : round,
            TotalDeaths = gm.TotalDeaths,
            TotalKills = kills,
            RunTime = gm.RunTime,
            Rank = rank ?? Rating.RankOf(gm.TotalDeaths),
            Finished = finished,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PlayerBuffs = new List<string>(gm.RoundStartPlayerBuffs),
            EnemyBuffs = new List<string>(gm.RoundStartEnemyBuffs),
        };

        var old = Read(ActiveSlot);

        if (finished)
        {
            // 通关：旧档也是通关成绩时，只在本次更好才覆盖
            if (old != null && old.Finished && !IsBetter(data, old)) return;
        }
        else
        {
            // 未完成：旧档是通关成绩时，不覆盖（保留旧成绩）
            if (old != null && old.Finished) return;
        }

        Write(ActiveSlot, data);
    }

    /// <summary>比较成绩：评级更高优先；评级相同比用时（短更好）。</summary>
    private static bool IsBetter(SaveData a, SaveData b)
    {
        int pa = Rating.Priority(a.Rank);
        int pb = Rating.Priority(b.Rank);
        if (pa != pb) return pa > pb;
        return a.RunTime < b.RunTime;
    }

    // ---------- 静态文件读写（菜单也能用，不依赖实例） ----------

    public static void Write(int slot, SaveData data)
    {
        try
        {
            File.WriteAllText(SlotPath(slot), JsonSerializer.Serialize(data));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SaveService] 写档失败：{e.Message}");
        }
    }

    public static SaveData? Read(int slot)
    {
        try
        {
            var path = SlotPath(slot);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static bool Has(int slot) => File.Exists(SlotPath(slot));

    public static void Delete(int slot)
    {
        var path = SlotPath(slot);
        if (!File.Exists(path)) return;
        try { File.Delete(path); } catch { /* 忽略 */ }
    }

    private static string SlotPath(int slot) =>
        ProjectSettings.GlobalizePath($"user://ggj_save_{slot}.json");

    // ---------- 辅助 ----------

    /// <summary>击败数 = 已通关（1..round-1）各关固定敌人数之和。</summary>
    private static int ComputeKills(int round, RunConfig? cfg)
    {
        var c = cfg ?? RunConfig.CreateDefault();
        int kills = 0;
        for (int r = 1; r < round; r++)
        {
            var w = c.GetWave(r);
            kills += w.WaveCount * w.EnemiesPerWave;
        }
        return kills;
    }
}
