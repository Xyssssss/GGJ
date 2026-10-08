using System;
using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 所有 UI 场景的公共基类：文案加载、中文字体兜底、按钮、弹窗、淡入淡出。
/// MainMenu 与 Hud 都继承它，复用同一套表现，避免重复代码。
/// 子类实现 OnUiReady()，在里头构建 UI 并给 Root 赋值。
/// </summary>
public abstract partial class UiBase : CanvasLayer
{
    /// <summary>菜单导航器：键盘/鼠标预选 + 边框流光 + 粒子 + 缩放。MakeButton(hoverFx:true) 自动注册。</summary>
    protected MenuNav Nav { get; private set; } = null!;

    /// <summary>当前 UI 的根 Control（子类在 OnUiReady 里赋值，用于整体淡出 / 字体兜底）。</summary>
    protected Control Root { get; set; } = null!;

    protected StringsData Strings { get; private set; } = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UiInput.Register(GetViewport());

        // 可能作为首场景启动，Main.cs 尚未注入配置 → 自行兜底加载文案
        Strings = GameManager.I.Strings
            ?? Res.Load<StringsData>("res://data/strings.tres")
            ?? StringsData.CreateDefault();

        Nav = new MenuNav { Name = "MenuNav" };
        AddChild(Nav);

        OnUiReady();

        if (Root != null) ApplyCjkFont(Root);
    }

    /// <summary>子类在这里构建 UI，并赋值 Root。</summary>
    protected abstract void OnUiReady();

    protected string T(string key) => Strings.Get(key);

    // ---------- 字体 ----------

    /// <summary>中文兜底：用系统字体渲染 CJK，避免默认字体显示方块。挂在 Root 上会下传给所有子节点。</summary>
    protected static void ApplyCjkFont(Control root)
    {
        try
        {
            var sys = new SystemFont
            {
                FontNames = new string[]
                {
                    "Microsoft YaHei", "SimHei", "PingFang SC",
                    "Noto Sans CJK SC", "Source Han Sans SC"
                }
            };
            root.AddThemeFontOverride("font", sys);
        }
        catch (Exception)
        {
            // 找不到系统字体就退回默认（可能显示方块）
        }
    }

    // ---------- 按钮 ----------

    /// <summary>
    /// 造按钮。hoverFx = 是否接入菜单导航（键盘/鼠标预选 + 边框流光 + 向右下洒落粒子 + 缩放）。
    /// iconPath 可空；minSize / fontSize 可调。
    /// </summary>
    protected Button MakeButton(string text, Action onClick, bool hoverFx = true, string? iconPath = null, Vector2? minSize = null, int fontSize = 24)
    {
        var btn = new Button { Text = text };
        btn.CustomMinimumSize = minSize ?? new Vector2(320, 60);
        btn.AddThemeFontSizeOverride("font_size", fontSize);

        if (iconPath != null)
        {
            var tex = Res.Load<Texture2D>(iconPath);
            if (tex != null) btn.Icon = tex;
        }

        btn.Pressed += () =>
        {
            Bus.Pub(new SfxRequest { Key = "ui" });   // UI 点击音效：所有按钮统一在这里响
            onClick();
        };
        if (hoverFx) Nav.Add(btn);
        return btn;
    }

    // ---------- 淡入淡出 ----------

    /// <summary>逐个淡入（进场用）。</summary>
    protected void FadeIn(IReadOnlyList<Control> items, float dur = 0.7f, float stagger = 0.12f)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var c = items[i];
            c.Modulate = new Color(1f, 1f, 1f, 0f);
            var tw = CreateTween();
            tw.TweenProperty(c, "modulate:a", 1f, dur)
              .SetDelay(i * stagger)
              .SetTrans(Tween.TransitionType.Quad)
              .SetEase(Tween.EaseType.Out);
        }
    }

    /// <summary>整体淡出后执行 onDone（切场景用）。</summary>
    protected void FadeOut(Control root, float dur, Action onDone)
    {
        var tw = CreateTween();
        tw.TweenProperty(root, "modulate:a", 0f, dur)
          .SetTrans(Tween.TransitionType.Quad)
          .SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(onDone));
    }

    // ---------- 弹窗 ----------

    protected Control BuildOverlay(string title, params Control[] rows)
        => BuildOverlay(title, out _, rows);

    /// <summary>半透明遮罩 + 居中面板。content 是内容区（可后续往里填/清空）。</summary>
    protected Control BuildOverlay(string title, out VBoxContainer content, params Control[] rows)
    {
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.Visible = false;

        var dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.65f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        margin.AddChild(box);

        var titleLabel = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(titleLabel);

        box.AddChild(new HSeparator());

        content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        box.AddChild(content);

        foreach (var r in rows) content.AddChild(r);

        Root.AddChild(root);
        return root;
    }

    protected void ShowOverlay(Control overlay)
    {
        overlay.Visible = true;
        overlay.Modulate = new Color(1f, 1f, 1f, 0f);
        var tw = CreateTween();
        tw.TweenProperty(overlay, "modulate:a", 1f, 0.4f)
          .SetTrans(Tween.TransitionType.Quad)
          .SetEase(Tween.EaseType.Out);
    }

    protected void HideOverlay(Control overlay)
    {
        var tw = CreateTween();
        tw.TweenProperty(overlay, "modulate:a", 0f, 0.25f)
          .SetTrans(Tween.TransitionType.Quad)
          .SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(() => overlay.Visible = false));
    }
}
