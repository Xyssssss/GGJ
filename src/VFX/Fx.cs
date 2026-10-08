using Godot;

namespace GGJ;

/// <summary>
/// 特效与手感反馈。GDD §7-7：受伤四件套（无敌帧闪烁 / 顿帧 / 震屏 / 受伤音效）。
/// 额外：开火枪口闪光（订阅 SpawnBulletRequest）。
///
/// TODO(程序C)：
///   · 震屏：订阅 EntityDamaged，抖 Camera2D 的 Offset，强度取 Feel.ShakeStrength
///   · 命中火花 / 死亡爆散：对象池 + AnimatedSprite2D
/// </summary>
public partial class Fx : Node
{
    private static readonly Texture2D? MuzzleTex = GD.Load<Texture2D>("res://art/anim/bullet_orb.png");
    private readonly Texture2D?[] _deathFrames = new Texture2D?[4];

    private bool _inHitStop;
    private ulong _hitStopEndMs;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // 顿帧时自己不能被自己冻住
        for (int i = 0; i < 4; i++) _deathFrames[i] = GD.Load<Texture2D>($"res://art/anim/death_{i + 1}.png");
        Bus.Sub<EntityDamaged>(this, OnDamaged);
        Bus.Sub<EntityDied>(this, OnDied);
        Bus.Sub<SpawnBulletRequest>(this, OnBulletSpawned);
    }

    private void OnDamaged(EntityDamaged e)
    {
        // TODO：震屏 + 火花
        if (e.TargetIsPlayer) HitStop(GameManager.I.Feel?.HitStopTime ?? 0.05f);
    }

    private void OnDied(EntityDied e)
    {
        if (!e.TargetIsPlayer) SpawnDeathVfx(e.Position);
    }

    /// <summary>敌人死亡时，在死亡点播一段一次性爆散特效，播完自动销毁。</summary>
    private void SpawnDeathVfx(Vector2 pos)
    {
        var spr = new AnimatedSprite2D { GlobalPosition = pos, Centered = true };
        var frames = new SpriteFrames();
        frames.AddAnimation("death");
        foreach (var tex in _deathFrames)
            if (tex != null) frames.AddFrame("death", tex);
        if (frames.GetFrameCount("death") == 0) return;

        frames.SetAnimationSpeed("death", 12f);
        frames.SetAnimationLoop("death", false);   // 一次播放

        spr.SpriteFrames = frames;
        AddChild(spr);
        spr.Play("death");
        spr.AnimationFinished += () => spr.QueueFree();
    }

    /// <summary>开火时在枪口位置生成一个一次性的闪光精灵，播完即销毁。</summary>
    private void OnBulletSpawned(SpawnBulletRequest e)
    {
        if (MuzzleTex == null) return;

        var flash = new AnimatedSprite2D
        {
            GlobalPosition = e.Position,
            Centered = true,
        };

        var frames = new SpriteFrames();
        frames.AddAnimation("flash");
        int n = 2;   // hframes = 2
        int frameW = MuzzleTex.GetWidth() / n;
        int frameH = MuzzleTex.GetHeight();
        for (int i = 0; i < n; i++)
        {
            frames.AddFrame("flash", new AtlasTexture
            {
                Atlas = MuzzleTex,
                Region = new Rect2(i * frameW, 0, frameW, frameH),
            });
        }
        frames.SetAnimationSpeed("flash", 14f);
        frames.SetAnimationLoop("flash", false);   // 一次播放

        flash.SpriteFrames = frames;
        AddChild(flash);
        flash.Play("flash");
        flash.AnimationFinished += () => flash.QueueFree();
    }

    /// <summary>
    /// 顿帧。
    ///
    /// ⚠ 两个坑，都踩过：
    ///   ① Engine.TimeScale = 0 之后**必须有代码把它恢复成 1**，否则全场永久静止
    ///      （表现：玩家一挨打，子弹不动、刷怪停摆，而且没有任何报错）。
    ///   ② TimeScale = 0 时 delta 恒为 0，**不能用 delta 倒计时**，
    ///      必须用不受 TimeScale 影响的时钟（这里用 Time.GetTicksMsec）。
    /// </summary>
    public void HitStop(float seconds)
    {
        if (seconds <= 0f) return;

        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        if (end > _hitStopEndMs) _hitStopEndMs = end;

        if (_inHitStop) return;
        _inHitStop = true;
        Engine.TimeScale = 0.0;
    }

    public override void _Process(double delta)
    {
        if (!_inHitStop) return;
        if (Time.GetTicksMsec() < _hitStopEndMs) return;

        _inHitStop = false;
        Engine.TimeScale = 1.0;
    }
}