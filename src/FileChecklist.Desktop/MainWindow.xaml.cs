using static FileChecklist.Core.Text;
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
        TaskTitle.Text = task.Rows.Count == 0 ? T("未导入清单") : task.Title + "  /  " + (task.MatchMode switch { MatchMode.Stem => T("不含扩展名"), MatchMode.IdentifierPrefix => T("编号前缀"), _ => T("完整文件名") });
        RootsBox.ItemsSource = task.Roots.ToList(); if (task.Roots.Count > 0) RootsBox.SelectedIndex = 0;
        OutputBox.Text = string.IsNullOrEmpty(task.OutputFolder) ? T("选择一个独立的文件夹") : task.OutputFolder;
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
        IssuesButton.Content = F($"扫描问题 ({task.ScanErrors.Count})");
        FolderActions.Visibility = task.Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        IssuesButton.Visibility = task.ScanErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        SelectedLabel.Text = F($"第 {row.Number} 行 · {row.Name} · {row.Candidates.Count} 个候选");
        CandidatesBox.ItemsSource = row.Candidates.Select(c => new CandidateView(c)).ToList();
        CandidatesBox.SelectedIndex = Math.Max(0, row.Candidates.FindIndex(c => c.Path == row.Selected?.Path));
        SelectedDetail.Text = row.Detail + (row.DeliveredPath == null ? "" : T("\n上次交付：") + row.DeliveredPath);
        CandidateActions.IsEnabled = cancellation == null && task.LastScanUtc != null && row.Candidates.Count > 0 && row.Status != RowStatus.Delivered;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;
        AutoSave(); task = dialog.Result; task.Title = F($"文件交付 · {DateTime.Now:MM月dd日}"); taskFile = null; recoveryId = Guid.NewGuid().ToString("N");
        FilterBox.SelectedIndex = 0; SearchBox.Clear(); Refresh(); AutoSave(); StatusLine.Text = T("清单已导入。添加来源文件夹后，点击扫描。");
    }
    private void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("选择来源文件夹"), Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FolderNames)
            if (!task.Roots.Contains(path, StringComparer.OrdinalIgnoreCase)) task.Roots.Add(path);
        InvalidateScan();
    }
    private void RemoveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (RootsBox.SelectedItem is string root) { task.Roots.Remove(root); InvalidateScan(); }
    }
    private void InvalidateScan() { task.LastScanUtc = null; Refresh(); AutoSave(); StatusLine.Text = T("来源目录已变化，请重新扫描；之前的选择会在扫描后核对。"); }
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("选择交付目录（应独立于来源目录）") };
        if (dialog.ShowDialog(this) != true) return;
        if (!string.IsNullOrEmpty(task.OutputFolder) && !string.Equals(task.OutputFolder, dialog.FolderName, StringComparison.OrdinalIgnoreCase) && task.Rows.Any(r => r.DeliveredPath != null))
        {
            if (MessageBox.Show(this,T("更换目录后，已交付项也会等待重新交付。旧目录中的文件会保留。继续吗？"),T("更换交付目录"),MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            foreach (var row in task.Rows) { row.DeliveredPath = null; row.DeliveredHash = null; if (row.Status == RowStatus.Delivered) row.Status = RowStatus.Ready; }
        }
        task.OutputFolder = dialog.FolderName; Refresh(); AutoSave();
    }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanTask();
    private async Task ScanTask()
    {
        if (await Work(T("正在扫描来源目录，并核对上次交付的文件内容…"), ct => ChecklistEngine.Scan(task, ct)))
            FilterBox.SelectedIndex = task.Rows.Any(IsProblem) ? 1 : 0;
    }
    private async Task<bool> Work(string message, Action<CancellationToken> action)
    {
        if (cancellation != null) return false;
        cancellation = new CancellationTokenSource(); SetBusy(true); StatusLine.Text = message;
        bool ok = false;
        try { await Task.Run(() => action(cancellation.Token)); ok = true; StatusLine.Text = F($"完成。共 {task.Rows.Count} 项；待交付 {task.Rows.Count(r => r.Status == RowStatus.Ready)} 项，需要处理 {task.Rows.Count(IsProblem)} 项。"); }
        catch (OperationCanceledException) { StatusLine.Text = T("已取消。已完成的交付保留；未完成项可重新扫描后继续。"); }
        catch (Exception ex) { StatusLine.Text = T("操作未完成：") + ex.Message; MessageBox.Show(this, ex.Message, T("操作未完成"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); Refresh(); AutoSave(); }
        return ok;
    }
    private void SetBusy(bool busy)
    {
        SideActions.IsEnabled = TopActions.IsEnabled = FolderActions.IsEnabled = ScanActions.IsEnabled = CandidateActions.IsEnabled = !busy;
        CancelButton.Visibility = Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); StatusLine.Text = T("正在停止，等待当前文件操作返回…"); }
    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (RowsGrid.SelectedItem is not RowView row || CandidatesBox.SelectedItem is not CandidateView candidate) return;
        await ConfirmCandidate(row.Row, candidate.Candidate);
    }
    private async Task ConfirmCandidate(ChecklistRow row, Candidate candidate)
    { if (await Work(T("正在读取并记录所选文件的内容校验值…"), ct => ChecklistEngine.Choose(row, candidate, ct))) StatusLine.Text = T("已确认选中文件；交付时会再次校验内容。"); }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatesBox.SelectedItem is CandidateView selected)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + selected.Candidate.Path + "\"", UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this,ex.Message,T("无法打开文件夹")); }
        }
    }
    private async void Deliver_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(task.OutputFolder)) { Output_Click(sender,e); if (string.IsNullOrWhiteSpace(task.OutputFolder)) return; }
        try
        {
            // This is a user-facing choice, reflected in the concrete preview that follows.
            var settings = new Window { Owner=this, Title=T("交付命名"), Width=470, Height=245, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner };
            var box=new StackPanel { Margin=new Thickness(24) }; settings.Content=box;
            box.Children.Add(new TextBlock { Text=T("同名文件处理"), FontSize=14, FontWeight=FontWeights.SemiBold });
            var rename=new CheckBox { Content=T("自动添加序号，例如 A001 (2).jpg"), IsChecked=task.RenameConflicts, Margin=new Thickness(0,18,0,12) }; box.Children.Add(rename);
            box.Children.Add(new TextBlock { Text=T("不勾选时，重名目标会被跳过并记入报告。所有模式都保留已有文件。"), TextWrapping=TextWrapping.Wrap, Foreground=Brushes.SlateGray });
            var next=new Button { Content=T("下一步：查看复制列表"), HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,18,0,0), Style=(Style)FindResource("Primary") }; next.Click+=(_,_)=>settings.DialogResult=true; box.Children.Add(next);
            if(settings.ShowDialog()!=true)return;
            task.RenameConflicts=rename.IsChecked==true;
            var plan=ChecklistEngine.Plan(task);
            var preview=new DeliveryWindow(task,plan){Owner=this}; if(preview.ShowDialog()!=true)return;
            DeliveryResult? result=null;
            if(await Work(T("正在复制并校验交付副本…"),ct=>result=ChecklistEngine.Deliver(task,plan,ct)) && result!=null)
            {
                StatusLine.Text=F($"本次复制 {result.Copied} 个文件，{result.Reused} 项共用副本，失败 {result.Failed} 项。报告已保存到交付目录。");
                MessageBox.Show(this,F($"复制文件：{result.Copied}\n共用副本的清单项：{result.Reused}\n失败项：{result.Failed}\n\n交付报告：\n{result.ReportPath}\n\n未找到和待确认项也会列在报告中。"),T("交付结果"));
            }
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,T("无法准备交付"),MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if(task.Rows.Count==0){StatusLine.Text=T("先导入一份清单，再保存任务。");return;}
        var dialog=new SaveFileDialog { Filter=T("清单交付任务|*.fctask"), FileName=taskFile==null?T("文件交付任务.fctask"):Path.GetFileName(taskFile), DefaultExt=".fctask" };
        if(dialog.ShowDialog(this)!=true)return;
        try{ChecklistEngine.Save(task,dialog.FileName);taskFile=dialog.FileName;StatusLine.Text=T("任务已保存：")+taskFile;AutoSave();}catch(Exception ex){MessageBox.Show(this,ex.Message,T("保存失败"));}
    }
    private void Open_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog { Filter=T("清单交付任务|*.fctask") }; if(dialog.ShowDialog(this)==true)OpenTask(dialog.FileName);
    }
    internal void OpenTask(string path)
    {
        try{var loaded=ChecklistEngine.Load(path);AutoSave();task=loaded;taskFile=path;recoveryId=Guid.NewGuid().ToString("N");FilterBox.SelectedIndex=0;SearchBox.Clear();Refresh();StatusLine.Text=T("任务已恢复。请重新扫描，核对文件现状后继续补件。");AutoSave();}
        catch(Exception ex){MessageBox.Show(this,ex.Message,T("打开失败"),MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private void AutoSave()
    {
        if(task.Rows.Count==0)return;
        try
        {
            string path=Path.Combine(DataHome,"Recovery",recoveryId+".fctask"); ChecklistEngine.Save(task,path);
            File.WriteAllText(Path.Combine(DataHome,"last-task.txt"),path);
        }
        catch(Exception ex){StatusLine.Text=T("自动恢复副本保存失败，请手动保存任务：")+ex.Message;}
    }
    private void Recovery_Click(object sender,RoutedEventArgs e)
    {
        string pointer=Path.Combine(DataHome,"last-task.txt");
        try{if(!File.Exists(pointer)){StatusLine.Text=T("还没有可恢复的任务。");return;}string path=File.ReadAllText(pointer);OpenTask(path);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,T("恢复失败"));}
    }
    private void Issues_Click(object sender,RoutedEventArgs e) => ShowText(T("扫描问题"), task.ScanErrors.Count==0 ? T("未发现扫描错误。") : string.Join("\n\n",task.ScanErrors));
    private void Help_Click(object sender,RoutedEventArgs e) => ShowText(T("使用说明"), T("1. 导入清单，选择工作表和文件名列。\n\n2. 选择来源文件夹，扫描文件。\n\n3. 确认同名文件，选择交付目录，预览后复制。\n\n4. 从“文件”菜单保存任务，补件后重新扫描。\n\n匹配方式：完整文件名、不含扩展名、编号前缀。\n\n支持 .xlsx、CSV、TSV。Excel 公式使用已保存的结果。\n\n来源与交付目录需分开。符号链接和云占位文件会跳过。\n\n自动恢复目录：%LOCALAPPDATA%\\FileChecklist\\Recovery"));
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
        string source=Path.Combine(home,T("来源文件")); Directory.CreateDirectory(Path.Combine(source,T("已确认")));Directory.CreateDirectory(Path.Combine(source,T("旧版本")));
        File.WriteAllText(Path.Combine(source,T("产品说明.txt")),T("产品说明：这是一份用于体验的文件。\n编号：001"));
        File.WriteAllText(Path.Combine(source,T("已确认"),T("报价单.txt")),T("本次确认报价：120 元。"));File.WriteAllText(Path.Combine(source,T("旧版本"),T("报价单.txt")),T("旧报价：100 元。"));
        File.WriteAllText(Path.Combine(source,T("使用指南.md")),T("# 使用指南\n清单交付演示。\n"));
        task=ChecklistEngine.Import(T("文件名\t用途\t编号\n产品说明.txt\t客户归档\t001\n报价单.txt\t使用确认版\t002\n验收记录.txt\t项目负责人提供\t003\n使用指南.md\t随设备交付\t004\n产品说明.txt\t财务留存\t005"),'\t',true,0);
        task.Title=T("示例清单");task.Roots=[source];task.OutputFolder=Path.Combine(home,T("交付结果"));taskFile=null;recoveryId=Guid.NewGuid().ToString("N");
        FilterBox.SelectedIndex=0;SearchBox.Clear();Refresh();AutoSave();StatusLine.Text=T("示例已打开。点击“扫描文件”查找清单中的文件。");
    }
    private void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(cancellation!=null){e.Cancel=true;cancellation.Cancel();StatusLine.Text=T("正在停止操作。完成后可关闭窗口。");return;}AutoSave();
    }

    internal async Task RunSmoke(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            smokeDataHome = Path.Combine(output,"appdata");
            var args=Environment.GetCommandLineArgs();int languageArg=Array.IndexOf(args,"--lang");
            bool english=languageArg>=0?args[languageArg+1]=="en":!FileChecklist.Core.Text.IsChinese;
            if(Title!=(english?"FileChecklist":"清单交付") || ScanButton.Content.ToString()!=(english?"Scan files":"扫描文件"))throw new Exception("Main window language did not follow the requested language");
            var import=new ImportWindow { Owner=this };import.Show();import.Input.Text="file\tnote\na.txt\t保留备注";import.Column.SelectedIndex=0;
            if(import.Title!=(english?"Import checklist":"导入清单") || import.Header.Content.ToString()!=(english?"First row is a header":"第一行是表头"))throw new Exception("Import controls did not use the requested language");
            if(import.BuildTask().Rows[0].Cells[1]!="保留备注")throw new Exception("Import UI column mapping failed");
            import.Matching.SelectedIndex=2;if(import.BuildTask().MatchMode!=MatchMode.IdentifierPrefix)throw new Exception("Import UI match-mode selection failed");
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindow(import,Path.Combine(output,"import.png"));import.Close();
            var excel=new ImportWindow { Owner=this };excel.Show();await excel.LoadFile(Path.Combine(AppContext.BaseDirectory,"fixtures","checklist.xlsx"));
            if(excel.BuildTask().Rows.Count!=5 || excel.Column.SelectedIndex!=1 || excel.BuildTask().Rows[0].Cells[2]!="0007")throw new Exception("Real XLSX import controls lost rows or formatted ID");
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindow(excel,Path.Combine(output,"import-excel.png"));
            excel.Sheets.SelectedIndex=1;if(excel.BuildTask().Rows.Count!=2||excel.BuildTask().Rows[0].Name!="readme.txt")throw new Exception("Sheet switch lost first headerless filename");excel.Close();
            StartDemo(Path.Combine(output,"fixture")); await ScanTask();
            if(FilterBox.SelectedIndex!=1)throw new Exception("Scan should focus on the rows needing attention");
            if(task.Rows.Count!=5||task.Rows.Count(r=>r.Status==RowStatus.Ambiguous)!=1||task.Rows.Count(r=>r.Status==RowStatus.Missing)!=1)throw new Exception("Scan UI results incorrect");
            RowsGrid.SelectedItem=((IEnumerable<RowView>)RowsGrid.ItemsSource).Single(r=>r.Row.Status==RowStatus.Ambiguous);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(Path.Combine(output,"desktop.png"));
            double originalWidth=Width,originalHeight=Height;Width=MinWidth;Height=MinHeight;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(Path.Combine(output,"desktop-small.png"));
            Width=originalWidth;Height=originalHeight;
            CandidatesBox.SelectedIndex=0;await ConfirmCandidate(((RowView)RowsGrid.SelectedItem).Row,((CandidateView)CandidatesBox.SelectedItem).Candidate);
            var plan=ChecklistEngine.Plan(task);var preview=new DeliveryWindow(task,plan){Owner=this};preview.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindow(preview,Path.Combine(output,"preview.png"));preview.Close();
            if(preview.Title!=(english?"Confirm copies":"确认复制"))throw new Exception("Preview did not use the requested language");
            DeliveryResult? result=null;await Work("UI smoke: delivering",ct=>result=ChecklistEngine.Deliver(task,plan,ct));
            if(result?.Copied!=3||result.Reused!=1)throw new Exception("UI delivery result incorrect");
            var reportRows=ChecklistEngine.ParseTable(File.ReadAllText(result.ReportPath),',');
            if(!reportRows[0].Contains(english?"Status":"状态") || !reportRows.Any(r=>r.Contains(english?"Missing":"未找到")))throw new Exception("Delivery report did not use the requested language");
            string saved=Path.Combine(output,"roundtrip.fctask");ChecklistEngine.Save(task,saved);OpenTask(saved);
            if(DeliverButton.IsEnabled)throw new Exception("Restored task must require recheck");
            File.WriteAllText(Path.Combine(task.Roots[0],task.Rows.Single(r=>r.Status==RowStatus.Missing).Name),"补齐附件");await ScanTask();
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
    public string Status=>Row.Status switch {RowStatus.Ready=>T("待复制"),RowStatus.Delivered=>T("已完成"),_=>ChecklistEngine.StatusText(Row.Status)};
    public string Detail=>Row.Detail;
    public string Tint=>Row.Status switch {RowStatus.Delivered=>"#DCEFE9",RowStatus.Ready=>"#EAF3EA",RowStatus.Missing or RowStatus.Error or RowStatus.Invalid=>"#FCEAE5",RowStatus.Ambiguous or RowStatus.Changed=>"#FFF0D9",_=>"#EDF1F0"};
    public string Ink=>Row.Status switch {RowStatus.Delivered or RowStatus.Ready=>"#276249",RowStatus.Missing or RowStatus.Error or RowStatus.Invalid=>"#9B4331",RowStatus.Ambiguous or RowStatus.Changed=>"#8B591F",_=>"#637775"};
}
public sealed class CandidateView(Candidate candidate)
{
    public Candidate Candidate { get; }=candidate;
    public string FilePath=>Candidate.Path;
    public string Size=>F($"{Candidate.Length:N0} 字节");
    public string Modified=>Candidate.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
