namespace GGJ;

/// <summary>
/// 主菜单 / UI 层的跨模块事件。菜单只发事件、不直接调用别的模块。
/// 存档 / 读档 / 恢复进度由 P0（策划·逻辑手）订阅这些事件实现。
/// </summary>

/// <summary>请求新开一局（从第 1 轮开始）。P0 可订阅以清理 / 覆盖旧存档。</summary>
public readonly struct NewGameRequested
{
}

/// <summary>请求继续指定存档位（Slot 从 0 开始）。P0 订阅后读档并恢复进度。</summary>
public readonly struct ContinueGameRequested
{
    public readonly int Slot;
    public ContinueGameRequested(int slot) { Slot = slot; }
}
