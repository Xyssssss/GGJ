using System;
using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 三选一界面。每轮胜负后（游戏已暂停）从上方向中心淡入 3 张卡。
/// 每张卡：上方名称、下方效果说明。必须选一张才能继续。
/// 点选 → UpgradeService.Apply → Bus.Pub(UpgradeChosen)（RoundDirector 负责取消暂停）。
/// 词条抽取 PickChoices 由 UpgradeService 实现；返回空（词条池空）时显示 3 张占位卡兜底。
/// </summary>
public partial class UpgradePicker : UiBase
{
    private ColorRect _dim = null!;
    private Label _titleLabel = null!;
    private HBoxContainer _cards = null!;
    private VBoxContainer _content = null!;

    private bool _forPlayer;
    private bool _usingPlaceholder;
    private readonly List<Resource> _choices = new();

    protected override void OnUiReady()
    {
        Root = new Control();
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.Visible = false;   // 平时隐藏，收到 UpgradeOffered 才显示
        AddChild(Root);

        _dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(_dim);

        var wrapper = new Control();
        wrapper.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        wrapper.MouseFilter = Control.MouseFilterEnum.Ignore;
        Root.AddChild(wrapper);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 24);
        wrapper.AddChild(_content);

        _titleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _titleLabel.AddThemeFontSizeOverride("font_size", 36);
        _content.AddChild(_titleLabel);

        _cards = new HBoxContainer();
        _cards.AddThemeConstantOverride("separation", 32);
        _content.AddChild(_cards);

        Bus.Sub<UpgradeOffered>(this, OnOffered);
    }

    private void OnOffered(UpgradeOffered e)
    {
        _forPlayer = e.ForPlayer;

        foreach (var c in _cards.GetChildren()) c.QueueFree();
        _choices.Clear();

        var svc = FindUpgradeService();
        var real = svc?.PickChoices(e.ForPlayer, 3);
        _usingPlaceholder = real == null || real.Count == 0;

        if (_usingPlaceholder)
        {
            for (int i = 0; i < 3; i++)
            {
                if (e.ForPlayer)
                    _choices.Add(new PlayerUpgradeData { DisplayName = T("picker_placeholder") + " " + (i + 1), Description = T("picker_placeholder_desc") });
                else
                    _choices.Add(new EnemyUpgradeData { DisplayName = T("picker_placeholder") + " " + (i + 1), Description = T("picker_placeholder_desc") });
            }
        }
        else
        {
            _choices.AddRange(real!);
        }

        _titleLabel.Text = e.ForPlayer ? T("picker_title_player") : T("picker_title_enemy");

        for (int i = 0; i < _choices.Count; i++)
        {
            int idx = i;
            _cards.AddChild(MakeCardFromChoice(_choices[i], () => OnChoicePicked(_choices[idx])));
        }

        ShowPicker();
    }

    private void OnChoicePicked(Resource choice)
    {
        if (!_usingPlaceholder && choice != null)
        {
            FindUpgradeService()?.Apply(choice);
        }

        Root.Visible = false;
        Bus.Pub(new UpgradeChosen(_forPlayer));   // RoundDirector 取消暂停并推进 / 重打
    }

    // ---------- 显示动画：从上方向中心淡入 ----------

    private async void ShowPicker()
    {
        Root.Visible = true;
        _dim.Color = new Color(0f, 0f, 0f, 0f);
        _content.Modulate = new Color(1f, 1f, 1f, 0f);

        // 等两帧确保 _content 完成布局、Size 稳定（中文字体首次加载可能晚一帧）
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(this)) return;

        // 手动计算居中位置（不依赖容器延迟布局，避免偶现读到旧位置导致面板卡在顶部）
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var target = (viewportSize - _content.Size) / 2f;

        _content.Position = target + new Vector2(0f, -220f);

        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(_dim, "color:a", 0.6f, 0.3f);
        tw.TweenProperty(_content, "modulate:a", 1f, 0.4f);
        tw.TweenProperty(_content, "position", target, 0.4f)
          .SetTrans(Tween.TransitionType.Quad)
          .SetEase(Tween.EaseType.Out);
    }

    // ---------- 卡片 ----------

    private Control MakeCardFromChoice(Resource choice, Action onClick)
    {
        string name = "?";
        string desc = "";
        if (choice is PlayerUpgradeData p) { name = p.DisplayName; desc = p.Description; }
        else if (choice is EnemyUpgradeData en) { name = en.DisplayName; desc = en.Description; }
        return MakeCard(name, desc, onClick);
    }

    private Control MakeCard(string name, string desc, Action onClick)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(280, 200);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;

        // 亮纹边框：突出框线，用亮蓝白分隔三张卡
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.17f, 0.96f),
            BorderColor = new Color(0.72f, 0.85f, 1f, 0.9f),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
        };
        panel.AddThemeStyleboxOverride("panel", cardStyle);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        var nameLabel = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
        nameLabel.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(nameLabel);

        var descLabel = new Label
        {
            Text = desc,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        descLabel.AddThemeFontSizeOverride("font_size", 16);
        descLabel.Modulate = new Color(0.85f, 0.85f, 0.85f, 1f);
        box.AddChild(descLabel);

        panel.GuiInput += (e) =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                Bus.Pub(new SfxRequest { Key = "ui" });   // 三选一卡片点击 = UI 点击
                onClick();
            }
        };

        return panel;
    }

    /// <summary>定位 UpgradeService（Main.cs 挂在场景根下）。⚠️ 建议 P0 加单例后改用它。</summary>
    private UpgradeService? FindUpgradeService()
    {
        var main = GetTree().Root.GetNodeOrNull<Node>("Main");
        return main?.GetNodeOrNull<UpgradeService>("UpgradeService");
    }
}
