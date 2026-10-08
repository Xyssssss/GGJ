namespace GGJ;

/// <summary>
/// 物理层常量。与 project.godot 的 [layer_names] 一一对应，改这里同步改那里。
/// 约定：玩家 / 敌人用 CharacterBody2D；子弹用 Area2D。
/// </summary>
public static class Layers
{
    public const uint None         = 0u;
    public const uint Player       = 1u << 0;   // 1
    public const uint Enemy        = 1u << 1;   // 2
    public const uint EnemyBullet  = 1u << 2;   // 4
    public const uint PlayerBullet = 1u << 3;   // 8
}
