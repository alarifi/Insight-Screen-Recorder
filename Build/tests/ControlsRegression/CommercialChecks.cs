using System.Reflection;
using System.Linq.Expressions;
using InsightScreenRecorder;

internal static class CommercialChecks
{
 const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
 static void Wait(Task task){var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(watch.Elapsed.TotalSeconds>35)throw new TimeoutException();Application.DoEvents();Thread.Sleep(10);}task.GetAwaiter().GetResult();}
 internal static void Run(Assembly assembly,MainForm form,string evidence){
  var model=assembly.GetType("InsightScreenRecorder.RecordingLibrary")!;
  object? Call(string method,params object[] arguments)=>model.GetMethod(method,Any)!.Invoke(null,arguments);
  string folder=Path.Combine(evidence,"fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  string original=Path.Combine(folder,"Insight_20261123_100000_123_12345678.mp4");
  string fixture=File.ReadAllText("docs/evidence/controls-20261123/component-live-output.txt");File.Copy(fixture,original);File.WriteAllText(Path.Combine(folder,"broken.mkv"),"invalid test media");File.WriteAllText(Path.Combine(folder,"notes.txt"),"untouched");
  Check(((System.Collections.ICollection)Call("Scan",Path.Combine(folder,"missing"))!).Count==0,"missing library folder is harmless");
  var entries=((System.Collections.IEnumerable)Call("Scan",folder)!).Cast<object>().ToList();Check(entries.Count==2,"library scans only recording formats");
  Check((string)Call("DisplayName",Path.GetFileName(original))! == "Recording — 2026-11-23 10:00:00","friendly recording name without filesystem rename");
  Check((bool)Call("Matches",entries.Single(e=>(string)e.GetType().GetProperty("Path")!.GetValue(e)! == original),"12345678")!,"search matches original filename suffix");
  bool Rejected(string name){try{Call("Rename",original,name);return false;}catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException or IOException){return true;}}
  Check(Rejected("../escape.mp4")&&Rejected("CON.mp4")&&Rejected("different.mkv"),"rename rejects traversal, reserved names and extension changes");
  string occupied=Path.Combine(folder,"occupied.mp4");File.WriteAllText(occupied,"KEEP");Check(Rejected("occupied.mp4")&&File.ReadAllText(occupied)=="KEEP"&&File.Exists(original),"rename collision preserves both recordings");
  string renamed=(string)Call("Rename",original,"My recording")!;Check(File.Exists(renamed)&&!File.Exists(original),"rename preserves extension and changes only requested fixture");
  var duration=(Task<double>)Call("ReadDurationAsync",Path.GetFullPath("tools/ffprobe.exe"),renamed,CancellationToken.None)!;Wait(duration);Check(duration.Result>0,"real ffprobe duration is populated");
  var bad=(Task<double>)Call("ReadDurationAsync",Path.GetFullPath("tools/ffprobe.exe"),Path.Combine(folder,"broken.mkv"),CancellationToken.None)!;bool invalid=false;try{Wait(bad);}catch(IOException){invalid=true;}Check(invalid,"invalid recording produces explicit duration failure");
  var pageType=assembly.GetType("InsightScreenRecorder.RecordingLibraryPage")!;using(var page=(UserControl)Activator.CreateInstance(pageType,Any,null,new object[]{folder,Path.GetFullPath("tools/ffprobe.exe")},null)!){
   var body=(Control)typeof(MainForm).GetField("_body",Any)!.GetValue(form)!;var studio=body.Controls[0];body.Controls.Remove(studio);body.Controls.Add(page);form.Size=new Size(1200,575);Application.DoEvents();
   Wait((Task)pageType.GetMethod("RefreshAsync",Any)!.Invoke(page,null)!);
   var grid=(DataGridView)All(page).Single(c=>c.Name=="RecordingsGrid");var search=(TextBox)All(page).Single(c=>c.Name=="RecordingSearch");
   Check(grid.RowCount==3&&!grid.EnableHeadersVisualStyles&&grid.Columns.Count==4,"dark library includes duration and real fixture rows");
   typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(0,-1,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});
   Check(Convert.ToString(grid.Rows[0].Cells[0].Value)=="broken.mkv","column header sorts recording names");
   search.Text="My recording";Check(grid.RowCount==1,"library search filters visible rows");search.Text="";
   var firstRefresh=(Task)pageType.GetMethod("RefreshAsync",Any)!.Invoke(page,null)!;var secondRefresh=(Task)pageType.GetMethod("RefreshAsync",Any)!.Invoke(page,null)!;Wait(Task.WhenAll(firstRefresh,secondRefresh));Check(grid.RowCount==3,"rapid Refresh cancels stale duration reads safely");
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(evidence,"library-1200.png"));}
   form.Size=new Size(1000,575);Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(evidence,"library-1000.png"));}
   body.Controls.Remove(page);body.Controls.Add(studio);
  }
  var settingsType=assembly.GetType("InsightScreenRecorder.RecorderSettings")!;object settings=Activator.CreateInstance(settingsType)!;
  var parameter=Expression.Parameter(settingsType);var callback=Expression.Lambda(typeof(Func<,>).MakeGenericType(settingsType,typeof(string)),Expression.Constant(null,typeof(string)),parameter).Compile();
  var preferencesType=assembly.GetType("InsightScreenRecorder.PreferencesPage")!;
  using(var preferences=(UserControl)Activator.CreateInstance(preferencesType,Any,null,new object?[]{settings,callback,(Action)(()=>{}),null,null,null},null)!){
   bool Dirty()=>(bool)preferencesType.GetProperty("HasUnsavedChanges",Any)!.GetValue(preferences)!;
   Check(!Dirty(),"opening settings leaves draft clean");var number=All(preferences).OfType<NumericUpDown>().First();number.Value++;Check(Dirty(),"editing settings marks unsaved changes");
   preferencesType.GetMethod("SelectCategory",Any)!.Invoke(preferences,new object[]{"Sound"});Check(Dirty(),"category navigation keeps unsaved edits");
   Check((bool)preferencesType.GetMethod("SaveDraft",Any)!.Invoke(preferences,null)!&&!Dirty(),"successful Save establishes new clean baseline without user preferences writes");
   preferencesType.GetMethod("SelectCategory",Any)!.Invoke(preferences,new object[]{"Output"});All(preferences).OfType<NumericUpDown>().First().Value++;
   Check(!(bool)preferencesType.GetMethod("ResolveLeave",Any)!.Invoke(preferences,new object[]{DialogResult.Cancel})!&&Dirty(),"Cancel leaving retains unsaved settings");
   Check((bool)preferencesType.GetMethod("ResolveLeave",Any)!.Invoke(preferences,new object[]{DialogResult.No})!&&!Dirty(),"Discard clears draft even when page will remain alive in tray");
   preferencesType.GetMethod("SelectCategory",Any)!.Invoke(preferences,new object[]{"Advanced"});Check(!Dirty(),"Advanced category opens without dirtying saved settings");
   preferencesType.GetMethod("SelectCategory",Any)!.Invoke(preferences,new object[]{"Output"});
   var body=(Control)typeof(MainForm).GetField("_body",Any)!.GetValue(form)!;var studio=body.Controls[0];body.Controls.Remove(studio);body.Controls.Add(preferences);form.Size=new Size(1200,575);Application.DoEvents();
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(evidence,"settings-1200.png"));}
   body.Controls.Remove(preferences);body.Controls.Add(studio);
  }
  var catalogType=assembly.GetType("InsightScreenRecorder.MediaDeviceCatalog")!;var catalog=Activator.CreateInstance(catalogType)!;catalogType.GetMethod("Refresh")!.Invoke(catalog,null);
  string? previewId=null;
  using(var cameraPage=(UserControl)Activator.CreateInstance(preferencesType,Any,null,new object?[]{Activator.CreateInstance(settingsType),callback,(Action)(()=>{}),catalog,(Action<string>)(id=>previewId=id),null},null)!){
   var cameras=((System.Collections.IEnumerable)catalogType.GetProperty("Cameras")!.GetValue(catalog)!).Cast<object>().ToList();
   if(cameras.Count>0){var body=(Control)typeof(MainForm).GetField("_body",Any)!.GetValue(form)!;var studio=body.Controls[0];body.Controls.Remove(studio);body.Controls.Add(cameraPage);preferencesType.GetMethod("SelectCategory",Any)!.Invoke(cameraPage,new object[]{"Camera"});Application.DoEvents();
    ((IButtonControl)All(cameraPage).Single(c=>c.Text=="Preview camera")).PerformClick();Check(!string.IsNullOrEmpty(previewId),"default camera preview resolves actual camera ID without dirty selection");body.Controls.Remove(cameraPage);body.Controls.Add(studio);
   }
  }
  var validate=typeof(MainForm).GetMethod("ValidateOutputDirectory",Any)!;validate.Invoke(null,new object[]{folder});bool denied=false;try{validate.Invoke(null,new object[]{occupied});}catch(TargetInvocationException){denied=true;}Check(denied&&File.ReadAllText(occupied)=="KEEP","output validation rejects a file path without changing file");
  Check(!Directory.GetFiles(folder,".insight-write-check-*").Any(),"writeability check cleans its unique test file");
 }
}
