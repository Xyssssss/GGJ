using Godot;

namespace GGJ;

/// <summary>
/// 入口。只做三件事：加载配置 → 建服务 → 启动轮次。
/// ⚠️ 主场景保持极简，实体一律运行时装配，不要在 Main.tscn 里堆节点。
/// </summary>
public partial class Main : Node2D
{
    /// <summary>战斗场地背景图。换图只改这里，别的不用动。</summary>
    private const string BackgroundPath = "res://art/bg_topdown_1280x720.png";

    public override void _ExitTree()
    {
        // 离开战斗场景（回主菜单 / 结算跳转）时敌人节点随场景销毁，逐个 EnemyDespawned 不会触发，
        // 必须显式停掉精英/蜂群循环音并清零计数，否则 Autoload 里会残留一直响。
        AudioService.I?.StopAllLoops();
    }

    public override void _Ready()
    {
        ArenaBounds.Init(GetViewportRect());

        // 背景永远最先加、层级最低，玩家/敌人才能画在地面上
        AddBackground();

        // .tres 缺失或损坏时用代码默认值兜底，保证永远跑得起来
        GameManager.I.Cfg     = Res.Load<RunConfig>("res://data/run_config.tres")   ?? RunConfig.CreateDefault();
        GameManager.I.Feel    = Res.Load<FeelConfig>("res://data/feel_config.tres") ?? FeelConfig.CreateDefault();
        GameManager.I.Strings = Res.Load<StringsData>("res://data/strings.tres")    ?? StringsData.CreateDefault();

        InitStats();

        // 顺序有讲究：EnemyService / BulletService 要在 RoundDirector 之前
        var enemies  = new EnemyService  { Name = "EnemyService" };
        var bullets  = new BulletService { Name = "BulletService" };
        var spawner  = new SpawnDirector { Name = "SpawnDirector" };
        var upgrades = new UpgradeService { Name = "UpgradeService" };
        var fx       = new Fx { Name = "Fx" };
        var rounds   = new RoundDirector { Name = "RoundDirector" };

        AddChild(enemies);
        AddChild(bullets);
        AddChild(spawner);
        AddChild(upgrades);
        AddChild(fx);
        AddChild(rounds);

        enemies.Init(GameManager.I.Cfg.EnemyScene);
        bullets.Init(GameManager.I.Cfg.BulletScene);

        SpawnPlayer();

        // UI：程序 C 建好场景后自动生效；场景不存在就跳过。
        // ⚠️ 三选一必须有回应者，否则轮次结束后会永久暂停 —— 所以缺 Picker 时挂兜底。
        TryAddUi(ScenesPath.Hud);
        if (!TryAddUi(ScenesPath.Picker)) AddChild(new AutoUpgradeFallback { Name = "AutoUpgradeFallback" });
        TryAddUi(ScenesPath.Result);

        // 触摸控件（安卓版摇杆 + 发射/闪现按钮）：仅触摸设备显示，桌面端自动隐藏
        AddChild(new TouchControls { Name = "TouchControls" });

        rounds.Init(spawner, enemies);

        // 继续存档（未通关）→ 恢复进度与 buff；否则从头开始
        var resume = SaveService.I?.TakePendingResume();
        if (resume != null && resume.Round > 1)
            rounds.ResumeRun(resume, upgrades);
        else
            rounds.StartRun();
    }

    /// <summary>战斗背景：1280x720 铺满整屏、ZIndex 最低，永远垫底。图缺失只警告不报错。</summary>
    private void AddBackground()
    {
        var tex = Res.Load<Texture2D>(BackgroundPath);
        if (tex == null)
        {
            GD.PushWarning($"[Main] 找不到战斗背景图 {BackgroundPath}，本次无背景。");
            return;
        }

        var bg = new Sprite2D
        {
            Texture = tex,
            Centered = true,
            Position = ArenaBounds.Center,
            ZIndex = -100,
        };
        AddChild(bg);
    }

    private bool TryAddUi(string path)
    {
        var scene = Res.Packed("res://" + path);
        if (scene == null) return false;
        AddChild(scene.Instantiate());
        return true;
    }

    private void SpawnPlayer()
    {
        var scene = GameManager.I.Cfg.PlayerScene;
        if (scene == null)
        {
            GD.PushWarning("[Main] 没有 PlayerScene。检查 data/run_config.tres 的 PlayerScene 字段。");
            return;
        }
        var p = scene.Instantiate<Player>();
        p.Name = "Player";
        AddChild(p);
    }

    /// <summary>
    /// 把配置写进两条属性线。**基础值只在这里设置一次**，
    /// 之后所有加成都通过 AddModifier 叠加，不要回头改 base。
    /// </summary>
    private void InitStats()
    {
        var cfg = GameManager.I.Cfg;
        var feel = GameManager.I.Feel;

        var ps = GameManager.I.PlayerStats;
        ps.SetBase(PlayerStat.MaxHP, cfg.PlayerMaxHP);
        ps.SetBase(PlayerStat.MoveSpeed, cfg.PlayerMoveSpeed);
        ps.SetBase(PlayerStat.FireCooldown, cfg.FireCooldown);
        ps.SetBase(PlayerStat.BulletSpeed, cfg.BulletSpeed);
        ps.SetBase(PlayerStat.InvincibleTime, feel.InvincibleTime);
        ps.SetBase(PlayerStat.Pierce, 0f);
        ps.SetBase(PlayerStat.ExtraShots, 0f);
        ps.SetBase(PlayerStat.HitboxScale, 1f);
        ps.SetBase(PlayerStat.LifestealKills, 0f);   // 0 = 未激活，抽到词条卡后 Override 为阈值
        ps.SetBase(PlayerStat.LifestealAmount, 0f);  // 效果代码直接读 Cfg.LifestealAmount
        ps.SetBase(PlayerStat.GrowthKills, 0f);       // 同上
        ps.SetBase(PlayerStat.GrowthAmount, 0f);      // 同上
        ps.SetBase(PlayerStat.DashRange, cfg.DashRange);
        ps.SetBase(PlayerStat.DashCooldown, cfg.DashCooldown);
        ps.SetBase(PlayerStat.RicochetChance, cfg.RicochetChance);
        ps.SetBase(PlayerStat.FlagLaser, 0f);
        ps.SetBase(PlayerStat.FlagRicochet, 0f);
        ps.SetBase(PlayerStat.FlagDash, 0f);
        ps.SetBase(PlayerStat.FlagDeathblade, 0f);
        ps.SetBase(PlayerStat.DeathbladeWindow, cfg.DeathbladeWindow);

        var es = GameManager.I.EnemyStats;
        es.SetBase(EnemyStat.HP, cfg.EnemyHP);
        es.SetBase(EnemyStat.MoveSpeed, cfg.EnemyMoveSpeed);
        es.SetBase(EnemyStat.SpawnIntervalReduction, 0f);
        es.SetBase(EnemyStat.EnemiesPerWaveBonus, 0f);
        es.SetBase(EnemyStat.BodyScale, 1f);
        es.SetBase(EnemyStat.FlagAccelOnBounce, 0f);
        es.SetBase(EnemyStat.AccelBase, cfg.AccelBase);
        es.SetBase(EnemyStat.AccelDecay, cfg.AccelDecay);
        es.SetBase(EnemyStat.AccelCap, cfg.AccelCap);
        es.SetBase(EnemyStat.FlagElite, 0f);
        es.SetBase(EnemyStat.EliteChance, cfg.EliteChance);
        es.SetBase(EnemyStat.EliteHPMul, cfg.EliteHPMul);
        es.SetBase(EnemyStat.EliteSpeedMul, cfg.EliteSpeedMul);
        es.SetBase(EnemyStat.EliteScaleMul, cfg.EliteScaleMul);   // P2-17：从 EnemyService.Init 挪入
        es.SetBase(EnemyStat.FlagSpawnFourSides, 0f);
        es.SetBase(EnemyStat.FlagSplit, 0f);
        es.SetBase(EnemyStat.FlagTracker, 0f);
        es.SetBase(EnemyStat.TrackerCount, cfg.TrackerCount);
        es.SetBase(EnemyStat.TrackerSpeed, cfg.TrackerSpeed);
        es.SetBase(EnemyStat.TrackerHP, cfg.TrackerHP);   // [待实测] 初值 1，若"喂养"撞击流则改 2
    }
}
