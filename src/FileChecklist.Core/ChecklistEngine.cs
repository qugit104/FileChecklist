using static FileChecklist.Core.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileChecklist.Core;

public static class ChecklistEngine
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<List<string>> ParseTable(string text, char separator)
    {
        if (text.Length > 16_000_000) throw new FormatException(T("清单过大，请分批处理（上限 16 MB 文本）。"));
        text = text.TrimStart('\uFEFF');
        var result = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false;
        void Field() { row.Add(field.ToString()); field.Clear(); closed = false; }
        void Row() { Field(); if (row.Any(x => !string.IsNullOrWhiteSpace(x))) result.Add(row); row = []; }
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(ch);
            }
            else if (ch == separator) Field();
            else if (ch is '\r' or '\n')
            {
                Row(); if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (ch == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed) { if (ch != ' ') throw new FormatException(T("引号结束后出现了多余字符，请检查 CSV 格式。")); }
            else field.Append(ch);
        }
        if (quoted) throw new FormatException(T("清单有未闭合的引号，请检查最后几行。"));
        if (field.Length > 0 || row.Count > 0 || closed) Row();
        return result;
    }

    public static ChecklistTask Import(string text, char separator, bool hasHeader, int nameColumn)
    {
        var table = ParseTable(text, separator);
        if (table.Count == 0) throw new FormatException(T("请先粘贴文件清单。"));
        int width = table.Max(x => x.Count);
        if (nameColumn < 0 || nameColumn >= width) throw new FormatException(T("文件名列超出了清单范围。"));
        var task = new ChecklistTask();
        task.Headers = hasHeader ? table[0].ToList() : Enumerable.Range(1, width).Select(x => F($"原始列 {x}")).ToList();
        while (task.Headers.Count < width) task.Headers.Add(F($"原始列 {task.Headers.Count + 1}"));
        foreach (var cells in table.Skip(hasHeader ? 1 : 0))
        {
            while (cells.Count < width) cells.Add("");
            string name = cells[nameColumn].Trim();
            var row = new ChecklistRow { Number = task.Rows.Count + 1, Cells = cells, Name = name };
            if (!ValidName(name)) { row.Status = RowStatus.Invalid; row.Detail = T("文件名或编号不能留空，也不能包含路径。"); }
            task.Rows.Add(row);
        }
        if (task.Rows.Count == 0) throw new FormatException(T("清单只有表头，没有文件行。"));
        if (task.Rows.Count > 100_000) throw new FormatException(T("第一版最多处理 100,000 行，请拆分清单。"));
        return task;
    }

    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c));
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Within(string path, string root) => Paths.Equals(Full(path), Full(root))
        || Full(path).StartsWith(Path.EndsInDirectorySeparator(Full(root)) ? Full(root) : Full(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IEnumerable<string> MatchKeys(string fileName, MatchMode mode)
    {
        if (mode == MatchMode.ExactName) { yield return fileName; yield break; }
        string stem = Path.GetFileNameWithoutExtension(fileName);
        yield return stem;
        if (mode != MatchMode.IdentifierPrefix) yield break;
        for (int i = 1; i < stem.Length; i++)
            if (char.IsWhiteSpace(stem[i]) || "_-.([（【".Contains(stem[i])) yield return stem[..i];
    }

    // Do not follow junctions / symbolic links into directories the user did not select.
    private static void NoLinks(string path)
    {
        string? current = Full(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(F($"第一版不处理符号链接、目录联接或云端占位文件：{current}"));
            current = Path.GetDirectoryName(current);
        }
    }
    private static Candidate Describe(string path)
    {
        var info = new FileInfo(path);
        return new Candidate { Path = info.FullName, Length = info.Length, ModifiedUtc = info.LastWriteTimeUtc };
    }
    private static bool SameStamp(Candidate a, Candidate b) => a.Length == b.Length && a.ModifiedUtc == b.ModifiedUtc;
    private static string Hash(string path, CancellationToken token)
    {
        NoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024]; int read;
        while ((read = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hasher.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    public static void Scan(ChecklistTask task, CancellationToken token = default)
    {
        task.LastScanUtc = null;
        task.ScanErrors.Clear();
        if (!Enum.IsDefined(task.MatchMode)) throw new InvalidDataException(T("不支持此匹配规则。"));
        if (task.Roots.Count == 0) throw new InvalidOperationException(T("请添加至少一个来源文件夹。"));
        var wanted = task.Rows.Where(r => ValidName(r.Name)).Select(r => r.Name).ToHashSet(Names);
        var index = new Dictionary<string, List<Candidate>>(Names);
        var visited = new HashSet<string>(Paths);
        foreach (string root in task.Roots)
        {
            var stack = new Stack<string>(); stack.Push(Full(root));
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                if (!visited.Add(dir)) continue;
                try
                {
                    NoLinks(dir);
                    foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var attrs = File.GetAttributes(entry);
                            if ((attrs & FileAttributes.ReparsePoint) != 0) { task.ScanErrors.Add(T("跳过链接或占位文件：") + entry); continue; }
                            if ((attrs & FileAttributes.Directory) != 0) stack.Push(entry);
                            else
                            {
                                Candidate? candidate = null;
                                foreach (string key in MatchKeys(Path.GetFileName(entry), task.MatchMode).Distinct(Names))
                                {
                                    if (!wanted.Contains(key)) continue;
                                    if (!index.TryGetValue(key, out var list)) index[key] = list = [];
                                    list.Add(candidate ??= Describe(entry));
                                }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { task.ScanErrors.Add(entry + "：" + ex.Message); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { task.ScanErrors.Add(dir + "：" + ex.Message); }
            }
        }
        var hashes = new Dictionary<string, string>(Paths);
        string CachedHash(string path)
        {
            if (!hashes.TryGetValue(path, out string? value)) hashes[path] = value = Hash(path, token);
            return value;
        }
        foreach (var row in task.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (!ValidName(row.Name)) { row.Status = RowStatus.Invalid; row.Detail = T("文件名或编号不能留空，也不能包含路径。"); continue; }
            row.Candidates = index.GetValueOrDefault(row.Name, []).OrderBy(c => c.Path, Paths).ToList();
            var old = row.Selected;
            var current = old == null ? null : row.Candidates.FirstOrDefault(c => Paths.Equals(c.Path, old.Path));
            if (old != null && (current == null || !SameStamp(old, current)))
            {
                row.Status = RowStatus.Changed; row.Detail = T("原先选中的文件已变化或不可用，请重新选择并确认。"); continue;
            }
            if (row.Status == RowStatus.Changed) { row.Detail = T("变化尚未确认，请从候选文件中重新选择。"); continue; }
            bool needsConfirmation = task.MatchMode == MatchMode.IdentifierPrefix && old == null;
            var picked = current ?? (row.Candidates.Count == 1 && !needsConfirmation ? row.Candidates[0] : null);
            if (picked != null)
            {
                try
                {
                    picked.ContentHash = CachedHash(picked.Path);
                    if (old?.ContentHash != null && old.ContentHash != picked.ContentHash)
                    { row.Status = RowStatus.Changed; row.Detail = T("选中文件的内容发生变化，请重新确认。"); continue; }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { row.Status = RowStatus.Error; row.Selected = null; row.Detail = T("无法读取并校验源文件：") + ex.Message; task.ScanErrors.Add(row.Detail); continue; }
            }
            if (old != null && row.DeliveredPath != null && row.DeliveredHash != null)
            {
                try
                {
                    if (CachedHash(row.DeliveredPath) == row.DeliveredHash && picked?.ContentHash == row.DeliveredHash)
                    { row.Status = RowStatus.Delivered; row.Detail = T("已交付，源文件及交付副本内容校验一致。"); continue; }
                    row.Status = RowStatus.Changed; row.Detail = T("源文件或交付副本的内容已变化，请复核。"); continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { row.Status = RowStatus.Changed; row.Detail = T("无法验证上次交付：") + ex.Message; continue; }
            }
            if (current != null) { row.Selected = current; row.Status = RowStatus.Ready; row.Detail = T("保留上次选择，等待交付。"); }
            else if (row.Candidates.Count == 1 && !needsConfirmation) { row.Selected = row.Candidates[0]; row.Status = RowStatus.Ready; row.Detail = task.MatchMode == MatchMode.Stem ? T("不含扩展名匹配，等待交付。") : T("文件名精确匹配（忽略大小写），等待交付。"); }
            else if (row.Candidates.Count > 0) { row.Selected = null; row.Status = RowStatus.Ambiguous; row.Detail = F($"找到 {row.Candidates.Count} 个候选文件，请选择并确认。"); }
            else { row.Selected = null; row.Status = RowStatus.Missing; row.Detail = task.ScanErrors.Count > 0 ? T("未找到；扫描不完整，请查看扫描问题。") : T("在所选来源文件夹中未找到。"); }
        }
        if (task.ScanErrors.Count > 0)
            foreach (var row in task.Rows.Where(r => r.Status == RowStatus.Missing)) row.Detail = T("未找到；扫描不完整，请查看扫描问题。");
        task.LastScanUtc = DateTime.UtcNow;
    }

    public static void Choose(ChecklistRow row, Candidate candidate, CancellationToken token = default)
    {
        if (!row.Candidates.Any(c => Paths.Equals(c.Path, candidate.Path))) throw new InvalidOperationException(T("请选择当前候选列表中的文件。"));
        NoLinks(candidate.Path);
        var selected = Describe(candidate.Path); selected.ContentHash = Hash(candidate.Path, token);
        if (!SameStamp(selected, Describe(candidate.Path))) throw new IOException(T("读取期间文件发生变化，请重新扫描。"));
        row.Selected = selected; row.DeliveredHash = null; row.DeliveredPath = null;
        row.Status = RowStatus.Ready; row.Detail = T("已人工确认，等待交付。");
    }

    private static void ValidateOutput(ChecklistTask task)
    {
        if (string.IsNullOrWhiteSpace(task.OutputFolder)) throw new InvalidOperationException(T("请选择交付目录。"));
        if (task.Roots.Count == 0 || task.LastScanUtc == null) throw new InvalidOperationException(T("请先完成一次扫描。"));
        if (task.Roots.Any(r => Within(task.OutputFolder, r) || Within(r, task.OutputFolder)))
            throw new InvalidOperationException(T("来源与交付目录不能互相包含，请选择独立的交付文件夹。"));
        NoLinks(task.OutputFolder);
    }

    public static List<DeliveryItem> Plan(ChecklistTask task)
    {
        ValidateOutput(task);
        var result = new List<DeliveryItem>();
        var allocated = new Dictionary<string, string>(Paths);
        var sourceTargets = new Dictionary<string, string>(Paths);
        foreach (var row in task.Rows.Where(r => r.Status is RowStatus.Ready or RowStatus.Error && r.Selected != null))
        {
            var src = row.Selected!;
            string target = Path.Combine(Full(task.OutputFolder), Path.GetFileName(src.Path));
            string? problem = null;
            if (!task.Roots.Any(r => Within(src.Path, r))) problem = T("选中文件不属于来源目录，请重新扫描。");
            else if (sourceTargets.TryGetValue(src.Path, out var known)) target = known;
            else
            {
                string original = target; int suffix = 2;
                while (File.Exists(target) || Directory.Exists(target) || allocated.ContainsKey(target))
                {
                    if (!task.RenameConflicts) { problem = T("目标同名，已阻止覆盖；可启用自动加序号后重新预览。"); break; }
                    target = Path.Combine(task.OutputFolder, Path.GetFileNameWithoutExtension(original) + $" ({suffix++})" + Path.GetExtension(original));
                }
                if (problem == null) { allocated[target] = src.Path; sourceTargets[src.Path] = target; }
            }
            if (src.ContentHash == null) problem = T("缺少源文件内容校验记录，请重新扫描。");
            result.Add(new(row.Number, src.Path, target, problem, src.Length, src.ModifiedUtc, src.ContentHash));
        }
        return result;
    }

    public static DeliveryResult Deliver(ChecklistTask task, List<DeliveryItem> plan, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); ValidateOutput(task);
        Directory.CreateDirectory(task.OutputFolder);
        int copied = 0, reused = 0, failed = 0;
        var completed = new Dictionary<string, (string Path, string Hash)>(Paths);
        try
        {
            foreach (var item in plan)
            {
                token.ThrowIfCancellationRequested();
                var row = task.Rows.Single(r => r.Number == item.RowNumber);
                string? temp = null;
                try
                {
                    if (item.Problem != null) throw new IOException(item.Problem);
                    if (row.Selected == null || !Paths.Equals(row.Selected.Path, item.Source)) throw new IOException(T("预览后选择已变化，请重新预览。"));
                    if (!task.Roots.Any(r => Within(item.Source, r)) || !Paths.Equals(Path.GetDirectoryName(item.Destination), Full(task.OutputFolder)))
                        throw new IOException(T("复制路径超出任务范围。"));
                    NoLinks(item.Source); NoLinks(item.Destination);
                    var before = Describe(item.Source);
                    if (before.Length != item.Length || before.ModifiedUtc != item.ModifiedUtc) throw new IOException(T("预览后源文件发生变化，请重新扫描并确认。"));
                    if (completed.TryGetValue(item.Source, out var previous))
                    {
                        if (previous.Hash != item.ContentHash || Hash(item.Source, token) != previous.Hash || Hash(previous.Path, token) != previous.Hash) throw new IOException(T("复用副本前检测到文件变化。"));
                        row.DeliveredPath = previous.Path; row.DeliveredHash = previous.Hash; reused++;
                    }
                    else
                    {
                        temp = Path.Combine(task.OutputFolder, ".filechecklist-" + Guid.NewGuid().ToString("N") + ".tmp");
                        string hash;
                        using (var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                        {
                            byte[] buffer = new byte[128 * 1024]; int read;
                            while ((read = input.Read(buffer)) > 0)
                            { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); hasher.AppendData(buffer, 0, read); }
                            output.Flush(true); hash = Convert.ToHexString(hasher.GetHashAndReset());
                            if (!SameStamp(before, Describe(item.Source))) throw new IOException(T("复制时源文件发生变化，请重试。"));
                        }
                        if (hash != item.ContentHash) throw new IOException(T("预览后源文件内容发生变化，请重新扫描并确认。"));
                        if (Hash(temp, token) != hash) throw new IOException(T("副本内容校验失败。"));
                        token.ThrowIfCancellationRequested();
                        NoLinks(item.Destination);
                        File.Move(temp, item.Destination, false); temp = null;
                        row.DeliveredPath = item.Destination; row.DeliveredHash = hash;
                        completed[item.Source] = (item.Destination, hash); copied++;
                    }
                    row.Status = RowStatus.Delivered; row.Detail = T("已交付，副本通过 SHA-256 内容校验。");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { failed++; row.Status = RowStatus.Error; row.Detail = ex.Message; }
                finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch (OperationCanceledException) { ExportReport(task); throw; }
        return new(copied, reused, failed, ExportReport(task));
    }

    public static string StatusText(RowStatus status) => status switch
    {
        RowStatus.Ready => T("待交付"), RowStatus.Missing => T("未找到"), RowStatus.Ambiguous => T("待选择"), RowStatus.Changed => T("待复核"),
        RowStatus.Delivered => T("已交付"), RowStatus.Error => T("复制失败"), RowStatus.Invalid => T("清单有误"), _ => T("待扫描")
    };

    public static string ExportReport(ChecklistTask task)
    {
        Directory.CreateDirectory(task.OutputFolder);
        string path = Path.Combine(task.OutputFolder, F($"清单交付报告_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.csv"));
        using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew), new UTF8Encoding(true));
        WriteCsv(writer, new[] { T("清单行") }.Concat(task.Headers).Concat([T("文件名"), T("状态"), T("说明"), T("选中源文件"), T("交付路径"), "SHA256", T("扫描问题数量")]));
        foreach (var r in task.Rows)
            WriteCsv(writer, new[] { r.Number.ToString() }.Concat(r.Cells).Concat([r.Name, StatusText(r.Status), r.Detail, r.Selected?.Path ?? "", r.DeliveredPath ?? "", r.DeliveredHash ?? "", task.ScanErrors.Count.ToString()]));
        if (task.ScanErrors.Count > 0)
        {
            writer.WriteLine(); WriteCsv(writer, [T("扫描问题（以下条目未完成检查）")]);
            foreach (string error in task.ScanErrors) WriteCsv(writer, [error]);
        }
        return path;
    }
    private static void WriteCsv(TextWriter writer, IEnumerable<string> cells) => writer.WriteLine(string.Join(",", cells.Select(x =>
    {
        string trimmed = x.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) x = "'" + x;
        return "\"" + x.Replace("\"", "\"\"") + "\"";
    })));

    public static void Save(ChecklistTask task, string path, bool overwrite = true)
    {
        string absolute = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        string temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(task, JsonOptions), new UTF8Encoding(false)); File.Move(temp, absolute, overwrite); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static ChecklistTask Load(string path)
    {
        if (new FileInfo(path).Length > 64_000_000) throw new InvalidDataException(T("任务文件过大。"));
        var task = JsonSerializer.Deserialize<ChecklistTask>(File.ReadAllText(path)) ?? throw new InvalidDataException(T("任务为空。"));
        if (task.Version != 1) throw new InvalidDataException(T("不支持此任务格式版本。"));
        if (!Enum.IsDefined(task.MatchMode)) throw new InvalidDataException(T("不支持此匹配规则。"));
        if (task.Rows == null || task.Roots == null || task.Headers == null || task.ScanErrors == null || task.Rows.Count > 100_000
            || task.Rows.Select(r => r.Number).Distinct().Count() != task.Rows.Count
            || task.Rows.Any(r => r.Number < 1 || r.Cells == null || r.Candidates == null || r.Name == null)) throw new InvalidDataException(T("任务结构损坏。"));
        task.LastScanUtc = null; // A saved snapshot is not evidence of the current filesystem state.
        return task;
    }
}
