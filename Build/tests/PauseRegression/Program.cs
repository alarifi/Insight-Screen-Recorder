using InsightScreenRecorder;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Reflection;

string root=Path.GetFullPath(args[0]);string evidence=Path.Combine(root,args.FirstOrDefault(a=>a.StartsWith("--evidence="))?[11..]??"docs/evidence/pause-20261123");Directory.CreateDirectory(evidence);
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
Check(ScreenNames.Label(1,true,1920,1080)=="Screen 1 — Main screen · 1920 × 1080","main screen friendly name");
Check(ScreenNames.Label(2,false,2560,1440)=="Screen 2 · 2560 × 1440","second screen friendly name");
Check(!ScreenNames.Label(1,true,1920,1080).Contains(@"\\.\"),"no Windows device prefix in user label");
using(var idle=new RecordingSession()){
 Check(!idle.IsRecording&&!idle.IsPaused,"initial state");
 await idle.StopAsync();
 await idle.PauseAsync();
 Check(!idle.IsPaused,"idle pause is harmless");
}
if(args.Contains("--devices"))Console.WriteLine(JsonSerializer.Serialize(new{screens=Screen.AllScreens.Select((s,i)=>new{label=ScreenNames.Label(i+1,s.Primary,s.Bounds.Width,s.Bounds.Height)}),microphones=WindowsAudioCapture.EnumerateDevices(false).Count,speakers=WindowsAudioCapture.EnumerateDevices(true).Count,cameras=MediaFoundationCamera.EnumerateDevices().Count}));
if(!args.Contains("--live"))return;
string ffmpeg=Path.Combine(root,"tools/ffmpeg.exe"),probe=Path.Combine(root,"tools/ffprobe.exe");
if(args.Contains("--collision")){
 string target=Path.Combine(evidence,"collision-"+Guid.NewGuid().ToString("N")+".mp4");using var collision=new RecordingSession();int callbacks=0;
 collision.Finished+=(_,code)=>{Check(code!=0,"collision reports a failed finalization");callbacks++;};
 await collision.StartAsync(ffmpeg,target,15,0,0,320,240);await Task.Delay(1000);await collision.PauseAsync();
 File.WriteAllText(target,"KEEP EXISTING FILE");bool rejected=false;try{await collision.StopAsync();}catch(IOException){rejected=true;}
 Check(rejected&&callbacks==1,"destination collision rejects finalization exactly once");Check(File.ReadAllText(target)=="KEEP EXISTING FILE","existing destination remains unchanged");
 string parts=(string)typeof(RecordingSession).GetField("_directory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(collision)!;
 Check(Directory.GetFiles(parts,"part*.mp4").Any(p=>new FileInfo(p).Length>0),"collision preserves recoverable recording parts");return;
}
if(args.Contains("--failure")){
 var failure=new RecordingSession();var ended=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);failure.Finished+=(_,code)=>ended.TrySetResult(code);
 await failure.StartAsync(ffmpeg,Path.Combine(evidence,"unexpected-exit-"+Guid.NewGuid().ToString("N")+".mp4"),15,0,0,320,240);
 await Task.Delay(500);
 var segment=(RecordingEngine)typeof(RecordingSession).GetField("_part",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(failure)!;
 var child=(Process)typeof(RecordingEngine).GetField("_process",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(segment)!;child.Kill();
 try {Check(await ended.Task.WaitAsync(TimeSpan.FromSeconds(5))!=0,"unexpected encoder exit reports failure without deadlock");}
 catch(TimeoutException){Console.Error.WriteLine("FAIL unexpected encoder exit deadlocked before Finished");Environment.Exit(2);}
 Check(!failure.IsRecording,"unexpected exit clears recording state");failure.Dispose();return;
}
if(args.Contains("--rapid")){
 string path=Path.Combine(evidence,"rapid-"+Guid.NewGuid().ToString("N")+".mp4");using var rapid=new RecordingSession();
 await rapid.StartAsync(ffmpeg,path,15,0,0,320,240);await rapid.PauseAsync();Check(rapid.IsPaused,"pause immediately after start is safe");
 await rapid.ResumeAsync();await Task.Delay(1000);await rapid.StopAsync();Check(File.Exists(path),"rapid pause/resume produces one recording");return;
}
async Task<string> Run(string exe,params string[] arguments){
 using var p=new Process{StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
 foreach(var a in arguments)p.StartInfo.ArgumentList.Add(a);p.Start();var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));string output=await stdout;string error=await stderr;if(p.ExitCode!=0)throw new Exception(error);return output;
}
var settings=new RecorderSettings{CountdownSeconds=0,Fps=15,VideoFormat=args.Contains("--mkv")?"MKV":"MP4"};
if(args.Contains("--audio"))settings.SystemAudioEnabled=true;
if(args.Contains("--microphone"))settings.MicrophoneEnabled=true;
if(args.Contains("--camera"))settings.CameraEnabled=true;
using var marker=args.Contains("--markers")?new ScreenMarkerFixture():null;
string Tone(int frequency,int seconds){
 string path=Path.Combine(evidence,$"tone-{frequency}.wav");using var w=new BinaryWriter(File.Create(path));int samples=48000*seconds;
 w.Write("RIFF"u8);w.Write(36+samples*2);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(48000);w.Write(96000);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(samples*2);
 for(int i=0;i<samples;i++)w.Write((short)(3276*Math.Sin(2*Math.PI*frequency*i/48000)));return path;
}
RecordingSession? meterSession=null;float maxSystemMeter=0;bool pausedMetersZero=true;
async Task Stage(int frequency,int seconds){if(marker!=null&&settings.SystemAudioEnabled){using var player=new System.Media.SoundPlayer(Tone(frequency,seconds));var playing=Task.Run(player.PlaySync);while(!playing.IsCompleted){var levels=meterSession?.AudioLevels??(0f,0f);if(frequency==880)pausedMetersZero &= levels.Item1==0&&levels.Item2==0;else maxSystemMeter=Math.Max(maxSystemMeter,levels.Item1);await Task.Delay(25);}await playing;}else await Task.Delay(seconds*1000);}
string output=Path.Combine(evidence,"session-"+DateTime.Now.ToString("HHmmssfff")+"."+settings.VideoFormat.ToLowerInvariant());
var wall=Stopwatch.StartNew();DateTime started=DateTime.UtcNow;int finished=0;int exit=-999;
double activeSeconds=0;
using(var session=new RecordingSession()){
 meterSession=session;
 session.Finished+=(_,code)=>{Interlocked.Increment(ref finished);exit=code;};
 await session.StartAsync(ffmpeg,output,15,0,0,640,360,mediaSettings:settings);
 Check(session.IsRecording&&!session.IsPaused,"recording state");await Stage(440,2);await Task.Delay(400);
 await session.PauseAsync();Check(session.IsRecording&&session.IsPaused,"pause keeps the same logical recording");Check(finished==0,"pause does not publish a finished recording");
 await session.PauseAsync();var pausedTime=session.Elapsed;marker?.Color(Color.Lime);await Stage(880,3);Check(session.Elapsed==pausedTime,"active timer is frozen during pause");Check(!File.Exists(output),"pause does not publish an incomplete final file");
 marker?.Color(Color.Blue);await session.ResumeAsync();await session.ResumeAsync();Check(!session.IsPaused,"resume is idempotent");await Stage(660,2);await Task.Delay(400);
 await session.PauseAsync();Check(finished==0,"second pause still does not finalize");await session.StopAsync();await session.StopAsync();
 Check(finished==1&&exit==0,"stop while paused finalizes exactly once");Check(!session.IsRecording&&!session.IsPaused,"stopped state");
 activeSeconds=session.Elapsed.TotalSeconds;
 if(marker!=null&&settings.SystemAudioEnabled){Check(maxSystemMeter>.01f,"real recording source drives system audio meter");Check(pausedMetersZero&&session.AudioLevels==(0f,0f),"paused and stopped source meters are zero");}
 File.WriteAllText(Path.Combine(evidence,Path.GetFileNameWithoutExtension(output)+".log"),session.Diagnostics);
}
string json=await Run(probe,"-v","error","-show_streams","-show_format","-of","json",output);File.WriteAllText(output+".json",json);
using var doc=JsonDocument.Parse(json);double duration=double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
Check(duration>3&&duration<wall.Elapsed.TotalSeconds-2,"saved duration excludes the 3-second pause");
Check(Math.Abs(activeSeconds-duration)<.25,$"active timer agrees with saved media ({activeSeconds:F3}/{duration:F3})");
Check(doc.RootElement.GetProperty("streams").EnumerateArray().Any(s=>s.GetProperty("codec_type").GetString()=="video"),"video track exists");
if(settings.SystemAudioEnabled)Check(doc.RootElement.GetProperty("streams").EnumerateArray().Any(s=>s.GetProperty("codec_type").GetString()=="audio"),"audio track survives pause/resume");
await Run(ffmpeg,"-v","error","-i",output,"-f","null","NUL");Check(true,"entire final recording decodes without errors");
if(marker!=null){
 string pixels=output+".rgb";await Run(ffmpeg,"-v","error","-i",output,"-vf","crop=16:16:2:2,scale=1:1","-pix_fmt","rgb24","-f","rawvideo",pixels);byte[] rgb=File.ReadAllBytes(pixels);int red=0,blue=0,green=0;
 for(int i=0;i+2<rgb.Length;i+=3){if(rgb[i]>rgb[i+1]+60&&rgb[i]>rgb[i+2]+60)red++;if(rgb[i+2]>rgb[i]+60&&rgb[i+2]>rgb[i+1]+60)blue++;if(rgb[i+1]>rgb[i]+60&&rgb[i+1]>rgb[i+2]+60)green++;}
 Check(red>10&&blue>10&&green==0,$"pre/post frames present and paused green frames absent ({red}/{blue}/{green})");
 if(settings.SystemAudioEnabled){
  string pcm=output+".f32";await Run(ffmpeg,"-v","error","-i",output,"-vn","-ac","1","-ar","16000","-f","f32le",pcm);byte[] raw=File.ReadAllBytes(pcm);int count=raw.Length/4;
  double Amplitude(int hz){double re=0,im=0;for(int i=0;i<count;i++){double sample=BitConverter.ToSingle(raw,i*4);re+=sample*Math.Cos(2*Math.PI*hz*i/16000);im+=sample*Math.Sin(2*Math.PI*hz*i/16000);}return 2*Math.Sqrt(re*re+im*im)/count;}
  double a=Amplitude(440),b=Amplitude(660),gap=Amplitude(880);File.WriteAllText(output+".tones.json",JsonSerializer.Serialize(new{pre_440=a,post_660=b,paused_880=gap}));
  Check(a>.003&&b>.003&&gap<Math.Min(a,b)*.2,$"actual pre/post audio tones present; paused tone excluded ({a:F5}/{b:F5}/{gap:F5})");
 }
}
File.WriteAllText(output+".evidence.json",JsonSerializer.Serialize(new{start_utc=started,end_utc=DateTime.UtcNow,wall_seconds=wall.Elapsed.TotalSeconds,active_seconds=activeSeconds,video_seconds=duration,finished,exit,settings.SystemAudioEnabled,settings.MicrophoneEnabled,settings.CameraEnabled,output},new JsonSerializerOptions{WriteIndented=true}));

sealed class ScreenMarkerFixture : IDisposable
{
 private readonly Thread thread;private readonly Form form;
 public ScreenMarkerFixture(){var ready=new TaskCompletionSource<Form>(TaskCreationOptions.RunContinuationsAsynchronously);thread=new Thread(()=>{var f=new Form{Text="Insight recording test",FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=Point.Empty,ClientSize=new Size(640,360),BackColor=System.Drawing.Color.Red,TopMost=true,ShowInTaskbar=false};bool prepared=false;f.Shown+=(_,_)=>{if(prepared)return;prepared=true;f.BeginInvoke(()=>{f.Hide();f.Show();f.BringToFront();ready.TrySetResult(f);});};Application.Run(f);});thread.SetApartmentState(ApartmentState.STA);thread.Start();form=ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();}
 public void Color(Color color)=>form.Invoke(()=>{form.BackColor=color;form.Refresh();});
 public void Dispose(){if(!form.IsDisposed)form.Invoke(form.Close);thread.Join(5000);}
}
