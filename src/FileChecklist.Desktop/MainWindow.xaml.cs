using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileChecklist.Core;
using Microsoft.Win32;

namespace FileChecklist.Desktop;

public partial class MainWindow : Window
{
    private ChecklistTask task = new();
    private string? taskFile;
    private string recoveryId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? cancellation;
    private bool refreshing;
    private static string? smokeDataHome;
    private static string DataHome => smokeDataHome ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileChecklist");
    public MainWindow() { InitializeComponent(); Refresh(); }

    private void Refresh()
    {
        if (!IsInitialized) return;
        refreshing = true;
        int? selected = (RowsGrid.SelectedItem as RowView)?.Number;
        TaskTitle.Text = task.Rows.Count == 0 ? "未导入清单" : task.Title + "  /  " + (task.MatchMode switch { MatchMode.Stem => "不含扩展名", MatchMode.IdentifierPrefix => "编号前缀", _ => "完整文件名" });
        RootsBox.ItemsSource = task.Roots.ToList(); if (task.Roots.Count > 0) RootsBox.SelectedIndex = 0;
        OutputBox.Text = string.IsNullOrEmpty(task.OutputFolder) ? "选择一个独立的文件夹" : task.OutputFolder;
        TotalCount.Text = task.Rows.Count.ToString(); ReadyCount.Text = task.Rows.Count(r => r.Status == RowStatus.Ready).ToString();
        ProblemCount.Text = task.Rows.Count(IsProblem).ToString(); DoneCount.Text = task.Rows.Count(r => r.Status == RowStatus.Delivered).ToString();
        string search = SearchBox.Text.Trim();
        var rows = task.Rows.Where(r => FilterBox.SelectedIndex switch { 1 => IsProblem(r), 2 => r.Status == RowStatus.Ready, 3 => r.Status == RowStatus.Delivered, _ => true })
            .Where(r => search.Length == 0 || (r.Name + " " + string.Join(" ", r.Cells) + " " + r.Selected?.Path).Contains(search, StringComparison.OrdinalIgnoreCase)).Select(r => new RowView(r)).ToList();
        RowsGrid.ItemsSource = rows;
        RowsGrid.SelectedItem = rows.FirstOrDefault(r => r.Number == selected);
        EmptyPanel.Visibility = task.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = task.Rows.Count > 0 && task.Roots.Count > 0;
        DeliverButton.IsEnabled = task.Rows.Count > 0 && task.LastScanUtc != null;
        IssuesButton.Content = $"扫描问题 ({task.ScanErrors.Count})";
        refreshing = false; UpdateDetails();
    }
    private static bool IsProblem(ChecklistRow r) => r.Status is RowStatus.Missing or RowStatus.Ambiguous or RowStatus.Changed or RowStatus.Error or RowStatus.Invalid;
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (!refreshing && IsInitialized) Refresh(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (!refreshing && IsInitialized) Refresh(); }
    private void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!refreshing) UpdateDetails(); }
    private void UpdateDetails()
    {
        if (RowsGrid.SelectedItem is not RowView view) { CandidatePanel.Visibility = Visibility.Collapsed; return; }
        CandidatePanel.Visibility = Visibility.Visible;
        var row = view.Row;
        SelectedLabel.Text = $"第 {row.Number} 行 · {row.Name} · {row.Candidates.Count} 个候选";
        CandidatesBox.ItemsSource = row.Candidates.Select(c => new CandidateView(c)).ToList();
        CandidatesBox.SelectedIndex = Math.Max(0, row.Candidates.FindIndex(c => c.Path == row.Selected?.Path));
        SelectedDetail.Text = row.Detail + (row.DeliveredPath == null ? "" : "\n上次交付：" + row.DeliveredPath);
        CandidateActions.IsEnabled = cancellation == null && task.LastScanUtc != null && row.Candidates.Count > 0 && row.Status != RowStatus.Delivered;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;
        AutoSave(); task = dialog.Result; task.Title = $"文件交付 · {DateTime.Now:MM月dd日}"; taskFile = null; recoveryId = Guid.NewGuid().ToString("N");
        FilterBox.SelectedIndex = 0; SearchBox.Clear(); Refresh(); AutoSave(); StatusLine.Text = "清单已导入。添加来源文件夹后，点击扫描。";
    }
    private void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择来源文件夹", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FolderNames)
            if (!task.Roots.Contains(path, StringComparer.OrdinalIgnoreCase)) task.Roots.Add(path);
        InvalidateScan();
    }
    private void RemoveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (RootsBox.SelectedItem is string root) { task.Roots.Remove(root); InvalidateScan(); }
    }
    private void InvalidateScan() { task.LastScanUtc = null; Refresh(); AutoSave(); StatusLine.Text = "来源目录已变化，请重新扫描；之前的选择会在扫描后核对。"; }
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择交付目录（应独立于来源目录）" };
        if (dialog.ShowDialog(this) != true) return;
        if (!string.IsNullOrEmpty(task.OutputFolder) && !string.Equals(task.OutputFolder, dialog.FolderName, StringComparison.OrdinalIgnoreCase) && task.Rows.Any(r => r.DeliveredPath != null))
        {
            if (MessageBox.Show(this,"更换目录后，已交付项也会等待重新交付。旧目录中的文件会保留。继续吗？","更换交付目录",MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            foreach (var row in task.Rows) { row.DeliveredPath = null; row.DeliveredHash = null; if (row.Status == RowStatus.Delivered) row.Status = RowStatus.Ready; }
        }
        task.OutputFolder = dialog.FolderName; Refresh(); AutoSave();
    }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanTask();
    private async Task ScanTask()
    {
        await Work("正在扫描来源目录，并核对上次交付的文件内容…", ct => ChecklistEngine.Scan(task, ct));
    }
    private async Task<bool> Work(string message, Action<CancellationToken> action)
    {
        if (cancellation != null) return false;
        cancellation = new CancellationTokenSource(); SetBusy(true); StatusLine.Text = message;
        bool ok = false;
        try { await Task.Run(() => action(cancellation.Token)); ok = true; StatusLine.Text = $"完成。共 {task.Rows.Count} 项；待交付 {task.Rows.Count(r => r.Status == RowStatus.Ready)} 项，需要处理 {task.Rows.Count(IsProblem)} 项。"; }
        catch (OperationCanceledException) { StatusLine.Text = "已取消。已完成的交付保留；未完成项可重新扫描后继续。"; }
        catch (Exception ex) { StatusLine.Text = "操作未完成：" + ex.Message; MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); Refresh(); AutoSave(); }
        return ok;
    }
    private void SetBusy(bool busy)
    {
        SideActions.IsEnabled = TopActions.IsEnabled = FolderActions.IsEnabled = ScanActions.IsEnabled = CandidateActions.IsEnabled = !busy;
        CancelButton.Visibility = Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); StatusLine.Text = "正在停止，等待当前文件操作返回…"; }
    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (RowsGrid.SelectedItem is not RowView row || CandidatesBox.SelectedItem is not CandidateView candidate) return;
        await ConfirmCandidate(row.Row, candidate.Candidate);
    }
    private async Task ConfirmCandidate(ChecklistRow row, Candidate candidate)
    { if (await Work("正在读取并记录所选文件的内容校验值…", ct => ChecklistEngine.Choose(row, candidate, ct))) StatusLine.Text = "已确认选中文件；交付时会再次校验内容。"; }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatesBox.SelectedItem is CandidateView selected)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + selected.Candidate.Path + "\"", UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this,ex.Message,"无法打开文件夹"); }
        }
    }
    private async void Deliver_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(task.OutputFolder)) { Output_Click(sender,e); if (string.IsNullOrWhiteSpace(task.OutputFolder)) return; }
        try
        {
            // This is a user-facing choice, reflected in the concrete preview that follows.
            var settings = new Window { Owner=this, Title="交付命名", Width=470, Height=245, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner };
            var box=new StackPanel { Margin=new Thickness(24) }; settings.Content=box;
            box.Children.Add(new TextBlock { Text="同名文件处理", FontSize=14, FontWeight=FontWeights.SemiBold });
            var rename=new CheckBox { Content="自动添加序号，例如 A001 (2).jpg", IsChecked=task.RenameConflicts, Margin=new Thickness(0,18,0,12) }; box.Children.Add(rename);
            box.Children.Add(new TextBlock { Text="不勾选时，重名目标会被跳过并记入报告。所有模式都保留已有文件。", TextWrapping=TextWrapping.Wrap, Foreground=Brushes.SlateGray });
            var next=new Button { Content="下一步：查看复制列表", HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,18,0,0), Style=(Style)FindResource("Primary") }; next.Click+=(_,_)=>settings.DialogResult=true; box.Children.Add(next);
            if(settings.ShowDialog()!=true)return;
            task.RenameConflicts=rename.IsChecked==true;
            var plan=ChecklistEngine.Plan(task);
            var preview=new DeliveryWindow(task,plan){Owner=this}; if(preview.ShowDialog()!=true)return;
            DeliveryResult? result=null;
            if(await Work("正在复制并校验交付副本…",ct=>result=ChecklistEngine.Deliver(task,plan,ct)) && result!=null)
            {
                StatusLine.Text=$"本次复制 {result.Copied} 个文件，{result.Reused} 项共用副本，失败 {result.Failed} 项。报告已保存到交付目录。";
                MessageBox.Show(this,$"复制文件：{result.Copied}\n共用副本的清单项：{result.Reused}\n失败项：{result.Failed}\n\n交付报告：\n{result.ReportPath}\n\n未找到和待确认项也会列在报告中。","交付结果");
            }
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"无法准备交付",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if(task.Rows.Count==0){StatusLine.Text="先导入一份清单，再保存任务。";return;}
        var dialog=new SaveFileDialog { Filter="清单交付任务|*.fctask", FileName=taskFile==null?"文件交付任务.fctask":Path.GetFileName(taskFile), DefaultExt=".fctask" };
        if(dialog.ShowDialog(this)!=true)return;
        try{ChecklistEngine.Save(task,dialog.FileName);taskFile=dialog.FileName;StatusLine.Text="任务已保存："+taskFile;AutoSave();}catch(Exception ex){MessageBox.Show(this,ex.Message,"保存失败");}
    }
    private void Open_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog { Filter="清单交付任务|*.fctask" }; if(dialog.ShowDialog(this)==true)OpenTask(dialog.FileName);
    }
    internal void OpenTask(string path)
    {
        try{var loaded=ChecklistEngine.Load(path);AutoSave();task=loaded;taskFile=path;recoveryId=Guid.NewGuid().ToString("N");FilterBox.SelectedIndex=0;SearchBox.Clear();Refresh();StatusLine.Text="任务已恢复。请重新扫描，核对文件现状后继续补件。";AutoSave();}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"打开失败",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private void AutoSave()
    {
        if(task.Rows.Count==0)return;
        try
        {
            string path=Path.Combine(DataHome,"Recovery",recoveryId+".fctask"); ChecklistEngine.Save(task,path);
            File.WriteAllText(Path.Combine(DataHome,"last-task.txt"),path);
        }
        catch(Exception ex){StatusLine.Text="自动恢复副本保存失败，请手动保存任务："+ex.Message;}
    }
    private void Recovery_Click(object sender,RoutedEventArgs e)
    {
        string pointer=Path.Combine(DataHome,"last-task.txt");
        try{if(!File.Exists(pointer)){StatusLine.Text="还没有可恢复的任务。";return;}string path=File.ReadAllText(pointer);OpenTask(path);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"恢复失败");}
    }
    private void Issues_Click(object sender,RoutedEventArgs e) => ShowText("扫描问题", task.ScanErrors.Count==0 ? "未记录到扫描问题。只扫描所选文件夹，不代表其他位置也已检查。" : string.Join("\n\n",task.ScanErrors));
    private void Help_Click(object sender,RoutedEventArgs e) => ShowText("使用说明", "1. 粘贴 Excel 多列内容或导入 CSV / TSV，指定文件名列。第一版按完整文件名匹配，忽略大小写。\n\n2. 添加来源文件夹并扫描。选中清单行，可查看全部同名候选并确认使用哪个文件。重复清单行会保留。\n\n3. 选择独立的交付目录，点击“复制到目标文件夹”，预览路径后复制。不会覆盖已有文件。报告包含每一行的结果。\n\n4. 保存 .fctask 任务；文件补齐后重新打开并扫描。已交付文件通过内容校验后保留完成状态。变化的文件必须再次确认。\n\n自动恢复副本在 %LOCALAPPDATA%\\FileChecklist\\Recovery，每次操作结束保存；可从“文件”菜单选择“恢复最近任务”。手动保存的任务是当时的快照。\n\n第一版仅支持 Windows 本地普通文件夹，跳过符号链接、目录联接和云端占位文件。不判断内容是否属于正确业务版本。CSV 可用 Excel 打开；编号前导零请通过“从文本/CSV”导入并将对应列设为文本。\n\n本应用不上传文件，不需要账户。任务文件与内部报告包含完整本地路径，请只分享给需要的人。");
    private void ShowText(string title,string text)
    {
        var window=new Window { Owner=this,Title=title,Width=740,Height=500,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        window.Content=new TextBox { Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20) };window.ShowDialog();
    }
    private void Demo_Click(object sender,RoutedEventArgs e) => StartDemo();
    internal void StartDemo(string? home=null)
    {
        AutoSave();
        home??=Path.Combine(DataHome,"Examples",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        string source=Path.Combine(home,"来源文件"); Directory.CreateDirectory(Path.Combine(source,"已确认"));Directory.CreateDirectory(Path.Combine(source,"旧版本"));
        File.WriteAllText(Path.Combine(source,"产品说明.txt"),"产品说明：这是一份用于体验的文件。\n编号：001");
        File.WriteAllText(Path.Combine(source,"已确认","报价单.txt"),"本次确认报价：120 元。");File.WriteAllText(Path.Combine(source,"旧版本","报价单.txt"),"旧报价：100 元。");
        File.WriteAllText(Path.Combine(source,"使用指南.md"),"# 使用指南\n清单交付演示。\n");
        task=ChecklistEngine.Import("文件名\t用途\t编号\n产品说明.txt\t交付客户甲\t001\n报价单.txt\t请选择确认版本\t002\n缺少的附件.txt\t等待同事补件\t003\n使用指南.md\t交付客户乙\t004\n产品说明.txt\t原清单重复行，保留记录\t005",'\t',true,0);
        task.Title="示例清单";task.Roots=[source];task.OutputFolder=Path.Combine(home,"交付结果");taskFile=null;recoveryId=Guid.NewGuid().ToString("N");
        FilterBox.SelectedIndex=0;SearchBox.Clear();Refresh();AutoSave();StatusLine.Text="示例已打开。点击“扫描文件”查找清单中的文件。";
    }
    private void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(cancellation!=null){e.Cancel=true;cancellation.Cancel();StatusLine.Text="正在停止操作。完成后可关闭窗口。";return;}AutoSave();
    }

    internal async Task RunSmoke(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            smokeDataHome = Path.Combine(output,"appdata");
            var import=new ImportWindow { Owner=this };import.Show();import.Input.Text="file\tnote\na.txt\t保留备注";import.Column.SelectedIndex=0;
            if(import.BuildTask().Rows[0].Cells[1]!="保留备注")throw new Exception("Import UI column mapping failed");
            import.Matching.SelectedIndex=2;if(import.BuildTask().MatchMode!=MatchMode.IdentifierPrefix)throw new Exception("Import UI match-mode selection failed");
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindow(import,Path.Combine(output,"import.png"));import.Close();
            StartDemo(Path.Combine(output,"fixture")); await ScanTask();
            if(task.Rows.Count!=5||task.Rows.Count(r=>r.Status==RowStatus.Ambiguous)!=1||task.Rows.Count(r=>r.Status==RowStatus.Missing)!=1)throw new Exception("Scan UI results incorrect");
            RowsGrid.SelectedItem=((IEnumerable<RowView>)RowsGrid.ItemsSource).Single(r=>r.Row.Status==RowStatus.Ambiguous);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(Path.Combine(output,"desktop.png"));
            double originalWidth=Width,originalHeight=Height;Width=MinWidth;Height=MinHeight;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(Path.Combine(output,"desktop-small.png"));
            Width=originalWidth;Height=originalHeight;
            CandidatesBox.SelectedIndex=0;await ConfirmCandidate(((RowView)RowsGrid.SelectedItem).Row,((CandidateView)CandidatesBox.SelectedItem).Candidate);
            var plan=ChecklistEngine.Plan(task);var preview=new DeliveryWindow(task,plan){Owner=this};preview.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindow(preview,Path.Combine(output,"preview.png"));preview.Close();
            DeliveryResult? result=null;await Work("UI smoke: delivering",ct=>result=ChecklistEngine.Deliver(task,plan,ct));
            if(result?.Copied!=3||result.Reused!=1)throw new Exception("UI delivery result incorrect");
            string saved=Path.Combine(output,"roundtrip.fctask");ChecklistEngine.Save(task,saved);OpenTask(saved);
            if(DeliverButton.IsEnabled)throw new Exception("Restored task must require recheck");
            File.WriteAllText(Path.Combine(task.Roots[0],"缺少的附件.txt"),"补齐附件");await ScanTask();
            if(task.Rows.Count(r=>r.Status==RowStatus.Delivered)!=4||task.Rows.Count(r=>r.Status==RowStatus.Ready)!=1)throw new Exception("Supplement UI failed");
            await Work("UI smoke: supplement",ct=>result=ChecklistEngine.Deliver(task,ChecklistEngine.Plan(task),ct));
            if(result?.Copied!=1||task.Rows.Any(r=>r.Status!=RowStatus.Delivered))throw new Exception("Supplement delivery failed");
            File.WriteAllText(Path.Combine(output,"smoke-result.txt"),"PASS: import controls, scan, ambiguity selection, preview window, delivery, task save/load, recheck gate, missing-file supplement.\nAll five checklist rows delivered; four distinct files copied.");
            Application.Current.Shutdown(0);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"smoke-result.txt"),"FAIL: "+ex);Application.Current.Shutdown(1);}
    }
    private void Capture(string path)=>CaptureWindow(this,path);
    private static void CaptureWindow(Window window,string path)
    {
        window.UpdateLayout();var bmp=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        var backdrop=new DrawingVisual();using(var context=backdrop.RenderOpen())context.DrawRectangle(window.Background??Brushes.White,null,new Rect(0,0,window.ActualWidth,window.ActualHeight));
        bmp.Render(backdrop);bmp.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(path);encoder.Save(file);
    }
}

public sealed class RowView(ChecklistRow row)
{
    public ChecklistRow Row { get; }=row;
    public int Number=>Row.Number;
    public string Name=>Row.Name;
    public string Original=>string.Join(" · ",Row.Cells);
    public string Status=>Row.Status switch {RowStatus.Ready=>"待复制",RowStatus.Delivered=>"已完成",_=>ChecklistEngine.StatusText(Row.Status)};
    public string Detail=>Row.Detail;
    public string Tint=>Row.Status switch {RowStatus.Delivered=>"#DCEFE9",RowStatus.Ready=>"#EAF3EA",RowStatus.Missing or RowStatus.Error or RowStatus.Invalid=>"#FCEAE5",RowStatus.Ambiguous or RowStatus.Changed=>"#FFF0D9",_=>"#EDF1F0"};
    public string Ink=>Row.Status switch {RowStatus.Delivered or RowStatus.Ready=>"#276249",RowStatus.Missing or RowStatus.Error or RowStatus.Invalid=>"#9B4331",RowStatus.Ambiguous or RowStatus.Changed=>"#8B591F",_=>"#637775"};
}
public sealed class CandidateView(Candidate candidate)
{
    public Candidate Candidate { get; }=candidate;
    public string FilePath=>Candidate.Path;
    public string Size=>$"{Candidate.Length:N0} 字节";
    public string Modified=>Candidate.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
