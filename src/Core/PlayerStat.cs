namespace GGJ;

/// <summary>
/// 玩家线属性枚举。早冻结 —— 加新词条先在这里加枚举，别再搞一套平行体系。
/// 命名约定：Flag 前缀 = 纯开关（0 / 1），其余 = 数值。
/// </summary>
public enum PlayerStat
{
    // 数值类
    MaxHP,
    MoveSpeed,
    FireCooldown,       // 秒
    BulletSpeed,
    ExtraShots,         // 连发：每次发射额外补射几发
    Pierce,             // 穿透个数（0 = 不穿透）
    InvincibleTime,
    HitboxScale,        // 碰撞体积倍率（娇小 → &lt; 1）
    DashRange,
    DashCooldown,
    LifestealKills,     // 吸血：累计击杀 X 个
    LifestealAmount,    // 吸血：恢复 Y 点
    RicochetChance,

    // 机制类开关
    FlagLaser,          // 激光：穿透 + 顺时针环绕玩家
    FlagRicochet,       // 跳弹：碰边缘按概率反射
    FlagDash,           // 闪现（鼠标右键）
    FlagDeathblade,     // 名刀：0 血后窗口内清场则复活判胜
    DeathbladeWindow,
    GrowthKills,        // 成长：累计击杀 X 个 → 生命上限 +Y
    GrowthAmount,       // 成长：每次增加的生命上限
}
