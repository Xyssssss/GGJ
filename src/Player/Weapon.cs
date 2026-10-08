using Godot;

namespace GGJ;

/// <summary>
/// 射击。固定向正右方，不可瞄准（核心机制，不要加瞄准）。
/// 词条效果：连发（补射）、激光（环绕+穿透）、闪现（右键瞬移）——均已实现。
/// </summary>
public partial class Weapon : Node
{
    // ==================== 常量 ====================

    // P2-17：ExtraShotInterval / LaserSpinDegPerSec 两个纯手感常量已搬进 run_config.tres，
    // 运行时读 GameManager.I.Cfg.Xxx（不会被词条修改，不走 StatBlock）。

    /// <summary>激光穿透个数。取极大值等效"穿透路径上所有敌人"（策划案激光效果）。</summary>
    /// <remarks>复用 Bullet 现有的 Pierce-- 逻辑，无需改动 Bullet。</remarks>
    private const int LaserPierce = int.MaxValue;

    /// <summary>
    /// 激光弹飞行速度（px/s）。光束 330px、一屏 1280px —— 4800px/s ≈ 0.27s 横穿全屏，
    /// 一出手就是"刷"的一道光线。激光不吃 BulletSpeed 词条（那是普通弹的事），恒定超高速。
    /// </summary>
    private const float LaserSpeed = 4800f;

    // ---- 环绕发射口特效（代表激光当前环绕发射点）----
    /// <summary>
    /// 发射口特效贴图 = bulletdoor（玩家新加的 64×64 单帧图，做"门/出口"标记）。
    /// 想换图：把美术图放进 art/ 后改这一行路径。
    /// </summary>
    private const string MuzzleFxTexPath = "res://art/anim/bulletdoor.png";
    /// <summary>
    /// 发射口绕玩家旋转的轨道半径（px）：激光生成/光束也从该半径喷出（与开火 Position 同源），
    /// 所以半径 = 标记贴着实际出弹点的距离。
    /// </summary>
    private const float MuzzleOrbit = 40f;
    /// <summary>发射口贴图的显示宽度（px）。贴图多大都会自动缩到这个尺寸。</summary>
    private const float MuzzleFxSize = 60f;

    // ==================== 字段 ====================

    /// <summary>主开火冷却计时（秒）。> 0 时不能开火。</summary>
    private float _cd;
    /// <summary>挂载节点（玩家）。Weapon 挂在 Player 下，用于读位置。</summary>
    private Node2D _owner = null!;

    // ---- 连发补射队列：主发射后按固定间隔补射 ExtraShots 发 ----
    /// <summary>剩余待补射的子弹数。</summary>
    private int _pendingShots;
    /// <summary>距下一次补射的剩余时间（秒）。</summary>
    private float _extraTimer;

    // ---- 激光环绕角度（度）：0 = 正右方，顺时针递增 ----
    /// <summary>激光当前发射角度。每帧持续推进（无论是否开火，保持环绕节奏）。</summary>
    private float _laserAngle;

    /// <summary>环绕发射口特效节点（Sprite2D，挂在玩家下）。仅激光激活时显示，每帧跟到 _laserAngle。</summary>
    private Node2D _muzzleFx = null!;

    // ---- 闪现：右键事件触发 + 冷却 ----
    // ⚠️ 文档第六章要求"输入加在 InputState.cs"，但 Core 目录只读（第五章严格 #2），存在冲突。
    //    输入检测暂做在 Weapon 内部，功能等价；如 P0/P1 同意改 InputState 再迁移。
    /// <summary>本帧是否捕获到右键按下事件（_Input 写入，UpdateDash 消费后清零）。</summary>
    private bool _dashQueued;
    /// <summary>闪现冷却计时（秒）。> 0 时不能闪现。</summary>
    private float _dashCd;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        _owner = GetParent<Node2D>();

        // ⚠️ 特效节点不能在此同步挂到 _owner 上：玩家可能是被 Main._Ready()
        //    里 AddChild 进场的，此时父节点正处在"装配子节点"的 blocked 状态，
        //    同步 AddChild 会报 "Parent node is busy setting up children"（P2-33）。
        //    延到帧末再搭，头几帧 UpdateMuzzleFx 靠判空安全跳过。
        Callable.From(BuildMuzzleFx).CallDeferred();
    }

    /// <summary>
    /// 搭建"环绕发射口"特效：一个挂在玩家下的 Sprite2D（贴图显示，和子弹/玩家同一套
    /// 已验证可靠的渲染方式；不用 _Draw 自绘）。
    /// 每帧由 UpdateMuzzleFx 放到当前环绕角度对应的枪口位置。
    /// </summary>
    private void BuildMuzzleFx()
    {
        var tex = GD.Load<Texture2D>(MuzzleFxTexPath);
        if (tex == null)
        {
            GD.PushWarning($"[Weapon] 发射口贴图加载失败：{MuzzleFxTexPath}（路径不存在 → 发射口不显示）");
            return;
        }

        _muzzleFx = new Node2D { Name = "LaserMuzzleFx", ZIndex = 10 };   // ZIndex=10：画在角色立绘最上层
        var sprite = new Sprite2D
        {
            Texture = tex,
            Modulate = Colors.White,   // 直接用 bulletdoor 原色（不染色）
        };
        // 按贴图原始尺寸自动缩放到统一显示大小（换多大贴图都不用改比例）
        Vector2 t = tex.GetSize();
        float longest = Mathf.Max(t.X, t.Y);
        sprite.Scale = longest > 0f ? Vector2.One * (MuzzleFxSize / longest) : Vector2.One;

        _muzzleFx.AddChild(sprite);
        _muzzleFx.Visible = false;   // 普通模式先藏起来
        _owner.AddChild(_muzzleFx);
    }

    /// <summary>
    /// 捕获右键"按下"事件（事件驱动，替代轮询边沿检测）。
    /// 一次按下只派发一个 Pressed 事件 —— 按住不松不会重复触发闪现。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Right
            && mb.Pressed)
        {
            _dashQueued = true;
        }
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;

        // 0) 激光环绕角度持续推进（无论是否开火，保持转动节奏）
        _laserAngle += GameManager.I.Cfg.LaserSpinDegPerSec * d;
        if (_laserAngle >= 360f) _laserAngle -= 360f;   // 取模防无限增长

        // 0.5) 闪现（边沿触发，独立于开火 CD）
        UpdateDash(d);

        // 0.6) 环绕发射口：激光激活时显示，每帧贴到当前环绕角度（在任何提前 return 之前，
        //      保证光点始终跟随角度转动）
        UpdateMuzzleFx();

        // 1) 推进连发补射队列（优先于主开火 CD，保证补射不被吞）
        if (_pendingShots > 0)
        {
            _extraTimer -= d;
            if (_extraTimer <= 0f)
            {
                FireOneBullet();                             // 补射一发
                _pendingShots--;
                _extraTimer += GameManager.I.Cfg.ExtraShotInterval;   // 累加而非赋值，防帧率波动累积漂移
                if (_pendingShots <= 0) _extraTimer = 0f;    // 补完清零
            }
        }

        // 2) 主开火 CD
        if (_cd > 0f) _cd -= d;
        if (_cd > 0f) return;              // 还在冷却
        if (!InputState.FireHeld) return;  // 没按住左键

        FireMain();
        _cd = Mathf.Max(0.05f, GameManager.I.PlayerStats.Get(PlayerStat.FireCooldown));
    }

    // ==================== 闪现 ====================

    /// <summary>
    /// 闪现：右键"按下瞬间"，向当前移动方向瞬移 DashRange 距离，冷却 DashCooldown 秒。
    /// 策划案：D=120px、T=3s。玩家静止时按右键不闪现（不浪费冷却）。
    /// 瞬移后位置被 ArenaBounds.ClampInside 夹住，不会出屏。
    /// </summary>
    private void UpdateDash(float d)
    {
        // 推进冷却
        if (_dashCd > 0f) _dashCd -= d;

        // 消费本帧捕获的按下事件（右键事件或触摸闪现按钮；无论是否成功都清掉）
        bool dashRequested = _dashQueued || TouchControls.ConsumeDash();
        if (!dashRequested) return;
        _dashQueued = false;

        var st = GameManager.I.PlayerStats;
        if (!st.HasFlag(PlayerStat.FlagDash)) return;
        if (_dashCd > 0f) return;   // 还在冷却

        // 方向 = 当前移动方向（WASD）。静止时不闪现（不浪费冷却）
        Vector2 move = InputState.MoveAxis();
        if (move.LengthSquared() < 0.0001f) return;

        // 瞬移：位置 = 当前 + 单位方向 × 距离，再夹进场内防出屏
        float range = st.Get(PlayerStat.DashRange);
        _owner.GlobalPosition = ArenaBounds.ClampInside(_owner.GlobalPosition + move.Normalized() * range);

        _dashCd = st.Get(PlayerStat.DashCooldown);
    }

    /// <summary>
    /// 每帧把发射口特效贴到"当前环绕角度 + MuzzleOrbit 轨道"上。
    /// 激光词条激活才显示，否则隐藏。
    /// </summary>
    private void UpdateMuzzleFx()
    {
        if (_muzzleFx == null) return;

        if (!GameManager.I.PlayerStats.HasFlag(PlayerStat.FlagLaser))
        {
            _muzzleFx.Visible = false;
            return;
        }

        _muzzleFx.Visible = true;
        _muzzleFx.GlobalPosition = _owner.GlobalPosition + AngleToDirection(_laserAngle) * MuzzleOrbit;
    }

    // ==================== 开火 ====================

    /// <summary>主发射：射出第一发，并按 ExtraShots 词条排入补射队列。</summary>
    private void FireMain()
    {
        FireOneBullet();

        // 读连发词条层数（词条未生效时为 0 = 不补射）
        int extra = (int)GameManager.I.PlayerStats.Get(PlayerStat.ExtraShots);

        if (extra > 0)
        {
            _pendingShots = extra;
            _extraTimer = GameManager.I.Cfg.ExtraShotInterval;   // 第一发补射的等待时间
        }
    }

    /// <summary>
    /// 实际射出一发子弹。主射与补射复用同一逻辑。
    /// 激光词条开启时：方向 = 当前环绕角度、穿透 = 无限；否则：固定向右 + 读 Pierce 词条。
    /// </summary>
    private void FireOneBullet()
    {
        var st = GameManager.I.PlayerStats;
        bool laser = st.HasFlag(PlayerStat.FlagLaser);

        Vector2 dir = laser
            ? AngleToDirection(_laserAngle)    // 激光：当前环绕角度（顺时针转动中）
            : Vector2.Right;                   // 普通炮弹：固定向右（核心机制，不可瞄准）
        int pierce = laser
            ? LaserPierce                      // 激光：穿透路径上所有敌人
            : (int)st.Get(PlayerStat.Pierce);  // 普通：读穿透词条（默认 0 = 命中即销毁）

        Bus.Pub(new SpawnBulletRequest
        {
            // 激光的枪口 = 环绕光点（MuzzleFx 所在位置），光束从光点上喷出来；
            // 普通弹仍是固定正右枪口偏移。
            Position  = _owner.GlobalPosition
                        + (laser ? dir * MuzzleOrbit : new Vector2(16f, 0f)),
            Direction = dir,
            // 激光不吃 BulletSpeed 词条，恒定超高速横扫
            Speed     = laser ? LaserSpeed : st.Get(PlayerStat.BulletSpeed),
            Damage    = 1,
            Pierce    = pierce,
            Laser     = laser,   // 标记激光：Bullet 换 jiguang 柱状射线贴图，与炮弹区分
        });

        // 音效区分：激光 = 激光射出音效，普通炮弹 = 炮弹射出音效
        Bus.Pub(new SfxRequest { Key = laser ? "laser" : "shoot" });
    }

    /// <summary>
    /// 角度（度）→ 单位方向向量。0° = 正右，90° = 正下（Godot 2D Y 轴向下，正角度顺时针）。
    /// </summary>
    private static Vector2 AngleToDirection(float deg)
    {
        float rad = Mathf.DegToRad(deg);
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }
}
