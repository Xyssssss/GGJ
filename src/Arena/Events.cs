namespace GGJ;

public readonly struct WaveStarted
{
    public readonly int WaveIndex;
    public readonly int Count;
    public WaveStarted(int waveIndex, int count) { WaveIndex = waveIndex; Count = count; }
}

/// <summary>本轮所有波次都刷完了 —— 胜利双条件之一。</summary>
public readonly struct AllWavesSpawned
{
    public readonly int Round;
    public AllWavesSpawned(int round) { Round = round; }
}
