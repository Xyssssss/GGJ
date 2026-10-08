using Godot;

namespace GGJ;

public enum DamageKind
{
    Bullet,     // 子弹
    Contact,    // 碰撞（玩家 ↔ 敌人）
    Laser,      // 激光
}

/// <summary>
/// 一次伤害的完整上下文。全游戏唯一的伤害入口是 DamageSystem.Deal(ref HitInfo)。
/// </summary>
public struct HitInfo
{
    public Node? Source;
    public Node? Target;
    public float BaseAmount;
    public float FinalAmount;
    public DamageKind Kind;
    public bool SourceIsPlayer;
    public bool TargetIsPlayer;
    public bool Canceled;
    public Vector2 Position;
}
