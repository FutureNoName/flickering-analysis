using TrackingScript.Helper;

namespace TrackingScript.Records
{
    public readonly record struct ObjectValue(
        string Id,
        string Category,
        Vector Position,
        Vector? Velocity);
}
