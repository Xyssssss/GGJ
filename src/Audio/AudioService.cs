using System;
using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 全局音效服务（Autoload: AudioService，跨场景常驻；ProcessMode=Always，暂停时仍能响）。
/// 其他模块用 Bus.Pub(new SfxRequest { Key = ... }) 请求播放，Key 是逻辑名。
/// 长音（精英/蜂群氛围）不通过 SfxRequest，而是由 AudioService 订阅敌人出生/消失事件自行启停。
///
/// ⚠️ 美术/音频替换文件即可换皮：保持 SoundMap 里文件名不变，替换同名文件（代码零改动）。
///    加载时按 .wav → .mp3 → .ogg 顺序探测，缺失的 key 只告警一次、不影响运行。
///
/// ⚠️ Godot 官方不支持 .flac 导入：炮弹素材若仍为 .flac，需先转成 .wav/.mp3/.ogg 放回 audio/，
///    转换完成后本服务自动加载，无需再改代码。
/// </summary>
public partial class AudioService : Node
{
    public static AudioService I { get; private set; } = null!;

    // 逻辑 key → 音频文件名（不含扩展名）。
    // 实装范围 = 本轮策划案要求且素材已就绪的音效。新增 .wav 只在这里登记即可。
    private static readonly (string Key, string File)[] SoundMap =
    {
        ("shoot", "炮弹射出音效"),   // ⚠️ 源文件是 .flac（Godot 不支持），转 wav/mp3/ogg 后即自动生效
        ("laser", "激光射出音效"),
        ("hit",   "子弹命中音效"),
        ("ui",    "UI点击音效"),
        // 循环氛围音：不占短音效声道，按"场上还有目标就响、清空就停"的引用计数启停
        ("elite", "精英敌人出没音效"),   // 有精英在场 → 循环；全部精英消灭/清场 → 停
        ("swarm", "马蜂窝以及蜜蜂出没音效"), // 有马蜂窝或马蜂在场 → 循环；全部清场 → 停
    };

    /// <summary>上面这些 key 里属于"长循环氛围音"的，加载为单实例常驻循环声道。</summary>
    private static readonly HashSet<string> LoopKeys = new() { "elite", "swarm" };

    /// <summary>循环氛围音各自的音量（dB，0 = 原始音量，负数越小越轻）。未登记的 key 默认 0。</summary>
    private static readonly Dictionary<string, float> LoopVolumeDb = new()
    {
        { "swarm", -11f },  // 蜂群循环声偏吵，压低一档
        { "elite", -6f },   // 精英出没声稍微压低一点（幅度小于蜂群）
    };

    /// <summary>每类短音效的并行声道数。连续快速射击/命中时声道轮播，避免互相打断/丢音。</summary>
    private const int VoicesPerSound = 4;

    /// <summary>key → 两次播放的最小间隔（毫秒）。0 = 不限。命中音给一个短节流，防"一发子弹同帧穿一群敌人"瞬时叠加爆音。</summary>
    private static readonly Dictionary<string, ulong> MinGapMs = new()
    {
        { "hit", 40 },
    };

    /// <summary>key → 播放起始偏移（秒）。部分素材开头带一段静音，从头播要等静音放完才出声、
    /// 点击后有"延迟感"；从偏移处直接起播可消除。未登记 = 0（从头播）。</summary>
    private static readonly Dictionary<string, float> StartOffsetSec = new()
    {
        { "ui", 0.5f },   // UI点击音效前段约 0.5s 静音，从 0.5s 起播
    };

    private readonly Dictionary<string, List<AudioStreamPlayer>> _voices = new();
    private readonly Dictionary<string, int> _nextVoice = new();
    private readonly Dictionary<string, ulong> _lastPlayed = new();

    // ---- 循环氛围音（elite/swarm）：每类只有 1 个常驻播放器，引用计数归零才停 ----
    private readonly Dictionary<string, AudioStreamPlayer> _loops = new();
    private readonly Dictionary<string, bool> _loopOn = new();
    private readonly Dictionary<string, int> _loopRefs = new();   // 场上关联实体的实时计数

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;   // 暂停（三选一 / 结算）时 UI 音效仍能响
        LoadAll();
        Bus.Sub<SfxRequest>(this, OnSfx);
        // 循环氛围音：跟随敌人实体出生/消失启停（elite=精英，swarm=马蜂窝/马蜂）
        Bus.Sub<EnemySpawned>(this, OnEnemySpawned);
        Bus.Sub<EnemyDespawned>(this, OnEnemyDespawned);
        // 游戏对局 BGM：随轮次推进切换强度（P2-18 附加）
        Bus.Sub<RoundStarted>(this, OnRoundStarted);
        Bus.Sub<RunFinished>(this, OnRunFinished);
    }

    private void LoadAll()
    {
        foreach (var (key, file) in SoundMap)
        {
            var stream = LoadStream(file);
            if (stream == null)
            {
                GD.PushWarning($"[AudioService] 找不到音效 audio/{file}（已探测 .wav/.mp3/.ogg）。" +
                               (file == "炮弹射出音效" ? "素材当前是 .flac，Godot 不支持，请转成 wav/mp3/ogg 后放入 audio/。" : ""));
                continue;
            }

            if (LoopKeys.Contains(key))
            {
                // 循环氛围音：单实例常驻，播完自动从头循环，直到 StopLoop 显式停
                var lp = new AudioStreamPlayer { Stream = stream };
                lp.VolumeDb = LoopVolumeDb.GetValueOrDefault(key, 0f);   // 按表设置音量（默认原声）
                lp.ProcessMode = ProcessModeEnum.Always;
                lp.Finished += () => { if (_loopOn[key]) lp.Play(); };
                AddChild(lp);
                _loops[key] = lp;
                _loopOn[key] = false;
                _loopRefs[key] = 0;
                continue;
            }

            var list = new List<AudioStreamPlayer>(VoicesPerSound);
            for (int i = 0; i < VoicesPerSound; i++)
            {
                var p = new AudioStreamPlayer { Stream = stream };
                p.ProcessMode = ProcessModeEnum.Always;
                AddChild(p);
                list.Add(p);
            }
            _voices[key] = list;
        }
    }

    private static AudioStream? LoadStream(string file)
    {
        foreach (var ext in new[] { ".wav", ".mp3", ".ogg" })
        {
            string path = $"res://audio/{file}{ext}";
            if (!ResourceLoader.Exists(path)) continue;
            return GD.Load<AudioStream>(path);
        }
        return null;
    }

    private void OnSfx(SfxRequest r)
    {
        if (string.IsNullOrEmpty(r.Key)) return;
        if (!_voices.TryGetValue(r.Key, out var list) || list.Count == 0) return;

        // 节流：高并发 key 限制两次播放的最小间隔，防止同帧多目标命中瞬时叠加爆音
        if (MinGapMs.TryGetValue(r.Key, out ulong gap) && gap > 0)
        {
            ulong now = Time.GetTicksMsec();
            if (_lastPlayed.TryGetValue(r.Key, out ulong last) && now - last < gap) return;
            _lastPlayed[r.Key] = now;
        }

        // 优先找空闲声道；全部忙时轮询覆盖最早开始的，保证连续触发不丢音
        // 起始偏移：跳过素材前段静音，避免点击后"延迟感"
        float offset = StartOffsetSec.TryGetValue(r.Key, out float o) ? o : 0f;

        int start = _nextVoice.TryGetValue(r.Key, out int n) ? n : 0;
        for (int i = 0; i < list.Count; i++)
        {
            int idx = (start + i) % list.Count;
            if (!list[idx].Playing)
            {
                list[idx].Play(offset);
                _nextVoice[r.Key] = (idx + 1) % list.Count;
                return;
            }
        }
        list[start].Play(offset);
        _nextVoice[r.Key] = (start + 1) % list.Count;
    }

    // ------------------------------------------------------------------
    // 循环氛围音：随场上目标实体增减引用计数，0→1 起播，>0→0 停
    // ------------------------------------------------------------------

    private void OnEnemySpawned(EnemySpawned e)
    {
        if (e.Enemy is not EnemyBase eb) return;
        string? key = LoopKeyOf(eb.Kind);
        if (key == null) return;

        int n = _loopRefs[key] + 1;
        _loopRefs[key] = n;
        if (n == 1) StartLoop(key);
    }

    private void OnEnemyDespawned(EnemyDespawned e)
    {
        if (e.Enemy is not EnemyBase eb) return;
        string? key = LoopKeyOf(eb.Kind);
        if (key == null) return;

        // 防对象池复用竞态：上一只的皮肤可能已被 Configure 覆盖，按本次实体实际 Kind 减，
        // 只对"确实登记过对应类型"的 key 生效（_loopRefs 兜底到 0，不会负）
        int n = Math.Max(0, _loopRefs[key] - 1);
        _loopRefs[key] = n;
        if (n == 0) StopLoop(key);
    }

    /// <summary>皮肤类别 → 对应氛围音 key；不关心出没音的类别返回 null。</summary>
    private static string? LoopKeyOf(EnemySkinKind kind) => kind switch
    {
        EnemySkinKind.Elite => "elite",
        EnemySkinKind.Hive or EnemySkinKind.Bee => "swarm",
        _ => null,
    };

    private void StartLoop(string key)
    {
        if (!_loopOn.TryGetValue(key, out bool on) || on) return;
        if (!_loops.TryGetValue(key, out var p)) return;
        _loopOn[key] = true;
        p.Play();
    }

    private void StopLoop(string key)
    {
        if (!_loopOn.TryGetValue(key, out bool on) || !on) return;
        if (!_loops.TryGetValue(key, out var p)) return;
        _loopOn[key] = false;
        p.Stop();
    }

    /// <summary>停掉所有循环氛围音并清零计数。战斗场景退出时调用，防止切走后声音残留。</summary>
    public void StopAllLoops()
    {
        foreach (var key in LoopKeys)
        {
            if (_loopOn.TryGetValue(key, out bool on) && on) StopLoop(key);
            if (_loopRefs.ContainsKey(key)) _loopRefs[key] = 0;
        }
    }

    // ==================== 游戏对局 BGM（P2-18 附加：随轮次推进切强度）====================
    //
    // 曲目梯度（audio/bgm/，森林自然风，由 Tests/GenBgm4.cs 程序合成，可随时替换同名文件）：
    //   bgm_game_1.wav  96 BPM  C大调 悠然漫步 → 轮 1~2
    //   bgm_game_2.wav 104 BPM  F大调 溪流轻快 → 轮 3~4
    //   bgm_game_3.wav 112 BPM  G大调 林间跳跃 → 轮 5~6
    //   bgm_game_4.wav 120 BPM  D大调 生机奔跑 → 轮 7~8
    //
    // 生命周期：RoundStarted（每关开始/切关）→ 换对应强度曲循环；
    // 同一关内失败重打不重启（不打断氛围）；RunFinished（整局结束）→ 停。
    // 主菜单 BGM（UIbgm）由 MainMenu 场景自持，切进游戏场景时自动释放停止。

    private AudioStreamPlayer? _gameBgm;
    private string? _currentGameBgmKey;

    private void OnRoundStarted(RoundStarted e)
    {
        // 轮次 → 强度档位：轮 1-2 → 曲 1，3-4 → 曲 2，5-6 → 曲 3，7-8 → 曲 4
        int idx = Mathf.Clamp((e.Round - 1) / 2 + 1, 1, 4);
        PlayGameBgm($"bgm_game_{idx}");
    }

    private void OnRunFinished(RunFinished e)
    {
        _gameBgm?.Stop();
        _currentGameBgmKey = null;
    }

    private void PlayGameBgm(string key)
    {
        if (_currentGameBgmKey == key) return;   // 同关重打不重启（不打断氛围）

        string path = $"res://audio/bgm/{key}.wav";
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"[AudioService] 游戏 BGM 缺失（跳过）：{path}");
            return;
        }

        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;   // 无缝循环
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / 2;
        }

        if (_gameBgm == null)
        {
            _gameBgm = new AudioStreamPlayer
            {
                VolumeDb = +6f,                              // 对局 BGM 音量（-2 → +6：大胆放大；若源素材本身偏轻，此档约翻倍响度）
                ProcessMode = ProcessModeEnum.Always,         // 三选一暂停时音乐继续
            };
            AddChild(_gameBgm);
            _gameBgm.Finished += () => _gameBgm.Play();       // 兜底循环
        }

        _gameBgm.Stream = stream;
        _gameBgm.Play();
        _currentGameBgmKey = key;
        GD.Print($"[AudioService] 对局 BGM：{key}");
    }
}
