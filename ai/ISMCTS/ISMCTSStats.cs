using System;

public class ISMCTSStats
{
    public float Sum { get; private set; } = 0;
    public float SumOfSquares { get; private set; } = 0;
    public float Min { get; private set; } = float.MaxValue;
    public float Max { get; private set; } = float.MinValue;

    public void Update(float value)
    {
        Sum += value;
        SumOfSquares += value * value;
        Min = Math.Min(Min, value);
        Max = Math.Max(Max, value);
    }
}