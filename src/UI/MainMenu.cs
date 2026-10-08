using System;
using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 主菜单。正上方游戏名 + 四个按钮：开始游戏 / 新游戏 / 开发者团队 / 退出游戏。
/// 继承 UiBase 复用：文案、字体兜底、按钮、弹窗、淡入淡出、悬停缩放。
/// </summary>
public partial class MainMenu : UiBase
{
    private const string GameScenePath = "res://Main.tscn";
    private const string BackgroundPath = "res://art/bg_full_1280x720.jpg";

    private Control _creditsOverlay = null!;
    private Control _slotsOverlay = null!;
    private Control _confirmDeleteOverlay = null!;
    private Button _slotsBackBtn = null!;
    private int _pendingDeleteSlot = -1;
    private VBoxContainer _slotsList = null!;
    private readonly List<Control> _fadeItems = new();
    private readonly List<Button> _mainButtons = new();
    private bool _leaving;
    private AudioStreamPlayer? _bgm;

    protected override void OnUiReady()
    {
        BuildUi();
        FadeIn(_fadeItems);
        PlayMenuBgm();
    }

    /// <summary>
    /// 初始界面 BGM（audio/bgm/UIbgm.wav，循环播放）。
    /// 播放器挂在主菜单场景内 —— 点"开始游戏"切场景时随节点释放自动停止。
    /// </summary>
    private void PlayMenuBgm()
    {
        const string path = "res://audio/bgm/UIbgm.wav";
        if (!ResourceLoader.Exists(path)) return;

        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;   // 无缝循环
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / 2;                     // 16-bit mono：字节/2 = 采样数
        }

        _bgm = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = -10f,   // 音量压低，作背景
        };
        AddChild(_bgm);
        _bgm.Play();
    }

    private void BuildUi()
    {
        Root = new Control();
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Root);

        AddBackground(Root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(center);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 20);
        center.AddChild(layout);

        // 标题（中文主标题 + 英文副标题）
        var title = new Label
        {
            Text = T("menu_title"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 72);
        layout.AddChild(title);
        _fadeItems.Add(title);

        var subtitle = new Label
        {
            Text = T("menu_subtitle"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.72f, 0.76f, 0.82f, 1f),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 26);
        layout.AddChild(subtitle);
        _fadeItems.Add(subtitle);

        // 标题与按钮之间留白
        layout.AddChild(new Control { CustomMinimumSize = new Vector2(0, 40) });

        var continueBtn = MakeButton(T("menu_continue"), OnContinuePressed);
        var newGameBtn = MakeButton(T("menu_new_game"), OnNewGamePressed);
        var creditsBtn = MakeButton(T("menu_credits"), OnCreditsPressed);
        var quitBtn = MakeButton(T("menu_quit"), OnQuitPressed);

        _mainButtons.Add(continueBtn);
        _mainButtons.Add(newGameBtn);
        _mainButtons.Add(creditsBtn);
        _mainButtons.Add(quitBtn);

        layout.AddChild(continueBtn);
        layout.AddChild(newGameBtn);
        layout.AddChild(creditsBtn);
        layout.AddChild(quitBtn);

        _fadeItems.Add(continueBtn);
        _fadeItems.Add(newGameBtn);
        _fadeItems.Add(creditsBtn);
        _fadeItems.Add(quitBtn);

        _creditsOverlay = BuildCreditsOverlay();
        _slotsOverlay = BuildSlotsOverlay();
        _confirmDeleteOverlay = BuildConfirmDeleteOverlay();   // 最后建，显示在最顶层
    }

    /// <summary>背景：优先静态图 res://art/background.png；没有就纯色。动图可换成 AnimatedTexture / AnimatedSprite2D。</summary>
    private void AddBackground(Control parent)
    {
        var tex = Res.Load<Texture2D>(BackgroundPath);
        if (tex != null)
        {
            var tr = new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            tr.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            parent.AddChild(tr);
            return;
        }

        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.09f, 0.13f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(bg);
    }

    // ---------- 按钮 ----------

    private void OnContinuePressed()
    {
        RefreshSlots();
        ShowOverlay(_slotsOverlay);
        SetMainButtonsDisabled(true);
        Nav.Clear();   // 清空预选，不自动预选
    }

    private void OnNewGamePressed()
    {
        var svc = SaveService.I;
        int slot = svc != null ? svc.PickNewSlot() : 0;
        svc?.StartNew(slot);
        Bus.Pub(new NewGameRequested());   // 通知 P0：新开一局
        StartGame();
    }

    private void OnCreditsPressed()
    {
        ShowOverlay(_creditsOverlay);
        SetMainButtonsDisabled(true);
        Nav.Clear();   // 清空预选，不自动预选
    }

    private void OnQuitPressed() => GetTree().Quit();

    private void OnSlotPressed(int slot)
    {
        if (!SaveService.Has(slot)) return;   // 空位点了无反应

        SaveService.I?.Continue(slot);        // 设置 ActiveSlot + PendingResume
        _slotsOverlay.Visible = false;
        Bus.Pub(new ContinueGameRequested(slot));   // 通知 P0：读档恢复
        StartGame();
    }

    private void CloseCredits()
    {
        HideOverlay(_creditsOverlay);
        SetMainButtonsDisabled(false);
        Nav.Clear();
    }

    private void CloseSlots()
    {
        HideOverlay(_slotsOverlay);
        SetMainButtonsDisabled(false);
        Nav.Clear();
    }

    private void SetMainButtonsDisabled(bool disabled)
    {
        foreach (var b in _mainButtons) b.Disabled = disabled;
    }

    /// <summary>ESC：关闭最顶层弹窗（开发者团队 / 存档位）。</summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Escape) return;

        if (_confirmDeleteOverlay.Visible) CancelDelete();
        else if (_creditsOverlay.Visible) CloseCredits();
        else if (_slotsOverlay.Visible) CloseSlots();
        GetViewport().SetInputAsHandled();
    }

    private void StartGame()
    {
        if (_leaving) return;
        _leaving = true;
        FadeOut(Root, 0.3f, () => GetTree().ChangeSceneToFile(GameScenePath));
    }

    // ---------- 弹窗 ----------

    private Control BuildCreditsOverlay()
    {
        var rows = new Control[]
        {
            MakeCreditLine(T("credits_role_design"), T("credits_name_design")),
            MakeCreditLine(T("credits_role_program"), T("credits_name_program")),
            MakeCreditLine(T("credits_role_art"), T("credits_name_art")),
            MakeButton(T("menu_back"), CloseCredits),
        };
        return BuildOverlay(T("credits_title"), rows);
    }

    private Control BuildSlotsOverlay()
    {
        _slotsOverlay = BuildOverlay(T("menu_continue"), out var content);
        _slotsList = new VBoxContainer();
        _slotsList.AddThemeConstantOverride("separation", 8);
        content.AddChild(_slotsList);
        _slotsBackBtn = MakeButton(T("menu_back"), CloseSlots);
        content.AddChild(_slotsBackBtn);
        return _slotsOverlay;
    }

    private Control BuildConfirmDeleteOverlay()
    {
        var yesBtn = MakeButton(T("confirm_yes"), ConfirmDelete, hoverFx: true, minSize: new Vector2(120, 44), fontSize: 20);
        var noBtn = MakeButton(T("confirm_no"), CancelDelete, hoverFx: true, minSize: new Vector2(120, 44), fontSize: 20);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(yesBtn);
        row.AddChild(noBtn);

        return BuildOverlay(T("confirm_delete_title"), row);
    }

    /// <summary>重建存档位列表（每个位 = 继续按钮 + 删除按钮）。</summary>
    private void RefreshSlots()
    {
        foreach (var child in _slotsList.GetChildren()) child.QueueFree();

        for (int i = 0; i < SaveService.SlotCount; i++)
        {
            int slot = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var info = MakeButton(SlotButtonText(slot), () => OnSlotPressed(slot), hoverFx: false, minSize: new Vector2(300, 56), fontSize: 18);
            info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(info);

            var del = MakeButton(T("menu_delete"), () => OnDeleteSlot(slot), hoverFx: false, minSize: new Vector2(72, 56), fontSize: 18);
            row.AddChild(del);

            _slotsList.AddChild(row);
        }
    }

    private void OnDeleteSlot(int slot)
    {
        if (!SaveService.Has(slot)) return;   // 空位点了无反应
        _pendingDeleteSlot = slot;
        ShowOverlay(_confirmDeleteOverlay);
        _slotsBackBtn.Disabled = true;   // 确认窗口在最顶层：禁用被盖住的返回按钮
        Nav.Clear();
    }

    private void ConfirmDelete()
    {
        if (_pendingDeleteSlot >= 0 && SaveService.Has(_pendingDeleteSlot))
            SaveService.Delete(_pendingDeleteSlot);
        _pendingDeleteSlot = -1;
        HideOverlay(_confirmDeleteOverlay);
        _slotsBackBtn.Disabled = false;
        Nav.Clear();
        RefreshSlots();
    }

    private void CancelDelete()
    {
        _pendingDeleteSlot = -1;
        HideOverlay(_confirmDeleteOverlay);
        _slotsBackBtn.Disabled = false;
        Nav.Clear();
    }

    /// <summary>存档位按钮文案：名字 + 关键进度。</summary>
    private string SlotButtonText(int slot)
    {
        var d = SaveService.Read(slot);
        string head = T("menu_slot") + " " + (slot + 1);
        if (d == null) return head + "\n" + T("save_empty");

        string rating = d.Finished ? d.Rank : T("save_unfinished");
        string timeLabel = d.Finished ? T("save_time") : T("save_time_elapsed");
        return head + "\n"
            + T("save_round") + " " + d.Round + "    "
            + T("save_rating") + " " + rating + "\n"
            + T("save_deaths") + " " + d.TotalDeaths + "    "
            + timeLabel + " " + FormatTime(d.RunTime);
    }

    private static string FormatTime(float sec)
    {
        int s = (int)sec;
        int m = s / 60;
        s %= 60;
        return m + ":" + s.ToString("00");
    }

    private Control MakeCreditLine(string role, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        // 角色列固定宽 + 居中（角色名中心对齐），姓名列固定宽 + 居中（人名中心对齐）
        var roleLabel = new Label
        {
            Text = role,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(150, 0),
        };
        roleLabel.AddThemeFontSizeOverride("font_size", 22);

        var nameLabel = new Label
        {
            Text = name,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(220, 0),
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 22);

        row.AddChild(roleLabel);
        row.AddChild(nameLabel);
        return row;
    }
}
