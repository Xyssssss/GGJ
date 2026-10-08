using Godot;

namespace GGJ;

/// <summary>资源加载小工具：路径不存在就返回 null，不让异常打断启动流程。</summary>
public static class Res
{
    public static PackedScene? Packed(string path)
    {
        try
        {
            return ResourceLoader.Exists(path) ? (ResourceLoader.Load(path) as PackedScene) : null;
        }
        catch
        {
            return null;
        }
    }

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            return ResourceLoader.Exists(path) ? (ResourceLoader.Load(path) as T) : null;
        }
        catch
        {
            return null;
        }
    }
}
