using Godot;

namespace GGJ;

/// <summary>
/// 全游戏唯一的伤害入口。所有扣血必须走这里，方便以后加穿透 / 激光 / 反伤。
///
/// ⚠️ 铁律：无敌帧判定、穿透、激光、反伤一律实现为 IModifier 或走 Canceled 标志，
///    不许把具体词条的判断写进本文件。
/// </summary>
public static class DamageSystem
{
    /// <summary>返回 true 表示目标死亡。</summary>
    public static bool Deal(ref HitInfo hit)
    {
        if (hit.Canceled) return false;

        var target = hit.Target;
        if (target == null || !GodotObject.IsInstanceValid(target)) return false;

        var health = target.GetNodeOrNull<Health>("Health");
        if (health == null || !GodotObject.IsInstanceValid(health)) return false;

        hit.FinalAmount = hit.BaseAmount;

        // ---- 接触伤害：无敌帧内双方都不结算（GDD §3.4）----
        if (hit.Kind == DamageKind.Contact)
        {
            bool playerInvolved = hit.SourceIsPlayer || hit.TargetIsPlayer;

            if (GameManager.I.DeathbladeActive)
            {
                // 名刀窗口是 §3.4 的显式例外：玩家不掉血，但敌人照常被击杀。
                if (hit.TargetIsPlayer) { hit.Canceled = true; return false; }
            }
            else if (playerInvolved && PlayerInvincible())
            {
                hit.Canceled = true;
                return false;
            }
        }

        // ---- ① 攻击方修饰器 / ② 防守方修饰器 ----
        // TODO(程序B)：词条系统接入点
        //   UpgradeService.ModifyOutgoing(ref hit);
        //   UpgradeService.ModifyIncoming(ref hit);
        if (hit.Canceled) return false;

        // ---- ③ 结算 ----
        var pos = target is Node2D n2 ? n2.GlobalPosition : Vector2.Zero;
        bool killed = health.ApplyDamage(hit.FinalAmount);

        Bus.Pub(new EntityDamaged(target, hit.FinalAmount, pos, killed, hit.TargetIsPlayer));
        if (killed) Bus.Pub(new EntityDied(target, pos, hit.TargetIsPlayer));

        return killed;
    }

    private static bool PlayerInvincible()
    {
        var p = GameManager.I.Player;
        if (p == null || !GodotObject.IsInstanceValid(p)) return false;
        var h = p.GetNodeOrNull<Health>("Health");
        return h != null && h.Invincible;
    }
}
