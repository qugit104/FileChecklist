using FileChecklist.Core;
using System.Text;
using System.Globalization;

// Existing diagnostic assertions exercise Chinese text explicitly; language tests use scoped cultures.
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");

var tests = new (string, Action)[] {
    ("English scan and CSV preserve original multilingual request cells", () => InCulture("en-US", () => InTemp((root, output) => {
        Put(root,"中文.txt","payload");
        var task=ChecklistEngine.Import("文件名\t备注\n中文.txt\t001\nmissing.txt\t保留原始文本",'\t',true,0);
        task.Roots=[root];task.OutputFolder=output;ChecklistEngine.Scan(task);
        Eq("Missing",ChecklistEngine.StatusText(task.Rows[1].Status));
        True(task.Rows[1].Detail.Contains("not found",StringComparison.OrdinalIgnoreCase));
        var result=ChecklistEngine.Deliver(task,ChecklistEngine.Plan(task));
        var report=ChecklistEngine.ParseTable(File.ReadAllText(result.ReportPath),',');
        True(report[0].Contains("Status"));True(report[1].Contains("Delivered"));True(report[2].Contains("Missing"));
        Eq("001",task.Rows[0].Cells[1]);Eq("保留原始文本",task.Rows[1].Cells[1]);Eq("payload",File.ReadAllText(Path.Combine(output,"中文.txt")));
    }))),
    ("English core errors flow to background scans", () => InCulture("en-US", () => InTemp((root,output) => {
        var task=Make(root+"-missing",output,"a.txt");Task.Run(()=>ChecklistEngine.Scan(task)).GetAwaiter().GetResult();
        True(task.Rows[0].Detail.Contains("incomplete",StringComparison.OrdinalIgnoreCase));
        try { ChecklistEngine.Import("",',',false,0); throw new Exception("Expected empty-input error"); }
        catch(FormatException ex) { True(ex.Message.Contains("checklist",StringComparison.OrdinalIgnoreCase)); }
    }))),
    ("Chinese task can resume in English without translating user data", () => InTemp((root,output) => {
        Put(root,"a.txt","payload"); var task=Make(root,output,"a.txt\nmissing.txt\n../secret.txt"); task.Title="客户原始标题";ChecklistEngine.Scan(task);
        var result=ChecklistEngine.Deliver(task,ChecklistEngine.Plan(task));Eq(1,result.Copied);
        string saved=Path.Combine(output,"saved.fctask");ChecklistEngine.Save(task,saved);
        InCulture("en-US",()=>{var loaded=ChecklistEngine.Load(saved);ChecklistEngine.Scan(loaded);Eq("客户原始标题",loaded.Title);Eq(RowStatus.Delivered,loaded.Rows[0].Status);Eq("Delivered",ChecklistEngine.StatusText(loaded.Rows[0].Status));True(loaded.Rows[0].Detail.Contains("verified",StringComparison.OrdinalIgnoreCase));True(loaded.Rows[2].Detail.Contains("folder path",StringComparison.OrdinalIgnoreCase));});
        Eq("未找到",ChecklistEngine.StatusText(RowStatus.Missing));
    })),
    ("CSV quoted fields / embedded newlines / BOM", () => {
        var rows = ChecklistEngine.ParseTable("\uFEFFname,note\r\n\"a,b.txt\",\"line1\nline2 \"\"ok\"\"\"\r\n", ',');
        Eq(2, rows.Count); Eq("a,b.txt", rows[1][0]); Eq("line1\nline2 \"ok\"", rows[1][1]);
    }),
    ("Malformed CSV rejected", () => Throws<FormatException>(() => ChecklistEngine.ParseTable("\"unfinished", ','))),
    ("Order, duplicate rows, leading zeros, empty name retained", () => {
        var t = ChecklistEngine.Import("id\tfile\n001\ta.txt\n002\ta.txt\n003\t", '\t', true, 1);
        Eq(3,t.Rows.Count); Eq("001",t.Rows[0].Cells[0]); Eq(3,t.Rows[2].Number); Eq(RowStatus.Invalid,t.Rows[2].Status);
    }),
    ("Invalid names never escape a selected directory", () => {
        var t=ChecklistEngine.Import("../secret.txt\nC:\\secret.txt\na.txt", '\t',false,0);
        Eq(RowStatus.Invalid,t.Rows[0].Status); Eq(RowStatus.Invalid,t.Rows[1].Status);
    }),
    ("Recursive exact match, missing and ambiguity", () => InTemp((root,output) => {
        Put(root,"子目录/a.txt","a"); Put(root,"one/b.txt","1"); Put(root,"two/b.txt","2");
        var t=Make(root,output,"a.txt\nb.txt\nabsent.txt"); ChecklistEngine.Scan(t);
        Eq(RowStatus.Ready,t.Rows[0].Status); Eq(RowStatus.Ambiguous,t.Rows[1].Status); Eq(2,t.Rows[1].Candidates.Count); Eq(RowStatus.Missing,t.Rows[2].Status);
    })),
    ("Exact mode never silently falls back to stem", () => InTemp((root,output) => {
        Put(root,"A001.pdf","data");var t=Make(root,output,"A001");ChecklistEngine.Scan(t);Eq(RowStatus.Missing,t.Rows[0].Status);
    })),
    ("Stem mode finds case-insensitive name without extension", () => InTemp((root,output) => {
        Put(root,"nested/A001.PDF","data");var t=Make(root,output,"a001");t.MatchMode=MatchMode.Stem;ChecklistEngine.Scan(t);
        Eq(RowStatus.Ready,t.Rows[0].Status);var result=ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t));Eq(1,result.Copied);True(File.Exists(Path.Combine(output,"A001.PDF")));
    })),
    ("Stem mode exposes multiple extensions for manual choice", () => InTemp((root,output) => {
        Put(root,"A001.pdf","pdf");Put(root,"A001.dwg","dwg");var t=Make(root,output,"A001");t.MatchMode=MatchMode.Stem;ChecklistEngine.Scan(t);
        Eq(RowStatus.Ambiguous,t.Rows[0].Status);Eq(2,t.Rows[0].Candidates.Count);Eq(0,ChecklistEngine.Plan(t).Count);
    })),
    ("Identifier prefix respects boundaries and retains confirmed choice", () => InTemp((root,output) => {
        Put(root,"A001_report.pdf","ok");Put(root,"A001-revised.pdf","also");Put(root,"A0010_report.pdf","wrong");Put(root,"XA001_report.pdf","wrong");
        var t=Make(root,output,"A001");t.MatchMode=MatchMode.IdentifierPrefix;ChecklistEngine.Scan(t);Eq(2,t.Rows[0].Candidates.Count);Eq(RowStatus.Ambiguous,t.Rows[0].Status);
        ChecklistEngine.Choose(t.Rows[0],t.Rows[0].Candidates[0]);ChecklistEngine.Scan(t);Eq(RowStatus.Ready,t.Rows[0].Status);Eq(1,ChecklistEngine.Plan(t).Count);
    })),
    ("A single prefix suggestion still requires confirmation", () => InTemp((root,output) => {
        Put(root,"A-001_report.pdf","ok");var t=Make(root,output,"A-001");t.MatchMode=MatchMode.IdentifierPrefix;ChecklistEngine.Scan(t);
        Eq(1,t.Rows[0].Candidates.Count);Eq(RowStatus.Ambiguous,t.Rows[0].Status);True(t.Rows[0].Selected==null);Eq(0,ChecklistEngine.Plan(t).Count);
    })),
    ("Prefix mode and confirmed choice survive task save/load", () => InTemp((root,output) => {
        Put(root,"A001_final.pdf","ok");var t=Make(root,output,"A001");t.MatchMode=MatchMode.IdentifierPrefix;ChecklistEngine.Scan(t);ChecklistEngine.Choose(t.Rows[0],t.Rows[0].Candidates[0]);
        var file=Path.Combine(output,"saved.fctask");ChecklistEngine.Save(t,file);var loaded=ChecklistEngine.Load(file);Eq(MatchMode.IdentifierPrefix,loaded.MatchMode);ChecklistEngine.Scan(loaded);Eq(RowStatus.Ready,loaded.Rows[0].Status);
    })),
    ("Unknown matching mode rejected", () => InTemp((root,output) => {
        var t=Make(root,output,"a.txt");t.MatchMode=(MatchMode)99;Throws<InvalidDataException>(()=>ChecklistEngine.Scan(t));
    })),
    ("Missing root reported, not silently successful", () => InTemp((root,output) => {
        var t=Make(root+"-missing",output,"a.txt"); ChecklistEngine.Scan(t); True(t.ScanErrors.Count>0); True(t.Rows[0].Detail.Contains("不完整"));
    })),
    ("Ambiguous choice persists across rescan", () => InTemp((root,output) => {
        Put(root,"one/a.txt","1"); Put(root,"two/a.txt","2"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t);
        var chosen=t.Rows[0].Candidates[1]; ChecklistEngine.Choose(t.Rows[0],chosen); ChecklistEngine.Scan(t);
        Eq(chosen.Path,t.Rows[0].Selected!.Path); Eq(RowStatus.Ready,t.Rows[0].Status);
    })),
    ("Changed selected file requires confirmation", () => InTemp((root,output) => {
        Put(root,"a.txt","old"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); Put(root,"a.txt","different"); ChecklistEngine.Scan(t);
        Eq(RowStatus.Changed,t.Rows[0].Status); Eq(0,ChecklistEngine.Plan(t).Count);
    })),
    ("Delivery retains source and emits report for missing row", () => InTemp((root,output) => {
        Put(root,"中文.txt","payload"); var t=Make(root,output,"中文.txt\nmissing.txt"); ChecklistEngine.Scan(t);
        var r=ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t)); Eq(1,r.Copied); Eq("payload",File.ReadAllText(Path.Combine(root,"中文.txt")));
        Eq("payload",File.ReadAllText(t.Rows[0].DeliveredPath!)); True(File.ReadAllText(r.ReportPath).Contains("missing.txt"));
    })),
    ("Existing destination never overwritten", () => InTemp((root,output) => {
        Put(root,"a.txt","new"); Put(output,"a.txt","keep"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t);
        var p=ChecklistEngine.Plan(t); True(p[0].Problem!=null); var r=ChecklistEngine.Deliver(t,p); Eq(1,r.Failed); Eq("keep",File.ReadAllText(Path.Combine(output,"a.txt")));
    })),
    ("Explicit rename handles output collisions", () => InTemp((root,output) => {
        Put(root,"a.txt","new"); Put(output,"a.txt","keep"); var t=Make(root,output,"a.txt"); t.RenameConflicts=true; ChecklistEngine.Scan(t);
        var r=ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t)); Eq(1,r.Copied); True(t.Rows[0].DeliveredPath!=Path.Combine(output,"a.txt"));
    })),
    ("Duplicate requests reuse one copied file", () => InTemp((root,output) => {
        Put(root,"a.txt","data"); var t=Make(root,output,"a.txt\na.txt"); ChecklistEngine.Scan(t);
        var r=ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t)); Eq(1,r.Copied); Eq(1,r.Reused); Eq(t.Rows[0].DeliveredPath,t.Rows[1].DeliveredPath);
    })),
    ("Source mutation after preview is blocked", () => InTemp((root,output) => {
        Put(root,"a.txt","old"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); var p=ChecklistEngine.Plan(t); Put(root,"a.txt","changed");
        var r=ChecklistEngine.Deliver(t,p); Eq(1,r.Failed); True(!File.Exists(Path.Combine(output,"a.txt")));
    })),
    ("Same-size same-time source change after preview is blocked", () => InTemp((root,output) => {
        var src=Put(root,"a.txt","aaa"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); var p=ChecklistEngine.Plan(t);
        var stamp=File.GetLastWriteTimeUtc(src); File.WriteAllText(src,"bbb"); File.SetLastWriteTimeUtc(src,stamp);
        var r=ChecklistEngine.Deliver(t,p); Eq(1,r.Failed); True(!File.Exists(Path.Combine(output,"a.txt")));
    })),
    ("Loaded snapshots require fresh scan", () => InTemp((root,output) => {
        Put(root,"a.txt","a"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); var f=Path.Combine(output,"task.fctask");ChecklistEngine.Save(t,f);
        var loaded=ChecklistEngine.Load(f);Throws<InvalidOperationException>(()=>ChecklistEngine.Plan(loaded));
    })),
    ("Overlapping roots do not duplicate candidates", () => InTemp((root,output) => {
        Put(root,"nested/a.txt","a"); var t=Make(root,output,"a.txt"); t.Roots.Add(Path.Combine(root,"nested"));ChecklistEngine.Scan(t);Eq(1,t.Rows[0].Candidates.Count);
    })),
    ("Destination created after preview never overwritten", () => InTemp((root,output) => {
        Put(root,"a.txt","source"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); var p=ChecklistEngine.Plan(t); Put(output,"a.txt","racer");
        var r=ChecklistEngine.Deliver(t,p); Eq(1,r.Failed); Eq("racer",File.ReadAllText(Path.Combine(output,"a.txt")));
    })),
    ("Output within source rejected", () => InTemp((root,output) => {
        Put(root,"a.txt","a"); var t=Make(root,Path.Combine(root,"out"),"a.txt"); ChecklistEngine.Scan(t); Throws<InvalidOperationException>(()=>ChecklistEngine.Plan(t));
    })),
    ("Drive root contains its output descendants", () => InTemp((root,output) => {
        var t=Make(Path.GetPathRoot(root)!,output,"a.txt");t.LastScanUtc=DateTime.UtcNow;
        Throws<InvalidOperationException>(()=>ChecklistEngine.Plan(t));
    })),
    ("Task roundtrip and supplement only missing item", () => InTemp((root,output) => {
        Put(root,"a.txt","a"); var t=Make(root,output,"a.txt\nb.txt"); ChecklistEngine.Scan(t); ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t));
        string saved=Path.Combine(Path.GetDirectoryName(root)!,"task.fctask"); ChecklistEngine.Save(t,saved); var restored=ChecklistEngine.Load(saved);
        Put(root,"b.txt","b"); ChecklistEngine.Scan(restored); Eq(RowStatus.Delivered,restored.Rows[0].Status); Eq(RowStatus.Ready,restored.Rows[1].Status);
        var r=ChecklistEngine.Deliver(restored,ChecklistEngine.Plan(restored)); Eq(1,r.Copied); Eq(0,r.Failed);
    })),
    ("Modified delivered file detected by content hash", () => InTemp((root,output) => {
        Put(root,"a.txt","aaa"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t));
        var dest=t.Rows[0].DeliveredPath!; var stamp=File.GetLastWriteTimeUtc(dest); File.WriteAllText(dest,"bbb"); File.SetLastWriteTimeUtc(dest,stamp); ChecklistEngine.Scan(t);
        Eq(RowStatus.Changed,t.Rows[0].Status);
    })),
    ("Recheck completed source detects same size/timestamp changes", () => InTemp((root,output) => {
        var src=Put(root,"a.txt","aaa"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t));
        var stamp=File.GetLastWriteTimeUtc(src); File.WriteAllText(src,"bbb"); File.SetLastWriteTimeUtc(src,stamp); ChecklistEngine.Scan(t); Eq(RowStatus.Changed,t.Rows[0].Status);
    })),
    ("Cancellation prevents copies", () => InTemp((root,output) => {
        Put(root,"a.txt","a"); var t=Make(root,output,"a.txt"); ChecklistEngine.Scan(t); using var cts=new CancellationTokenSource(); cts.Cancel();
        Throws<OperationCanceledException>(()=>ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t),cts.Token)); True(!File.Exists(Path.Combine(output,"a.txt")));
    })),
    ("Report formula cells neutralized", () => InTemp((root,output) => {
        Put(root,"a.txt","a"); var t=ChecklistEngine.Import("file\tnote\na.txt\t=1+1",'\t',true,0); t.Roots=[root]; t.OutputFolder=output; ChecklistEngine.Scan(t);
        var r=ChecklistEngine.Deliver(t,ChecklistEngine.Plan(t)); var report=File.ReadAllText(r.ReportPath); True(report.Contains("'=1+1"));
    })),
    ("CLI plan is dry-run and preserves original columns", () => InTemp((root,output) => {
        Put(root,"A001.pdf","ok");var csv=Put(root,"request.csv","name,note\nA001,001\nmissing,002");var state=Path.Combine(Path.GetDirectoryName(root)!,"cli.fctask");
        using var log=new StringWriter();Eq(2,FileChecklist.Cli.CommandLine.Run(["plan",csv,"--source",root,"--output",output,"--task",state,"--mode","stem","--header","--json"],log));
        True(!Directory.Exists(output));var t=ChecklistEngine.Load(state);Eq("001",t.Rows[0].Cells[1]);Eq(MatchMode.Stem,t.MatchMode);True(log.ToString().Contains("\"Missing\""));
    })),
    ("CLI refuses copying without explicit yes", () => InTemp((root,output) => {
        Put(root,"a.txt","ok");var t=Make(root,output,"a.txt");var state=Path.Combine(Path.GetDirectoryName(root)!,"cli.fctask");ChecklistEngine.Save(t,state);
        using var log=new StringWriter();Eq(64,FileChecklist.Cli.CommandLine.Run(["copy",state],log));True(!Directory.Exists(output));
    })),
    ("CLI prefix review, choose, copy and repeat do not duplicate files", () => InTemp((root,output) => {
        Put(root,"A001_report.pdf","ok");var csv=Put(root,"request.csv","A001");var state=Path.Combine(Path.GetDirectoryName(root)!,"cli.fctask");using var log=new StringWriter();
        Eq(2,FileChecklist.Cli.CommandLine.Run(["plan",csv,"--source",root,"--output",output,"--task",state,"--mode","prefix"],log));
        Eq(0,FileChecklist.Cli.CommandLine.Run(["choose",state,"--row","1","--candidate","1"],log));
        Eq(0,FileChecklist.Cli.CommandLine.Run(["copy",state,"--yes"],log));Eq("ok",File.ReadAllText(Path.Combine(output,"A001_report.pdf")));
        Eq(0,FileChecklist.Cli.CommandLine.Run(["copy",state,"--yes"],log));Eq(1,Directory.GetFiles(output,"*.pdf").Length);
    })),
    ("CLI candidate numbers remain attached to the reviewed path", () => InTemp((root,output) => {
        var expected=Put(root,"b/A001_report.txt","reviewed");var csv=Put(root,"request.csv","A001");var state=Path.Combine(Path.GetDirectoryName(root)!,"cli.fctask");using var log=new StringWriter();
        Eq(2,FileChecklist.Cli.CommandLine.Run(["plan",csv,"--source",root,"--output",output,"--task",state,"--mode","prefix"],log));
        Put(root,"a/A001_report.txt","new unreviewed");Eq(0,FileChecklist.Cli.CommandLine.Run(["choose",state,"--row","1","--candidate","1"],log));
        Eq(Path.GetFullPath(expected),ChecklistEngine.Load(state).Rows[0].Selected!.Path);
    })),
    ("CLI cannot overwrite an existing task snapshot", () => InTemp((root,output) => {
        var csv=Put(root,"list.csv","a.txt");var state=Put(root,"saved.fctask","keep this");using var log=new StringWriter();
        Eq(1,FileChecklist.Cli.CommandLine.Run(["plan",csv,"--source",root,"--output",output,"--task",state],log));Eq("keep this",File.ReadAllText(state));
    })),
    ("CLI rejects typo options without side effects", () => InTemp((root,output) => {
        var csv=Put(root,"list.csv","a.txt");using var log=new StringWriter();Eq(64,FileChecklist.Cli.CommandLine.Run(["plan",csv,"--source",root,"--output",output,"--task",Path.Combine(output,"task"),"--mod","stem"],log));True(!Directory.Exists(output));
    })),
    ("Unsupported task format rejected", () => InTemp((root,output) => {
        var f=Put(root,"bad.fctask","{\"Version\":99}"); Throws<InvalidDataException>(()=>ChecklistEngine.Load(f));
    }))
};
int failures=0;
foreach(var (name,run) in tests) { try {run(); Console.WriteLine("PASS "+name);} catch(Exception ex){failures++; Console.WriteLine("FAIL "+name+": "+ex.Message);} }
Console.WriteLine($"TOTAL {tests.Length}; PASS {tests.Length-failures}; FAIL {failures}");
return failures==0?0:1;
static void True(bool value) {if(!value)throw new Exception("Assertion failed");}
static void Eq<T>(T expected,T actual) {if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}; got {actual}");}
static void Throws<T>(Action action) where T:Exception {bool caught=false; try{action();}catch(T){caught=true;}True(caught);}
static void InCulture(string culture,Action action) { var before=CultureInfo.CurrentUICulture;try{CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);action();}finally{CultureInfo.CurrentUICulture=before;} }
static ChecklistTask Make(string root,string output,string names) {var t=ChecklistEngine.Import(names,'\t',false,0);t.Roots=[root];t.OutputFolder=output;return t;}
static string Put(string root,string relative,string content){var p=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,content,new UTF8Encoding(false));return p;}
static void InTemp(Action<string,string> run){var p=Path.Combine(Path.GetTempPath(),"FileChecklistTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(p,"source"));try{run(Path.Combine(p,"source"),Path.Combine(p,"delivery"));}finally{Directory.Delete(p,true);}}
