using Godot;

namespace GGJ;

/// <summary>
/// 三选一 UI 还没做出来时的兜底：自动抽卡、自动选一张、立即生效。
///
/// 为什么需要：RoundDirector 在轮次结束时会 GetTree().Paused = true 并等 UpgradeChosen。
/// 如果场上没有任何订阅者回应，游戏就会永久暂停（而且没有任何报错）。
/// Scenes/ui/upgrade_picker.tscn 建好后，Main 会优先加载它，本类自动不启用。
///
/// P2-18：从"直接跳过"升级为"真出货"——抽 3 张 → 随机选 1 张 → Apply 生效 →
/// 回应 UpgradeChosen。这样三选一 UI 没好之前，主循环已经带着真 buff 跑，策划可实测数值。
/// </summary>
public partial class AutoUpgradeFallback : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // 暂停 / 非暂停都要能收到事件
        Bus.Sub<UpgradeOffered>(this, OnOffered);
    }

    private void OnOffered(UpgradeOffered e)
    {
        // ---- 隔离区 1：抽卡 + 生效（异常在这里面就地捕获，不影响主循环）----
        Resource? picked = null;
        try
        {
            var svc = GetParent()?.GetNodeOrNull<UpgradeService>("UpgradeService");
            var choices = svc?.PickChoices(e.ForPlayer);
            if (choices != null && choices.Count > 0)
            {
                picked = choices[Rng.Index(choices.Count)];   // 随机选 1 张
                svc!.Apply(picked);                            // 立即生效（AddModifier + 记入已拥有）
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[兜底] 抽卡/生效异常：{ex}");
        }

        string cardName = picked switch
        {
            PlayerUpgradeData p => p.DisplayName,
            EnemyUpgradeData en => en.DisplayName,
            _ => "（词条池为空，跳过）",
        };
        GD.Print($"[兜底] 三选一 UI 尚未制作，自动选择：{cardName}（{(e.ForPlayer ? "玩家线，重打本轮" : "敌人线，进入下一轮")}）");

        // ---- 隔离区 2：回应选择（内部触发 BeginRound 整条链）----
        // ⚠️ 无论上面是否异常，都必须回应 UpgradeChosen，否则游戏永久暂停。
        // 包 try-catch：链上任何异常在这里现形（PushError 必定显示在输出面板），且不再
        // 向上传播打断 Bus.Pub 的其他订阅者（修复"展示日志消失"的隐蔽断链）。
        GetTree().Paused = false;
        try
        {
            Bus.Pub(new UpgradeChosen(e.ForPlayer));
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[兜底] 回应选择时异常（BeginRound 链）：{ex}");
        }
    }
}
