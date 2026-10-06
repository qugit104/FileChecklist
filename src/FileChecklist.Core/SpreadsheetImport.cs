using System.Globalization;
using System.IO.Compression;
using System.Text;
using ExcelDataReader;
using ExcelNumberFormat;
using static FileChecklist.Core.Text;

namespace FileChecklist.Core;

public sealed record ImportSheet(string Name, List<List<string>> Rows);
public sealed record ImportGuess(bool HasHeader, int NameColumn);

public static class SpreadsheetImport
{
    public static List<ImportSheet> Read(string path)
    {
        if (new FileInfo(path).Length > 16_000_000) throw new InvalidDataException(T("请使用小于 16 MB 的 Excel 文件。"));
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zip.Entries.Sum(e => e.Length) > 64_000_000 || zip.Entries.Count > 10_000)
                    throw new InvalidDataException(T("Excel 内容过大，请只保留需要的清单后重试。"));
            }
            stream.Position = 0;
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            var sheets = new List<ImportSheet>();
            long characters = 0;
            do
            {
                if (sheets.Count >= 100 || reader.FieldCount > 512 || reader.RowCount > 100_001)
                    throw new InvalidDataException(T("表格过大，请限制在 100 个工作表、512 列和 100000 行以内。"));
                var rows = new List<List<string>>();
                while (reader.Read())
                {
                    var cells = new List<string>();
                    for (int col = 0; col < reader.FieldCount; col++)
                    {
                        var value = reader.GetValue(col);
                        string cell = value switch { null or DBNull => "", string s => s, bool b => b ? "TRUE" : "FALSE", _ => new NumberFormat(reader.GetNumberFormatString(col) ?? "General").Format(value, CultureInfo.InvariantCulture) };
                        characters += cell.Length;
                        if (characters > 16_000_000) throw new InvalidDataException(T("Excel 内容过大，请只保留需要的清单后重试。"));
                        cells.Add(cell);
                    }
                    if (cells.Any(c => !string.IsNullOrWhiteSpace(c))) rows.Add(cells);
                }
                sheets.Add(new ImportSheet(reader.Name, rows));
            } while (reader.NextResult());
            if (sheets.Count == 0) throw new InvalidDataException(T("Excel 中没有工作表。"));
            return sheets;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException and not OutOfMemoryException)
        { throw new InvalidDataException(T("无法读取 Excel。请用 Excel 另存为未加密的 .xlsx 文件后重试。"), ex); }
    }

    // Only recognize explicit column labels. A first filename must never be guessed away as a header.
    public static ImportGuess Suggest(List<List<string>> rows)
    {
        if (rows.Count == 0) return new(false, 0);
        string[] labels = ["文件名", "文件名称", "附件名称", "附件名", "filename", "file", "documentname", "文件编号", "编号", "documentid", "id"];
        foreach (var label in labels)
        {
            int column = rows[0].FindIndex(x => x.Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant() == label);
            if (column >= 0) return new(true, column);
        }
        return new(false, 0);
    }

    public static string ToText(IEnumerable<IEnumerable<string>> rows) => string.Join("\n", rows.Select(row => string.Join("\t", row.Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\""))));
}
