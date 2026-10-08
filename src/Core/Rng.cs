using System;
using Godot;

namespace GGJ;

/// <summary>可播种随机源。所有游戏内随机都走这里，方便复现 bug。</summary>
public static class Rng
{
    private static Random _r = new();

    public static void SetSeed(int seed) => _r = new Random(seed);

    public static float Range(float min, float max) => min + (float)_r.NextDouble() * (max - min);

    public static int RangeInt(int min, int maxExclusive) => _r.Next(min, maxExclusive);

    public static int Index(int count) => count <= 0 ? 0 : _r.Next(count);

    public static bool Chance(float p) => _r.NextDouble() < p;

    public static Vector2 Direction()
    {
        double a = _r.NextDouble() * Math.PI * 2.0;
        return new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
    }
}
