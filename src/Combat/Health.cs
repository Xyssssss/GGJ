using Godot;

namespace GGJ;

/// <summary>
/// 血量组件。挂在任何可受伤的实体下，**节点名必须叫 "Health"**
/// （DamageSystem 按这个名字找，改名就断链）。
/// </summary>
public partial class Health : Node
{
    [Export] public int MaxHP { get; set; } = 1;

    public int Current { get; private set; }
    public bool IsDead => Current <= 0;
    public bool Invincible => _invTimer > 0f;

    private float _invTimer;

    public override void _Ready()
    {
        Current = MaxHP;
    }

    public override void _Process(double delta)
    {
        if (_invTimer > 0f) _invTimer -= (float)delta;
    }

    public void FullHeal() => Current = MaxHP;

    /// <summary>恢复指定血量（不超过 MaxHP）。已死则忽略。</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        Current = Mathf.Min(MaxHP, Current + amount);
        bool isPlayer = GetParent() is Node owner && owner.IsInGroup("player");
        Bus.Pub(new EntityHealed(GetParent(), amount, isPlayer));
    }

    public void SetMaxHP(int v, bool healToFull = false)
    {
        MaxHP = v;
        Current = healToFull ? MaxHP : Mathf.Min(Current, MaxHP);
    }

    public void StartInvincible(float seconds) => _invTimer = Mathf.Max(_invTimer, seconds);

    public void ClearInvincible() => _invTimer = 0f;

    /// <summary>
    /// 返回 true 表示本次伤害致死。
    /// ⚠️ 受击无敌帧只给玩家（策划案：仅玩家受击"进入1.5s无敌帧"，敌人条目无此描述）。
    /// 敌人无受击无敌帧 —— 否则连发/速射/激光词条对单体无效，精英怪变成打不动的肉盾。
    /// </summary>
    public bool ApplyDamage(float amount)
    {
        if (Current <= 0) return false;
        if (Invincible) return false;

        Current -= Mathf.Max(1, (int)amount);

        // 只有宿主是玩家才启动受击无敌帧。Health 挂在实体（Player / EnemyBase）下面，
        // GetParent() 即宿主；Player._Ready 里已 AddToGroup("player")。
        if (GetParent() is Node ownerNode && ownerNode.IsInGroup("player"))
            StartInvincible(GameManager.I.Feel?.InvincibleTime ?? 1.5f);

        if (Current <= 0)
        {
            Current = 0;
            return true;
        }
        return false;
    }
}
