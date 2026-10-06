using static FileChecklist.Core.Text;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FileChecklist.Core;
using Microsoft.Win32;

namespace FileChecklist.Desktop;

public sealed class ImportWindow : Window
{
    internal readonly TextBox Input = new() { AcceptsReturn = true, AcceptsTab = true, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), MinHeight = 200 };
    internal readonly CheckBox Header = new() { Content = T("第一行是表头"), IsChecked = true };
    internal readonly ComboBox Column = new() { MinWidth = 220 };
    internal readonly ComboBox Matching = new() { Width = 260, ItemsSource = new[] { T("完整文件名（含扩展名）"), T("不含扩展名，例如 A001 → A001.pdf"), T("编号前缀，例如 A001 → A001_报告.pdf") }, SelectedIndex = 0 };
    private readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 12) };
    private readonly ComboBox separator = new() { Width = 120, ItemsSource = new[] { T("Excel / 制表符"), T("CSV / 逗号") }, SelectedIndex = 0 };
    public ChecklistTask? Result { get; private set; }
    public ImportWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = T("导入清单"); Width = 810; Height = 580; MinWidth = 700; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = T("粘贴表格内容，或选择 CSV / TSV 文件。"), Margin = new Thickness(0, 0, 0, 6) });
        heading.Children.Add(new TextBlock { Text = T("选择文件名或编号列。其他列和行顺序会保留。"), Margin = new Thickness(0, 0, 0, 12), Foreground = Brushes.DimGray });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; heading.Children.Add(buttons);
        var paste = new Button { Content = T("从剪贴板粘贴") }; paste.Click += (_, _) => { try { Input.Text = Clipboard.GetText(); separator.SelectedIndex = 0; } catch (Exception ex) { info.Text = ex.Message; } }; buttons.Children.Add(paste);
        var file = new Button { Content = T("导入 CSV / TSV") }; file.Click += (_, _) => ReadFile(); buttons.Children.Add(file); buttons.Children.Add(separator);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) }; footer.Children.Add(options);
        Header.Margin = new Thickness(0, 0, 24, 0); options.Children.Add(Header);
        options.Children.Add(new TextBlock { Text = T("文件名所在列："), VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(Column);
        var matchOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        matchOptions.Children.Add(new TextBlock { Text = T("匹配规则："), VerticalAlignment = VerticalAlignment.Center }); matchOptions.Children.Add(Matching);
        matchOptions.Children.Add(new TextBlock { Text = T("编号前缀的候选需手动确认"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray }); footer.Children.Add(matchOptions);
        footer.Children.Add(info);
        var apply = new Button { Content = T("导入"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, Style = (Style)Application.Current.FindResource("Primary") };
        apply.Click += (_, _) => { try { Result = BuildTask(); DialogResult = true; } catch (Exception ex) { info.Text = ex.Message; } }; footer.Children.Add(apply);
        panel.Children.Add(Input);
        Input.TextChanged += (_, _) => UpdateColumns(); Header.Click += (_, _) => UpdateColumns(); separator.SelectionChanged += (_, _) => UpdateColumns();
        info.Text = T("可直接粘贴一列文件名；没有表头时，取消上方勾选。");
    }
    internal ChecklistTask BuildTask()
    {
        var task = ChecklistEngine.Import(Input.Text, separator.SelectedIndex == 1 ? ',' : '\t', Header.IsChecked == true, Column.SelectedIndex);
        task.MatchMode = (MatchMode)Matching.SelectedIndex;
        return task;
    }
    private void UpdateColumns()
    {
        try
        {
            var table = ChecklistEngine.ParseTable(Input.Text, separator.SelectedIndex == 1 ? ',' : '\t');
            int selected = Math.Max(0, Column.SelectedIndex);
            int count = table.Count == 0 ? 0 : table.Max(r => r.Count);
            Column.ItemsSource = Enumerable.Range(0, count).Select(i => F($"第 {i + 1} 列") + (Header.IsChecked == true && i < table[0].Count ? " · " + table[0][i] : "")).ToList();
            if (count > 0) Column.SelectedIndex = Math.Min(selected, count - 1);
            info.Text = F($"已识别 {Math.Max(0, table.Count - (Header.IsChecked == true ? 1 : 0))} 行数据、{count} 列。原始文本会保留，匹配时去除文件名首尾空格。");
        }
        catch (FormatException ex) { info.Text = ex.Message; }
    }
    private void ReadFile()
    {
        var dialog = new OpenFileDialog { Filter = T("清单文件|*.csv;*.tsv;*.txt|所有文件|*.*") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 16_000_000) throw new IOException(T("请导入小于 16 MB 的清单。"));
            var bytes = File.ReadAllBytes(dialog.FileName);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
            }
            separator.SelectedIndex = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Input.Text = text;
        }
        catch (Exception ex) { info.Text = T("导入失败：") + ex.Message; }
    }
}

public sealed class DeliveryWindow : Window
{
    public DeliveryWindow(ChecklistTask task, List<DeliveryItem> plan)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = T("确认复制"); Width = 1080; Height = 560; MinWidth = 760; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        int canCopy = plan.Count(p => p.Problem == null);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = F($"可复制 {canCopy} 项，冲突 {plan.Count - canCopy} 项。清单共 {task.Rows.Count} 项。"), Margin = new Thickness(0, 0, 0, 8) });
        top.Children.Add(new TextBlock { Text = T("未找到和未确认的项目不会复制，会记入报告。多行选中同一文件时，只复制一份。"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; DockPanel.SetDock(foot, Dock.Bottom); panel.Children.Add(foot);
        var cancel = new Button { Content = T("返回检查") }; cancel.Click += (_, _) => DialogResult = false; foot.Children.Add(cancel);
        var apply = new Button { Content = canCopy == 0 ? T("生成报告") : F($"复制 {canCopy} 项"), Style = (Style)Application.Current.FindResource("Primary") }; apply.Click += (_, _) => DialogResult = true; foot.Children.Add(apply);
        var grid = new DataGrid { ItemsSource = plan.Select(p => new { p.RowNumber, Name = Path.GetFileName(p.Destination), p.Source, p.Destination, p.Problem }) };
        grid.Columns.Add(new DataGridTextColumn { Header = T("行"), Binding = new Binding("RowNumber"), Width = 44 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("交付文件名"), Binding = new Binding("Name"), Width = 155, ElementStyle = (Style)Application.Current.FindResource("CellText") });
        grid.Columns.Add(new DataGridTextColumn { Header = T("源文件"), Binding = new Binding("Source"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 240, ElementStyle = (Style)Application.Current.FindResource("CellText") });
        grid.Columns.Add(new DataGridTextColumn { Header = T("实际交付路径"), Binding = new Binding("Destination"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 240, ElementStyle = (Style)Application.Current.FindResource("CellText") });
        grid.Columns.Add(new DataGridTextColumn { Header = T("阻止原因"), Binding = new Binding("Problem"), Width = 240, ElementStyle = (Style)Application.Current.FindResource("CellText") }); panel.Children.Add(grid);
    }
}
