using System.Diagnostics;
using System.Globalization;
namespace InsightScreenRecorder;
internal static class RecordingCommand
{
    internal static void Populate(ProcessStartInfo psi, string output, int fps, int x, int y, int width, int height, int crf, int outputWidth, bool cursor, int keyframeSeconds, bool cfr, RecorderSettings settings, MediaFoundationCamera? camera, IReadOnlyList<WindowsAudioCapture> audio)
    {
        void Add(params string[] args) { foreach(var a in args) psi.ArgumentList.Add(a); }
        string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        Add("-hide_banner","-loglevel","warning","-n","-filter_threads","1","-filter_complex_threads","1","-thread_queue_size","8","-probesize","32","-analyzeduration","0","-f","gdigrab","-framerate",N(fps),"-offset_x",N(x),"-offset_y",N(y),"-video_size",$"{width}x{height}","-draw_mouse",cursor?"1":"0","-i","desktop");
        int index=1; int cameraIndex=-1;
        if(camera != null) { cameraIndex=index++; Add("-thread_queue_size","4","-f","rawvideo","-pixel_format","bgra","-video_size",$"{camera.Width}x{camera.Height}","-framerate",N(camera.FramesPerSecond),"-probesize","32","-analyzeduration","0","-i",camera.PipePath); }
        var audioIndices = new List<int>();
        foreach(var source in audio) { audioIndices.Add(index++); Add("-thread_queue_size","128","-f",source.SampleFormat,"-ar",N(source.SampleRate),"-ac",N(source.Channels),"-probesize","32","-analyzeduration","0","-i",source.PipePath); }
        var filters = new List<string>(); string video="0:v";
        if(camera != null) {
            int overlayWidth=Math.Max(2,Math.Min(width*settings.CameraWidthPercent/100, height*camera.Width/camera.Height)/2*2);
            filters.Add($"[{cameraIndex}:v]"+(settings.CameraMirror?"hflip,":"")+$"scale={overlayWidth}:-2[cam]");
            string ox=settings.CameraPosition.Contains("right")?"max(0,main_w-overlay_w-16)":"min(16,max(0,main_w-overlay_w))";
            string oy=settings.CameraPosition.Contains("Bottom")?"max(0,main_h-overlay_h-16)":"min(16,max(0,main_h-overlay_h))";
            filters.Add($"[0:v][cam]overlay=x=\'{ox}\':y=\'{oy}\':eof_action=pass[overlay]"); video="overlay";
        }
        if(outputWidth>0) { filters.Add($"[{video}]scale={outputWidth}:-2[scaled]"); video="scaled"; }
        if(audio.Count>0) {
            string inputs=string.Concat(audioIndices.Select(i=>$"[{i}:a]"));
            filters.Add(inputs+(audio.Count>1?$"amix=inputs={audio.Count}:duration=longest:dropout_transition=0:normalize=0,":"")+$"aresample={settings.AudioSampleRate}:async=1:first_pts=0[audio]");
        }
        if(filters.Count>0) Add("-filter_complex",string.Join(';',filters));
        Add("-map",video=="0:v"?video:$"[{video}]");
        if(audio.Count>0) Add("-map","[audio]","-c:a","aac","-b:a",N(settings.AudioBitrate)+"k","-ar",N(settings.AudioSampleRate),"-ac",N(settings.AudioChannels)); else Add("-an");
        Add("-c:v","libx264","-threads:v","2","-preset","ultrafast","-crf",N(crf),"-g",N(fps*keyframeSeconds),"-pix_fmt","yuv420p","-fps_mode",cfr?"cfr":"vfr");
        if(Path.GetExtension(output).Equals(".mp4",StringComparison.OrdinalIgnoreCase)) Add("-movflags","+faststart");
        Add(output);
    }
}

