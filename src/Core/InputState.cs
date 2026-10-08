using Godot;

namespace GGJ;

/// <summary>
/// 输入读取的唯一入口。直接轮询按键，不依赖 Godot InputMap
/// （省掉 project.godot 配置，0 基础队友不会踩坑）。要支持手柄改这一个文件就够。
/// </summary>
public static class InputState
{
    public static Vector2 MoveAxis()
    {
        var v = TouchControls.MoveAxis;   // 触摸摇杆
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  v.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) v.X += 1f;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    v.Y -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  v.Y += 1f;
        return v.LengthSquared() > 1f ? v.Normalized() : v;
    }

    public static bool FireHeld
    {
        get
        {
            // 发射按钮（安卓触摸 + 电脑测试的鼠标点击发射按钮）
            if (TouchControls.FireHeld) return true;
            // 触摸设备：屏蔽鼠标左键（触摸会模拟鼠标左键，导致点哪都开火）
            if (TouchControls.Active) return false;
            // 电脑端：鼠标左键（悬停在 UI 上时不发射）
            if (UiInput.PointerOverUi) return false;
            return Input.IsMouseButtonPressed(MouseButton.Left);
        }
    }

    public static bool DashHeld => Input.IsMouseButtonPressed(MouseButton.Right);
}
