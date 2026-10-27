using TrackingScript.Records;

namespace TrackingScript.Matching;

public sealed class Track(string sensor, int session, string id)
{
    public string Sensor { get; } = sensor;
    public int Session { get; } = session;
    public string Id { get; } = id;
    public List<Observation> Observations { get; } = [];

    public Observation First => Observations[0];
    public Observation Last => Observations[^1];
    public double Duration => (Last.Time - First.Time) / 1e9;
}