using System;
using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 战斗内 HUD。
/// 左上角：轮次·波次 / 血条（数字叠中间，按血量变色）。
/// 右上角：设置（= 暂停）。
/// 设置弹窗：我方强化 / 敌方强化 / 继续游戏 / 返回主菜单（自动存档）。
/// </summary>
public partial class Hud : UiBase
{
    private const string MainMenuScenePath = "res://Scenes/ui/main_menu.tscn";

    private Label _roundLabel = null!;
    private ProgressBar _hpBar = null!;
    private ProgressBar _hpGhostBar = null!;
    private Label _hpText = null!;
    private StyleBoxFlat _hpFill = null!;
    private StyleBoxFlat _hpGhostFill = null!;
    private int _lastHp = -1;
    private int _lastMax = -1;
    private Tween? _hpTween;

    private Control _myBuffsOverlay = null!;
    private VBoxContainer _myBuffsList = null!;
    private Control _enemyBuffsOverlay = null!;
    private VBoxContainer _enemyBuffsList = null!;
    private Control _settingsOverlay = null!;
    private readonly List<Button> _settingsButtons = new();

    private int _currentWave = 1;

    protected override void OnUiReady()
    {
        Root = new Control();
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(Root);

        BuildTopLeft(Root);
        BuildTopRight(Root);
        _settingsOverlay = BuildSettingsOverlay();   // 先建设置（暂停）
        BuildBuffsOverlays();                        // 后建强化弹窗 → 在设置之上

        Bus.Sub<EntityDamaged>(this, _ => Refresh());
        Bus.Sub<EntityHealed>(this, _ => Refresh());
        Bus.Sub<RoundStarted>(this, _ => { _currentWave = 1; _lastHp = -1; _lastMax = -1; Refresh(); });
        Bus.Sub<WaveStarted>(this, e => { _currentWave = e.WaveIndex; Refresh(); });
        Bus.Sub<DeathbladeStarted>(this, _ => Refresh());

        Refresh();
    }

    /// <summary>每帧轮询调试热键（F1 跳关）。</summary>
    public override void _Process(double delta) => DebugTools.Poll(GetTree());

    /// <summary>
    /// 局内键盘快捷键：
    /// - ESC：游戏进行中 → 打开设置；设置窗口打开时 → 退出游戏（返回主菜单）。
    /// - 空格：设置窗口打开时 → 继续游戏。
    /// 上下选择 / 回车确认由 Nav（MenuNav）处理。
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey key || !key.Pressed || key.Echo) return;

        if (key.Keycode == Key.Escape)
        {
            // 从最顶层窗口开始逐层关闭：详情页 → 设置 → 打开设置
            if (_myBuffsOverlay.Visible) CloseBuffs(true);
            else if (_enemyBuffsOverlay.Visible) CloseBuffs(false);
            else if (_settingsOverlay.Visible) OnBackToMenu();
            else OnSettingsPressed();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Space)
        {
            // 空格只在设置窗口（无详情页遮挡）时生效
            if (_settingsOverlay.Visible && !_myBuffsOverlay.Visible && !_enemyBuffsOverlay.Visible)
                OnContinueGame();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------- 布局 ----------

    private void BuildTopLeft(Control parent)
    {
        var corner = new MarginContainer();
        corner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        corner.AddThemeConstantOverride("margin_left", 16);
        corner.AddThemeConstantOverride("margin_top", 16);
        corner.MouseFilter = Control.MouseFilterEnum.Ignore;
        parent.AddChild(corner);

        var box = new VBoxContainer();
        box.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        box.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        box.AddThemeConstantOverride("separation", 10);
        corner.AddChild(box);

        _roundLabel = new Label();
        _roundLabel.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(_roundLabel);

        // 血条 + 中间数字（三层：深色底 → 延迟条 → 真正条）
        var barWrap = new Control { CustomMinimumSize = new Vector2(240, 28) };
        box.AddChild(barWrap);

        var bg = new ColorRect { Color = new Color(0.13f, 0.14f, 0.17f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        barWrap.AddChild(bg);

        // 延迟条（底层，扣血棕色 / 加血浅绿，值慢慢滚动）
        _hpGhostBar = new ProgressBar { MinValue = 0, MaxValue = 3, Value = 3, ShowPercentage = false };
        _hpGhostBar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hpGhostFill = new StyleBoxFlat { BgColor = new Color(0.3f, 0.8f, 0.35f) };
        _hpGhostBar.AddThemeStyleboxOverride("fill", _hpGhostFill);
        _hpGhostBar.AddThemeStyleboxOverride("background", new StyleBoxEmpty());
        barWrap.AddChild(_hpGhostBar);

        // 真正条（顶层，绿/黄/红，立即反映当前血量）
        _hpBar = new ProgressBar { MinValue = 0, MaxValue = 3, Value = 3, ShowPercentage = false };
        _hpBar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hpFill = new StyleBoxFlat { BgColor = new Color(0.3f, 0.8f, 0.35f) };
        _hpBar.AddThemeStyleboxOverride("fill", _hpFill);
        _hpBar.AddThemeStyleboxOverride("background", new StyleBoxEmpty());
        barWrap.AddChild(_hpBar);

        _hpText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hpText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hpText.AddThemeFontSizeOverride("font_size", 16);
        barWrap.AddChild(_hpText);
    }

    private void BuildTopRight(Control parent)
    {
        var corner = new MarginContainer();
        corner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        corner.AddThemeConstantOverride("margin_right", 16);
        corner.AddThemeConstantOverride("margin_top", 16);
        corner.MouseFilter = Control.MouseFilterEnum.Ignore;
        parent.AddChild(corner);

        var btn = MakeButton(T("hud_settings"), OnSettingsPressed, hoverFx: false, minSize: new Vector2(120, 44), fontSize: 22);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        corner.AddChild(btn);
    }

    private void BuildBuffsOverlays()
    {
        _myBuffsOverlay = BuildOverlay(T("hud_my_buffs"), out var myContent);
        _myBuffsList = new VBoxContainer();
        _myBuffsList.AddThemeConstantOverride("separation", 6);
        myContent.AddChild(_myBuffsList);
        myContent.AddChild(MakeButton(T("menu_back"), () => CloseBuffs(true), hoverFx: true, minSize: new Vector2(120, 36), fontSize: 18));

        _enemyBuffsOverlay = BuildOverlay(T("hud_enemy_buffs"), out var enemyContent);
        _enemyBuffsList = new VBoxContainer();
        _enemyBuffsList.AddThemeConstantOverride("separation", 6);
        enemyContent.AddChild(_enemyBuffsList);
        enemyContent.AddChild(MakeButton(T("menu_back"), () => CloseBuffs(false), hoverFx: true, minSize: new Vector2(120, 36), fontSize: 18));
    }

    private Control BuildSettingsOverlay()
    {
        var myBuffs = MakeButton(T("hud_my_buffs"), OnMyBuffsPressed, hoverFx: true, minSize: new Vector2(240, 44), fontSize: 20);
        var enemyBuffs = MakeButton(T("hud_enemy_buffs"), OnEnemyBuffsPressed, hoverFx: true, minSize: new Vector2(240, 44), fontSize: 20);
        var continueBtn = MakeButton(T("hud_continue"), OnContinueGame, hoverFx: true, minSize: new Vector2(240, 48), fontSize: 22);
        var backMenu = MakeButton(T("hud_back_menu"), OnBackToMenu, hoverFx: true, minSize: new Vector2(240, 48), fontSize: 22);

        _settingsButtons.Add(myBuffs);
        _settingsButtons.Add(enemyBuffs);
        _settingsButtons.Add(continueBtn);
        _settingsButtons.Add(backMenu);

        return BuildOverlay(T("hud_settings"), new Control[] { myBuffs, enemyBuffs, continueBtn, backMenu });
    }

    // ---------- 刷新 ----------

    private void Refresh()
    {
        int round = GameManager.I.Round;
        _roundLabel.Text = string.Format(T("hud_round"), round, _currentWave);

        int hp = 0;
        int max = (int)GameManager.I.PlayerStats.Get(PlayerStat.MaxHP);
        var p = GameManager.I.Player;
        if (p != null && GodotObject.IsInstanceValid(p))
        {
            var health = p.HealthComp;
            if (GodotObject.IsInstanceValid(health))
            {
                hp = health.Current;
                max = health.MaxHP;
            }
        }

        int clampedHp = Mathf.Clamp(hp, 0, max);
        int clampedMax = Math.Max(1, max);

        _hpBar.MaxValue = clampedMax;
        _hpGhostBar.MaxValue = clampedMax;
        _hpText.Text = hp + "/" + max;

        if (clampedMax != _lastMax)
        {
            // 最大血量变化（换轮 / 满血重置等）：直接同步，不做动画
            _lastMax = clampedMax;
            _lastHp = clampedHp;
            _hpTween?.Kill();
            _hpBar.Value = clampedHp;
            _hpGhostBar.Value = clampedHp;
            _hpFill.BgColor = HpColor(clampedHp);
            _hpGhostFill.BgColor = HpColor(clampedHp);
        }
        else if (clampedHp != _lastHp)
        {
            int oldHp = _lastHp;
            _lastHp = clampedHp;
            AnimateHpChange(oldHp, clampedHp);
        }
    }

    /// <summary>
    /// 血量变化动画：
    /// - 扣血：真正条立即跳变，棕色延迟条从旧血量慢慢滚到新血量（表示被扣掉的血）。
    /// - 加血：真正条先停在旧血量，浅绿延迟条往上补（补充液体），滚完真正条才占满。
    /// 每点血约 1.5 秒。
    /// </summary>
    private void AnimateHpChange(int oldHp, int newHp)
    {
        _hpTween?.Kill();

        if (newHp < oldHp)
        {
            // 扣血：真正条立即反映新血量，棕色延迟条慢慢追
            _hpBar.Value = newHp;
            _hpFill.BgColor = HpColor(newHp);
            _hpGhostFill.BgColor = new Color(0.55f, 0.35f, 0.20f);   // 棕色
            _hpGhostBar.Value = oldHp;
        }
        else
        {
            // 加血：真正条先停在旧血量，浅绿延迟条往上补
            _hpBar.Value = oldHp;
            _hpFill.BgColor = HpColor(oldHp);
            _hpGhostFill.BgColor = new Color(0.55f, 0.90f, 0.55f);   // 浅绿
            _hpGhostBar.Value = oldHp;
        }

        float duration = Math.Abs(newHp - oldHp) * 0.9f;
        _hpTween = CreateTween();
        _hpTween.TweenProperty(_hpGhostBar, "value", (float)newHp, duration);

        if (newHp > oldHp)
        {
            // 加血滚完后，真正条才占满到新血量
            _hpTween.TweenCallback(Callable.From(() =>
            {
                _hpBar.Value = newHp;
                _hpFill.BgColor = HpColor(newHp);
            }));
        }
    }

    /// <summary>血条颜色：3+ 绿，2 黄，1 红。</summary>
    private static Color HpColor(int hp) =>
        hp >= 3 ? new Color(0.3f, 0.8f, 0.35f)
        : hp == 2 ? new Color(0.95f, 0.75f, 0.2f)
        : new Color(0.85f, 0.25f, 0.25f);

    // ---------- 按钮 ----------

    private void OnMyBuffsPressed() => ShowBuffs(forPlayer: true);
    private void OnEnemyBuffsPressed() => ShowBuffs(forPlayer: false);

    private void OnSettingsPressed()
    {
        GetTree().Paused = true;
        ShowOverlay(_settingsOverlay);
        SetSettingsButtonsDisabled(false);
        Nav.Clear();   // 清空预选，不自动预选任何按钮
    }

    private void OnContinueGame()
    {
        HideOverlay(_settingsOverlay);
        GetTree().Paused = false;
        Nav.Clear();
    }

    private void OnBackToMenu()
    {
        SaveService.I?.SaveNow();   // 退出即自动存档
        GetTree().Paused = false;   // 先取消暂停，否则主菜单会被冻结
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    // ---------- 强化列表 ----------

    private void ShowBuffs(bool forPlayer)
    {
        var list = forPlayer ? _myBuffsList : _enemyBuffsList;
        var overlay = forPlayer ? _myBuffsOverlay : _enemyBuffsOverlay;

        foreach (var child in list.GetChildren()) child.QueueFree();

        var entries = BuildBuffEntries(forPlayer);
        if (entries.Count == 0)
        {
            var empty = new Label { Text = T("hud_no_buffs"), HorizontalAlignment = HorizontalAlignment.Center };
            empty.AddThemeFontSizeOverride("font_size", 20);
            list.AddChild(empty);
        }
        else
        {
            foreach (var e in entries) list.AddChild(e);
        }

        ShowOverlay(overlay);
        SetSettingsButtonsDisabled(true);   // 详情页在最顶层：禁用被盖住的设置按钮，键盘只响应详情页
        Nav.Clear();                        // 清空预选，不自动预选
    }

    private void CloseBuffs(bool forPlayer)
    {
        var overlay = forPlayer ? _myBuffsOverlay : _enemyBuffsOverlay;
        HideOverlay(overlay);
        SetSettingsButtonsDisabled(false);  // 回到设置窗口，恢复设置按钮
        Nav.Clear();
    }

    private void SetSettingsButtonsDisabled(bool disabled)
    {
        foreach (var b in _settingsButtons) b.Disabled = disabled;
    }

    private List<Control> BuildBuffEntries(bool forPlayer)
    {
        var groups = new Dictionary<string, BuffGroup>();

        if (forPlayer)
        {
            foreach (var u in GameManager.I.PlayerUpgrades)
                if (u != null) AddToGroup(groups, u.DisplayName, u.Description);
        }
        else
        {
            foreach (var u in GameManager.I.EnemyUpgrades)
                if (u != null) AddToGroup(groups, u.DisplayName, u.Description);
        }

        var entries = new List<Control>();
        foreach (var g in groups.Values)
        {
            var nameLabel = new Label
            {
                Text = g.Count > 1 ? g.Name + " ×" + g.Count : g.Name,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 22);
            entries.Add(nameLabel);

            if (!string.IsNullOrEmpty(g.Description))
            {
                var d = new Label { Text = g.Description, HorizontalAlignment = HorizontalAlignment.Left };
                d.AddThemeFontSizeOverride("font_size", 16);
                d.Modulate = new Color(0.82f, 0.82f, 0.82f, 1f);
                entries.Add(d);
            }
        }
        return entries;
    }

    private static void AddToGroup(Dictionary<string, BuffGroup> groups, string name, string desc)
    {
        if (!groups.TryGetValue(name, out var g))
        {
            g = new BuffGroup { Name = name, Description = desc };
            groups[name] = g;
        }
        g.Count++;
    }

    private sealed class BuffGroup
    {
        public string Name = "";
        public string Description = "";
        public int Count;
    }
}
