using System.Reflection;
using System.Diagnostics;

internal static class StabilityChecks
{
 const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 internal static void Run(Assembly assembly,string evidence){
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
  var settings=Activator.CreateInstance(assembly.GetType("InsightScreenRecorder.RecorderSettings")!)!;
  var audio=Activator.CreateInstance(typeof(List<>).MakeGenericType(assembly.GetType("InsightScreenRecorder.WindowsAudioCapture")!))!;
  var start=new ProcessStartInfo("ffmpeg");assembly.GetType("InsightScreenRecorder.RecordingCommand")!.GetMethod("Populate",Any)!.Invoke(null,new object?[]{start,"fixture.mp4",30,0,0,1920,1080,23,0,true,2,false,settings,null,audio});
  string Arg(string key){int index=start.ArgumentList.IndexOf(key);return index<0?"":start.ArgumentList[index+1];}
  Check(Arg("-threads:v")=="2"&&Arg("-filter_complex_threads")=="1","encoder and filter thread budgets are explicit");
  Check(Arg("-thread_queue_size")=="8","desktop raw frame queue is bounded to eight");
  var guardType=assembly.GetType("InsightScreenRecorder.RecordingPowerGuard")!;
  using(var guard=(IDisposable)Activator.CreateInstance(guardType,Any,null,Array.Empty<object>(),null)!){Check((bool)guardType.GetProperty("Active",Any)!.GetValue(guard)!,"native recording power request acquires without capture");guard.Dispose();Check(!(bool)guardType.GetProperty("Active",Any)!.GetValue(guard)!,"power request releases and repeated Dispose is harmless");}
  var journalType=assembly.GetType("InsightScreenRecorder.RecordingJournal")!;string log=Path.Combine(evidence,"journal-fixture-"+Guid.NewGuid().ToString("N")+".log");
  var journal=Activator.CreateInstance(journalType,Any,null,new object[]{log},null)!;journalType.GetMethod("Write",Any)!.Invoke(journal,new object[]{"START fixture"});journalType.GetMethod("Write",Any)!.Invoke(journal,new object[]{"out_time_us=100000"});
  Check(File.ReadAllText(log).Contains("START fixture")&&File.ReadAllText(log).Contains("out_time_us=100000"),"journal persists state and progress before finalization");
  long bytes=new FileInfo(log).Length;foreach(string line in new[]{"bitrate=   4.5kbits/s","speed= 1.0x","frame=10","out_time_us=200000"})journalType.GetMethod("Write",Any)!.Invoke(journal,new object[]{line});Check(new FileInfo(log).Length==bytes,"padded progress lines do not bypass two-second sampling");
 }
}
