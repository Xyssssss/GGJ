using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 菜单键盘/鼠标导航：管理一组按钮的"预选"（高亮）状态。
/// - 预选 = 鼠标悬停 或 键盘上下选中；特效（两侧细小亮点粒子 + 放大）只出现在被预选的按钮上。
/// - 鼠标移开按钮即取消预选（特效消失）；键盘选中则保持到下次切换。
/// - 上/左(W/A/↑/←) = 上一个，下/右(S/D/↓/→) = 下一个（左右等同上下）。
/// - 回车 = 确认（触发 Pressed）；鼠标左键走 Button 内建点击。
/// </summary>
public partial class MenuNav : Node
{
    private readonly List<Button> _buttons = new();
    private readonly Dictionary<Button, CpuParticles2D> _left = new();
    private readonly Dictionary<Button, CpuParticles2D> _right = new();
    private readonly Dictionary<Button, Tween> _scaleTweens = new();

    private int _index = -1;   // -1 = 未预选任何按钮

    private static ImageTexture? _dotTex;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>注册一个按钮：挂特效（默认隐藏），接入鼠标悬停/移开预选与点击缩放。</summary>
    public void Add(Button btn)
    {
        _buttons.Add(btn);
        btn.FocusMode = Control.FocusModeEnum.None;

        var lp = MakeParticles(btn, left: true);
        var rp = MakeParticles(btn, left: false);
        btn.AddChild(lp);
        btn.AddChild(rp);
        _left[btn] = lp;
        _right[btn] = rp;

        btn.Resized += () => OnResized(btn);
        btn.MouseEntered += () => Select(btn);
        btn.MouseExited += () => { if (_buttons.IndexOf(btn) == _index) Clear(); };
        btn.ButtonDown += () => AnimateScale(btn, 0.94f);
        btn.ButtonUp += () => AnimateScale(btn, IsSelected(btn) ? 1.06f : 1f);
    }

    private void OnResized(Button btn)
    {
        btn.PivotOffset = btn.Size / 2f;
        _left[btn].Position = new Vector2(0f, btn.Size.Y * 0.5f);
        _right[btn].Position = new Vector2(btn.Size.X, btn.Size.Y * 0.5f);
    }

    /// <summary>小颗粒光点：从按钮两侧边中点向两侧水平流动，渐渐淡出。</summary>
    private CpuParticles2D MakeParticles(Button btn, bool left)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, 1f, 1f, 1f));
        ramp.AddPoint(0.5f, new Color(1f, 1f, 1f, 1f));
        ramp.SetColor(1, new Color(1f, 1f, 1f, 0f));

        var p = new CpuParticles2D
        {
            Emitting = false,
            Visible = false,
            LocalCoords = true,
            Amount = 26,
            Lifetime = 1.0f,
            Explosiveness = 0.15f,
            Direction = new Vector2(left ? -1f : 1f, 0f),     // 纯水平向两侧，不下落
            Spread = 12f,
            Gravity = new Vector2(0f, 0f),
            InitialVelocityMin = 40f,
            InitialVelocityMax = 80f,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 0.9f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(4f, 26f),
            Texture = GetDotTexture(),
            Color = new Color(0.85f, 0.93f, 1f, 0.9f),         // 非常浅的蓝白
            ColorRamp = ramp,                                  // 渐渐淡出
        };
        p.Position = new Vector2(left ? 0f : btn.Size.X, btn.Size.Y * 0.5f);
        return p;
    }

    /// <summary>代码生成的小亮点纹理：中心亮、边缘柔和光晕（高斯衰减），细小清晰。</summary>
    private static ImageTexture GetDotTexture()
    {
        if (_dotTex != null) return _dotTex;

        const int size = 16;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d2 = dx * dx + dy * dy;
                float a = Mathf.Exp(-d2 * 6f);   // 高斯光晕：中心亮、边缘柔和
                if (a < 0.02f) a = 0f;
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        _dotTex = ImageTexture.CreateFromImage(img);
        return _dotTex;
    }

    // ---------- 预选切换 ----------

    public void Select(Button btn)
    {
        int idx = _buttons.IndexOf(btn);
        if (idx < 0) return;
        _index = idx;
        Apply();
    }

    public void Clear()
    {
        _index = -1;
        Apply();
    }

    /// <summary>dir = -1 上一个，+1 下一个。跳过不可见/禁用按钮。</summary>
    public void Move(int dir)
    {
        if (_buttons.Count == 0) return;

        int next = _index;
        for (int i = 0; i < _buttons.Count; i++)
        {
            next = next < 0
                ? (dir > 0 ? 0 : _buttons.Count - 1)
                : (next + dir + _buttons.Count) % _buttons.Count;
            if (IsUsable(_buttons[next]))
            {
                _index = next;
                Apply();
                return;
            }
        }
    }

    /// <summary>回车确认：触发当前预选按钮的 Pressed。</summary>
    /// <summary>回车确认：触发当前预选按钮的 Pressed。返回是否真正触发了按钮。</summary>
    public bool Confirm()
    {
        if (_index < 0 || _index >= _buttons.Count) return false;
        var btn = _buttons[_index];
        if (!IsUsable(btn)) return false;
        btn.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private bool IsSelected(Button btn)
        => _index >= 0 && _index < _buttons.Count && _buttons[_index] == btn;

    private static bool IsUsable(Button btn)
        => GodotObject.IsInstanceValid(btn) && btn.IsVisibleInTree() && !btn.Disabled;

    private void Apply()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            var btn = _buttons[i];
            bool sel = i == _index;
            _left[btn].Visible = sel;
            _left[btn].Emitting = sel;
            _right[btn].Visible = sel;
            _right[btn].Emitting = sel;
            AnimateScale(btn, sel ? 1.06f : 1f);
        }
    }

    private void AnimateScale(Button btn, float target)
    {
        if (_scaleTweens.TryGetValue(btn, out var old)) old?.Kill();
        var tw = CreateTween();
        tw.TweenProperty(btn, "scale", Vector2.One * target, 0.1f)
          .SetTrans(Tween.TransitionType.Quad)
          .SetEase(Tween.EaseType.Out);
        _scaleTweens[btn] = tw;
    }

    // ---------- 键盘 ----------

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey key || !key.Pressed || key.Echo) return;

        bool up = key.Keycode is Key.W or Key.Up or Key.A or Key.Left;
        bool down = key.Keycode is Key.S or Key.Down or Key.D or Key.Right;

        if (up) { Move(-1); return; }
        if (down) { Move(1); return; }

        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            // 只有真正触发了按钮才标记 handled，否则让其他导航器（如有）继续处理
            if (Confirm()) GetViewport().SetInputAsHandled();
        }
    }
}
