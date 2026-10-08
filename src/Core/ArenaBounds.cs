using Godot;

namespace GGJ;

/// <summary>
/// 单屏竞技场边界。所有"屏幕内"的判断都必须走这里，
/// 不许在业务代码里写死 1280x720。Main._Ready 里 Init 一次。
/// </summary>
public static class ArenaBounds
{
    public static Rect2 Rect { get; private set; } = new(0, 0, 1280, 720);

    public static void Init(Rect2 r) => Rect = r;

    public static Vector2 Center => Rect.Position + Rect.Size * 0.5f;

    public static Vector2 ClampInside(Vector2 p, float margin = 0f)
    {
        var r = Rect.Grow(-margin);
        return new Vector2(
            Mathf.Clamp(p.X, r.Position.X, r.End.X),
            Mathf.Clamp(p.Y, r.Position.Y, r.End.Y));
    }

    public static bool Inside(Vector2 p) => Rect.HasPoint(p);

    /// <summary>
    /// 光学反射：轴对齐边界下退化为"分量取反"，速度大小不变（纯弹性）。
    /// 方向判断（v.X &lt; 0f）必须有，否则敌人会贴墙抖动。
    /// 返回本次是否发生反射。
    /// </summary>
    public static bool Reflect(ref Vector2 v, Vector2 p)
    {
        bool hit = false;
        if (p.X <= Rect.Position.X && v.X < 0f) { v.X = -v.X; hit = true; }
        if (p.X >= Rect.End.X && v.X > 0f) { v.X = -v.X; hit = true; }
        if (p.Y <= Rect.Position.Y && v.Y < 0f) { v.Y = -v.Y; hit = true; }
        if (p.Y >= Rect.End.Y && v.Y > 0f) { v.Y = -v.Y; hit = true; }
        return hit;
    }

    public enum Edge { Right, Left, Top, Bottom }

    public static Vector2 RandomPointOnEdge(Edge edge, float inset = 20f)
    {
        var r = Rect;
        return edge switch
        {
            Edge.Right  => new Vector2(r.End.X - inset, Rng.Range(r.Position.Y + inset, r.End.Y - inset)),
            Edge.Left   => new Vector2(r.Position.X + inset, Rng.Range(r.Position.Y + inset, r.End.Y - inset)),
            Edge.Top    => new Vector2(Rng.Range(r.Position.X + inset, r.End.X - inset), r.Position.Y + inset),
            Edge.Bottom => new Vector2(Rng.Range(r.Position.X + inset, r.End.X - inset), r.End.Y - inset),
            _           => Center
        };
    }

    /// <summary>朝场内的默认方向。右边缘出生 → 向正左（180°），本作默认。</summary>
    public static Vector2 DefaultDirFrom(Edge edge)
    {
        return edge switch
        {
            Edge.Right  => Vector2.Left,
            Edge.Left   => Vector2.Right,
            Edge.Top    => Vector2.Down,
            Edge.Bottom => Vector2.Up,
            _           => Vector2.Left
        };
    }
}
