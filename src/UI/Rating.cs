namespace GGJ;

/// <summary>结算评级：按总失败次数。SSS=0 / SS=1 / S=2 / A=3-4 / B=5-7 / C=8-12 / D=13+。</summary>
public static class Rating
{
    public static string RankOf(int deaths) =>
        deaths <= 0 ? "SSS"
        : deaths == 1 ? "SS"
        : deaths == 2 ? "S"
        : deaths <= 4 ? "A"
        : deaths <= 7 ? "B"
        : deaths <= 12 ? "C"
        : "D";

    /// <summary>评级转优先级（用于比较，SSS 最高 → D 最低）。</summary>
    public static int Priority(string rank) =>
        rank switch
        {
            "SSS" => 6,
            "SS" => 5,
            "S" => 4,
            "A" => 3,
            "B" => 2,
            "C" => 1,
            _ => 0,
        };
}
