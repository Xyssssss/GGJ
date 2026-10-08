using Godot;

namespace GGJ;

// ⚠️ 事件定义散在各模块自己的 Events.cs，不要合并成一个 GameEvents.cs ——
//    三个人同时改一个文件 = 天天解冲突。加新事件就在自己模块下加。

public readonly struct EntityDamaged
{
    public readonly Node? Target;
    public readonly float Amount;
    public readonly Vector2 Position;
    public readonly bool Killed;
    public readonly bool TargetIsPlayer;

    public EntityDamaged(Node? target, float amount, Vector2 pos, bool killed, bool targetIsPlayer)
    {
        Target = target; Amount = amount; Position = pos; Killed = killed; TargetIsPlayer = targetIsPlayer;
    }
}

public readonly struct EntityDied
{
    public readonly Node? Target;
    public readonly Vector2 Position;
    public readonly bool TargetIsPlayer;

    public EntityDied(Node? target, Vector2 pos, bool targetIsPlayer)
    {
        Target = target; Position = pos; TargetIsPlayer = targetIsPlayer;
    }
}

public readonly struct PlayerHurt
{
    public readonly int HpLeft;
    public readonly Vector2 Position;
    public PlayerHurt(int hpLeft, Vector2 pos) { HpLeft = hpLeft; Position = pos; }
}

public readonly struct EntityHealed
{
    public readonly Node? Target;
    public readonly int Amount;
    public readonly bool TargetIsPlayer;

    public EntityHealed(Node? target, int amount, bool targetIsPlayer)
    {
        Target = target; Amount = amount; TargetIsPlayer = targetIsPlayer;
    }
}
