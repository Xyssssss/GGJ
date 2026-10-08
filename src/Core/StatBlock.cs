using System;
using System.Collections.Generic;

namespace GGJ;

/// <summary>
/// 属性聚合器。玩家线 / 敌人线各一个实例，完全独立。
/// 结算顺序：Base → 所有 Add → 所有 Mul → Override 最后。
/// 程序 A 只读 Get()，不需要知道词条系统长什么样。
/// </summary>
public sealed class StatBlock
{
    private readonly List<IModifier> _mods = new();
    private readonly Dictionary<Enum, float> _base = new();
    private readonly Dictionary<Enum, float> _cache = new();
    private bool _dirty = true;

    public void SetBase(Enum stat, float value)
    {
        _base[stat] = value;
        _dirty = true;
    }

    public float GetBase(Enum stat) => _base.TryGetValue(stat, out var v) ? v : 0f;

    public void AddModifier(IModifier m)
    {
        _mods.Add(m);
        _dirty = true;
    }

    public void RemoveModifier(IModifier m)
    {
        _mods.Remove(m);
        _dirty = true;
    }

    public void ClearModifiers()
    {
        _mods.Clear();
        _dirty = true;
    }

    public float Get(Enum stat)
    {
        if (_dirty) Recalc();
        return _cache.TryGetValue(stat, out var v) ? v : 0f;
    }

    /// <summary>开关类词条复用同一套：&gt; 0 视为开启。</summary>
    public bool HasFlag(Enum stat) => Get(stat) > 0f;

    private void Recalc()
    {
        _cache.Clear();
        foreach (var kv in _base) _cache[kv.Key] = kv.Value;

        foreach (var m in _mods)
            if (m.Op == ModifierOp.Add)
                _cache[m.Stat] = _cache.GetValueOrDefault(m.Stat) + m.Value;

        foreach (var m in _mods)
            if (m.Op == ModifierOp.Mul)
                _cache[m.Stat] = _cache.GetValueOrDefault(m.Stat) * (1f + m.Value);

        foreach (var m in _mods)
            if (m.Op == ModifierOp.Override)
                _cache[m.Stat] = m.Value;

        _dirty = false;
    }
}
