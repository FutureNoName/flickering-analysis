namespace TrackingScript.Records
{
    public sealed record Collision(
        string Sensor,
        long Time,
        string Id,
        string Source,
        int Count);
}
