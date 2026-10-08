using System.Collections.Generic;
using Godot;

namespace GGJ;

/// <summary>
/// 极简对象池。子弹 / 敌人走这里，避免每帧 Instantiate 掉帧。
/// 回收时不 Free，只是隐藏 + 移出屏幕 + 停物理帧。
/// </summary>
public sealed class Pool<T> where T : Node2D
{
    private readonly PackedScene _scene;
    private readonly Node _parent;
    private readonly Stack<T> _free = new();

    /// <summary>
    /// 已借出（在途）实例登记本。
    /// ⚠️ 防重复归还：Area2D 的 body_entered 在同一物理帧会为每个同时进入的
    /// 物体各派发一次回调 —— 一发子弹扎进重叠的敌群里会收到 N 次"命中"，
    /// 若无脑 Return N 次，同一节点会在 _free 栈里记 N 笔账；之后 Rent 把
    /// 同一个实例借给 N 个"分身"，表现为子弹凭空消失 / 互相吞（P2-19 吞弹 bug）。
    /// Return 时先查是否在途：不在途 = 已还过，直接忽略。
    /// </summary>
    private readonly HashSet<T> _inUse = new();

    public Pool(PackedScene scene, Node parent)
    {
        _scene = scene;
        _parent = parent;
    }

    public int FreeCount => _free.Count;

    public T Rent()
    {
        T n;
        if (_free.Count > 0)
        {
            n = _free.Pop();
        }
        else
        {
            n = _scene.Instantiate<T>();
            _parent.AddChild(n);
        }
        _inUse.Add(n);
        n.SetPhysicsProcess(true);
        n.Visible = true;
        return n;
    }

    public void Return(T n)
    {
        if (!GodotObject.IsInstanceValid(n)) return;

        // 防重复归还：只有真正在途的实例才允许还池。同一实例第二次 Return 时
        // Remove 返回 false → 忽略，否则会重复入栈导致"一节点多分身"。
        if (!_inUse.Remove(n))
        {
            GD.PushWarning($"[Pool] 重复归还 {typeof(T).Name}，已忽略（防分身 bug）。");
            return;
        }

        // ⚠️ "隐藏 + 停物理 + 入栈"整体延到本帧消息队列 flush 时再执行，理由有二：
        //   ① Return 常由物理回调（OnBodyEntered / 清场）触发，直接改 visible /
        //      physics / global_position 会报 "Can't change this state while flushing queries"。
        //   ② 绝不能"同步入栈、只把清理排 deferred"——若同帧内 deferred 执行前又有
        //      Rent 复用了这个节点（典型：ClearAll 清场后同一帧 BeginRound 刷下一轮首波，
        //      或击杀帧与波次定时刷怪撞在同一帧），帧尾的 deferred 会把刚复活的
        //      敌人/子弹一起隐藏 + 瞬移出屏 → 场上留隐形怪（打不到、清不掉），
        //      轮次胜利条件（场上清空）永远不满足 → "敌人死了但下一波/下一轮不来"。
        //      延迟入栈后：同帧的 Rent 只会新建实例，能拿到的永远是已清理好的节点。
        Callable.From(() => FinishReturn(n)).CallDeferred();
    }

    /// <summary>deferred：真正执行隐藏 / 停物理 / 入栈。</summary>
    private void FinishReturn(T n)
    {
        if (!GodotObject.IsInstanceValid(n)) return;
        // 保险：极端时序下节点被重新租出（回到 _inUse），状态由 Rent/Launch 接管，跳过清理。
        if (_inUse.Contains(n)) return;

        n.SetPhysicsProcess(false);
        n.Visible = false;
        n.GlobalPosition = new Vector2(-99999f, -99999f);
        // ⚠️ 立即把物理服务器里的 body 也挪到屏外回收位。
        //    只设 GlobalPosition 的话，服务器 body 的 transform 要到下一次物理 flush 才更新；
        //    若这只实体回收前紧贴玩家（敌我同帧相撞 / 玩家撞死最后一只怪），在下一次 flush 前
        //    玩家 Area2D 的 overlap 快照仍残留它 → 若它在本轮出生时被复用，
        //    玩家新轮首帧会把"逻辑已出生到远处的敌"误判为接触 → 开局莫名掉血 + 刚刷的怪秒死。
        //    这里在 deferred（idle 阶段，不在物理 flush 中）用 BodySetState 强制立即同步。
        //    ⚠️ 只对物理体（CharacterBody2D/RigidBody2D 等）做：BodySetState 只认 body RID，
        //    子弹等 Area2D 的 GetRid() 是 area RID，喂给 body_set_state 会报 "body is null"。
        if (n is PhysicsBody2D pb)
            PhysicsServer2D.BodySetState(pb.GetRid(), PhysicsServer2D.BodyState.Transform,
                new Transform2D(0f, new Vector2(-99999f, -99999f)));
        _free.Push(n);
    }
}
