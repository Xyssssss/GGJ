using Godot;

namespace GGJ;

/// <summary>
/// 供 P2 判断鼠标是否悬停在 UI 上（避免点击按钮时误触发开火）。
/// UiBase 启动时会注册视口。
/// </summary>
public static class UiInput
{
    private static Viewport? _viewport;

    public static void Register(Viewport viewport) => _viewport = viewport;

    /// <summary>鼠标当前是否悬停在某个可交互的 Control（UI）上。</summary>
    public static bool PointerOverUi =>
        _viewport != null && _viewport.GuiGetHoveredControl() != null;
}
