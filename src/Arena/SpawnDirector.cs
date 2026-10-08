using Godot;

namespace GGJ;

/// <summary>
/// 波次刷怪导演。**定时推进**：到点就刷，不等上一波清完（GDD §3.7，刻意的压迫感）。
/// 词条效果：四向出生、精英、追踪怪——均已实现。
/// </summary>
public partial class SpawnDirector : Node
{
    // 普通怪基础体型倍率（>1 = 变大）。只作用于普通波次怪（SkinKind.Normal），
    // 精英/追踪/马蜂窝/分裂小怪不受影响；敌人线"庞大"词条在此之上再乘。
    private const float NormalEnemyVisualScale = 1.2f;

    private WaveConfig _wave = WaveConfig.Create(3, 3, 5f);
    private int _round = 1;
    private int _wavesSpawned;
    private float _timer;
    private bool _running;

    public bool AllWavesSpawned => _wavesSpawned >= _wave.WaveCount;
    public int WavesSpawned => _wavesSpawned;
    public int WaveCount => _wave.WaveCount;

    public void BeginRound(int round)
    {
        _round = round;
        _wave = GameManager.I.Cfg.GetWave(round);
        _wavesSpawned = 0;
        _running = true;
        SpawnWave();            // 首波立即刷出，无延迟；计时器在 SpawnWave 末尾重置
    }

    public void Stop() => _running = false;

    public override void _Process(double delta)
    {
        if (!_running || AllWavesSpawned) return;

        _timer -= (float)delta;
        if (_timer > 0f) return;

        SpawnWave();
    }

    /// <summary>密集词条在这里生效：刷怪间隔 -X%。</summary>
    private float WaveInterval =>
        _wave.WaveInterval * (1f - GameManager.I.EnemyStats.Get(EnemyStat.SpawnIntervalReduction));

    private void SpawnWave()
    {
        _wavesSpawned++;

        var st = GameManager.I.EnemyStats;
        int count = _wave.EnemiesPerWave + (int)st.Get(EnemyStat.EnemiesPerWaveBonus);
        int hp = (int)st.Get(EnemyStat.HP);
        float scale = st.Get(EnemyStat.BodyScale);

        // 四向出生：词条开启时每只敌人随机挑一条边出生；关闭时维持原"右边缘出生"。
        bool fourSides = st.HasFlag(EnemyStat.FlagSpawnFourSides);

        for (int i = 0; i < count; i++)
        {
            // 开启：Rng.RangeInt(0,4) 随机选边（0..3 对应 Right/Left/Top/Bottom）
            // 关闭：固定右边缘（原行为）
            var edge = fourSides
                ? (ArenaBounds.Edge)Rng.RangeInt(0, 4)
                : ArenaBounds.Edge.Right;

            // 开启：朝内 ±45° 扇形随机方向（策划案"行动方向改为随机"）；
            // 关闭：垂直向内（右边缘 → 向左，原行为）
            Vector2 dir = fourSides
                ? RandomInwardDirection(edge)
                : ArenaBounds.DefaultDirFrom(edge);

            Bus.Pub(new SpawnEnemyRequest
            {
                Position  = ArenaBounds.RandomPointOnEdge(edge),
                Direction = dir,
                SpeedMul  = 1f,
                HP        = hp,
                Scale     = scale * NormalEnemyVisualScale,
                IsTracker = false,
                CanSplit  = false,   // 普通怪不是分裂源：分裂职能收口到独立马蜂窝个体（见 TrySpawnSplitters）
                SkinKind  = EnemySkinKind.Normal,   // 鸟/虫/甲虫随机换皮，不受分裂词条影响
            });
        }

        Bus.Pub(new WaveStarted(_wavesSpawned, count));

        // ---- 精英怪：每轮每波按概率额外刷 1 个，不占普通波次 count ----
        // ⚠️ 策划案规则：是"每轮每波"都判，不是"每轮只刷 1 个"。
        TrySpawnElite(st);

        // ---- 马蜂窝（分裂怪）：分裂词条开启后，每波额外刷几只独立母体 ----
        TrySpawnSplitters(st);

        // ---- 追踪怪：每波额外刷 TrackerCount 个，持续追踪玩家 ----
        // ⚠️ 血量/速度恒定读 TrackerHP/TrackerSpeed，免疫敌人线所有强化
        TrySpawnTrackers(st);

        // ⚠️ 必须在这里重置计时器。若留给下一帧去减，0 - delta &lt; 0 会立刻再刷一波，
        //    表现成"首波和第二波同时出现"。
        _timer = WaveInterval;

        if (AllWavesSpawned)
        {
            _running = false;
            Bus.Pub(new AllWavesSpawned(_round));
        }
    }

    /// <summary>
    /// 四向出生时生成"大致朝场内"的随机方向。
    /// 以垂直向内为基准 ±45° 扇形随机：既满足策划案"方向随机"，
    /// 又保证一定朝场内飞（完全随机会有敌人出生后立即朝外飞出屏，体验差）。
    /// </summary>
    private static Vector2 RandomInwardDirection(ArenaBounds.Edge edge)
    {
        // 各边缘的垂直朝内基准角（度）：0=右、90=下（Godot 2D Y 轴向下，正角度=顺时针）
        float baseAngleDeg = edge switch
        {
            ArenaBounds.Edge.Right  => 180f,   // 右边缘出生 → 基准朝左
            ArenaBounds.Edge.Left   => 0f,     // 左边缘出生 → 基准朝右
            ArenaBounds.Edge.Top    => 90f,    // 上边缘出生 → 基准朝下
            ArenaBounds.Edge.Bottom => -90f,   // 下边缘出生 → 基准朝上
            _                       => 180f,
        };

        float offset = Rng.Range(-45f, 45f);               // ±45° 扇形随机
        float angle = Mathf.DegToRad(baseAngleDeg + offset);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    /// <summary>
    /// 精英怪（策划案"巨型个体"）：每轮每波按 EliteChance 概率额外刷 1 个。
    /// 属性：血量 HP×EliteHPMul(5)、移速×EliteSpeedMul(0.4 极慢)、体积×2。
    /// 换皮：精英池只剩鸽子（考拉已移作追踪怪造型，见 Tracker）。
    /// 出生边复用四向逻辑（未开四向时从右边缘出生）。
    /// </summary>
    private void TrySpawnElite(StatBlock st)
    {
        if (!st.HasFlag(EnemyStat.FlagElite)) return;

        float chance = st.Get(EnemyStat.EliteChance);
        if (!Rng.Chance(chance)) return;

        // 出生边 + 方向：复用四向逻辑保持一致
        bool fourSides = st.HasFlag(EnemyStat.FlagSpawnFourSides);
        var edge = fourSides
            ? (ArenaBounds.Edge)Rng.RangeInt(0, 4)
            : ArenaBounds.Edge.Right;
        Vector2 dir = fourSides
            ? RandomInwardDirection(edge)
            : ArenaBounds.DefaultDirFrom(edge);

        // 精英属性 = 基础值 × 词条倍率
        int baseHP = (int)st.Get(EnemyStat.HP);
        int eliteHP = Mathf.Max(1, (int)(baseHP * st.Get(EnemyStat.EliteHPMul)));   // 防 0 血
        float speedMul = st.Get(EnemyStat.EliteSpeedMul);
        float baseScale = st.Get(EnemyStat.BodyScale);
        // P2-16：体积倍率数据驱动（EnemyStat.EliteScaleMul，基础值在 Main.InitStats 设置）
        float scaleMul = st.Get(EnemyStat.EliteScaleMul);

        Bus.Pub(new SpawnEnemyRequest
        {
            Position  = ArenaBounds.RandomPointOnEdge(edge),
            Direction = dir,
            SpeedMul  = speedMul,
            HP        = eliteHP,
            Scale     = baseScale * scaleMul,
            IsTracker = false,
            CanSplit  = false,   // 精英不是分裂源：分裂职能收口到独立马蜂窝个体（见 TrySpawnSplitters）
            SkinKind  = EnemySkinKind.Elite,   // 换皮：精英只出鸽子（考拉移作追踪怪），普通怪永不出现鸽子皮
        });
    }

    /// <summary>
    /// 马蜂窝（分裂怪）：分裂词条（FlagSplit）开启后，每波**额外**刷出几只独立母体，
    /// 用马蜂窝造型，只有它死亡才裂出 2 只马蜂（EnemyService.SpawnSplit）。
    /// 普通怪 / 精英 / 追踪怪都不再参与分裂 —— 分裂职能收口到这种"单独个体"上，
    /// 避免"全屏怪集体变马蜂窝"的观感（P2 美术验收反馈）。
    /// </summary>
    private void TrySpawnSplitters(StatBlock st)
    {
        bool enabled = EnemyService.SplitEnabled;   // 分裂词条（FlagSplit）开启才生效
        if (!enabled) return;

        var cfg = GameManager.I.Cfg;
        int n = cfg.SplitHivePerWave;
        if (n <= 0) return;

        int baseHP = (int)st.Get(EnemyStat.HP);
        // 母体比普通怪耐打（倍率在 run_config.tres），否则一枪就碎、马蜂根本来不及"破巢而出"
        int hiveHP = Mathf.Max(1, (int)(baseHP * cfg.SplitHiveHPMul));
        float baseScale = st.Get(EnemyStat.BodyScale);

        bool fourSides = st.HasFlag(EnemyStat.FlagSpawnFourSides);

        for (int i = 0; i < n; i++)
        {
            var edge = fourSides
                ? (ArenaBounds.Edge)Rng.RangeInt(0, 4)
                : ArenaBounds.Edge.Right;
            Vector2 dir = fourSides
                ? RandomInwardDirection(edge)
                : ArenaBounds.DefaultDirFrom(edge);

            Bus.Pub(new SpawnEnemyRequest
            {
                Position  = ArenaBounds.RandomPointOnEdge(edge),
                Direction = dir,
                SpeedMul  = cfg.SplitHiveSpeedMul,   // 巢慢速漂浮
                HP        = hiveHP,
                Scale     = baseScale,
                IsTracker = false,
                CanSplit  = true,                    // 唯一的死亡分裂源；裂出的马蜂由 EnemyService 设 false
                SkinKind  = EnemySkinKind.Hive,      // 马蜂窝（beehome_idle）造型
            });
        }
    }

    /// <summary>
    /// 追踪怪（策划案"嗅探者"）：每波额外刷 TrackerCount 个，持续追踪玩家。
    /// ⚠️ 血量/速度恒定读 TrackerHP/TrackerSpeed，❌ 不读 HP/MoveSpeed——
    ///    免疫敌人线所有强化（EnemyBase.Configure 与 OnBounced 里已处理）。
    /// </summary>
    private void TrySpawnTrackers(StatBlock st)
    {
        if (!st.HasFlag(EnemyStat.FlagTracker)) return;

        int count = (int)st.Get(EnemyStat.TrackerCount);
        if (count <= 0) return;

        int trackerHP = Mathf.Max(1, (int)st.Get(EnemyStat.TrackerHP));   // 策划案初值 1
        bool fourSides = st.HasFlag(EnemyStat.FlagSpawnFourSides);

        for (int i = 0; i < count; i++)
        {
            // 追踪怪会主动找玩家，出生边/方向只影响出场瞬间；复用四向逻辑保持一致
            var edge = fourSides
                ? (ArenaBounds.Edge)Rng.RangeInt(0, 4)
                : ArenaBounds.Edge.Right;
            Vector2 dir = fourSides
                ? RandomInwardDirection(edge)
                : ArenaBounds.DefaultDirFrom(edge);

            Bus.Pub(new SpawnEnemyRequest
            {
                Position  = ArenaBounds.RandomPointOnEdge(edge),
                Direction = dir,
                SpeedMul  = 1f,      // 追踪怪速度由 EnemyBase 读 TrackerSpeed 决定，此字段不生效
                HP        = trackerHP,
                Scale     = 1f,      // 追踪怪体积恒定，不随 BodyScale 变化
                IsTracker = true,
                CanSplit  = false,   // 追踪怪恒定属性，不参与敌人线强化（不分裂）
                SkinKind  = EnemySkinKind.Tracker,   // 追踪怪固定考拉造型（考拉已从精英池移除）
            });
        }
    }
}
