using System.Globalization;
using GlobExpressions;
using CommandLine;
using MathNet.Numerics;
using System.Text.Json;
using TrackingScript.Helper;
using TrackingScript.CommandLine;
using TrackingScript.Records;
using TrackingScript.Matching;

namespace TrackingScript;

public static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var parser = new Parser(settings =>
        {
            settings.HelpWriter = Console.Out;
            settings.ParsingCulture = CultureInfo.InvariantCulture;
        });
        return parser.ParseArguments<Options>(args).MapResult(
            Run,
            errors => errors.All(error => error is HelpRequestedError or VersionRequestedError) ? 0 : 1);
    }

    private static int Run(Options options)
    {
        try
        {
            Validate(options);
            var paths = CollectPaths(options.Inputs);
            if (paths.Count == 0)
                throw new ArgumentException("No inputs matched.");

            var input = Load(paths, options.StreamId);
            var tracking = BuildTracks(input.Frames, options.RecordingGap);
            var matches = FindMatches(tracking.Tracks, options);
            Save(paths, input, tracking, matches, options);
            Console.WriteLine($"Wrote {Path.Combine(options.OutputDir, "report.txt")}");
            return 0;
        }
        catch (Exception err)
        {
            Console.Error.WriteLine($"Error: {err.Message}");
            return 1;
        }
    }

    private static void Validate(Options options)
    {
        var positive = new[] { options.TimeWindow, options.MaxDistance,
        options.RecordingGap, options.FitWindow, options.Margin };
        if (positive.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentException("Matching thresholds must be finite and positive.");
        if (!double.IsFinite(options.TurnMargin) || options.TurnMargin < 0)
            throw new ArgumentException("turn-margin must be finite and >= 0.");
        if (!double.IsFinite(options.TurnAngle) || options.TurnAngle < 2 || options.TurnAngle > 45)
            throw new ArgumentException("turn-angle must be between 2 and 45 degrees.");
    }

    private static List<string> CollectPaths(IEnumerable<string> inputs)
    {
        IEnumerable<string> Expand(string input)
        {
            var full = Path.GetFullPath(input);
            if (File.Exists(full))
                return [full];
            if (Directory.Exists(full))
                return Directory.EnumerateFiles(full, "*.json");

            var wildcard = full.IndexOfAny(['*', '?']);
            if (wildcard < 0)
                return [];

            var separator = full.LastIndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], wildcard);
            var root = full[..(separator + 1)];
            if (!Directory.Exists(root))
                return [];

            var pattern = full[(separator + 1)..].Replace(Path.DirectorySeparatorChar, '/');
            return Glob.Files(root, pattern)
                .Select(file => Path.Combine(root, file))
                .Concat(Glob.Directories(root, pattern)
                    .SelectMany(directory => Directory.EnumerateFiles(
                        Path.Combine(root, directory), "*.json")));
        }

        return [.. inputs.SelectMany(Expand).Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)];
    }

    private static InputData Load(List<string> paths, string streamId)
    {
        var frames = new Dictionary<(string Sensor, long Time), Frame>();

        foreach (var path in paths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                AddFrame(
                    frames,
                    $"raw:{streamId}",
                    ParsingHelper.FileTime(path),
                    root,
                    raw: true,
                    source: Path.GetFileName(path),
                    path: path);
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("frames", out var batch)
                     && batch.ValueKind == JsonValueKind.Array)
            {
                var sensor =
                    $"aggregated:{ParsingHelper.Text(root.GetProperty("intersection_id"))}";

                var index = 0;

                foreach (var frame in batch.EnumerateArray())
                {
                    var timestamp = frame.GetProperty("frame_timestamp");

                    var time =
                        ParsingHelper.ReadLong(timestamp.GetProperty("seconds"))
                        * TimeHelper.NanosecondsPerSecond
                        + ParsingHelper.ReadLong(timestamp.GetProperty("nanosec"));

                    AddFrame(
                        frames,
                        sensor,
                        time,
                        frame.GetProperty("objects"),
                        raw: false,
                        source: $"{Path.GetFileName(path)}:frames[{index}]",
                        path: path);

                    index++;
                }
            }
            else
            {
                throw new ArgumentException(
                    $"Unsupported input structure: {path}");
            }
        }

        return new InputData(
            [.. frames.Values
            .OrderBy(frame => frame.Sensor, StringComparer.Ordinal)
            .ThenBy(frame => frame.Time)]);
    }

    private static void AddFrame(
        Dictionary<(string Sensor, long Time), Frame> frames,
        string sensor,
        long time,
        JsonElement objects,
        bool raw,
        string source,
        string path)
    {
        var observations = new List<Observation>();

        foreach (var obj in objects.EnumerateArray())
        {
            var id = ParsingHelper.Text(
                obj.GetProperty(raw ? "obj_id" : "track_id"));

            var category = ParsingHelper.Text(
                obj.GetProperty(raw ? "obj_type" : "class_name"));

            var position = raw
                ? obj.GetProperty("psr").GetProperty("position")
                : obj.GetProperty("bounding_box").GetProperty("position");

            var xy = ParsingHelper.ReadVector(position);
            Vector? velocity = null;

            if (!raw
                && obj.TryGetProperty("velocity", out var velocityJson)
                && velocityJson.ValueKind == JsonValueKind.Object
                && velocityJson.TryGetProperty("x", out _)
                && velocityJson.TryGetProperty("y", out _))
            {
                velocity = ParsingHelper.ReadVector(velocityJson);
            }

            if (!xy.IsFinite || velocity is { IsFinite: false })
            {
                throw new ArgumentException(
                    $"Invalid position or velocity: {path}");
            }

            observations.Add(
                new Observation(id, category, xy, velocity, time, source));
        }

        var key = (sensor, time);

        if (frames.TryGetValue(key, out var existing))
        {
            if (!SameObjects(existing.Objects, observations))
            {
                throw new ArgumentException(
                    $"Conflicting same-time frames: {source}");
            }

            return;
        }

        frames.Add(key, new Frame(sensor, time, source, observations));
    }

    private static bool SameObjects(List<Observation> first, List<Observation> second)
    {
        static Dictionary<ObjectValue, int> Counts(IEnumerable<Observation> observations) =>
            observations.GroupBy(obj => new ObjectValue(obj.Id, obj.Category, obj.Position, obj.Velocity))
                .ToDictionary(group => group.Key, group => group.Count());

        var a = Counts(first);
        var b = Counts(second);
        return a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
    }

    private static TrackingData BuildTracks(List<Frame> frames, double gap)
    {
        var tracks = new List<Track>();
        var collisions = 0;
        var gaps = 0;
        var active = new Dictionary<string, Track>();
        Frame? previous = null;
        var session = 0;
        var gapNs = TimeHelper.ToNanoseconds(gap);

        foreach (var frame in frames)
        {
            var newSession =
                previous is null
                || frame.Sensor != previous.Sensor
                || frame.Time - previous.Time > gapNs;

            if (newSession)
            {
                if (previous is not null && frame.Sensor == previous.Sensor)
                {
                    gaps++;
                }

                active.Clear();
                session++;
            }

            var current = new Dictionary<string, Track>();

            foreach (var group in frame.Objects.GroupBy(obj => obj.Id))
            {
                var objects = group.ToList();

                if (objects.Count > 1)
                {
                    collisions++;

                    continue;
                }

                if (!active.TryGetValue(group.Key, out var track))
                {
                    track = new Track(frame.Sensor, session, group.Key);
                    tracks.Add(track);
                }

                track.Observations.Add(objects[0]);
                current.Add(group.Key, track);
            }

            active = current;
            previous = frame;
        }

        return new TrackingData(tracks, collisions, gaps);
    }

    private static Motion FitMotion(Track track, bool tail, double window)
    {
        var anchor = tail ? track.Last : track.First;
        var windowNs = TimeHelper.ToNanoseconds(window);

        var observations = track.Observations
            .Where(obj => Math.Abs(obj.Time - anchor.Time) <= windowNs)
            .ToList();

        if (observations.Count < 2)
        {
            return new Motion(
                anchor.Velocity ?? new Vector(0, 0),
                observations.Count,
                0);
        }

        var times = observations
            .Select(obj => (obj.Time - anchor.Time) / 1e9)
            .ToArray();

        var (A, B) = Fit.Line(times, [.. observations.Select(obj => obj.Position.X)]);
        var y = Fit.Line(times, [.. observations.Select(obj => obj.Position.Y)]);
        var velocity = new Vector(B, y.B);
        var intercept = new Vector(A, y.A);
        var squaredError = observations.Select((obj, i) =>
        {
            var error = obj.Position - intercept - velocity * times[i];
            return error.X * error.X + error.Y * error.Y;
        }).Average();

        return new Motion(velocity, observations.Count, Math.Sqrt(squaredError));
    }

    private static Prediction Predict(
        Observation old,
        Observation next,
        Motion oldMotion,
        Motion newMotion,
        double dt,
        Options options)
    {
        var oldVelocity = oldMotion.Velocity;
        var newVelocity = newMotion.Velocity;

        var forward = Vector.Distance(
            old.Position + oldVelocity * dt,
            next.Position);

        var backward = Vector.Distance(
            next.Position - newVelocity * dt,
            old.Position);

        var result = new Prediction(forward, backward);
        var oldSpeed = oldVelocity.Length;
        var newSpeed = newVelocity.Length;

        if (Math.Min(oldSpeed, newSpeed) < 0.5
            || Math.Min(oldMotion.Samples, newMotion.Samples) < 3
            || options.TurnMargin == 0)
        {
            return result;
        }

        var dot = oldVelocity.X * newVelocity.X
            + oldVelocity.Y * newVelocity.Y;

        var cosine = Math.Clamp(dot / (oldSpeed * newSpeed), -1, 1);
        var angle = Math.Acos(cosine) * 180 / Math.PI;

        var speedChange =
            Math.Abs(oldSpeed - newSpeed) / Math.Max(oldSpeed, newSpeed);

        if (angle < 2 || angle > options.TurnAngle || speedChange > 0.35)
            return result;

        var averageVelocity = (oldVelocity + newVelocity) * 0.5;

        var correction = Math.Max(
            Vector.Distance(oldVelocity, averageVelocity),
            Vector.Distance(newVelocity, averageVelocity)) * dt;

        if (correction > options.TurnMargin)
            return result;

        var turnForward = Vector.Distance(
            old.Position + averageVelocity * dt,
            next.Position);

        var turnBackward = Vector.Distance(
            next.Position - averageVelocity * dt,
            old.Position);

        if (Math.Max(turnForward, turnBackward) < Math.Max(forward, backward))
        {
            result.Forward = turnForward;
            result.Backward = turnBackward;
            result.TurnAssisted = true;
        }

        return result;
    }

    private static List<EntityMatch> FindMatches(
        List<Track> tracks,
        Options options)
    {
        var starts = tracks
            .Select(track => FitMotion(track, false, options.FitWindow))
            .ToArray();

        var ends = tracks
            .Select(track => FitMotion(track, true, options.FitWindow))
            .ToArray();

        var buckets = Enumerable.Range(0, tracks.Count)
            .GroupBy(index => (tracks[index].Sensor, tracks[index].Session));

        var matches = new List<EntityMatch>();
        var windowNs = TimeHelper.ToNanoseconds(options.TimeWindow);

        foreach (var bucket in buckets)
        {
            var indices = bucket
                .OrderBy(index => tracks[index].First.Time)
                .ToArray();

            var startTimes = indices
                .Select(index => tracks[index].First.Time)
                .ToArray();

            foreach (var oldIndex in indices)
            {
                var oldTrack = tracks[oldIndex];
                var old = oldTrack.Last;
                var oldMotion = ends[oldIndex];

                var first = TimeHelper.UpperBound(startTimes, old.Time);
                var last = TimeHelper.UpperBound(startTimes, old.Time + windowNs);

                for (var i = first; i < last; i++)
                {
                    var newIndex = indices[i];
                    var newTrack = tracks[newIndex];
                    var next = newTrack.First;
                    var newMotion = starts[newIndex];
                    var dt = (next.Time - old.Time) / 1e9;

                    var prediction = Predict(
                        old, next, oldMotion, newMotion, dt, options);

                    if (Math.Max(prediction.Forward, prediction.Backward)
                        > options.MaxDistance)
                    {
                        continue;
                    }

                    var velocityDifference = Vector.Distance(
                        oldMotion.Velocity,
                        newMotion.Velocity);

                    var score =
                        (prediction.Forward + prediction.Backward)
                        / (2 * options.MaxDistance)
                        + 0.15 * dt / options.TimeWindow
                        + 0.10 * Math.Min(velocityDifference / 5, 2);

                    var kind = oldTrack.Id == newTrack.Id
                        ? "same_id_gap"
                        : old.Category == next.Category
                            ? "dropout"
                            : "switch";

                    if (kind == "dropout"
                        && (!long.TryParse(oldTrack.Id, out var oldId)
                        || !long.TryParse(newTrack.Id, out var newId)
                        || newId <= oldId))
                    {
                        continue;
                    }

                    matches.Add(new EntityMatch
                    {
                        OldIndex = oldIndex,
                        NewIndex = newIndex,
                        Gap = dt,
                        VelocityDifference = velocityDifference,
                        Score = score,
                        Support = Math.Min(oldMotion.Samples, newMotion.Samples),
                        Residual = Math.Max(
                            oldMotion.Residual, newMotion.Residual),
                        Kind = kind,
                        Prediction = prediction
                    });
                }
            }
        }

        var outgoing = matches
            .GroupBy(match => match.OldIndex)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(match => match.Score).ToList());

        var incoming = matches
            .GroupBy(match => match.NewIndex)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(match => match.Score).ToList());

        foreach (var match in matches)
        {
            var from = outgoing[match.OldIndex];
            var to = incoming[match.NewIndex];

            var outgoingMargin = from.Count > 1
                ? from[1].Score - match.Score
                : double.PositiveInfinity;

            var incomingMargin = to.Count > 1
                ? to[1].Score - match.Score
                : double.PositiveInfinity;

            var margin = Math.Min(outgoingMargin, incomingMargin);

            if (!ReferenceEquals(from[0], match)
                || !ReferenceEquals(to[0], match)
                || margin < options.Margin)
            {
                continue;
            }

            match.Level = "plausible";

            var oldTrack = tracks[match.OldIndex];
            var newTrack = tracks[match.NewIndex];

            if (match.Support >= 3
                && Math.Min(oldTrack.Duration, newTrack.Duration) >= 0.5
                && match.Gap <= 1
                && match.VelocityDifference <= 2
                && match.Residual <= 0.3
                && Math.Max(
                    match.Prediction.StraightForward,
                    match.Prediction.StraightBackward) <= 1)
            {
                match.Level = "stronger";
            }
        }

        return matches;
    }

    private static List<Trajectory> BuildTrajectories(
        List<Track> tracks,
        List<EntityMatch> matches,
        params string[] levels)
    {
        var acceptedLevels = levels.ToHashSet();

        var successors = matches
            .Where(match => acceptedLevels.Contains(match.Level))
            .ToDictionary(match => match.OldIndex, match => match.NewIndex);

        var incoming = successors.Values.ToHashSet();
        var trajectories = new List<Trajectory>();

        for (var index = 0; index < tracks.Count; index++)
        {
            if (incoming.Contains(index))
                continue;

            var segments = new List<int> { index };

            while (successors.TryGetValue(segments[^1], out var next))
                segments.Add(next);

            var counts = segments
                .SelectMany(segment => tracks[segment].Observations)
                .GroupBy(obj => obj.Category)
                .ToDictionary(group => group.Key, group => group.Count());

            var highestCount = counts.Values.Max();

            var winners = counts
                .Where(pair => pair.Value == highestCount)
                .Select(pair => pair.Key)
                .OrderBy(category => category, StringComparer.Ordinal)
                .ToList();

            var lastCategory = tracks[segments[^1]].Last.Category;
            var category = winners.Contains(lastCategory)
                ? lastCategory
                : winners[0];

            trajectories.Add(new Trajectory(category, segments));
        }

        if (trajectories.Sum(trajectory => trajectory.Segments.Count)
            != tracks.Count)
        {
            throw new InvalidOperationException("Invalid trajectory chain.");
        }

        return trajectories;
    }

    private static void Save(
        List<string> paths, InputData input, TrackingData tracking,
        List<EntityMatch> matches, Options options)
    {
        var reportPath = Path.GetFullPath(Path.Combine(options.OutputDir, "report.txt"));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (paths.Contains(reportPath, comparer))
            throw new ArgumentException("The report would overwrite an input.");

        var groups = new Dictionary<string, List<Trajectory>>
        {
            ["unmerged"] = BuildTrajectories(tracking.Tracks, matches),
            ["stronger_only"] = BuildTrajectories(tracking.Tracks, matches, "stronger"),
            ["inclusive"] = BuildTrajectories(tracking.Tracks, matches, "stronger", "plausible")
        };
        var counts = groups.ToDictionary(pair => pair.Key, pair => pair.Value
            .GroupBy(trajectory => trajectory.Category)
            .ToDictionary(group => group.Key, group => group.Count()));
        var categories = counts.Values.SelectMany(group => group.Keys).Distinct()
            .OrderBy(category => category, StringComparer.Ordinal).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        var report = ReportBuilder.BuildReport(paths, input, tracking, matches, groups, counts, categories);
        File.WriteAllText(reportPath, report);
    }
}