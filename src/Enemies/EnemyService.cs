using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 敌人服务：对象池 + 存活计数（胜利条件的"场上清空"判定靠 AliveCount）。
/// 由 Main 创建。其他模块只发 SpawnEnemyRequest，不直接实例化敌人。
/// </summary>
public partial class EnemyService : Node
{
    // P2-17：分裂小怪参数（SplitHP/SplitSpeedMul/SplitScale）已搬进 run_config.tres，
    // 运行时读 GameManager.I.Cfg.Xxx（纯手感值，不会被词条修改，不走 StatBlock）。

    private Pool<EnemyBase>? _pool;
    private readonly List<EnemyBase> _active = new();

    // 分裂"排队未落地"的请求：死亡处理在物理回调链里只记账不建实（立即建会
    // 在 Rent→AddChild 时撞 "Can't change this state while flushing queries"），
    // 实体由 _Process（idle 阶段，远离物理 flush）统一生成。
    private readonly Queue<SpawnEnemyRequest> _pending = new();

    /// <summary>
    /// 场上活着的敌人数 —— 胜利双条件之一。
    /// ⚠️ 必须计入 _pending：分裂在物理回调里记账但实体要到下一帧 idle 才落地，
    /// 若只算 _active，RoundDirector 在落地前查到 ==0 会误判"清场胜利"，最后一击
    /// 的马蜂窝就裂不出来了。
    /// </summary>
    public int AliveCount => _active.Count + _pending.Count;

    public override void _Ready()
    {
        Bus.Sub<SpawnEnemyRequest>(this, OnSpawn);
        Bus.Sub<EntityDied>(this, OnEntityDied);
    }

    /// <summary>
    /// 分裂词条是否全局生效（FlagSplit）。
    /// SpawnDirector 用它决定"每波是否额外刷独立马蜂窝个体"，
    /// 死亡判定用它配合 CanSplit（只有马蜂窝为 true）触发裂巢。
    /// 共用这一处，保证"刷出来"和"会裂"永远同步。
    /// </summary>
    public static bool SplitEnabled => GameManager.I.EnemyStats.HasFlag(EnemyStat.FlagSplit);

    public void Init(PackedScene? scene)
    {
        if (scene == null)
        {
            GD.PushWarning("[EnemyService] 没有 EnemyScene，刷怪功能关闭。检查 data/run_config.tres。");
            return;
        }
        _pool = new Pool<EnemyBase>(scene, this);

        // P2-17：EliteScaleMul 的 SetBase 已挪进 Main.InitStats（基础值只在 Main 设置一次）。
    }

    private void OnSpawn(SpawnEnemyRequest r)
    {
        if (_pool == null) return;

        // 注：存活上限机制已按策划要求移除（P2-15），场上敌人数量不再设限。
        // _active 列表仍保留 —— AliveCount（胜利条件的"场上清空"判定）依赖它。
        var e = _pool.Rent();
        e.Configure(r.Position, r.Direction, r.SpeedMul, r.HP, r.Scale, r.IsTracker, r.CanSplit, r.SkinKind);
        _active.Add(e);
        Bus.Pub(new EnemySpawned(e));
    }

    private void OnEntityDied(EntityDied d)
    {
        if (d.Target is not EnemyBase eb) return;
        // ⚠️ 幂等：首次死亡处理后 Despawn 会把 Active 置 false；同帧重复的死亡事件
        //   （激光无限穿透 / 同一敌人被多颗子弹结算）直接忽略，否则会二次 Despawn
        //   → Pool 报"重复归还"，且分裂怪会重复裂巢、重复发 EnemyDespawned。
        if (!eb.Active) return;

        // ---- 分裂：只有马蜂窝（CanSplit=true）被打死才裂，在 despawn 前刷 2 只马蜂 ----
        // 双重判断：CanSplit（实例级，只有马蜂窝为 true）+ SplitEnabled（词条开启才裂）
        Vector2 deathPos = eb.GlobalPosition;   // 先记位置，despawn 后节点仍有效但稳妥起见提前取
        if (eb.CanSplit && SplitEnabled)
        {
            EnqueueSplit(deathPos);   // ⚠️ 只记账：此链在物理回调里，实建交给 _Process（见 EnqueueSplit 注释）
        }

        Despawn(eb);
    }

    /// <summary>
    /// 死亡位置排队裂 2 只小怪：1 血、速度 150%、体积减半、方向随机散开。
    /// 小怪 CanSplit = false —— 防止"裂→死→再裂"无限套娃。
    ///
    /// ⚠️ 为什么不能在这里直接 Pub(SpawnEnemyRequest)：
    /// 调用链 Bullet.OnBodyEntered(物理 flush) → DamageSystem.Deal → EntityDied → 本方法。
    /// 直接 pub 会让订阅者 OnSpawn 同步执行 Rent()；若池空，Rent 会 Instantiate +
    /// AddChild 一个新 body 注册进 PhysicsServer2D，撞上 "Can't change this state while
    /// flushing queries"（godot_physics_server_2d.cpp body_set_shape_disabled 断言，满屏红错）。
    /// 先入队、下一帧 EnemyService._Process（idle 阶段）再 pub——此时 AddChild 安全。
    /// AliveCount 已计入 _pending，RoundDirector 的"清场"判定不受这 1 帧延迟影响。
    /// </summary>
    private void EnqueueSplit(Vector2 pos)
    {
        var cfg = GameManager.I.Cfg;
        for (int i = 0; i < 2; i++)
        {
            _pending.Enqueue(new SpawnEnemyRequest
            {
                Position  = pos,
                Direction = Rng.Direction(),   // 两只各自随机方向，避免完全重叠
                SpeedMul  = cfg.SplitSpeedMul,
                HP        = cfg.SplitHP,
                Scale     = cfg.SplitScale,
                IsTracker = false,
                CanSplit  = false,             // 小怪不再裂
                SkinKind  = EnemySkinKind.Bee, // 分裂子怪固定"马蜂"造型（bee_walk）
            });
        }
    }

    /// <summary>清场。每轮开始 / 名刀成功时调用。</summary>
    public void ClearAll()
    {
        _pending.Clear();   // 未落地的分裂请求一并作废，防止漏到下一轮才刷
        for (int i = _active.Count - 1; i >= 0; i--)
            Despawn(_active[i]);
    }

    private void Despawn(EnemyBase e)
    {
        // ⚠️ P2-10：无效实例也必须从 _active 移除！
        // 否则调用方拿到失效引用时（历史上：存活上限循环拿 _active[0]）列表长度不变 → 死循环冻死整局。
        // 失效的 Godot 对象无法归还对象池，只能直接移除引用。
        if (!GodotObject.IsInstanceValid(e))
        {
            _active.Remove(e);
            return;
        }
        // ⚠️ 幂等：已 Deactivate（Active=false）说明已被 Despawn 处理过，直接忽略，
        //    防 OnEntityDied / ClearAll 等路径对同一实例重复归还。
        if (!e.Active) return;
        e.Deactivate();
        _pool?.Return(e);
        _active.Remove(e);
        Bus.Pub(new EnemyDespawned(e));
    }

    public override void _Process(double delta)
    {
        // 先落地排队的分裂怪：idle 阶段不在物理 flush 中，此时 Rent→AddChild 安全。
        // （OnEntityDied 里若同步 pub 就会在物理回调里 AddChild → flushing queries 满屏红错）
        while (_pending.Count > 0)
            Bus.Pub(_pending.Dequeue());

        // 防御性清理：Godot 里被销毁的 Node 不是 null，必须用 IsInstanceValid 判断
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(_active[i]) || !_active[i].Active)
                _active.RemoveAt(i);
        }
    }
}
