using TrackingScript.Matching;

namespace TrackingScript.Records;

public sealed record TrackingData(List<Track> Tracks, int Collisions, int Gaps);
