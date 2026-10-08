using System;
using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 类型安全事件总线。写完即冻结，所有人只读，不要往里加业务判断。
/// 用法：Bus.Sub&lt;MyEvent&gt;(this, OnMyEvent); Bus.Pub(new MyEvent{...});
/// 订阅时传 owner 节点，节点销毁自动解绑，不需要手动 Unsub。
/// </summary>
public static class Bus
{
    private static readonly Dictionary<Type, List<Delegate>> Handlers = new();

    public static void Sub<T>(Node owner, Action<T> handler) where T : struct
    {
        var t = typeof(T);
        if (!Handlers.TryGetValue(t, out var list)) Handlers[t] = list = new List<Delegate>();
        list.Add(handler);
        owner.TreeExiting += () => Unsub(handler);
    }

    public static void Unsub<T>(Action<T> handler) where T : struct
    {
        if (Handlers.TryGetValue(typeof(T), out var list)) list.Remove(handler);
    }

    /// <summary>倒序遍历：允许回调里取消订阅。</summary>
    public static void Pub<T>(T evt) where T : struct
    {
        if (!Handlers.TryGetValue(typeof(T), out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            ((Action<T>)list[i])(evt);
    }
}
