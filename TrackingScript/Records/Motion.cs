using TrackingScript.Helper;

namespace TrackingScript.Records;

public sealed record Motion(
    Vector Velocity,
    int Samples,
    double Residual);
