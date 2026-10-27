using TrackingScript.Helper;

namespace TrackingScript.Records;

public sealed record Observation(
    string Id,
    string Category,
    Vector Position,
    Vector? Velocity,
    long Time,
    string Source);
