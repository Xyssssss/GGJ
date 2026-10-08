using Godot;

namespace GGJ;

/// <summary>
/// 全部面向玩家的文案。换皮只改 data/strings.tres，代码一行不动。
/// ⚠️ 代码里不允许出现硬编码的显示文本，一律走 GameManager.I.T(key)。
/// </summary>
[GlobalClass]
public partial class StringsData : Resource
{
    [Export] public Godot.Collections.Dictionary Entries { get; set; } = new();

    public string Get(string key, string fallback = "")
    {
        if (Entries != null && Entries.TryGetValue(key, out var v))
        {
            var s = v.AsString();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return string.IsNullOrEmpty(fallback) ? key : fallback;
    }

    public static StringsData CreateDefault() => new()
    {
        Entries = new Godot.Collections.Dictionary
        {
            { "player_name", "幸存者" },
            { "enemy_name",  "来袭者" },
            { "ui_hp",       "生命" },
        }
    };
}
