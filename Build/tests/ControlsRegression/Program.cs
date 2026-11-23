using InsightScreenRecorder;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value)!;
 [STAThread] static void Main(string[] args){
  ApplicationConfiguration.Initialize();string evidence=Path.GetFullPath(args[0]);Directory.CreateDirectory(evidence);
  var assembly=typeof(MainForm).Assembly;var settingsType=assembly.GetType("InsightScreenRecorder.RecorderSettings")!;
  if(args.Contains("--combo-paint")){ComboPaintChecks.Run(assembly,evidence);return;}
  if(args.Contains("--stability")){StabilityChecks.Run(assembly,evidence);return;}
  if(args.Contains("--commercial"))Check(assembly.GetType("InsightScreenRecorder.RecordingLibrary")!=null,"recording library model exists");
  object settings=Activator.CreateInstance(settingsType)!;var open=settingsType.GetProperty("OpenFolderAfterSave");
  Check(open!=null&&Equals(open.GetValue(settings),true),"auto-open after successful save defaults to true");
  object migrated=JsonSerializer.Deserialize("{}",settingsType)!;Check(Equals(open!.GetValue(migrated),true),"old settings missing option inherit enabled default");
  object disabled=JsonSerializer.Deserialize("{\"OpenFolderAfterSave\":false}",settingsType)!;Check(Equals(open.GetValue(disabled),false),"explicit disabled preference survives loading");
  using var form=new MainForm();var record=(Control)Field(form,"_record");var type=record.GetType();var recording=type.GetProperty("Recording")!;var paused=type.GetProperty("Paused")!;
  Check(record.Text=="REC"&&record.AccessibleName=="Start recording","idle circle caption and accessibility");
  recording.SetValue(record,true);Check(record.Text=="Pause"&&record.AccessibleName=="Pause recording","recording circle becomes Pause");
  paused.SetValue(record,true);Check(record.Text=="Resume"&&record.AccessibleName=="Resume recording","paused circle becomes Resume");
  paused.SetValue(record,false);recording.SetValue(record,false);Check(record.Text=="REC","finished circle resets to REC");
  form.Show();Application.DoEvents();
  foreach(int width in new[]{1200,1000}){
   form.Size=new Size(width,575);form.PerformLayout();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
   bitmap.Save(Path.Combine(evidence,$"component-{width}.png"));
   var capture=form.Controls.Find("CaptureCard",true).Single();var action=form.Controls.Find("RecordingCard",true).Single();
   if(args.Contains("--layout")){
    var middle=form.Controls.Find("MediaCard",true).Single();Check(middle.Width>capture.Width*1.5,$"middle card has more width at {width}");
    typeof(MainForm).GetMethod("SetShortcutWarning",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,new object[]{"Fixture shortcut conflict"});
    var fix=(Control)Field(form,"_shortcutWarning");Check(fix.Text=="Fix shortcut"&&fix.Height>=30,$"shortcut action has usable height at {width}");
    Check(((ComboBox)Field(form,"_screen")).Text=="Screen 1 — Main","screen selector avoids duplicate resolution");
    foreach(var field in new[]{"_full","_region"}){var button=(Control)Field(form,field);Check(TextRenderer.MeasureText(button.Text,button.Font,Size.Empty,TextFormatFlags.NoPadding).Width<=button.Width-16,$"capture caption fits at {width}: {button.Text}");}
   }
   Check(capture.Padding==action.Padding,$"recording and capture cards have equal padding at {width}");
   var stop=(Control)Field(form,"_stop");Check(stop.Text=="Stop",$"only Stop below timer at {width}");
   Check(!form.Controls.Find("PauseResumeButton",true).Any(),"separate Pause button removed");
   var output=form.Controls.Find("OutputBar",true).Single();var browse=output.Controls.Find("BrowseFolderButton",true).Single();var folder=output.Controls.Find("OpenFolderButton",true).Single();
   Check(TextRenderer.MeasureText(folder.Text,folder.Font).Width<=folder.Width-16,$"Open folder caption fits without truncation at {width}");
   Check(browse.Text=="Browse",$"Browse caption has no trailing dots at {width}");
   Point Center(Control c)=>form.PointToClient(c.PointToScreen(new Point(c.Width/2,c.Height/2)));
   var media=form.Controls.Find("MediaCard",true).Single();
   Check(Math.Abs(Center(folder).X-Center(action).X)<=8,$"Open folder centered below recording card at {width}");
   Check(Center(browse).X>form.PointToClient(media.PointToScreen(Point.Empty)).X&&Center(browse).X<form.PointToClient(media.PointToScreen(new Point(media.Width,0))).X,$"Browse below media card at {width}");
   var headings=new[]{"CAPTURE AREA","MEDIA INPUTS","SCREEN CAPTURE"};
   IEnumerable<Control> Descendants(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Descendants(child)));
   foreach(var title in Descendants(form).OfType<Label>().Where(l=>headings.Contains(l.Text)))Check(title.TextAlign==ContentAlignment.MiddleCenter,$"centered {title.Text}");
   Check(!Descendants(action).OfType<Label>().Any(l=>l.Text.Contains("H.264")),"codec hint removed from recording card");
   Check(browse.Width>=80&&folder.Width>=95&&stop.Width>=100,$"action labels have usable widths at {width}");
   Check(record.Parent!.BackColor==MainFormSurface(assembly),"recording background matches card surface");
  }
  if(args.Contains("--layout")){
   using var combo=(ComboBox)Activator.CreateInstance(assembly.GetType("InsightScreenRecorder.StyledComboBox")!)!;combo.Width=100;combo.Items.Add("Headphones (High Definition Audio Device — full device name)");combo.SelectedIndex=0;
   typeof(ComboBox).GetMethod("OnDropDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(combo,new object[]{EventArgs.Empty});
   Check(combo.DropDownWidth>=TextRenderer.MeasureText(combo.Text,combo.Font).Width+24,"opened device list expands to full item width");
   foreach(var button in form.Controls.Find("SourceDevice",true).OfType<ComboBox>())Check(button.Font.Size>=7.5f,"device font is not reduced");
  }
  var ticks=new List<int>();var watch=System.Diagnostics.Stopwatch.StartNew();
  var countdown=(Task)assembly.GetType("InsightScreenRecorder.RecordingCountdown")!.GetMethod("ShowAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{form,new Rectangle(0,0,640,360),3,(Action<int>)(number=>{ticks.Add(number);if(number==2){var f=Application.OpenForms.Cast<Form>().Single(f=>f.GetType().Name=="RecordingCountdown");using var picture=new Bitmap(f.Width,f.Height);f.DrawToBitmap(picture,f.ClientRectangle);picture.Save(Path.Combine(evidence,"countdown-2.png"));}})})!;
  while(!countdown.IsCompleted){Application.DoEvents();Thread.Sleep(10);}countdown.GetAwaiter().GetResult();
  Check(ticks.SequenceEqual(new[]{3,2,1})&&watch.Elapsed.TotalSeconds>=3,"visible countdown delivers 3,2,1 before completion");
  Check(!Application.OpenForms.Cast<Form>().Any(f=>f.GetType().Name=="RecordingCountdown"&&f.Visible),"countdown closes before capture can begin");
  if(args.Contains("--commercial"))CommercialChecks.Run(assembly,form,evidence);
  if(args.Contains("--live")){
   var engine=Field(form,"_engine");var engineType=engine.GetType();string media=Path.Combine(evidence,"controls-live-"+Guid.NewGuid().ToString("N")+".mp4");
   void WaitUntil(Func<bool> predicate){var timeout=System.Diagnostics.Stopwatch.StartNew();while(!predicate()){if(timeout.Elapsed.TotalSeconds>25)throw new TimeoutException("UI component transition timed out");Application.DoEvents();Thread.Sleep(10);}}
   void Await(Task task){WaitUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
   var captureSettings=Activator.CreateInstance(settingsType)!;
   Await((Task)engineType.GetMethod("StartAsync")!.Invoke(engine,new object?[]{Path.GetFullPath("tools/ffmpeg.exe"),media,15,0,0,320,240,23,0,true,2,false,captureSettings})!);
   typeof(MainForm).GetMethod("RefreshRecordingControls",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
   Check(record.Enabled&&record.Text=="Pause","main red circle is enabled as Pause during real capture");
   ((IButtonControl)record).PerformClick();WaitUntil(()=>Equals(engineType.GetProperty("IsPaused")!.GetValue(engine),true)&&record.Text=="Resume"&&record.Enabled&&!Equals(Field(form,"_recordingTransition"),true)&&((Control)Field(form,"_stateBadge")).Text.Contains("PAUSED"));
   Check(true,"red circle click actually pauses capture and becomes Resume");
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(evidence,"component-paused.png"));}
   ((IButtonControl)record).PerformClick();WaitUntil(()=>Equals(engineType.GetProperty("IsPaused")!.GetValue(engine),false)&&record.Text=="Pause"&&record.Enabled);
   Check(true,"same circle click actually resumes capture");
   ((IButtonControl)record).PerformClick();WaitUntil(()=>record.Text=="Resume"&&record.Enabled);
   ((IButtonControl)Field(form,"_stop")).PerformClick();WaitUntil(()=>File.Exists(media)&&record.Text=="REC"&&record.Enabled);
   Check(new FileInfo(media).Length>0&&!((Control)Field(form,"_stop")).Enabled,"Stop while paused saves and returns controls to idle");
   File.WriteAllText(Path.Combine(evidence,"component-live-output.txt"),media);
  }
 }
 static Color MainFormSurface(Assembly assembly)=>(Color)typeof(MainForm).GetField("Surface",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
}
