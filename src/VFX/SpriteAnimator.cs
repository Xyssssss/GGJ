using Godot;

namespace GGJ;

/// <summary>皮肤类别：决定敌人从哪个贴图池换皮。</summary>
public enum EnemySkinKind
{
    /// <summary>普通怪：从默认池（鸟/虫/甲虫）随机。</summary>
    Normal = 0,
    /// <summary>精英怪（Boss）：从鸽子大体积池随机（考拉已移作追踪怪造型）。</summary>
    Elite = 1,
    /// <summary>分裂怪（马蜂窝）：分裂词条开启后每波额外刷出的独立母体，死亡裂出马蜂。</summary>
    Hive = 2,
    /// <summary>分裂子怪：马蜂。母体被打死后裂出的 2 个小怪用此造型。</summary>
    Bee = 3,
    /// <summary>追踪怪：固定考拉造型（考拉不再作为精英皮出现）。</summary>
    Tracker = 4,
}

/// <summary>
/// 通用精灵动画控制器。挂在 AnimatedSprite2D 上。
/// - 美术给纹理（可带 hframes 的精灵表），自动切帧建 SpriteFrames。
/// - 父节点是 CharacterBody2D 且有 WalkTexture 时，按速度自动切 idle / walk。
/// - WalkFrames > 1 时按横向精灵表切帧（Rect2）。
/// - 可选 RandomIdleTextures / RandomWalkTextures：每次"生成"随机选一套（同 idx 配对），
///   适合"多种敌人共用一个场景、随机换皮"的场景。对象池复用节点不触发 _Ready，
///   改由 NOTIFICATION_VISIBILITY_CHANGED 通知在"回收→再租出"时重新随机。
/// </summary>
public partial class SpriteAnimator : AnimatedSprite2D
{
    [Export] public Texture2D? IdleTexture;
    [Export] public int IdleFrames = 1;
    [Export] public Texture2D? WalkTexture;
    [Export] public int WalkFrames = 1;
    [Export] public float AnimSpeed = 8f;
    [Export] public bool FlipWithDirection = false;   // 朝向跟随水平移动方向
    [Export] public bool ArtFacesLeft = true;          // 美术默认朝左
    /// <summary>贴图内容自动居中：扫描非透明 bbox，把视觉中心对齐节点原点。
    /// 解决素材上下留白不均导致的发射点/视觉错位（如幼苗贴图内容偏下）。默认 false，玩家场景开启。</summary>
    [Export] public bool AutoCenterContent { get; set; }

    // 普通怪随机换皮：RandomIdleTextures[i] 与 RandomWalkTextures[i] 是同一套
    [Export] public Texture2D?[]? RandomIdleTextures;
    [Export] public Texture2D?[]? RandomWalkTextures;
    [Export] public int RandomIdleFrames = 1;
    [Export] public int RandomWalkFrames = 4;

    // 精英(Boss)专属换皮：从这两池随机，普通怪永不出现
    [Export] public Texture2D?[]? EliteIdleTextures;
    [Export] public Texture2D?[]? EliteWalkTextures;
    [Export] public int EliteIdleFrames = 1;
    [Export] public int EliteWalkFrames = 4;

    // 分裂怪（马蜂窝）专属：分裂词条开启后每波额外刷出的独立母体用此造型
    [Export] public Texture2D?[]? HiveIdleTextures;
    [Export] public Texture2D?[]? HiveWalkTextures;
    [Export] public int HiveIdleFrames = 4;
    [Export] public int HiveWalkFrames = 4;

    // 分裂子怪（马蜂）专属：母体死亡裂出的 2 个小怪用此造型
    [Export] public Texture2D?[]? BeeIdleTextures;
    [Export] public Texture2D?[]? BeeWalkTextures;
    [Export] public int BeeIdleFrames = 4;
    [Export] public int BeeWalkFrames = 4;

    // 追踪怪（考拉）专属：固定考拉造型，普通怪/精英永不出现
    [Export] public Texture2D?[]? TrackerIdleTextures;
    [Export] public Texture2D?[]? TrackerWalkTextures;
    [Export] public int TrackerIdleFrames = 1;
    [Export] public int TrackerWalkFrames = 4;

    private bool _autoSwitch;
    private string _current = "";
    private bool _lastVisible;
    private EnemySkinKind _kind;

    public override void _Ready()
    {
        PickRandomTextureSet();
        Rebuild();

        _autoSwitch = WalkTexture != null && GetParent() is CharacterBody2D;
        _lastVisible = IsVisibleInTree();
    }

    /// <summary>
    /// 按皮肤类别换皮：Normal/Elite/Hive/Bee/Tracker 各从自己的池随机选一套。
    /// 对象池复用必须每次显式调用（回收→再租出会按上次状态再随机一次，以这里为准覆盖）。
    /// </summary>
    public void ApplySkin(EnemySkinKind kind)
    {
        _kind = kind;
        PickRandomTextureSet();
        Rebuild();
        ApplyDisplayScale();
    }

    /// <summary>对象池复用节点时不会再触发 _Ready，这里用可见性变化通知补上：
    /// 敌人被回收到池（父节点 Visible=false）再租出（Visible=true）时，重新随机换皮。
    /// 用 _lastVisible 沿检测，避免进入场景树时的重复随机。</summary>
    public override void _Notification(int what)
    {
        base._Notification(what);

        if (what != NotificationVisibilityChanged) return;

        bool vis = IsVisibleInTree();
        if (vis && !_lastVisible)
        {
            PickRandomTextureSet();
            Rebuild();
        }
        _lastVisible = vis;
    }

    /// <summary>如果提供了对应池的纹理数组，随机选一套（同 idx）覆盖单纹理字段。</summary>
    private void PickRandomTextureSet()
    {
        // 按皮肤类别取池；池没配（可能 tscn 忘了加）就退回普通池，保证不白屏
        var idles  = Pool(_kind == EnemySkinKind.Elite   ? EliteIdleTextures   : null,
                          _kind == EnemySkinKind.Hive    ? HiveIdleTextures    : null,
                          _kind == EnemySkinKind.Bee     ? BeeIdleTextures     : null,
                          _kind == EnemySkinKind.Tracker ? TrackerIdleTextures : null,
                          RandomIdleTextures);
        if (idles == null || idles.Length == 0) return;

        int idx = GD.RandRange(0, idles.Length - 1);
        IdleTexture = idles[idx];
        IdleFrames = FramesFor(_kind, isIdle: true);

        var walks = Pool(_kind == EnemySkinKind.Elite   ? EliteWalkTextures   : null,
                         _kind == EnemySkinKind.Hive    ? HiveWalkTextures    : null,
                         _kind == EnemySkinKind.Bee     ? BeeWalkTextures     : null,
                         _kind == EnemySkinKind.Tracker ? TrackerWalkTextures : null,
                         RandomWalkTextures);
        if (walks != null && idx < walks.Length)
        {
            WalkTexture = walks[idx];
            WalkFrames = FramesFor(_kind, isIdle: false);
        }
        else
        {
            WalkTexture = null;   // 上一只残留在对象池里的 walk 不能带到本只
        }

        ApplyDisplayScale();
    }

    /// <summary>
    /// 大帧贴图归一化：把"512 宽精灵表（每帧 128px）"缩半显示，与普通怪（每帧 64px）体积一致。
    /// 原因：精英（鸽子）/追踪怪（考拉）素材是普通怪的 2 倍像素宽，若不缩放，贴图会明显
    /// 大于碰撞体，玩家打中"翅膀/边缘"却不掉血（命中判定是碰撞圆，不是贴图）。
    /// 归一只影响视觉显示，不碰父节点 Scale（碰撞体积由父节点控制，保持精英/追踪的体积语义）。
    /// </summary>
    private void ApplyDisplayScale()
    {
        float mul = 1f;
        if (IdleTexture != null && _kind is EnemySkinKind.Elite or EnemySkinKind.Tracker)
        {
            int frameW = Mathf.Max(1, IdleTexture.GetWidth() / Mathf.Max(1, IdleFrames));
            // 目标：最终画面宽度接近普通怪帧宽 64px（相对父 scale 而言）。
            // 例如帧宽 128 → mul=0.5；若以后换 64px 素材，mul 自动回 1，无需改配置。
            mul = Mathf.Clamp(64f / frameW, 0.25f, 1f);
        }
        Scale = Vector2.One * mul;
    }

    /// <summary>把"分类专属池"与兜底普通池串起来：只取第一个非空的。</summary>
    private static Texture2D?[]? Pool(Texture2D?[]? elite, Texture2D?[]? hive, Texture2D?[]? bee, Texture2D?[]? tracker, Texture2D?[]? normal)
        => elite   is { Length: > 0 } ? elite
         : hive    is { Length: > 0 } ? hive
         : bee     is { Length: > 0 } ? bee
         : tracker is { Length: > 0 } ? tracker
         : normal;

    private int FramesFor(EnemySkinKind kind, bool isIdle) => kind switch
    {
        EnemySkinKind.Elite   => isIdle ? EliteIdleFrames   : EliteWalkFrames,
        EnemySkinKind.Hive    => isIdle ? HiveIdleFrames    : HiveWalkFrames,
        EnemySkinKind.Bee     => isIdle ? BeeIdleFrames     : BeeWalkFrames,
        EnemySkinKind.Tracker => isIdle ? TrackerIdleFrames : TrackerWalkFrames,
        _                     => isIdle ? RandomIdleFrames  : RandomWalkFrames,
    };

    /// <summary>用当前 Idle/WalkTexture 重建 SpriteFrames 并切回 idle 动画。</summary>
    private void Rebuild()
    {
        BuildFrames();

        if (SpriteFrames != null && SpriteFrames.HasAnimation("idle"))
        {
            Play("idle");
            _current = "idle";
        }

        ApplyContentCenter();
    }

    /// <summary>
    /// 贴图内容自动居中：扫描 IdleTexture 第一帧的非透明 bbox，把"内容中心"贴齐节点原点。
    /// 仅当 AutoCenterContent = true 时生效（玩家场景开启，敌人保持原行为）。
    /// </summary>
    private void ApplyContentCenter()
    {
        if (!AutoCenterContent) return;
        var tex = IdleTexture;
        if (tex == null) { Offset = Vector2.Zero; return; }

        var img = tex.GetImage();
        if (img == null) return;

        int w = img.GetWidth();
        int h = img.GetHeight();
        int frameW = w / Mathf.Max(1, IdleFrames);
        int frameH = h;   // 横向精灵表：帧高 = 贴图高
        if (frameW <= 0 || frameH <= 0) return;

        int minX = frameW, minY = frameH, maxX = -1, maxY = -1;
        for (int y = 0; y < frameH; y++)
        {
            for (int x = 0; x < frameW; x++)
            {
                if (img.GetPixel(x, y).A <= 0.01f) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) { Offset = Vector2.Zero; return; }

        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float ox = cx - frameW * 0.5f;   // 内容中心相对帧中心的偏移
        float oy = cy - frameH * 0.5f;
        // Centered=true + Offset = -offset → 内容中心落在节点原点（= Player 原点 = 发射点基准）
        Offset = new Vector2(-ox, -oy);
    }

    /// <summary>公开重建入口：外部换 IdleTexture/WalkTexture 后调用。</summary>
    public void Refresh() => Rebuild();

    public override void _PhysicsProcess(double delta)
    {
        UpdateFlip();

        if (!_autoSwitch) return;
        var body = (CharacterBody2D)GetParent();
        string target = body.Velocity.LengthSquared() > 1f ? "walk" : "idle";
        if (target != _current && SpriteFrames != null && SpriteFrames.HasAnimation(target))
        {
            Play(target);
            _current = target;
        }
    }

    /// <summary>朝向跟随水平速度：碰墙反射后速度反向，自动翻转朝向。</summary>
    private void UpdateFlip()
    {
        if (!FlipWithDirection) return;
        var parent = GetParent();
        if (parent is not CharacterBody2D body) return;

        float vx = body.Velocity.X;
        if (Mathf.Abs(vx) < 1f) return;   // 静止不翻转，保持当前朝向

        FlipH = ArtFacesLeftForKind() ? (vx > 0f) : (vx < 0f);
    }

    /// <summary>
    /// 素材默认朝向的基准，按皮肤类别区分——不同片的动物头朝向不同：
    /// - 普通怪（虫/鸟/甲虫）：美术头朝右，基准 = ArtFacesLeft(false)（不翻转时朝右）。
    /// - 精英（鸽子）与追踪怪（考拉）：这两张精灵表头朝左，需用"头朝左"基准反向翻转，
    ///   否则跟着普通怪的基准会变成"头朝移动反方向"（从右往左走却头朝右）。
    /// </summary>
    private bool ArtFacesLeftForKind() => _kind switch
    {
        EnemySkinKind.Elite or EnemySkinKind.Tracker => true,
        _ => ArtFacesLeft,
    };

    private void BuildFrames()
    {
        var frames = new SpriteFrames();
        bool added = false;
        if (IdleTexture != null) { AddAnimation(frames, "idle", IdleTexture, IdleFrames); added = true; }
        if (WalkTexture != null) { AddAnimation(frames, "walk", WalkTexture, WalkFrames); added = true; }
        if (!added) return;
        SpriteFrames = frames;
    }

    private void AddAnimation(SpriteFrames frames, string name, Texture2D tex, int count)
    {
        frames.AddAnimation(name);
        int n = Mathf.Max(1, count);
        if (n == 1)
        {
            frames.AddFrame(name, tex);
        }
        else
        {
            // 横向精灵表切帧（假设美术按 hframes 横向排列）
            int frameW = tex.GetWidth() / n;
            int frameH = tex.GetHeight();
            for (int i = 0; i < n; i++)
            {
                var atlas = new AtlasTexture
                {
                    Atlas = tex,
                    Region = new Rect2(i * frameW, 0, frameW, frameH),
                };
                frames.AddFrame(name, atlas);
            }
        }
        frames.SetAnimationSpeed(name, AnimSpeed);
        frames.SetAnimationLoop(name, true);
    }
}
