using System.Text;
using System.Text.Json;
using FileChecklist.Core;

namespace FileChecklist.Cli;

public static class CommandLine
{
    private static readonly HashSet<string> Flags = ["--header", "--yes", "--json", "--rename-conflicts"];
    private static readonly Dictionary<string, string[]> Options = new()
    {
        ["plan"] = ["--source", "--output", "--task", "--mode", "--column", "--header", "--delimiter", "--json", "--rename-conflicts"],
        ["scan"] = ["--json"], ["choose"] = ["--row", "--candidate", "--json"], ["copy"] = ["--yes", "--json"]
    };

    public static int Run(string[] args, TextWriter output)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }
            if (args.Length < 2 || !Options.TryGetValue(args[0], out var allowed)) throw new UsageException("Expected plan, scan, choose or copy. Use --help.");
            string command = args[0], input = args[1];
            var values = new Dictionary<string, List<string>>();
            for (int i = 2; i < args.Length; i++)
            {
                string key = args[i];
                if (!allowed.Contains(key)) throw new UsageException("Unknown option: " + key);
                if (key != "--source" && values.ContainsKey(key)) throw new UsageException("Repeated option: " + key);
                string value = "true";
                if (!Flags.Contains(key))
                {
                    if (++i >= args.Length || args[i].StartsWith("--")) throw new UsageException("Missing value for " + key);
                    value = args[i];
                }
                if (!values.TryGetValue(key, out var list)) values[key] = list = [];
                list.Add(value);
            }
            string Get(string key, string? fallback = null) => values.TryGetValue(key, out var v) ? v[0] : fallback ?? throw new UsageException("Required option: " + key);
            int Number(string key, string? fallback = null) => int.TryParse(Get(key, fallback), out int n) && n > 0 ? n : throw new UsageException(key + " must be a positive integer.");
            bool json = values.ContainsKey("--json");
            ChecklistTask task; string statePath; DeliveryResult? delivered = null;
            if (command == "plan")
            {
                statePath = Path.GetFullPath(Get("--task"));
                if (File.Exists(statePath)) throw new IOException("Task already exists. Choose a new --task path, or use scan to resume it.");
                if (new FileInfo(input).Length > 16_000_000) throw new IOException("Checklist exceeds the 16 MB limit.");
                string delimiter = Get("--delimiter", Path.GetExtension(input).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? "comma" : "tab");
                char separator = delimiter switch { "comma" => ',', "tab" => '\t', _ => throw new UsageException("--delimiter must be comma or tab.") };
                task = ChecklistEngine.Import(File.ReadAllText(input, new UTF8Encoding(false, true)), separator, values.ContainsKey("--header"), Number("--column", "1") - 1);
                task.Title = Path.GetFileName(input);
                task.Roots = values.TryGetValue("--source", out var roots) ? roots.Select(Path.GetFullPath).Distinct().ToList() : throw new UsageException("Required option: --source");
                task.OutputFolder = Path.GetFullPath(Get("--output"));
                task.MatchMode = Get("--mode", "exact") switch { "exact" => MatchMode.ExactName, "stem" => MatchMode.Stem, "prefix" => MatchMode.IdentifierPrefix, _ => throw new UsageException("--mode must be exact, stem or prefix.") };
                task.RenameConflicts = values.ContainsKey("--rename-conflicts");
                ChecklistEngine.Scan(task);
                _ = ChecklistEngine.Plan(task);
                ChecklistEngine.Save(task, statePath, overwrite: false);
            }
            else
            {
                if (command == "copy" && !values.ContainsKey("--yes")) throw new UsageException("Copy requires --yes. Run scan first to review the current plan.");
                statePath = Path.GetFullPath(input);
                task = ChecklistEngine.Load(statePath);
                int rowNumber = 0; Candidate? requested = null;
                if (command == "choose")
                {
                    rowNumber = Number("--row"); int index = Number("--candidate") - 1;
                    var previousRow = task.Rows.SingleOrDefault(r => r.Number == rowNumber) ?? throw new UsageException("Row not found.");
                    requested = index < previousRow.Candidates.Count ? previousRow.Candidates[index] : throw new UsageException("Candidate not found. Run scan to list candidates.");
                }
                ChecklistEngine.Scan(task);
                if (requested != null)
                {
                    var row = task.Rows.Single(r => r.Number == rowNumber);
                    var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    var current = row.Candidates.SingleOrDefault(c => c.Path.Equals(requested.Path, comparer));
                    if (current == null || current.Length != requested.Length || current.ModifiedUtc != requested.ModifiedUtc)
                        throw new IOException("The requested candidate changed or disappeared. Run scan and review again.");
                    if (row.Status == RowStatus.Delivered) throw new IOException("This row is already delivered. Start a new task to deliver it again.");
                    ChecklistEngine.Choose(row, current);
                }
                if (command == "copy") delivered = ChecklistEngine.Deliver(task, ChecklistEngine.Plan(task));
                ChecklistEngine.Save(task, statePath);
            }
            var plan = ChecklistEngine.Plan(task);
            WriteResult(output, task, statePath, plan, delivered, json);
            bool unresolved = task.ScanErrors.Count > 0 || task.Rows.Any(r => r.Status is not (RowStatus.Ready or RowStatus.Delivered)) || plan.Any(p => p.Problem != null);
            return unresolved ? 2 : 0;
        }
        catch (UsageException ex) { output.WriteLine("Usage error: " + ex.Message); return 64; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException or JsonException)
        { output.WriteLine("Error: " + ex.Message); return 1; }
    }

    private static void WriteResult(TextWriter output, ChecklistTask task, string statePath, List<DeliveryItem> plan, DeliveryResult? delivered, bool json)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                task = statePath, mode = task.MatchMode.ToString(), output = task.OutputFolder, scanErrors = task.ScanErrors,
                delivery = delivered, plan,
                rows = task.Rows.Select(r => new { number = r.Number, key = r.Name, status = r.Status.ToString(), original = r.Cells, detail = r.Detail,
                    selected = r.Selected?.Path, delivered = r.DeliveredPath,
                    candidates = r.Candidates.Select((c, i) => new { number = i + 1, path = c.Path, bytes = c.Length, modifiedUtc = c.ModifiedUtc }) })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        output.WriteLine($"Task: {statePath}\nMode: {task.MatchMode}    Rows: {task.Rows.Count}\nOutput: {task.OutputFolder}");
        foreach (var row in task.Rows)
        {
            output.WriteLine($"{row.Number,4}  {row.Status,-10}  {row.Name}");
            if (row.Status is RowStatus.Ambiguous or RowStatus.Changed)
                for (int i = 0; i < row.Candidates.Count; i++) output.WriteLine($"      [{i + 1}] {row.Candidates[i].Path}");
            if (row.Status is RowStatus.Error or RowStatus.Changed or RowStatus.Invalid) output.WriteLine("      " + row.Detail);
        }
        foreach (var item in plan) output.WriteLine($"  #{item.RowNumber}: {item.Source} -> {item.Destination}" + (item.Problem == null ? "" : " [BLOCKED: " + item.Problem + "]"));
        foreach (string error in task.ScanErrors) output.WriteLine("Scan warning: " + error);
        if (delivered != null) output.WriteLine($"Copied: {delivered.Copied}; shared: {delivered.Reused}; failed: {delivered.Failed}\nReport: {delivered.ReportPath}");
        else output.WriteLine("No files copied. Review candidates before running copy --yes.");
    }

    private sealed class UsageException(string message) : Exception(message);
    private const string Help = """
        FileChecklist — collect files from a checklist, keep track of every row.

        plan <list.csv|list.tsv|list.txt> --source <folder> --output <folder> --task <new.fctask>
             [--mode exact|stem|prefix] [--header] [--column 1] [--delimiter comma|tab]
             [--rename-conflicts] [--json]
        scan <task.fctask> [--json]
        choose <task.fctask> --row <number> --candidate <number> [--json]
        copy <task.fctask> --yes [--json]

        Repeat --source for multiple roots. UTF-8 input; columns are numbered from 1.
        plan writes only a new task snapshot, never copies source files.
        scan/choose update the snapshot. Every copy rescans and never overwrites targets.
        Prefix candidates always need an explicit choose, even when there is only one.
        Candidate numbers refer to the last saved scan, not a newly reordered list.
        Exit codes: 0 = ready/completed; 2 = unresolved rows or conflicts; 1 = error; 64 = usage.
        """;
}
