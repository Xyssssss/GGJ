using Godot;

namespace GGJ;

/// <summary>
/// 玩家。无重力、8 向自由移动、只能向正右方开炮。
/// 移动参数全部从 GameManager.I.PlayerStats 读，本文件不写死任何数值。
/// </summary>
public partial class Player : CharacterBody2D
{
    private Health _health = null!;
    private Area2D _hurtbox = null!;
    private SpriteAnimator _animator = null!;
    private Vector2 _vel;
    private int _lifestealCounter;
    private int _growthCounter;

    // 进化皮肤（按累计 buff 数切换：0~1 原始 / 2~4 sprout / 5+ flower）
    [Export] public Texture2D? Growth1Idle;       // sprout
    [Export] public Texture2D? Growth1Walk;
    [Export] public int Growth1IdleFrames = 2;
    [Export] public int Growth1WalkFrames = 4;

    [Export] public Texture2D? Growth2Idle;       // flower
    [Export] public Texture2D? Growth2Walk;
    [Export] public int Growth2IdleFrames = 2;
    [Export] public int Growth2WalkFrames = 4;

    // 缓存"原始"皮肤（从 SpriteAnimator 读，stage 0 时还原）
    private Texture2D? _origIdle;
    private Texture2D? _origWalk;
    private int _origIdleFrames;
    private int _origWalkFrames;
    private int _growthStage = -1;   // -1 = 未初始化

    public Health HealthComp => _health;

    public override void _Ready()
    {
        _health = GetNode<Health>("Health");
        _hurtbox = GetNode<Area2D>("Hurtbox");
        _animator = GetNode<SpriteAnimator>("AnimatedSprite2D");
        AddToGroup("player");

        CollisionLayer = Layers.Player;
        CollisionMask  = Layers.Enemy;
        MotionMode     = MotionModeEnum.Floating;    // 无重力

        GameManager.I.Player = this;

        Bus.Sub<EntityDied>(this, OnEnemyDied);

        int maxHp = (int)GameManager.I.PlayerStats.Get(PlayerStat.MaxHP);
        _health.SetMaxHP(maxHp, healToFull: true);
        GlobalPosition = ArenaBounds.Center;

        // 缓存原始皮肤，并按当前 buff 数应用阶段
        _origIdle = _animator.IdleTexture;
        _origWalk = _animator.WalkTexture;
        _origIdleFrames = _animator.IdleFrames;
        _origWalkFrames = _animator.WalkFrames;
        UpdateGrowthStage();
    }

    /// <summary>每轮开始：回满血、回中心、清无敌。</summary>
    public void ResetForRound()
    {
        int maxHp = (int)GameManager.I.PlayerStats.Get(PlayerStat.MaxHP);
        _health.SetMaxHP(maxHp, healToFull: true);
        _health.ClearInvincible();
        // 重开短保护：轮次切换瞬间可能残留"上一轮贴着玩家的旧敌"的过期物理快照，
        // 若一上来就结算会让新轮开局莫名掉血。给 0.6s 出生保护吞掉这类残留一击。
        _health.StartInvincible(0.6f);
        GlobalPosition = ArenaBounds.Center;
        _vel = Vector2.Zero;
        Velocity = Vector2.Zero;
        _lifestealCounter = 0;
        _growthCounter = 0;

        // 选 buff 后 buff 数量变了，每轮刷新一次外观
        UpdateGrowthStage();
    }

    /// <summary>
    /// 按玩家获得的 buff 总数量计算阶段，更换精灵皮肤。
    /// 玩家线所有词条都算，不特指某一类。
    /// 0~1 → 原始（StageAnimator 配置的 IdleTexture/WalkTexture）
    /// 2~4 → sprout
    /// 5+ → flower
    /// </summary>
    public void UpdateGrowthStage()
    {
        int count = 0;
        foreach (var u in GameManager.I.PlayerUpgrades)
            if (u != null)
                count++;

        int stage = count < 2 ? 0 : (count < 5 ? 1 : 2);
        if (stage == _growthStage) return;
        _growthStage = stage;

        var (idle, walk, idleF, walkF) = stage switch
        {
            0 => (_origIdle, _origWalk, _origIdleFrames, _origWalkFrames),
            1 => (Growth1Idle, Growth1Walk, Growth1IdleFrames, Growth1WalkFrames),
            _ => (Growth2Idle, Growth2Walk, Growth2IdleFrames, Growth2WalkFrames),
        };
        _animator.IdleTexture = idle;
        _animator.WalkTexture = walk;
        _animator.IdleFrames = idleF;
        _animator.WalkFrames = walkF;
        _animator.Refresh();
    }

    public override void _PhysicsProcess(double delta)
    {
        float d = (float)delta;
        UpdateMove(d);
        UpdateBlink();
        CheckContact();
    }

    private void UpdateMove(float d)
    {
        var feel = GameManager.I.Feel;
        float accel = feel?.Accel ?? 2000f;
        float friction = feel?.Friction ?? 1600f;
        float speed = GameManager.I.PlayerStats.Get(PlayerStat.MoveSpeed);

        Vector2 wish = InputState.MoveAxis() * speed;

        _vel = wish.LengthSquared() > 0f
            ? _vel.MoveToward(wish, accel * d)
            : _vel.MoveToward(Vector2.Zero, friction * d);

        Velocity = _vel;
        MoveAndSlide();
        GlobalPosition = ArenaBounds.ClampInside(GlobalPosition);   // 撞边缘停住，不反弹
    }

    private void UpdateBlink()
    {
        var sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (sprite == null) return;
        sprite.Visible = !_health.Invincible || (int)(Time.GetTicksMsec() / 80) % 2 == 0;
    }

    /// <summary>
    /// 碰撞：双方各 -1 血。无敌帧内双方都不结算（GDD §3.4）。
    /// 名刀窗口内：敌人照常死，玩家不掉血。
    ///
    /// ⚠️ 用 Area2D 受伤盒 + 每帧重叠检测，**不要**改回 GetSlideCollisionCount()。
    ///    玩家静止时自己的 MoveAndSlide 速度为 0，检测不到撞上来的敌人 —— 会漏伤害。
    /// </summary>
    private void CheckContact()
    {
        foreach (var other in _hurtbox.GetOverlappingBodies())
        {
            if (other == null || !GodotObject.IsInstanceValid(other)) continue;
            if (!other.IsInGroup("enemy")) continue;

            var enemyHealth = other.GetNodeOrNull<Health>("Health");
            if (enemyHealth == null) continue;

            bool deathblade = GameManager.I.DeathbladeActive;
            if (_health.Invincible && !deathblade) return;   // 双方都不结算

            var epos = other is Node2D en ? en.GlobalPosition : GlobalPosition;
            // 服务器实际物理位置：区分"逻辑位置已就位但物理层还留在旧处/屏外"
            Vector2? serverPos = null;
            if (other is CollisionObject2D coll)
            {
                try
                {
                    serverPos = PhysicsServer2D.BodyGetState(coll.GetRid(), PhysicsServer2D.BodyState.Transform)
                        .AsTransform2D().Origin;
                }
                catch { }
            }

            // ⚠️ 防御：Area2D 的 overlap 快照滞后于物理服务器实际状态。回收/复用会先把敌的
            //   GlobalPosition 移到出生点（远处），但物理服务器与玩家 hurtbox 的残留 overlap
            //   要等下一次物理 flush 才清除 —— 新轮首帧会把"刚复用的远敌"误判成接触。
            //   服务器里该敌 body 此刻根本不在玩家身边 → 是过期快照，跳过本次结算。
            if (serverPos is Vector2 sp && sp.DistanceTo(GlobalPosition) > 300f)
                continue;

            var toEnemy = new HitInfo
            {
                Source = this, Target = other,
                SourceIsPlayer = true, TargetIsPlayer = false,
                BaseAmount = 1f, Kind = DamageKind.Contact,
                Position = epos,
            };
            DamageSystem.Deal(ref toEnemy);

            var toPlayer = new HitInfo
            {
                Source = other, Target = this,
                SourceIsPlayer = false, TargetIsPlayer = true,
                BaseAmount = 1f, Kind = DamageKind.Contact,
                Position = GlobalPosition,
            };
            DamageSystem.Deal(ref toPlayer);   // 名刀窗口内会被 DamageSystem 取消
            return;                            // 一帧只结算一次接触伤害
        }
    }

    /// <summary>
    /// 吸血（寄生根须）与成长：玩家击杀敌人时计数。
    /// 吸血到阈值 → 回血；成长到阈值 → 生命上限 +Y。两个计数器独立。
    /// 阈值由 .tres 词条卡 Override 设定（0 = 未激活）；回血/成长量从 Cfg 读。
    /// </summary>
    private void OnEnemyDied(EntityDied e)
    {
        if (e.TargetIsPlayer) return;   // 只关心敌人死亡

        var st = GameManager.I.PlayerStats;

        // ---- 吸血 ----
        float lk = st.Get(PlayerStat.LifestealKills);
        if (lk > 0f)
        {
            _lifestealCounter++;
            if (_lifestealCounter >= (int)lk)
            {
                _health.Heal(GameManager.I.Cfg.LifestealAmount);
                _lifestealCounter = 0;
            }
        }

        // ---- 成长 ----
        float gk = st.Get(PlayerStat.GrowthKills);
        if (gk > 0f)
        {
            _growthCounter++;
            if (_growthCounter >= (int)gk)
            {
                int amount = GameManager.I.Cfg.GrowthAmount;
                int newBase = (int)st.GetBase(PlayerStat.MaxHP) + amount;
                st.SetBase(PlayerStat.MaxHP, newBase);
                _health.SetMaxHP((int)st.Get(PlayerStat.MaxHP));
                _growthCounter = 0;
            }
        }
    }
}
