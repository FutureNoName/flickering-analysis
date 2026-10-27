namespace TrackingScript.Records;

public sealed record Frame(
    string Sensor,
    long Time,
    string Source,
    List<Observation> Objects);
