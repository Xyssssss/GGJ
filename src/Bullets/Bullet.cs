using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 子弹。Area2D（不是物理体），出屏即回收，命中即销毁（穿透词条除外）。
/// 由 BulletService 从对象池租用，不要自己 QueueFree。
/// </summary>
public partial class Bullet : Area2D
{
    public Vector2 Direction = Vector2.Right;
    public float Speed = 500f;
    public int Damage = 1;
    public int Pierce;

    /// <summary>本发是否为激光弹（决定用哪套贴图）。对象池复用，须在 Launch 里重置。</summary>
    private bool _isLaser;

    /// <summary>普通炮弹精灵（SpriteAnimator 驱动 muzzle_flash 动画）。</summary>
    private AnimatedSprite2D _normalSprite = null!;
    /// <summary>激光专用精灵（jiguang 柱状射线，Launch 时旋转到飞行方向）。</summary>
    private Sprite2D _laserSprite = null!;
    /// <summary>碰撞盒。激光会沿飞行方向拉长到整条光束（普通弹保持原始 16×6）。</summary>
    private CollisionShape2D _collision = null!;

    // ---- 激光外观（jiguang 抠图：光柱本体约占贴图全宽 ~0.83，从贴图左侧喷出向右渐亮）----
    /// <summary>整张贴图的显示宽度（px）。换算到光柱本体约 330px —— 这才是"激光"该有的长度。</summary>
    private const float LaserTexDisplayWidth = 400f;
    /// <summary>碰撞盒长度（px）。拉长到整条光束，让敌人被扫到就立刻吃到伤害。</summary>
    private const float LaserHitLength = 330f;
    /// <summary>贴图前推比例 ≈ 使光柱左端（枪口端）正好搭在发射点上。</summary>
    private const float LaserMuzzleAnchor = 0.42f;
    /// <summary>
    /// 纵向压缩比：贴图横向拉长成 400px 后，本体高约 60px 太肥；
    /// 再纵向压到 ~0.35，本体只剩 ~20px，才是"一道激光"而不是一根荧光棒。
    /// 小于此值光尾会被压得太扁发虚，调小要重新看效果。
    /// </summary>
    private const float LaserThinRatio = 0.35f;

    private readonly HashSet<Node> _alreadyHit = new();

    /// <summary>
    /// 本发是否已"销毁"。防止同一物理帧收到多次命中回调（一发子弹同帧扎进多只
    /// 重叠敌人时，Godot 的 body_entered 会逐只派发）：
    /// 普通弹第一次命中即销毁，之后的回调应直接忽略 —— 否则既会重复入池（Pool
    /// 已加防重，这里再堵一层），又会让已消失的子弹继续给后面的敌人结算伤害。
    /// </summary>
    private bool _dead;

    // ---- 跳弹：发射时刻一次性判定（P2-11），飞行途中不再每帧摇骰子 ----
    /// <summary>本发是否会反弹（Launch 时摇一次骰子定格）。</summary>
    private bool _willBounce;
    /// <summary>本发是否已反射过。每发至多反射 1 次（策划案规则）。</summary>
    private bool _bounced;

    public override void _Ready()
    {
        // ⚠️ CollisionLayer / Mask / Monitoring 在 bullet.tscn 里已配，
        //    _Ready 不要再赋一次——重复写一遍在某些时序下会被 Godot
        //    报 "Can't change this state while flushing queries"（P2-20）。
        //    这里只订阅信号。
        _normalSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        _laserSprite  = GetNode<Sprite2D>("LaserSprite");
        _collision    = GetNode<CollisionShape2D>("CollisionShape2D");
        BodyEntered += OnBodyEntered;
    }

    /// <summary>从对象池租用时调用。⚠️ 必须重置所有状态，防止上一发的残留。</summary>
    public void Launch(Vector2 pos, Vector2 dir, float speed, int damage, int pierce, bool laser)
    {
        GlobalPosition = pos;
        Direction = dir.Normalized();
        Speed = speed;
        Damage = damage;
        Pierce = pierce;
        _isLaser = laser;
        _alreadyHit.Clear();
        _dead = false;   // ⚠️ 对象池复用，必须重置，否则上一发的"已销毁"状态会带到下一发

        ApplyLaserVisual();   // 切换外观：激光 = jiguang 柱状射线，普通弹 = muzzle_flash

        // 跳弹词条在发射时刻一次性判定（P2-11）：开关 + 概率都在此刻定格，
        // 飞行途中不再每帧调用 Rng.Chance（原实现每弹每帧摇一次，60fps 下 ~60 次/秒/弹）。
        var ps = GameManager.I.PlayerStats;
        bool enabled = ps.HasFlag(PlayerStat.FlagRicochet);

        float chance = ps.Get(PlayerStat.RicochetChance);

        _willBounce = enabled && Rng.Chance(chance);   // 全程唯一一次随机调用
        _bounced = false;
    }

    /// <summary>
    /// 切换弹体外观（每次发射调用，保证对象池复用后外观正确）：
    /// - 普通炮弹：muzzle_flash 动画精灵（黄色闪光），碰撞盒还原成原始 16×6；
    /// - 激光弹：jiguang 柱状射线贴图拉长到 ~330px（真正的"激光"长度），
    ///   旋转到飞行方向，并把光柱起点推到枪口上；碰撞盒同步拉长覆盖整条光束，
    ///   这样敌人被扫过立刻吃伤害，而不是等 16px 的小点慢慢碾过去。
    /// </summary>
    private void ApplyLaserVisual()
    {
        _normalSprite.Visible = !_isLaser;
        _laserSprite.Visible  = _isLaser;

        float angle = Direction.Angle();
        if (_isLaser)
        {
            var texSize = _laserSprite.Texture?.GetSize() ?? Vector2.Zero;
            float s = texSize.X > 0f ? LaserTexDisplayWidth / texSize.X : 0.4f;

            _laserSprite.Rotation = angle;
            _laserSprite.Scale    = new Vector2(s, s * LaserThinRatio);   // 拉长的同时压扁成细激光
            // 贴图沿飞行方向前推，使光柱左端（枪口端）搭在发射点上
            _laserSprite.Position = Direction * (LaserTexDisplayWidth * LaserMuzzleAnchor);

            // 碰撞盒：从枪口延伸到光束尽头（激光一发贯穿路径，靠这个长盒即时命中）
            _collision.Rotation = angle;
            _collision.Scale    = new Vector2(LaserHitLength / 16f, 1f);   // 原矩形 16 宽，按比例拉长
            _collision.Position = Direction * (LaserHitLength * 0.5f);
        }
        else
        {
            _laserSprite.Rotation  = 0f;
            _laserSprite.Scale     = Vector2.One;
            _laserSprite.Position  = Vector2.Zero;
            _collision.Rotation    = 0f;
            _collision.Scale       = Vector2.One;
            _collision.Position    = Vector2.Zero;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += Direction * Speed * (float)delta;

        // 跳弹：越界时按发射时刻的判定结果光学反射，每发至多 1 次。
        // 未开启 / 已弹过 / 未摇中 → 走原来的出屏销毁逻辑。
        if (!_bounced && _willBounce && TryRicochet())
        {
            _bounced = true;   // 标记已弹，后续不再反射
            return;            // 反射成功，本帧不做出屏判定
        }

        if (!ArenaBounds.Inside(GlobalPosition)) Retire();
    }

    /// <summary>
    /// 越界时的光学反射。概率已在 Launch 时判定（_willBounce），这里只做几何判定。
    /// 复用 ArenaBounds.Reflect（已带方向判断，不会贴墙抖动）。
    /// 返回 true 表示本次发生了反射。
    /// </summary>
    private bool TryRicochet()
    {
        // 光学反射（入射角 = 反射角，速度大小不变）
        Vector2 v = Direction;
        Vector2 p = GlobalPosition;
        bool bounced = ArenaBounds.Reflect(ref v, p);
        if (!bounced) return false;   // 没碰到边界，不反射

        // 应用反射方向，并把子弹拉回场内一点，
        // 避免下一帧又被判越界直接销毁（反射就白做了）。
        Direction = v;
        GlobalPosition = ArenaBounds.ClampInside(GlobalPosition);

        // ⚠️ 方向变了，激光贴图和碰撞盒必须跟着转到新朝向，
        //    否则出现"光束朝旧方向飞"的鬼畜（P2-31）。
        RefreshLaserOrientation();
        return true;
    }

    /// <summary>
    /// 发射后方向改变时（目前只有跳弹反弹），重摆激光的贴图与碰撞盒：
    /// 旋转到新 Direction、锚点前推到枪口端。普通弹无需处理（贴图对称）。
    /// 缩放在发射时已定（ApplyLaserVisual），这里只改朝向相关属性。
    /// </summary>
    private void RefreshLaserOrientation()
    {
        if (!_isLaser) return;
        float angle = Direction.Angle();

        _laserSprite.Rotation = angle;
        _laserSprite.Position = Direction * (LaserTexDisplayWidth * LaserMuzzleAnchor);

        _collision.Rotation = angle;
        _collision.Position = Direction * (LaserHitLength * 0.5f);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_dead) return;   // 同帧多回调：本发已销毁，后续命中一律忽略
        if (!GodotObject.IsInstanceValid(body)) return;
        if (!body.IsInGroup("enemy")) return;
        if (!_alreadyHit.Add(body)) return;

        var hit = new HitInfo
        {
            Source = null,                 // 发射者可能已被回收，传 null
            Target = body,
            SourceIsPlayer = true,
            TargetIsPlayer = false,
            BaseAmount = Damage,
            Kind = DamageKind.Bullet,
            Position = body.GlobalPosition,
        };
        Bus.Pub(new SfxRequest { Key = "hit" });   // 子弹命中音效：命中目标瞬间播放（AudioService 有 40ms 节流防爆音）
        DamageSystem.Deal(ref hit);

        if (Pierce <= 0) Retire();
        else Pierce--;
    }

    /// <summary>
    /// 幂等销毁出口：命中 / 出屏 / 轮次清场共用。已销毁（_dead）后再次调用直接忽略，
    /// 配合 Pool.Return 的防重，杜绝重复归还。
    /// </summary>
    internal void Retire()
    {
        if (_dead) return;   // 幂等：同一发只允许销毁一次
        _dead = true;
        Bus.Pub(new DespawnBullet { Bullet = this });
    }
}
