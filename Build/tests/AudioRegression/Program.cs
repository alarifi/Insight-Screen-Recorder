using InsightScreenRecorder;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

string root = Path.GetFullPath(args[0]);
string evidence = Path.Combine(root, "docs/evidence/audio-regression-133");
Directory.CreateDirectory(evidence);
var report = new List<string>();
void Log(string message) { Console.WriteLine(message); report.Add(message); File.WriteAllLines(Path.Combine(evidence,"results.txt"),report); }
async Task<string> Run(string tool, params string[] arguments) {
    var info = new ProcessStartInfo(Path.Combine(root,"tools",tool)) { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true };
    foreach (var arg in arguments) info.ArgumentList.Add(arg);
    using var process = Process.Start(info)!;
    var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    string output=await stdout+await stderr;
    if(process.ExitCode!=0) throw new Exception(output);
    return output;
}
var before=WindowsAudioCapture.ReadEndpointState(true);
Log("Playback before: "+before);
Log("Microphone: "+WindowsAudioCapture.ReadEndpointState(false));
if(args.Contains("--unmute-playback")) {
    // Explicit maintenance action, never done at app startup or during recording.
    var outer=typeof(WindowsAudioCapture); var flags=BindingFlags.NonPublic;
    object enumerator=Activator.CreateInstance(outer.GetNestedType("MMDeviceEnumerator",flags)!)!;
    object? endpoint=null, volume=null;
    try {
        object?[] endpointArgs={0,1,null};
        var et=outer.GetNestedType("IMMDeviceEnumerator",flags)!;
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR((int)et.GetMethod("GetDefaultAudioEndpoint")!.Invoke(enumerator,endpointArgs)!);
        endpoint=endpointArgs[2]!;
        object?[] activation={new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),(uint)23,IntPtr.Zero,null};
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR((int)outer.GetNestedType("IMMDevice",flags)!.GetMethod("Activate")!.Invoke(endpoint,activation)!);
        volume=activation[3]!;
        object?[] muteArgs={false,Guid.Empty};
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR((int)outer.GetNestedType("IAudioEndpointVolume",flags)!.GetMethod("SetMute")!.Invoke(volume,muteArgs)!);
    } finally {
        foreach(var obj in new[]{volume,endpoint,enumerator}) if(obj!=null)System.Runtime.InteropServices.Marshal.ReleaseComObject(obj);
    }
    var after=WindowsAudioCapture.ReadEndpointState(true);
    Log("Playback after explicit unmute: "+after);
    if(after.Muted || Math.Abs(after.Volume-before.Volume)>.0001)throw new Exception("Unmute failed or changed volume");
}
foreach(var file in Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Insight Screen Recorder"),"*.mp4").OrderByDescending(File.GetLastWriteTimeUtc).Take(3)) {
    string probe=await Run("ffprobe.exe","-v","error","-show_streams","-of","json",file);
    using var json=JsonDocument.Parse(probe);
    if(!json.RootElement.GetProperty("streams").EnumerateArray().Any(s=>s.GetProperty("codec_type").GetString()=="audio"))throw new Exception("Audio track missing: "+file);
    string stats=await Run("ffmpeg.exe","-hide_banner","-i",file,"-vn","-af","volumedetect","-f","null","NUL");
    Log(Path.GetFileName(file)+": "+string.Join("; ",stats.Split('\n').Where(l=>l.Contains("mean_volume:")||l.Contains("max_volume:")).Select(l=>l.Trim())));
    File.WriteAllText(Path.Combine(evidence,Path.GetFileNameWithoutExtension(file)+".json"),probe);
}
// A real output tone through the current Windows output and both native input
// sources verifies the actual recorder graph rather than stream metadata alone.
string tone=Path.Combine(evidence,"tone.wav");
using(var writer=new BinaryWriter(File.Create(tone))) {
    int samples=48000*3;
    writer.Write("RIFF"u8); writer.Write(36+samples*2); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples*2);
    for(int i=0;i<samples;i++)writer.Write((short)(3276*Math.Sin(2*Math.PI*440*i/48000)));
}
string outputFile=Path.Combine(evidence,"native-headset-"+DateTime.Now.ToString("HHmmss")+".mp4");
using(var engine=new RecordingEngine()) {
    await engine.StartAsync(Path.Combine(root,"tools/ffmpeg.exe"),outputFile,15,0,0,640,360,mediaSettings:new RecorderSettings{SystemAudioEnabled=true,MicrophoneEnabled=true,AudioBitrate=320});
    await Task.Delay(1500);
    await Task.Run(()=>{using var player=new System.Media.SoundPlayer(tone);player.PlaySync();});
    await Task.Delay(1500);
    var sources=(List<WindowsAudioCapture>)typeof(RecordingEngine).GetField("_audio",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine)!;
    foreach(var source in sources) {
        Log($"Native {source.SampleRate}Hz {source.Channels}ch: packets={source.CapturedPackets}, maxPeak={source.MaxPeakLevel}, error={source.Error ?? "none"}");
        if(source.CapturedPackets==0||source.Error!=null)throw new Exception("Native capture failure");
    }
    if(sources[0].MaxPeakLevel<.01)throw new Exception("Playback test tone not present at native input");
    await engine.StopAsync();
    File.WriteAllText(Path.Combine(evidence,"engine.log"),engine.Diagnostics);
}
string actualStats=await Run("ffmpeg.exe","-hide_banner","-i",outputFile,"-vn","-af","volumedetect","-f","null","NUL");
Log("Encoded file: "+string.Join("; ",actualStats.Split('\n').Where(l=>l.Contains("mean_volume:")||l.Contains("max_volume:")).Select(l=>l.Trim())));
var match=System.Text.RegularExpressions.Regex.Match(actualStats,@"max_volume: (-?[\d.]+) dB");
if(!match.Success || double.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)<-25)throw new Exception("Test tone too quiet in encoded AAC");
Log("PASS actual WASAPI microphone packets + system tone in encoded AAC; playback unmuted. Spoken voice quality requires the user's speech test.");
