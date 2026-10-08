using System;
using Godot;

namespace GGJ;

/// <summary>
/// 移动端触摸控件（安卓版）：左下角 360° 虚拟摇杆 + 右侧「发射」「闪现」两个圆形按钮。
/// 只在触摸设备上显示；桌面端自动隐藏，不影响键鼠。
/// 接入点：
///   · InputState.MoveAxis / FireHeld 会合并这里的摇杆轴与发射按钮
///   · Weapon 闪现会消费 ConsumeDash()
/// 参考 joystick.gd：用 _input 拿全局坐标（event.position 是 viewport 坐标），
/// 摇杆头相对底座用局部坐标定位，触摸落在控件矩形内才响应，支持多点触控。
/// </summary>
public partial class TouchControls : CanvasLayer
{
    private static TouchControls? _i;
    public static TouchControls? I => _i;

    /// <summary>是否在触摸设备上启用（安卓）。电脑端为 false（但按钮仍会显示供鼠标测试）。</summary>
    public static bool Active => _i != null && _i._isTouchDevice;

    /// <summary>摇杆产生的移动轴（长度 0~1）。</summary>
    public static Vector2 MoveAxis => _i?._moveAxis ?? Vector2.Zero;
    /// <summary>发射按钮是否按住（替代鼠标左键）。</summary>
    public static bool FireHeld => _i?._fireHeld ?? false;

    private bool _isTouchDevice;
    private Vector2 _moveAxis;
    private bool _fireHeld;
    private bool _dashQueued;

    // ---- 摇杆 ----
    private Control _stickBase = null!;   // 底座（全局坐标定位）
    private Control _stickKnob = null!;   // 摇杆头（底座的子节点，局部坐标）
    private Vector2 _stickCenter;         // 摇杆中心（全局坐标）
    private int _stickTouch = -1;
    private bool _stickMouse;             // 鼠标是否按住摇杆（电脑端测试）
    private const float StickRadius = 70f;

    // ---- 按钮 ----
    private Control _fireBtn = null!;
    private Control _dashBtn = null!;
    private int _fireTouch = -1;
    private int _dashTouch = -1;
    private bool _fireMouse;              // 鼠标是否按住发射按钮
    private bool _dashMouse;              // 鼠标是否按住闪现按钮

    public override void _Ready()
    {
        _i = this;
        ProcessMode = ProcessModeEnum.Always;
        Layer = 10;   // 画在 HUD 之上

        _isTouchDevice = DisplayServer.IsTouchscreenAvailable();
        BuildStick();
        BuildButtons();
        UpdateDashVisibility();
    }

    public override void _Process(double delta) => UpdateDashVisibility();

    public override void _Input(InputEvent e)
    {
        HandleStickInput(e);
        HandleFireInput(e);
        HandleDashInput(e);
    }

    /// <summary>读取并清空「闪现」请求（Weapon 每帧调用，等价于右键按下边沿）。</summary>
    public static bool ConsumeDash()
    {
        if (_i == null) return false;
        bool q = _i._dashQueued;
        _i._dashQueued = false;
        return q;
    }

    /// <summary>闪现按钮只在玩家拿到「闪现」buff 后显示。</summary>
    private void UpdateDashVisibility()
    {
        if (_dashBtn == null) return;
        var gm = GameManager.I;
        bool hasDash = gm != null && gm.PlayerStats != null && gm.PlayerStats.HasFlag(PlayerStat.FlagDash);
        _dashBtn.Visible = hasDash;
    }

    // ---------- 摇杆 ----------

    private void BuildStick()
    {
        // 底座（全局坐标，左下角留边距）
        _stickBase = MakeCircle(160, new Color(1f, 1f, 1f, 0.08f));
        _stickBase.Position = new Vector2(50, 410);
        AddChild(_stickBase);

        // 摇杆头（底座的子节点，局部坐标，初始居中）
        _stickKnob = MakeCircle(40, new Color(1f, 1f, 1f, 0.28f));
        _stickKnob.Position = _stickBase.Size / 2f - _stickKnob.Size / 2f;
        _stickBase.AddChild(_stickKnob);

        // 摇杆中心 = 底座中心（全局坐标）
        _stickCenter = _stickBase.Position + _stickBase.Size / 2f;
    }

    private void HandleStickInput(InputEvent e)
    {
        if (e is InputEventScreenTouch t)
        {
            if (t.Pressed)
            {
                if (_stickTouch == -1 && !_stickMouse && RectOf(_stickBase).HasPoint(t.Position))
                {
                    _stickTouch = t.Index;
                    UpdateStick(t.Position);
                }
            }
            else if (t.Index == _stickTouch)
            {
                ResetStick();
            }
        }
        else if (e is InputEventScreenDrag d && d.Index == _stickTouch)
        {
            UpdateStick(d.Position);
        }
        else if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (_stickTouch == -1 && !_stickMouse && RectOf(_stickBase).HasPoint(mb.Position))
                {
                    _stickMouse = true;
                    UpdateStick(mb.Position);
                }
            }
            else if (_stickMouse)
            {
                ResetStick();
            }
        }
        else if (e is InputEventMouseMotion mm && _stickMouse)
        {
            UpdateStick(mm.Position);
        }
    }

    /// <summary>touchPos 是全局坐标（viewport），与 _stickCenter 同坐标系，方向正确。</summary>
    private void UpdateStick(Vector2 touchPos)
    {
        Vector2 vec = touchPos - _stickCenter;
        if (vec.Length() > StickRadius) vec = vec.Normalized() * StickRadius;
        _moveAxis = vec / StickRadius;
        // 摇杆头是底座子节点，用局部坐标定位
        _stickKnob.Position = vec + _stickBase.Size / 2f - _stickKnob.Size / 2f;
    }

    private void ResetStick()
    {
        _stickTouch = -1;
        _stickMouse = false;
        _moveAxis = Vector2.Zero;
        _stickKnob.Position = _stickBase.Size / 2f - _stickKnob.Size / 2f;
    }

    // ---------- 按钮 ----------

    private void BuildButtons()
    {
        // 发射（替代鼠标左键）：右下偏上
        _fireBtn = MakeTouchButton(112, "发射", new Color(0.92f, 0.50f, 0.18f, 0.32f));
        _fireBtn.Position = new Vector2(1120, 460);
        AddChild(_fireBtn);

        // 闪现（替代鼠标右键）：发射按钮正下方，仅拿到闪现 buff 后显示
        _dashBtn = MakeTouchButton(92, "闪现", new Color(0.30f, 0.60f, 1f, 0.32f));
        _dashBtn.Position = new Vector2(1130, 590);
        AddChild(_dashBtn);
    }

    private void HandleFireInput(InputEvent e)
    {
        if (_fireBtn == null) return;

        if (e is InputEventScreenTouch t)
        {
            if (t.Pressed)
            {
                if (_fireTouch == -1 && !_fireMouse && RectOf(_fireBtn).HasPoint(t.Position))
                {
                    _fireTouch = t.Index;
                    _fireHeld = true;
                }
            }
            else if (t.Index == _fireTouch)
            {
                _fireTouch = -1;
                _fireHeld = false;
            }
        }
        else if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (_fireTouch == -1 && !_fireMouse && RectOf(_fireBtn).HasPoint(mb.Position))
                {
                    _fireMouse = true;
                    _fireHeld = true;
                }
            }
            else if (_fireMouse)
            {
                _fireMouse = false;
                _fireHeld = false;
            }
        }
    }

    private void HandleDashInput(InputEvent e)
    {
        if (_dashBtn == null || !_dashBtn.Visible) return;

        if (e is InputEventScreenTouch t)
        {
            if (t.Pressed)
            {
                if (_dashTouch == -1 && !_dashMouse && RectOf(_dashBtn).HasPoint(t.Position))
                {
                    _dashTouch = t.Index;
                    _dashQueued = true;
                }
            }
            else if (t.Index == _dashTouch)
            {
                _dashTouch = -1;
            }
        }
        else if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (_dashTouch == -1 && !_dashMouse && RectOf(_dashBtn).HasPoint(mb.Position))
                {
                    _dashMouse = true;
                    _dashQueued = true;
                }
            }
            else if (_dashMouse)
            {
                _dashMouse = false;
            }
        }
    }

    private static Rect2 RectOf(Control c) => new(c.Position, c.Size);

    // ---------- 绘制辅助 ----------

    private static Control MakeTouchButton(float size, string text, Color color)
    {
        var btn = new Control
        {
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var bg = MakeCircle(size, color);
        btn.AddChild(bg);

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        btn.AddChild(label);

        return btn;
    }

    private static Control MakeCircle(float diameter, Color color)
    {
        var panel = new PanelContainer
        {
            Size = new Vector2(diameter, diameter),
            CustomMinimumSize = new Vector2(diameter, diameter),
            MouseFilter = Control.MouseFilterEnum.Stop,   // Stop：让鼠标悬停在摇杆/按钮上时 PointerOverUi=true，避免同时误触发射
        };
        panel.AddThemeStyleboxOverride("panel", CircleStyle(diameter, color));
        return panel;
    }

    private static StyleBoxFlat CircleStyle(float size, Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = (int)(size / 2f),
        CornerRadiusTopRight = (int)(size / 2f),
        CornerRadiusBottomLeft = (int)(size / 2f),
        CornerRadiusBottomRight = (int)(size / 2f),
    };
}
