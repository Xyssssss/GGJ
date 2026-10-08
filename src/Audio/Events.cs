namespace GGJ;

/// <summary>播放音效请求。Key 是逻辑名（shoot / laser / hit / ui），AudioService 负责映射到音频文件。</summary>
public struct SfxRequest
{
    public string Key;
}
