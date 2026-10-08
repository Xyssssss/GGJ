using Godot;

namespace GGJ;

/// <summary>
/// 调试工具（发布前删掉本文件 + Hud 里那行 Poll 调用即可）。
/// F1：跳过当前轮，视为胜利，进下一轮（三选一暂停 / 结算暂停时不生效）。
/// F2：消灭场上所有敌人（走正常击杀流程，死亡特效 / 吸血成长 / 分裂词条都会正常触发）。
/// </summary>
public static class DebugTools
{
    private static bool _f1Down;
    private static bool _f2Down;

    public static void Poll(SceneTree tree)
    {
        bool down = Input.IsKeyPressed(Key.F1);

        // 只在“非暂停 + 按下瞬间”触发一次，避免按住连跳
        if (!tree.Paused && down && !_f1Down)
        {
            // ForPlayer = false → 相当于本轮胜利，RoundDirector 会推进到下一轮
            Bus.Pub(new UpgradeChosen(false));
            GD.Print("[Debug] 跳过当前轮 → 下一轮");
        }

        _f1Down = down;

        bool f2 = Input.IsKeyPressed(Key.F2);
        if (!tree.Paused && f2 && !_f2Down) KillAllEnemies(tree);
        _f2Down = f2;
    }

    /// <summary>
    /// F2：对场上每只存活敌人结算巨额伤害，走 DamageSystem → EntityDied 完整链路
    /// （Fx 播死亡特效、Player 计吸血/成长、EnemyService 回收）。
    /// ⚠️ 敌人线开了分裂词条时，母体死亡会裂出小怪 → 必须多扫几轮，直到场上没有活怪才算清完。
    /// </summary>
    private static void KillAllEnemies(SceneTree tree)
    {
        int killed = 0;
        for (int sweep = 0; sweep < 8; sweep++)
        {
            var enemies = tree.GetNodesInGroup("enemy");
            bool aliveAny = false;
            foreach (var n in enemies)
            {
                // 跳过对象池里空闲（已 Deactivate）的实例：它们也还挂在 "enemy" 组里
                if (n is not EnemyBase eb) continue;
                if (!GodotObject.IsInstanceValid(eb) || !eb.Active) continue;
                aliveAny = true;

                var hit = new HitInfo
                {
                    Source = GameManager.I?.Player,
                    Target = eb,
                    SourceIsPlayer = true,
                    TargetIsPlayer = false,
                    BaseAmount = 999999f,     // 敌人无受击无敌帧，巨额伤害必定致死
                    Kind = DamageKind.Bullet,
                    Position = eb.GlobalPosition,
                };
                if (DamageSystem.Deal(ref hit)) killed++;
            }
            if (!aliveAny) break;   // 本趟一个活怪都没打 → 清干净了
        }
        GD.Print($"[Debug] F2 清场完成，共击杀 {killed} 只");
    }
}
