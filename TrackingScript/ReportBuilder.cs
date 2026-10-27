using System.Text;
using TrackingScript.Helper;
using TrackingScript.Matching;
using TrackingScript.Records;

namespace TrackingScript
{
    public class ReportBuilder
    {
        public static string BuildReport(
            List<string> paths,
            InputData input,
            TrackingData tracking,
            List<EntityMatch> matches,
            Dictionary<string, List<Trajectory>> groups,
            Dictionary<string, Dictionary<string, int>> counts,
            List<string> categories)
        {
            var report = new StringBuilder();

            report.AppendLine("TRACKING RESULTS");
            report.AppendLine("================");
            report.AppendLine();
            report.AppendLine("TIME SPAN (UTC)");
            report.AppendLine("---------------");

            if (input.Frames.Count == 0)
                report.AppendLine("No frames.");

            foreach (var stream in input.Frames
                         .GroupBy(frame => frame.Sensor)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var first = stream.Min(frame => frame.Time);
                var last = stream.Max(frame => frame.Time);

                report.AppendLine($"Stream:   {stream.Key}");
                report.AppendLine($"Start:    {TimeHelper.Utc(first, true)}");
                report.AppendLine($"End:      {TimeHelper.Utc(last, true)}");
                report.AppendLine($"Elapsed:  {TimeHelper.Duration(last - first)}");
                report.AppendLine($"Frames:   {stream.Count()}");
                report.AppendLine();
            }

            if (tracking.Gaps > 0)
            {
                report.AppendLine(
                    $"Recording gaps: {tracking.Gaps} " +
                    "(included in elapsed time).");
            }

            report.AppendLine();
            report.AppendLine("ESTIMATED COUNTS");
            report.AppendLine("----------------");
            report.AppendLine(
                "Category         Unmerged    Stronger only    Inclusive");
            report.AppendLine(
                "------------------------------------------------------");

            foreach (var category in categories)
            {
                var unmerged = counts["unmerged"].GetValueOrDefault(category);
                var stronger = counts["stronger_only"].GetValueOrDefault(category);
                var inclusive = counts["inclusive"].GetValueOrDefault(category);

                report.AppendLine(
                    $"{category,-16} {unmerged,8} {stronger,16} {inclusive,12}");
            }

            report.AppendLine(
                "------------------------------------------------------");
            report.AppendLine(
                $"{"TOTAL",-16} {groups["unmerged"].Count,8} " +
                $"{groups["stronger_only"].Count,16} " +
                $"{groups["inclusive"].Count,12}");

            report.AppendLine();
            report.AppendLine("Inclusive merges stronger and plausible links.");
            report.AppendLine("Counts estimate trajectories, not verified objects.");
            report.AppendLine();
            report.AppendLine("EVENT TOTALS");
            report.AppendLine("------------");
            report.AppendLine("Event             Stronger    Plausible    Ambiguous");

            foreach (var kind in new[] { "dropout", "switch", "same_id_gap" })
            {
                var stronger = matches.Count(match =>
                    match.Kind == kind && match.Level == "stronger");
                var plausible = matches.Count(match =>
                    match.Kind == kind && match.Level == "plausible");
                var ambiguous = matches.Count(match =>
                    match.Kind == kind && match.Level == "ambiguous");

                report.AppendLine(
                    $"{EventLabel(kind),-16} " +
                    $"{stronger,9} {plausible,12} {ambiguous,12}");
            }

            var turnLinks = matches.Count(match =>
                match.Prediction.TurnAssisted && match.Level != "ambiguous");

            report.AppendLine($"Turn-assisted accepted links: {turnLinks}");

            if (tracking.Collisions > 0)
            {
                report.AppendLine(
                    $"Excluded same-frame ID collisions: {tracking.Collisions}");
            }

            report.AppendLine();
            report.AppendLine("EXAMPLES");
            report.AppendLine("--------");

            if (matches.Count == 0)
                report.AppendLine("No candidate events found.");

            var eventNumber = 0;

            foreach (var match in matches.OrderBy(
                         match => tracking.Tracks[match.NewIndex].First.Time))
            {
                var oldTrack = tracking.Tracks[match.OldIndex];
                var newTrack = tracking.Tracks[match.NewIndex];
                var old = oldTrack.Last;
                var next = newTrack.First;

                var title =
                    $"{++eventNumber}. {EventLabel(match.Kind)} [{match.Level}]";

                if (match.Prediction.TurnAssisted)
                    title += " (turn-assisted)";

                report.AppendLine(title);
                report.AppendLine($"   ID:       {oldTrack.Id} -> {newTrack.Id}");
                report.AppendLine(
                    $"   Category: {old.Category} -> {next.Category}");
                report.AppendLine(
                    $"   Time:     {TimeHelper.Utc(old.Time, true)} -> " +
                    $"{TimeHelper.Utc(next.Time, true)} UTC");
                report.AppendLine($"   Gap:      {match.Gap:F3} s");
                report.AppendLine($"   Sources:  {old.Source} -> {next.Source}");
                report.AppendLine();
            }

            report.AppendLine();
            report.AppendLine("INPUTS");
            report.AppendLine("------");

            foreach (var path in paths)
                report.AppendLine(path);

            return report.ToString();
        }

        private static string EventLabel(string kind) => kind switch
        {
            "dropout" => "Dropout",
            "switch" => "Switch",
            "same_id_gap" => "Same-ID gap",
            _ => kind
        };
    }
}
