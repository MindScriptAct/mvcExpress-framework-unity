using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MvcExpress.PerformanceDashboard;

internal static class Program
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ReportsRoot = Path.Combine(Root, "BenchmarkResults", "Performance");
    private static readonly string Runner = Path.Combine(Root, "Tools", "Performance", "Run-PerformanceBenchmarks.ps1");

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "mvcExpress Performance Dashboard";
        Directory.CreateDirectory(ReportsRoot);

        if (args.Contains("--generate-all", StringComparer.OrdinalIgnoreCase))
        {
            Report[] reports = LoadReports();
            foreach (Report report in reports) HtmlReport.WriteReport(report);
            Console.WriteLine($"Generated {reports.Length} HTML report(s).");
            return 0;
        }
        if (args.Length == 3 && args[0].Equals("--compare", StringComparison.OrdinalIgnoreCase))
        {
            Report before = Report.TryLoad(Path.GetFullPath(args[1])) ?? throw new InvalidOperationException("The baseline report directory is invalid.");
            Report after = Report.TryLoad(Path.GetFullPath(args[2])) ?? throw new InvalidOperationException("The candidate report directory is invalid.");
            Console.WriteLine(HtmlReport.WriteComparison(before, after));
            return 0;
        }

        while (true)
        {
            Header("PERFORMANCE DASHBOARD");
            int choice = Select(new[]
            {
                "Run performance benchmarks",
                "List saved reports",
                "Open a report",
                "Compare two reports",
                "Delete a report snapshot",
                "Generate HTML for all reports",
                "Exit"
            });

            try
            {
                switch (choice)
                {
                    case 0: RunBenchmarks(); break;
                    case 1: ListReports(); break;
                    case 2: OpenReport(); break;
                    case 3: CompareReports(); break;
                    case 4: DeleteReport(); break;
                    case 5: GenerateAllReports(); break;
                    default: return 0;
                }
            }
            catch (Exception exception)
            {
                WriteColored($"\nError: {exception.Message}", ConsoleColor.Red);
                Pause();
            }
        }
    }

    private static void RunBenchmarks()
    {
        Header("RUN BENCHMARKS");
        Console.WriteLine("Choose which Unity scripting backend Player(s) to benchmark.");
        int backendChoice = Select(new[] { "Mono only", "IL2CPP only", "Mono + IL2CPP", "Back" });
        if (backendChoice == 3) return;

        Console.Write("\nReport label (for example, before-pooling-refactor): ");
        string label = Console.ReadLine()?.Trim() ?? string.Empty;
        Console.WriteLine("Each run is a separate sample used to calculate median, mean, and p95 results.");
        int runs = ReadNumber("Independent runs per backend", 15, 1, 100);
        bool buildPlayers = Confirm("Build fresh benchmark Players before running", true);
        Console.WriteLine("Some release Players cannot expose reliable allocation counters. Timing results remain valid.");
        bool timingOnly = Confirm("Keep timing results when allocation counters are unavailable", true);

        string backends = backendChoice switch
        {
            0 => "'Mono'",
            1 => "'IL2CPP'",
            _ => "@('Mono','IL2CPP')"
        };
        string command = $"& {PowerShellQuote(Runner)} -Backends {backends} -Runs {runs} -Label {PowerShellQuote(label)}";
        if (!buildPlayers) command += " -SkipBuild";
        if (timingOnly) command += " -AllowTimingOnly";

        Console.WriteLine("\nRunning benchmark campaign. Unity build output follows...\n");
        int exitCode = RunPowerShell(command);
        if (exitCode != 0) throw new InvalidOperationException($"Benchmark campaign failed with exit code {exitCode}.");

        Report? latest = LoadReports().FirstOrDefault();
        if (latest is null) throw new InvalidOperationException("No report was produced.");
        string html = HtmlReport.WriteReport(latest);
        WriteColored($"\nReport created: {latest.DisplayName}", ConsoleColor.Green);
        Console.WriteLine(html);
        if (Confirm("Open the HTML report", true)) OpenFile(html);
    }

    private static void ListReports()
    {
        Header("SAVED REPORTS");
        Report[] reports = LoadReports();
        if (reports.Length == 0)
        {
            Console.WriteLine("No reports found.");
            Pause();
            return;
        }

        Console.WriteLine($"{"#",3}  {"Date",19}  {"Label",24}  {"Branch",24}  {"Commit",8}  Backends");
        Console.WriteLine(new string('─', Math.Min(Console.WindowWidth - 1, 105)));
        for (int i = 0; i < reports.Length; i++)
        {
            Report report = reports[i];
            Console.WriteLine($"{i + 1,3}  {report.Created.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {Trim(report.Label, 24),-24}  {Trim(report.Branch, 24),-24}  {Trim(report.Commit, 8),-8}  {report.Backends}");
        }
        Pause();
    }

    private static void OpenReport()
    {
        Report? report = ChooseReport("OPEN REPORT");
        if (report is null) return;
        string html = HtmlReport.WriteReport(report);
        OpenFile(html);
    }

    private static void CompareReports()
    {
        Report[] reports = LoadReports();
        if (reports.Length < 2) throw new InvalidOperationException("At least two reports are required for comparison.");

        Report? before = ChooseReport("SELECT BASELINE (BEFORE)", reports);
        if (before is null) return;
        Report? after = ChooseReport("SELECT CANDIDATE (AFTER)", reports.Where(report => report.Directory != before.Directory).ToArray());
        if (after is null) return;

        string html = HtmlReport.WriteComparison(before, after);
        WriteColored($"\nComparison created: {html}", ConsoleColor.Green);
        OpenFile(html);
    }

    private static void DeleteReport()
    {
        Report? report = ChooseReport("DELETE REPORT SNAPSHOT");
        if (report is null) return;

        Header("CONFIRM DELETE");
        WriteColored("This permanently deletes the report snapshot and every file in its directory:", ConsoleColor.Yellow);
        Console.WriteLine($"\n{report.DisplayName}");
        Console.WriteLine(report.Directory);
        if (!Confirm("Permanently delete this report", false)) return;

        Directory.Delete(report.Directory, true);
        WriteColored("\nReport snapshot deleted.", ConsoleColor.Green);
        Pause();
    }

    private static void GenerateAllReports()
    {
        Report[] reports = LoadReports();
        foreach (Report report in reports) HtmlReport.WriteReport(report);
        WriteColored($"Generated {reports.Length} HTML report(s).", ConsoleColor.Green);
        Pause();
    }

    private static Report? ChooseReport(string title, Report[]? reports = null)
    {
        reports ??= LoadReports();
        Header(title);
        if (reports.Length == 0)
        {
            Console.WriteLine("No reports found.");
            Pause();
            return null;
        }

        var choices = reports.Select(report => report.DisplayName).Append("Back").ToArray();
        int choice = Select(choices);
        return choice == reports.Length ? null : reports[choice];
    }

    private static Report[] LoadReports() => Directory.Exists(ReportsRoot)
        ? Directory.GetDirectories(ReportsRoot)
            .Select(Report.TryLoad)
            .Where(report => report is not null)
            .Cast<Report>()
            .OrderByDescending(report => report.Created)
            .ToArray()
        : Array.Empty<Report>();

    private static void Header(string title)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  mvcExpress  {title,-48}║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static int Select(IReadOnlyList<string> items)
    {
        int selected = 0;
        int top = Console.CursorTop;
        ConsoleKey key;
        do
        {
            Console.SetCursorPosition(0, top);
            for (int i = 0; i < items.Count; i++)
            {
                bool active = i == selected;
                Console.ForegroundColor = active ? ConsoleColor.Black : ConsoleColor.Gray;
                Console.BackgroundColor = active ? ConsoleColor.Cyan : ConsoleColor.Black;
                Console.Write($" {(active ? '▶' : ' ')} {items[i]} ");
                Console.ResetColor();
                Console.WriteLine(new string(' ', Math.Max(0, 65 - items[i].Length)));
            }
            key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.UpArrow) selected = (selected - 1 + items.Count) % items.Count;
            if (key == ConsoleKey.DownArrow) selected = (selected + 1) % items.Count;
        } while (key is not ConsoleKey.Enter and not ConsoleKey.Escape);
        return key == ConsoleKey.Escape ? items.Count - 1 : selected;
    }

    private static int ReadNumber(string prompt, int defaultValue, int minimum, int maximum)
    {
        while (true)
        {
            Console.Write($"{prompt} [{defaultValue}]: ");
            string? value = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            if (int.TryParse(value, out int number) && number >= minimum && number <= maximum) return number;
            WriteColored($"Enter a number from {minimum} to {maximum}.", ConsoleColor.Yellow);
        }
    }

    private static bool Confirm(string prompt, bool defaultValue)
    {
        Console.Write($"{prompt} [{(defaultValue ? "Y/n" : "y/N")}]: ");
        string value = Console.ReadLine()?.Trim().ToLowerInvariant() ?? string.Empty;
        return value.Length == 0 ? defaultValue : value is "y" or "yes";
    }

    private static int RunPowerShell(string command)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = Root,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private static string PowerShellQuote(string value) => $"'{value.Replace("'", "''")}'";
    private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
    private static void Pause() { Console.WriteLine("\nPress any key to continue..."); Console.ReadKey(true); }
    private static void WriteColored(string value, ConsoleColor color) { Console.ForegroundColor = color; Console.WriteLine(value); Console.ResetColor(); }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tools", "Performance", "Run-PerformanceBenchmarks.ps1"))) return directory.FullName;
            directory = directory.Parent;
        }
        directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tools", "Performance", "Run-PerformanceBenchmarks.ps1"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Run this application from inside the mvcExpress repository.");
    }
}

internal sealed record Report(string Directory, string SummaryPath, string Label, DateTime Created, string Branch, string Commit, bool Dirty, string Backends)
{
    public string DisplayName => $"{Created.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {(string.IsNullOrWhiteSpace(Label) ? "(unlabelled)" : Label)}  [{Backends}]  {ShortCommit}";
    public string ShortCommit => Commit.Length > 8 ? Commit[..8] : Commit;

    public static Report? TryLoad(string directory)
    {
        string? summary = System.IO.Directory.GetFiles(directory, "performance-summary-*.csv").FirstOrDefault();
        if (summary is null) return null;
        string metadataPath = Path.Combine(directory, "performance-metadata.json");
        if (!File.Exists(metadataPath))
        {
            DateTime fallback = System.IO.Directory.GetCreationTimeUtc(directory);
            return new Report(directory, summary, string.Empty, fallback, string.Empty, string.Empty, false, ReadBackends(summary));
        }

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(metadataPath));
        JsonElement root = json.RootElement;
        string label = GetString(root, "label");
        string branch = GetString(root, "gitBranch");
        string commit = GetString(root, "gitCommit");
        bool dirty = root.TryGetProperty("gitDirty", out JsonElement dirtyValue) && dirtyValue.GetBoolean();
        DateTime created = DateTime.TryParse(GetString(root, "createdUtc"), null, DateTimeStyles.RoundtripKind, out DateTime parsed) ? parsed : System.IO.Directory.GetCreationTimeUtc(directory);
        string backends = root.TryGetProperty("backends", out JsonElement values)
            ? string.Join(", ", values.EnumerateArray().Select(value => value.GetString()))
            : ReadBackends(summary);
        return new Report(directory, summary, label, created, branch, commit, dirty, backends);
    }

    private static string GetString(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
    private static string ReadBackends(string summary) => string.Join(", ", Csv.Read(summary).Select(row => row.GetValueOrDefault("Backend", string.Empty)).Distinct());
}

internal static class Csv
{
    public static List<Dictionary<string, string>> Read(string path)
    {
        string[][] records = Parse(File.ReadAllText(path)).ToArray();
        if (records.Length == 0) return new();
        string[] headers = records[0];
        return records.Skip(1).Select(values => headers.Select((header, index) => new { header, value = index < values.Length ? values[index] : string.Empty })
            .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static IEnumerable<string[]> Parse(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Any(value => value.Length > 0)) yield return row.ToArray();
                row.Clear();
            }
            else field.Append(character);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return row.ToArray(); }
    }
}

internal static class HtmlReport
{
    public static string WriteReport(Report report)
    {
        List<Dictionary<string, string>> rows = Csv.Read(report.SummaryPath);
        string path = Path.Combine(report.Directory, "performance-report.html");
        double maximum = rows.Select(row => Number(row, "MedianNanoseconds")).DefaultIfEmpty(1).Max();
        var body = new StringBuilder();
        body.Append(Metadata(report));
        foreach (IGrouping<string, Dictionary<string, string>> group in rows.GroupBy(row => row.GetValueOrDefault("Category", "Other")))
        {
            body.Append($"<section><h2>{H(group.Key)}</h2><div class='table-wrap'><table><thead><tr><th>Backend</th><th>Operation</th><th>Variant</th><th>Median ns</th><th>p95 ns</th><th>Mean ns</th><th>Bytes/op</th><th>Allocation source</th><th>Scope</th><th>Relative</th></tr></thead><tbody>");
            foreach (Dictionary<string, string> row in group.OrderBy(row => row.GetValueOrDefault("Backend")).ThenBy(row => row.GetValueOrDefault("Operation")))
            {
                double median = Number(row, "MedianNanoseconds");
                double width = maximum == 0 ? 0 : Math.Max(1, median / maximum * 100);
                body.Append($"<tr><td><span class='badge'>{H(row.GetValueOrDefault("Backend"))}</span></td><td>{H(row.GetValueOrDefault("Operation"))}</td><td>{H(row.GetValueOrDefault("Variant"))}</td><td class='number'>{median:N2}</td><td class='number'>{Number(row, "P95Nanoseconds"):N2}</td><td class='number'>{Number(row, "MeanNanoseconds"):N2}</td><td class='number'>{H(EmptyAsDash(row.GetValueOrDefault("MeanBytesPerOperation")))}</td><td>{H(EmptyAsDash(row.GetValueOrDefault("AllocationSource")))}</td><td>{H(EmptyAsDash(row.GetValueOrDefault("AllocationMeasurementScope")))}</td><td><div class='bar' style='width:{width:F2}%'></div></td></tr>");
            }
            body.Append("</tbody></table></div></section>");
        }
        File.WriteAllText(path, Page($"Performance report — {report.DisplayName}", body.ToString()));
        return path;
    }

    public static string WriteComparison(Report before, Report after)
    {
        var beforeRows = Csv.Read(before.SummaryPath).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var comparisons = new List<Comparison>();
        foreach (Dictionary<string, string> afterRow in Csv.Read(after.SummaryPath))
        {
            if (!beforeRows.TryGetValue(Key(afterRow), out Dictionary<string, string>? beforeRow)) continue;
            double oldValue = Number(beforeRow, "MedianNanoseconds");
            double newValue = Number(afterRow, "MedianNanoseconds");
            double delta = oldValue == 0 ? 0 : (newValue - oldValue) / oldValue * 100;
            comparisons.Add(new Comparison(
                afterRow.GetValueOrDefault("Backend", string.Empty),
                afterRow.GetValueOrDefault("Category", string.Empty),
                afterRow.GetValueOrDefault("Operation", string.Empty),
                afterRow.GetValueOrDefault("Variant", string.Empty),
                oldValue,
                newValue,
                delta));
        }

        string path = Path.Combine(after.Directory, $"comparison-{Safe(before.Label)}-to-{Safe(after.Label)}.html");
        int improved = comparisons.Count(row => row.Delta < -5);
        int regressed = comparisons.Count(row => row.Delta > 5);
        var body = new StringBuilder();
        body.Append($"<div class='cards'><div class='card'><small>BASELINE</small><strong>{H(before.DisplayName)}</strong></div><div class='card'><small>CANDIDATE</small><strong>{H(after.DisplayName)}</strong></div><div class='card good'><small>IMPROVED &gt;5%</small><strong>{improved}</strong></div><div class='card bad'><small>REGRESSED &gt;5%</small><strong>{regressed}</strong></div></div>");
        foreach (IGrouping<string, Comparison> group in comparisons.GroupBy(row => row.Category))
        {
            body.Append($"<section><h2>{H(group.Key)}</h2><div class='table-wrap'><table><thead><tr><th>Backend</th><th>Operation</th><th>Variant</th><th>Before ns</th><th>After ns</th><th>Change</th></tr></thead><tbody>");
            foreach (Comparison row in group.OrderByDescending(row => row.Delta))
            {
                string css = row.Delta > 5 ? "bad" : row.Delta < -5 ? "good" : "neutral";
                string arrow = row.Delta > 0 ? "▲" : row.Delta < 0 ? "▼" : "•";
                body.Append($"<tr><td><span class='badge'>{H(row.Backend)}</span></td><td>{H(row.Operation)}</td><td>{H(row.Variant)}</td><td class='number'>{row.Before:N2}</td><td class='number'>{row.After:N2}</td><td class='{css} number'><strong>{arrow} {row.Delta:+0.00;-0.00;0.00}%</strong></td></tr>");
            }
            body.Append("</tbody></table></div></section>");
        }
        File.WriteAllText(path, Page("mvcExpress performance comparison", body.ToString()));
        return path;
    }

    private static string Metadata(Report report) => $"<div class='cards'><div class='card'><small>REPORT</small><strong>{H(string.IsNullOrWhiteSpace(report.Label) ? "Unlabelled" : report.Label)}</strong></div><div class='card'><small>DATE</small><strong>{report.Created.ToLocalTime():yyyy-MM-dd HH:mm:ss}</strong></div><div class='card'><small>REVISION</small><strong>{H(report.Branch)} @ {H(report.ShortCommit)}{(report.Dirty ? " (dirty)" : "")}</strong></div><div class='card'><small>BACKENDS</small><strong>{H(report.Backends)}</strong></div></div>";
    private static string Key(Dictionary<string, string> row) => $"{row.GetValueOrDefault("Backend")}|{row.GetValueOrDefault("Category")}|{row.GetValueOrDefault("Variant")}|{row.GetValueOrDefault("Operation")}|{row.GetValueOrDefault("TableSize")}";
    private static double Number(Dictionary<string, string> row, string key) => double.TryParse(row.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
    private static string EmptyAsDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Safe(string value) => string.Concat((string.IsNullOrWhiteSpace(value) ? "report" : value).Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

    private static string Page(string title, string body) => $$"""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{{H(title)}}</title>
<style>:root{color-scheme:dark;--bg:#0b1220;--panel:#121d31;--line:#263653;--text:#e7edf7;--muted:#91a2bd;--cyan:#35c8e6;--good:#40d17d;--bad:#ff667a}*{box-sizing:border-box}body{margin:0;background:linear-gradient(135deg,#08101d,#111b2e);color:var(--text);font:14px Inter,Segoe UI,sans-serif}main{max-width:1500px;margin:auto;padding:32px}h1{font-size:30px;margin:0 0 24px}h2{margin-top:32px;color:#c7d8ef}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:14px}.card{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:18px}.card small{display:block;color:var(--muted);margin-bottom:8px}.card strong{font-size:16px}.table-wrap{overflow:auto;border:1px solid var(--line);border-radius:12px}table{width:100%;border-collapse:collapse;background:rgba(18,29,49,.88)}th,td{padding:11px 13px;border-bottom:1px solid var(--line);text-align:left;white-space:nowrap}th{color:var(--muted);font-size:12px;text-transform:uppercase}tr:hover{background:#182641}.number{text-align:right;font-variant-numeric:tabular-nums}.badge{background:#19384a;color:#6fe4fa;border-radius:20px;padding:4px 9px}.bar{height:8px;min-width:2px;border-radius:5px;background:linear-gradient(90deg,var(--cyan),#7367f0)}.good{color:var(--good)}.bad{color:var(--bad)}.neutral{color:var(--muted)}footer{color:var(--muted);margin-top:30px}</style></head><body><main><h1>{{H(title)}}</h1>{{body}}<footer>Generated by mvcExpress Performance Dashboard at {{DateTime.Now:yyyy-MM-dd HH:mm:ss}}</footer></main></body></html>
""";

    private sealed record Comparison(string Backend, string Category, string Operation, string Variant, double Before, double After, double Delta);
}
