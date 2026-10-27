using CommandLine;

namespace TrackingScript.CommandLine;

public sealed class Options
{
    [Value(0, MetaName = "inputs", Min = 1, Required = true,
        HelpText = "Input JSON files, folders, or glob patterns.")]
    public IEnumerable<string> Inputs { get; set; } = Array.Empty<string>();

    [Option('o', "output-dir", Default = "tracking_results", HelpText = "Report folder.")]
    public string OutputDir { get; set; } = "tracking_results";

    [Option("stream-id", Default = "default", HelpText = "Raw stream name.")]
    public string StreamId { get; set; } = "default";

    [Option("time-window", Default = 2.0, HelpText = "Maximum matching gap in seconds.")]
    public double TimeWindow { get; set; }

    [Option("max-dist", Default = 3.0, HelpText = "Maximum prediction error.")]
    public double MaxDistance { get; set; }

    [Option("recording-gap", Default = 1.0, HelpText = "Session boundary gap in seconds.")]
    public double RecordingGap { get; set; }

    [Option("fit-window", Default = 0.5, HelpText = "Motion fitting window in seconds.")]
    public double FitWindow { get; set; }

    [Option("margin", Default = 0.2, HelpText = "Minimum matching score margin.")]
    public double Margin { get; set; }

    [Option("turn-margin", Default = 0.5, HelpText = "Maximum turn correction; zero disables it.")]
    public double TurnMargin { get; set; }

    [Option("turn-angle", Default = 25.0, HelpText = "Maximum heading change in degrees.")]
    public double TurnAngle { get; set; }
}
