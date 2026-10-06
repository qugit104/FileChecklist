namespace FileChecklist.Core;

public enum RowStatus { Unchecked, Ready, Missing, Ambiguous, Changed, Delivered, Error, Invalid }
public enum MatchMode { ExactName, Stem, IdentifierPrefix }
public sealed class Candidate
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string? ContentHash { get; set; }
}
public sealed class ChecklistRow
{
    public int Number { get; set; }
    public List<string> Cells { get; set; } = [];
    public string Name { get; set; } = "";
    public RowStatus Status { get; set; }
    public string Detail { get; set; } = "";
    public List<Candidate> Candidates { get; set; } = [];
    public Candidate? Selected { get; set; }
    public string? DeliveredPath { get; set; }
    public string? DeliveredHash { get; set; }
}
public sealed class ChecklistTask
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "新交付任务";
    public List<string> Headers { get; set; } = [];
    public List<ChecklistRow> Rows { get; set; } = [];
    public List<string> Roots { get; set; } = [];
    public List<string> ScanErrors { get; set; } = [];
    public string OutputFolder { get; set; } = "";
    public bool RenameConflicts { get; set; }
    public MatchMode MatchMode { get; set; }
    public DateTime? LastScanUtc { get; set; }
}
public sealed record DeliveryItem(int RowNumber, string Source, string Destination, string? Problem, long Length, DateTime ModifiedUtc, string? ContentHash);
public sealed record DeliveryResult(int Copied, int Reused, int Failed, string ReportPath);
