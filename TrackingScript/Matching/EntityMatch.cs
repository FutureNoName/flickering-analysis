namespace TrackingScript.Matching;

public sealed class EntityMatch
{
    public required int OldIndex { get; init; }
    public required int NewIndex { get; init; }
    public required double Gap { get; init; }
    public required double VelocityDifference { get; init; }
    public required double Score { get; init; }
    public required int Support { get; init; }
    public required double Residual { get; init; }
    public required string Kind { get; init; }
    public required Prediction Prediction { get; init; }

    public string Level { get; set; } = "ambiguous";
}
