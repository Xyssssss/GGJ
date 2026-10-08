using Godot;

namespace GGJ;

/// <summary>
/// 子弹服务：持有对象池，处理生成 / 回收。由 Main 创建。
/// 其他模块只发事件，不直接碰池子。
/// </summary>
public partial class BulletService : Node
{
    private Pool<Bullet>? _pool;

    public void Init(PackedScene? scene)
    {
        if (scene == null)
        {
            GD.PushWarning("[BulletService] 没有 BulletScene，子弹功能关闭。检查 data/run_config.tres。");
            return;
        }
        _pool = new Pool<Bullet>(scene, this);
        Bus.Sub<SpawnBulletRequest>(this, OnSpawn);
        Bus.Sub<DespawnBullet>(this, OnDespawn);
        // 新轮开始前清空在途子弹：上一轮末尾射出、被三选一暂停冻结在
        // 右边缘（x≈1260，正好是新轮出生点）的子弹，解除暂停当帧会把
        // 新轮第 1 波当帧秒杀。RoundDirector.BeginRound 在刷第一波之前发布此事件。
        Bus.Sub<RoundClearing>(this, _ => ClearActive());
    }

    private void OnSpawn(SpawnBulletRequest r)
    {
        if (_pool == null) return;
        _pool.Rent().Launch(r.Position, r.Direction, r.Speed, r.Damage, r.Pierce, r.Laser);
    }

    private void OnDespawn(DespawnBullet e)
    {
        if (_pool == null || e.Bullet == null) return;
        if (!GodotObject.IsInstanceValid(e.Bullet)) return;
        _pool.Return(e.Bullet);
    }

    /// <summary>
    /// 清空所有在途子弹。由 RoundClearing 事件触发（新轮开始前）。
    /// 子弹都是本节点的子节点（Pool 用 this 作父节点）。
    /// 走 Bullet.Retire（幂等出口：置 _dead + 发事件 → OnDespawn → 回池），
    /// 避免直接 Return 导致"同一发已回池后又被物理回调 Despawn"造成重复归还。
    /// </summary>
    private void ClearActive()
    {
        if (_pool == null) return;
        foreach (var child in GetChildren())
        {
            if (child is Bullet b && GodotObject.IsInstanceValid(b) && b.Visible)
                b.Retire();
        }
    }
}
