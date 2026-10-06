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
    internal readonly TextBox Input = new() { AcceptsReturn = true, AcceptsTab = true, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), Height = 120 };
    internal readonly CheckBox Header = new() { Content = T("第一行是表头"), IsChecked = false };
    internal readonly ComboBox Column = new() { MinWidth = 220, MaxWidth = 420 };
    internal readonly ComboBox Matching = new() { Width = 260, ItemsSource = new[] { T("完整文件名（含扩展名）"), T("不含扩展名，例如 A001 → A001.pdf"), T("编号前缀，例如 A001 → A001_报告.pdf") }, SelectedIndex = 0 };
    internal readonly ComboBox Sheets = new() { MinWidth = 180, Visibility = Visibility.Collapsed, Margin = new Thickness(0,0,12,0) };
    private readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0,8,0,10) };
    private readonly ComboBox separator = new() { Width = 120, ItemsSource = new[] { T("Excel / 制表符"), T("CSV / 逗号") }, SelectedIndex = 0 };
    private readonly DataGrid preview = new() { AutoGenerateColumns = false, MinHeight = 120 };
    private readonly Expander editor = new() { Header = T("粘贴或编辑清单"), Margin = new Thickness(0,8,0,8) };
    private readonly TextBlock source = new() { Text = T("支持 .xlsx、CSV、TSV"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,12) };
    private List<ImportSheet> sheets = [];
    private bool updating;
    private bool loading;
    public ChecklistTask? Result { get; private set; }
    public ImportWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = T("导入清单"); Width = 850; Height = 640; MinWidth = 760; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1) await LoadFile(files[0]); };
        var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = T("导入清单"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(source);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) }; heading.Children.Add(buttons);
        var file = new Button { Content = T("打开 Excel / CSV…"), Style = (Style)Application.Current.FindResource("Primary") }; file.Click += async (_, _) => { var dialog = new OpenFileDialog { Filter = T("清单文件|*.xlsx;*.csv;*.tsv;*.txt|所有文件|*.*") }; if (dialog.ShowDialog(this) == true) await LoadFile(dialog.FileName); }; buttons.Children.Add(file);
        var paste = new Button { Content = T("从剪贴板粘贴") }; paste.Click += (_, _) => { try { sheets=[];Sheets.Visibility=Visibility.Collapsed;separator.SelectedIndex=0;Input.Text=Clipboard.GetText();source.Text=T("已粘贴的清单");editor.IsExpanded=true; } catch (Exception ex) { info.Text=ex.Message; } }; buttons.Children.Add(paste);
        buttons.Children.Add(new TextBlock { Text = T("拖入 .xlsx 文件"), Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,0,0) });
        var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,10) };heading.Children.Add(columns);columns.Children.Add(Sheets);
        columns.Children.Add(new TextBlock { Text = T("文件名列"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) });columns.Children.Add(Column);
        var footer = new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        editor.Content=Input;footer.Children.Add(editor);
        var advanced = new Expander { Header=T("导入设置"), Margin=new Thickness(0,0,0,8) };footer.Children.Add(advanced);
        var options=new StackPanel { Margin=new Thickness(0,6,0,0) };advanced.Content=options;
        var first=new StackPanel { Orientation=Orientation.Horizontal };Header.Margin=new Thickness(0,0,20,8);first.Children.Add(Header);first.Children.Add(separator);options.Children.Add(first);
        var modes=new StackPanel { Orientation=Orientation.Horizontal };modes.Children.Add(new TextBlock { Text=T("匹配规则："), VerticalAlignment=VerticalAlignment.Center });modes.Children.Add(Matching);options.Children.Add(modes);
        footer.Children.Add(info);
        var apply=new Button { Content=T("导入"), HorizontalAlignment=HorizontalAlignment.Right, Style=(Style)Application.Current.FindResource("Primary"), MinWidth=130 };
        apply.Click+=(_,_)=>{try{Result=BuildTask();DialogResult=true;}catch(Exception ex){info.Text=ex.Message;}};footer.Children.Add(apply);
        panel.Children.Add(preview);
        Input.TextChanged+=(_,_)=>UpdateColumns(true);Header.Click+=(_,_)=>UpdateColumns(false);separator.SelectionChanged+=(_,_)=>UpdateColumns(false);
        Sheets.SelectionChanged+=(_,_)=>{if(!updating&&Sheets.SelectedIndex>=0&&Sheets.SelectedIndex<sheets.Count){separator.SelectedIndex=0;Input.Text=SpreadsheetImport.ToText(sheets[Sheets.SelectedIndex].Rows);}};
        info.Text="";
    }
    internal ChecklistTask BuildTask()
    {
        var task=ChecklistEngine.Import(Input.Text,separator.SelectedIndex==1?',':'\t',Header.IsChecked==true,Column.SelectedIndex);
        task.MatchMode=(MatchMode)Matching.SelectedIndex;
        return task;
    }
    private void UpdateColumns(bool suggest)
    {
        if(updating)return;
        try
        {
            updating=true;
            var table=ChecklistEngine.ParseTable(Input.Text,separator.SelectedIndex==1?',':'\t');
            var guess=SpreadsheetImport.Suggest(table);int selected=Math.Max(0,Column.SelectedIndex);
            if(suggest){Header.IsChecked=guess.HasHeader;selected=guess.NameColumn;}
            int count=table.Count==0?0:table.Max(r=>r.Count);
            var names=Enumerable.Range(0,count).Select(i=>Header.IsChecked==true&&i<table[0].Count?table[0][i]:F($"第 {i+1} 列")).ToList();
            Column.ItemsSource=names;if(count>0)Column.SelectedIndex=Math.Min(selected,count-1);
            preview.Columns.Clear();
            for(int i=0;i<count;i++)preview.Columns.Add(new DataGridTextColumn { Header=names[i], Binding=new Binding($"[{i}]"), Width=new DataGridLength(1,DataGridLengthUnitType.Star), MinWidth=130, ElementStyle=(Style)Application.Current.FindResource("CellText") });
            preview.ItemsSource=table.Skip(Header.IsChecked==true?1:0).Take(20).Select(r=>Enumerable.Range(0,count).Select(i=>i<r.Count?r[i]:"").ToArray()).ToList();
            info.Text=F($"共 {Math.Max(0,table.Count-(Header.IsChecked==true?1:0))} 行，预览前 20 行。");
        }
        catch(FormatException ex){info.Text=ex.Message;}
        finally{updating=false;}
    }
    internal async Task LoadFile(string path)
    {
        if(loading)return;loading=true;IsEnabled=false;info.Text=T("正在读取清单…");
        try
        {
            if(Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))
            {
                var loaded=await Task.Run(()=>SpreadsheetImport.Read(path));
                sheets=loaded;updating=true;Sheets.ItemsSource=sheets.Select(s=>s.Name).ToList();Sheets.SelectedIndex=0;Sheets.Visibility=sheets.Count>1?Visibility.Visible:Visibility.Collapsed;separator.SelectedIndex=0;updating=false;
                Input.Text=SpreadsheetImport.ToText(sheets[0].Rows);editor.IsExpanded=false;
            }
            else
            {
                if(!new[]{".csv",".tsv",".txt"}.Contains(Path.GetExtension(path).ToLowerInvariant()))throw new IOException(T("请选择 .xlsx、CSV 或 TSV。旧版 .xls 请先另存为 .xlsx。"));
                if(new FileInfo(path).Length>16_000_000)throw new IOException(T("请导入小于 16 MB 的清单。"));
                var bytes=await File.ReadAllBytesAsync(path);string text;
                try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding("GB18030",EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetString(bytes);}
                sheets=[];Sheets.Visibility=Visibility.Collapsed;separator.SelectedIndex=Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase)?1:0;Input.Text=text;
            }
            source.Text=Path.GetFileName(path);UpdateColumns(true);
        }
        catch(Exception ex){info.Text=T("导入失败：")+ex.Message;}
        finally{updating=false;loading=false;IsEnabled=true;}
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
