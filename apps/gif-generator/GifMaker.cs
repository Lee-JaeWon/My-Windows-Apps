using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Reflection;

[assembly: AssemblyTitle("GIF Generator")]
[assembly: AssemblyProduct("GIF Generator")]
[assembly: AssemblyDescription("MP4 to GIF and MP4 speed converter")]
[assembly: AssemblyVersion("1.6.0.0")]

static class GUi {
    public static readonly Color Background=Color.FromArgb(14,18,16), Surface=Color.FromArgb(27,35,30), Surface2=Color.FromArgb(35,46,39), Text=Color.FromArgb(244,247,245), Muted=Color.FromArgb(158,171,162), Accent=Color.FromArgb(113,190,126);
    public static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static void Button(Button b,Color color){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=color;b.ForeColor=Text;b.Cursor=Cursors.Hand;using(var p=Round(new Rectangle(0,0,b.Width,b.Height),10))b.Region=new Region(p);b.Resize+=delegate{using(var p=Round(new Rectangle(0,0,b.Width,b.Height),10))b.Region=new Region(p);};}
}
class GRoundedPanel : Panel {
    public GRoundedPanel(){DoubleBuffered=true;BackColor=GUi.Surface;Resize+=delegate{using(var p=GUi.Round(new Rectangle(0,0,Width,Height),16))Region=new Region(p);};}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=GUi.Round(new Rectangle(0,0,Width-1,Height-1),16))using(var b=new SolidBrush(GUi.Surface))e.Graphics.FillPath(b,p);}
}
class GProgressBar : Control {
    int value,maximum=100;public int Maximum {get{return maximum;}set{maximum=Math.Max(1,value);Invalidate();}}public int Value {get{return value;}set{this.value=Math.Max(0,Math.Min(maximum,value));Invalidate();}}
    public GProgressBar(){DoubleBuffered=true;BackColor=GUi.Background;}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2;using(var p=new Pen(GUi.Surface2,6)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,4,y,Width-4,y);}if(value>0)using(var p=new Pen(GUi.Accent,6)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,4,y,4+(Width-8)*value/maximum,y);}}
}

class Engine {
    public volatile bool Cancelled;
    public Action<string,int> Status = delegate {};
    public long Limit = 50000000;
    const int MaxFrames = 999;
    string Bin(string name) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", name + ".exe"); }
    static string Q(string s) { return "\"" + s + "\""; }
    static string N(double n) { return n.ToString("0.############", CultureInfo.InvariantCulture); }
    int CountFrames(string path) {
        string count = Run("ffprobe", "-v error -select_streams v:0 -count_frames -show_entries stream=nb_read_frames -of default=noprint_wrappers=1:nokey=1 " + Q(path), null);
        int frames;
        if(!int.TryParse(count.Trim(), out frames) || frames < 1) throw new Exception("GIF 프레임 수를 확인할 수 없습니다.");
        return frames;
    }
    void Check() { if (Cancelled) throw new OperationCanceledException(); }
    string Run(string tool, string args, Action<string> line) {
        Check(); var output = new StringBuilder(); var errors = new StringBuilder();
        using (var p = new Process()) {
            p.StartInfo = new ProcessStartInfo(Bin(tool), args) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if(e.Data!=null) { lock(output) output.AppendLine(e.Data); if(line!=null) line(e.Data); } };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if(e.Data!=null) lock(errors) { if(errors.Length>12000) errors.Remove(0,6000); errors.AppendLine(e.Data); } };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            while(!p.WaitForExit(100)) { if(Cancelled) { try { p.Kill(); } catch {} p.WaitForExit(); Check(); } }
            p.WaitForExit(); Check();
            if(p.ExitCode!=0) throw new Exception("영상 처리에 실패했습니다.\n" + errors.ToString());
        } return output.ToString();
    }
    public string ChangeSpeed(string input, string output, double speed) {
        if(double.IsNaN(speed) || double.IsInfinity(speed) || speed<0.25 || speed>16) throw new Exception("배속은 0.25~16 사이로 선택해 주세요.");
        if(!File.Exists(input)) throw new Exception("MP4 파일을 찾을 수 없습니다.");
        if(File.Exists(output)) throw new Exception("같은 이름의 파일이 있습니다. 다른 이름으로 저장해 주세요.");
        Status("영상과 소리 정보를 확인하고 있습니다…",0);
        string json=Run("ffprobe","-v error -show_entries stream=codec_type,avg_frame_rate,duration:format=duration -of json "+Q(input),null);
        var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
        var streams=(System.Collections.ArrayList)root["streams"];
        bool hasVideo=false, hasAudio=false; double duration=0, fps=30;
        foreach(Dictionary<string,object> stream in streams) {
            if(stream["codec_type"].ToString()=="audio") hasAudio=true;
            if(stream["codec_type"].ToString()=="video" && !hasVideo) {
                hasVideo=true;
                if(stream.ContainsKey("duration")) double.TryParse(stream["duration"].ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
                if(stream.ContainsKey("avg_frame_rate")) {
                    string[] parts=stream["avg_frame_rate"].ToString().Split('/'); double a,b;
                    if(parts.Length==2 && double.TryParse(parts[0],out a) && double.TryParse(parts[1],out b) && b>0 && a>0) fps=a/b;
                }
            }
        }
        if(!hasVideo) throw new Exception("영상이 없는 파일입니다.");
        var format=(Dictionary<string,object>)root["format"];
        if(duration<=0 && format.ContainsKey("duration")) double.TryParse(format["duration"].ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(duration<=0) throw new Exception("영상 길이를 확인할 수 없습니다.");
        fps=Math.Max(1,Math.Min(60,fps));
        var tempo=new System.Collections.Generic.List<string>(); double remaining=speed;
        while(remaining>2) { tempo.Add("atempo=2"); remaining/=2; }
        while(remaining<0.5) { tempo.Add("atempo=0.5"); remaining/=0.5; }
        tempo.Add("atempo="+N(remaining));
        double expected=duration/speed;
        string staging=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            string video="setpts=(PTS-STARTPTS)/"+N(speed)+",fps="+N(fps)+",pad=ceil(iw/2)*2:ceil(ih/2)*2";
            string args="-hide_banner -loglevel error -y -threads 2 -i "+Q(input)+" -map 0:v:0 "+(hasAudio?"-map 0:a:0 ":"")+"-vf "+Q(video)+" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -threads 2 ";
            args+=hasAudio?"-af "+Q("asetpts=PTS-STARTPTS,"+string.Join(",",tempo.ToArray()))+" -c:a aac -b:a 192k ":"-an ";
            args+="-map_metadata -1 -map_chapters -1 -movflags +faststart -progress pipe:1 -nostats -f mp4 "+Q(staging);
            Run("ffmpeg",args,delegate(string line) {
                double us; if(line.StartsWith("out_time_us=") && double.TryParse(line.Substring(12),out us))
                    Status(N(speed)+"배속 MP4 생성 중…",Math.Max(0,Math.Min(99,(int)(us/1000000/expected*100))));
            });
            Check();
            string probe=Run("ffprobe","-v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 "+Q(staging),null);
            if(probe.Trim()!="h264" || new FileInfo(staging).Length==0) throw new Exception("결과 MP4 검증에 실패했습니다.");
            Check(); File.Move(staging,output);
            string summary=N(speed)+"배속 · "+(new FileInfo(output).Length/1000000.0).ToString("0.00")+" MB";
            Status("완료 · "+summary,100); return summary;
        } finally { if(File.Exists(staging)) File.Delete(staging); }
    }
    public string Convert(string input, string output) {
        if(!File.Exists(input)) throw new Exception("MP4 파일을 찾을 수 없습니다.");
        if(File.Exists(output)) throw new Exception("같은 이름의 결과 파일이 이미 있습니다. 다른 이름을 선택하세요.");
        Status("영상 정보를 확인하고 있습니다…",0);
        string json=Run("ffprobe", "-v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate,duration:format=duration -of json " + Q(input),null);
        var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
        var streams=(System.Collections.ArrayList)root["streams"];
        if(streams.Count==0) throw new Exception("영상이 없는 파일입니다.");
        var stream=(Dictionary<string,object>)streams[0];
        int width=System.Convert.ToInt32(stream["width"]), height=System.Convert.ToInt32(stream["height"]);
        double duration=0; var fmt=(Dictionary<string,object>)root["format"];
        if(fmt.ContainsKey("duration")) double.TryParse(fmt["duration"].ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(duration<=0 && stream.ContainsKey("duration")) double.TryParse(stream["duration"].ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(duration<=0) throw new Exception("영상 길이를 확인할 수 없습니다.");
        double sourceFps=30; string[] fraction=stream["avg_frame_rate"].ToString().Split('/');
        if(fraction.Length==2) { double a,b; if(double.TryParse(fraction[0],out a)&&double.TryParse(fraction[1],out b)&&b>0) sourceFps=a/b; }
        sourceFps=Math.Max(1,Math.Min(30,sourceFps));
        // Leave one frame of margin for timebase rounding; do not truncate the video.
        double frameFpsCap=(MaxFrames-1)/duration;
        string temp=Path.Combine(Path.GetTempPath(),"Gif50-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        string candidate=Path.Combine(temp,"candidate.gif"), best=Path.Combine(temp,"best.gif"), palette=Path.Combine(temp,"palette.png");
        double q=1, low=0, high=1; int refinement=0; string bestSettings="";
        try {
            for(int attempt=1; attempt<=18; attempt++) {
                Check();
                int edge=Math.Max(2,(int)Math.Round(Math.Max(width,height)*q));
                double fps=Math.Min(frameFpsCap,Math.Max(1,sourceFps*Math.Sqrt(q)));
                // Use the display aspect ratio after FFmpeg's automatic rotation.
                string scale="scale=w='if(gte(dar,1),"+edge+",max(2,trunc("+edge+"*dar)))':h='if(gte(dar,1),max(2,trunc("+edge+"/dar)),"+edge+")':flags=lanczos,setsar=1";
                string filters="fps="+N(fps)+","+scale;
                string label=attempt+"차 최적화 · 긴 변 "+edge+"px · "+fps.ToString("0.###")+"fps";
                Status(label+" | 색상 분석",0);
                Action<string> progress=delegate(string line) { if(line.StartsWith("out_time_us=")) { double us; if(double.TryParse(line.Substring(12),out us)) Status(label+" | 변환 중",Math.Min(99,(int)(us/1000000/duration*100))); } };
                Run("ffmpeg","-hide_banner -loglevel error -y -threads 2 -i "+Q(input)+" -vf "+Q(filters+",palettegen=stats_mode=diff")+" -frames:v 1 -threads 1 -update 1 "+Q(palette),null);
                Status(label+" | GIF 생성",0);
                Run("ffmpeg","-hide_banner -loglevel error -y -threads 2 -i "+Q(input)+" -i "+Q(palette)+" -filter_complex_threads 1 -filter_complex "+Q("[0:v:0]"+filters+"[v];[v][1:v]paletteuse=dither=sierra2_4a:diff_mode=rectangle")+" -an -loop 0 -progress pipe:1 -nostats "+Q(candidate),progress);
                long size=new FileInfo(candidate).Length;
                Status(label+" | 용량·프레임 수 확인",99);
                int frames=CountFrames(candidate);
                if(frames>MaxFrames) {
                    // Container duration can be inaccurate. Retry at a lower rate using the actual count.
                    frameFpsCap=fps*(MaxFrames-1)/frames*0.995;
                    continue;
                }
                if(size<=Limit) {
                    File.Copy(candidate,best,true); low=q; bestSettings=edge+"px · "+fps.ToString("0.###")+"fps · "+frames+"프레임 · "+(size/1000000.0).ToString("0.00")+" MB";
                    if(q>=0.999 || ++refinement>=4 || high-low<0.015) break;
                    q=(low+high)/2;
                } else {
                    high=q;
                    if(low>0) { if(++refinement>=4) break; q=(low+high)/2; }
                    else q=Math.Max(0.0001,q*Math.Min(0.85,Math.Pow((double)Limit/size*0.94,0.42)));
                }
            }
            Check(); if(!File.Exists(best)) throw new Exception("50MB 이하·999프레임 이하로 변환하지 못했습니다. 더 짧은 영상을 사용해 주세요.");
            if(new FileInfo(best).Length>Limit) throw new Exception("파일 크기 검증에 실패했습니다.");
            if(CountFrames(best)>MaxFrames) throw new Exception("프레임 수 검증에 실패했습니다.");
            // Copy to a unique sibling, then rename so an interrupted copy is never a finished GIF.
            string staging=output+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { File.Copy(best,staging,false); Check(); File.Move(staging,output); } finally { if(File.Exists(staging)) File.Delete(staging); }
            Status("완료 · "+bestSettings,100); return bestSettings;
        } finally { try { Directory.Delete(temp,true); } catch {} }
    }
}

class MainForm : Form {
    Label pathLabel, status, detail; Button select, start, cancel, open, reset; GProgressBar bar;
    string result,outputDirectory; Engine engine; bool busy,cancelRequested;
    readonly List<string> inputs=new List<string>();ListBox queue;
    Panel gifPage,speedPage;Button gifTabButton,speedTabButton;SpeedPanel speedPanel;int activeTab;
    Color bg=GUi.Background, panel=GUi.Surface2, muted=GUi.Muted;
    public MainForm() {
        Text="GIF Generator"; ClientSize=new Size(660,620); MinimumSize=MaximumSize=Size; FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen; BackColor=bg; ForeColor=GUi.Text; Font=new Font("맑은 고딕",10); AutoScaleMode=AutoScaleMode.Dpi;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Label title=LabelAt("GIF Generator",28,22,440,48,26,Color.White); title.Font=new Font(Font.FontFamily,26,FontStyle.Bold);
        Label credit=LabelAt("Created by jw",480,34,150,20,9,Color.White); credit.TextAlign=ContentAlignment.MiddleRight; credit.BringToFront();
        LabelAt("GIF 만들기와 MP4 배속 변환을 한곳에서",30,76,600,28,12,muted);
        var drop=new GRoundedPanel { Location=new Point(28,122),Size=new Size(604,112),AllowDrop=true }; Controls.Add(drop);
        pathLabel=new Label { Text="MP4 파일을 여기에 끌어 놓으세요",Location=new Point(18,20),Size=new Size(440,70),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,ForeColor=Color.White }; drop.Controls.Add(pathLabel);
        select=ButtonAt("파일 선택",460,34,120,42,false); drop.Controls.Add(select); select.Click+=delegate { using(var d=new OpenFileDialog {Filter="MP4 동영상|*.mp4",Title="GIF로 만들 MP4 선택",Multiselect=true}) if(d.ShowDialog()==DialogResult.OK) SetInputs(d.FileNames); }; select.BringToFront();
        AllowDrop=true; DragEnter+=EnterFile; DragDrop+=DropFile; drop.DragEnter+=EnterFile; drop.DragDrop+=DropFile;
        detail=LabelAt("50MB 이하 · 최대 999프레임 (Google Drive 전용)",30,246,600,25,10,muted);
        LabelAt("원본 길이 유지 · 무한 반복",30,270,600,23,9,muted);
        queue=new ListBox {Location=new Point(30,294),Size=new Size(600,82),BackColor=GUi.Surface,ForeColor=GUi.Text,BorderStyle=BorderStyle.None,IntegralHeight=false,HorizontalScrollbar=true};Controls.Add(queue);
        status=LabelAt("파일을 선택하면 시작할 수 있습니다.",30,386,600,34,10,Color.White);
        bar=new GProgressBar {Location=new Point(30,430),Size=new Size(600,10),Maximum=100}; Controls.Add(bar);
        start=ButtonAt("GIF 만들기",30,466,210,48,true); start.Enabled=false; start.Click+=async delegate { await BeginConvert(); };
        cancel=ButtonAt("취소",254,466,80,48,false); cancel.Enabled=false; cancel.Click+=delegate {cancelRequested=true;if(engine!=null)engine.Cancelled=true;status.Text="변환을 취소하고 있습니다…";};
        open=ButtonAt("저장 폴더 열기",348,466,160,48,false); open.Enabled=false; open.Click+=delegate { if(outputDirectory!=null) Process.Start("explorer.exe",outputDirectory); };
        reset=ButtonAt("초기화",522,466,108,48,false); reset.Click+=delegate { ResetInput(); };
        // Keep the GIF controls and their state on the first tab.
        var gifControls=new System.Collections.Generic.List<Control>();
        foreach(Control c in Controls) if(c.Top>=122) gifControls.Add(c);
        var switcher=new GRoundedPanel {Location=new Point(28,116),Size=new Size(604,42)};Controls.Add(switcher);
        gifTabButton=new Button {Text="MP4 → GIF",Location=new Point(3,3),Size=new Size(297,36)};GUi.Button(gifTabButton,GUi.Accent);switcher.Controls.Add(gifTabButton);
        speedTabButton=new Button {Text="MP4 배속 → MP4",Location=new Point(304,3),Size=new Size(297,36)};GUi.Button(speedTabButton,GUi.Surface2);switcher.Controls.Add(speedTabButton);
        gifPage=new Panel {Location=new Point(0,164),Size=new Size(660,450),BackColor=bg};speedPage=new Panel {Location=gifPage.Location,Size=gifPage.Size,BackColor=bg,Visible=false};Controls.Add(gifPage);Controls.Add(speedPage);
        foreach(Control c in gifControls) {c.Top-=110;gifPage.Controls.Add(c);}
        speedPanel=new SpeedPanel {Dock=DockStyle.Fill};speedPage.Controls.Add(speedPanel);
        gifTabButton.Click+=delegate{SelectTab(0);};speedTabButton.Click+=delegate{SelectTab(1);};SelectTab(0);
        FormClosing+=delegate(object s,FormClosingEventArgs e) {
            if(busy) {e.Cancel=true;cancelRequested=true;if(engine!=null)engine.Cancelled=true;status.Text="취소 중입니다. 완료 후 창을 닫아 주세요.";}
            if(speedPanel.Busy) {e.Cancel=true; speedPanel.Cancel();}
        };
    }
    void SelectTab(int index){if(busy||speedPanel.Busy)return;activeTab=index;gifPage.Visible=index==0;speedPage.Visible=index==1;gifTabButton.BackColor=index==0?GUi.Accent:GUi.Surface2;speedTabButton.BackColor=index==1?GUi.Accent:GUi.Surface2;gifTabButton.ForeColor=index==0?Color.FromArgb(15,26,18):GUi.Muted;speedTabButton.ForeColor=index==1?Color.FromArgb(15,26,18):GUi.Muted;}
    public void SelectSpeedTab(){SelectTab(1);}
    Label LabelAt(string text,int x,int y,int w,int h,int size,Color color) {var l=new Label {Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("맑은 고딕",size),ForeColor=color};Controls.Add(l);return l;}
    Button ButtonAt(string text,int x,int y,int w,int h,bool primary) {var b=new Button {Text=text,Location=new Point(x,y),Size=new Size(w,h)};GUi.Button(b,primary?GUi.Accent:panel);Controls.Add(b);return b;}
    void EnterFile(object s,DragEventArgs e) {e.Effect=!busy&&!speedPanel.Busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;}
    void DropFile(object s,DragEventArgs e) {if(busy||speedPanel.Busy)return; var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&files.Length>0) {if(activeTab==1)speedPanel.SetInput(files[0]);else SetInputs(files);}}
    void ResetInput() {
        if(busy)return;
        inputs.Clear();queue.Items.Clear();result=outputDirectory=null;engine=null;cancelRequested=false;
        pathLabel.Text="MP4 파일을 여기에 끌어 놓으세요";
        status.Text="파일을 선택하면 시작할 수 있습니다.";
        bar.Value=0; start.Enabled=cancel.Enabled=open.Enabled=false;
        select.Enabled=true; select.Focus();
    }
    public void SetInput(string file){SetInputs(new[]{file});}
    public void SetInputs(IEnumerable<string> files) {
        if(busy)return;var valid=new List<string>();int ignored=0;
        foreach(string file in files){if(File.Exists(file)&&Path.GetExtension(file).Equals(".mp4",StringComparison.OrdinalIgnoreCase)){if(!valid.Exists(x=>string.Equals(x,file,StringComparison.OrdinalIgnoreCase)))valid.Add(file);}else ignored++;}
        if(valid.Count==0){MessageBox.Show("MP4 파일을 선택해 주세요.");return;}
        inputs.Clear();inputs.AddRange(valid);queue.Items.Clear();foreach(string file in inputs)queue.Items.Add("대기  ·  "+Path.GetFileName(file));
        result=outputDirectory=null;open.Enabled=false;bar.Value=0;start.Enabled=true;
        pathLabel.Text=inputs.Count==1?Path.GetFileName(inputs[0])+"\n"+(new FileInfo(inputs[0]).Length/1000000.0).ToString("0.0")+" MB":inputs.Count+"개 MP4 파일 선택됨";
        status.Text=ignored>0?ignored+"개 항목은 MP4가 아니어서 제외했습니다.":"준비 완료 · 저장 위치를 선택하고 변환하세요.";
    }
    async Task BeginConvert() {
        if(inputs.Count==0)return;
        string dest=null,folder=null;
        if(inputs.Count==1)using(var d=new SaveFileDialog {Filter="GIF 이미지|*.gif",DefaultExt="gif",AddExtension=true,FileName=Path.GetFileNameWithoutExtension(inputs[0])+"_50MB.gif",InitialDirectory=Path.GetDirectoryName(inputs[0]),OverwritePrompt=false}) {
            if(d.ShowDialog()!=DialogResult.OK)return;dest=d.FileName;if(File.Exists(dest)){MessageBox.Show("같은 이름의 파일이 있습니다. 다른 이름으로 저장해 주세요.");return;}folder=Path.GetDirectoryName(dest);
        } else using(var d=new FolderBrowserDialog {Description="GIF 파일을 저장할 폴더를 선택하세요",SelectedPath=Path.GetDirectoryName(inputs[0]),ShowNewFolderButton=true}) {
            if(d.ShowDialog()!=DialogResult.OK)return;folder=d.SelectedPath;
        }
        await RunConversion(folder,dest);
    }
    public async Task ConvertBatchTest(string[] files,string folder){SetInputs(files);await RunConversion(folder,null);}
    async Task RunConversion(string folder,string dest){
        busy=true;cancelRequested=false;start.Enabled=select.Enabled=open.Enabled=reset.Enabled=false;cancel.Enabled=true;bar.Value=0;outputDirectory=folder;
        var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int successes=0,failures=0;var errors=new List<string>();
        try {
            for(int i=0;i<inputs.Count;i++){
                if(cancelRequested)break;
                int index=i;string file=inputs[i],target=dest??BatchDestination(folder,file,reserved);
                queue.Items[index]="변환 중  ·  "+Path.GetFileName(file);queue.TopIndex=index;status.Text=(index+1)+"/"+inputs.Count+"  "+Path.GetFileName(file);
                engine=new Engine();engine.Status=delegate(string text,int pct){if(!IsDisposed)BeginInvoke((Action)delegate{status.Text=(index+1)+"/"+inputs.Count+"  "+text;bar.Value=Math.Max(0,Math.Min(100,(index*100+pct)/inputs.Count));});};
                try{string info=await Task.Run(()=>engine.Convert(file,target));result=target;successes++;queue.Items[index]="완료  ·  "+Path.GetFileName(target);status.Text=(index+1)+"/"+inputs.Count+"  완료 · "+info;}
                catch(OperationCanceledException){cancelRequested=true;queue.Items[index]="취소  ·  "+Path.GetFileName(file);}
                catch(Exception ex){failures++;queue.Items[index]="실패  ·  "+Path.GetFileName(file)+"  ·  "+ex.Message;errors.Add(Path.GetFileName(file)+": "+ex.Message);}
                engine=null;bar.Value=Math.Min(100,(index+1)*100/inputs.Count);
            }
            status.Text=cancelRequested?"취소됨 · 완료 "+successes+"개"+(failures>0?" · 실패 "+failures+"개":""):"완료 "+successes+"개"+(failures>0?" · 실패 "+failures+"개":"");
            if(errors.Count>0)MessageBox.Show(this,string.Join("\n",errors.ToArray()),"변환하지 못한 파일",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            open.Enabled=successes>0;
        }finally{busy=false;engine=null;start.Enabled=select.Enabled=reset.Enabled=true;cancel.Enabled=false;}
    }
    static string BatchDestination(string folder,string file,HashSet<string> reserved){
        string baseName=Path.GetFileNameWithoutExtension(file)+"_50MB",candidate=Path.Combine(folder,baseName+".gif");int number=2;
        while(File.Exists(candidate)||reserved.Contains(candidate))candidate=Path.Combine(folder,baseName+" ("+(number++)+").gif");
        reserved.Add(candidate);return candidate;
    }
}
class SpeedPanel : UserControl {
    Label fileLabel,status; NumericUpDown rate; Button select,start,cancel,open,reset; GProgressBar bar;
    string input,result; Engine engine;
    public bool Busy {get;private set;}
    Color surface=GUi.Surface2, muted=GUi.Muted;
    public SpeedPanel() {
        BackColor=GUi.Background; ForeColor=GUi.Text; Font=new Font("맑은 고딕",10); AllowDrop=true;
        var drop=new GRoundedPanel {Location=new Point(28,12),Size=new Size(604,100),AllowDrop=true}; Controls.Add(drop);
        fileLabel=new Label {Text="MP4 파일을 여기에 끌어 놓으세요",Location=new Point(18,16),Size=new Size(430,68),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true}; drop.Controls.Add(fileLabel);
        select=MakeButton("파일 선택",460,30,120); drop.Controls.Add(select);
        select.Click+=delegate { using(var d=new OpenFileDialog {Filter="MP4 동영상|*.mp4",Title="배속할 MP4 선택"}) if(d.ShowDialog()==DialogResult.OK) SetInput(d.FileName); };
        LabelAt("재생 속도",30,127,90,30,10);
        rate=new NumericUpDown {Location=new Point(124,128),Size=new Size(118,30),Minimum=0.25M,Maximum=16M,DecimalPlaces=2,Increment=0.25M,Value=2M,BackColor=surface,ForeColor=Color.White,Font=new Font("맑은 고딕",12)}; Controls.Add(rate);
        LabelAt("배  (0.25~16배)",252,130,200,26,10);
        LabelAt("고화질 MP4 저장 · 소리 음높이 유지",30,174,600,25,10);
        LabelAt("1배 미만은 느리게, 1배 초과는 빠르게 재생됩니다.",30,198,600,25,9);
        status=LabelAt("파일과 배속을 선택하면 시작할 수 있습니다.",30,231,600,35,10); status.ForeColor=Color.White;
        bar=new GProgressBar {Location=new Point(30,272),Size=new Size(600,10)}; Controls.Add(bar);
        start=MakeButton("배속 MP4 만들기",30,298,210); start.BackColor=GUi.Accent; start.Enabled=false; start.Click+=async delegate {await Convert();};
        cancel=MakeButton("취소",254,298,80); cancel.Enabled=false; cancel.Click+=delegate {Cancel();};
        open=MakeButton("저장 폴더 열기",348,298,160); open.Enabled=false; open.Click+=delegate {if(result!=null)Process.Start("explorer.exe","/select,\""+result+"\"");};
        reset=MakeButton("초기화",522,298,108); reset.Click+=delegate {if(Busy)return;input=result=null;engine=null;fileLabel.Text="MP4 파일을 여기에 끌어 놓으세요";rate.Value=2M;bar.Value=0;status.Text="파일과 배속을 선택하면 시작할 수 있습니다.";start.Enabled=cancel.Enabled=open.Enabled=false;};
        DragEventHandler enter=delegate(object s,DragEventArgs e) {e.Effect=!Busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;};
        DragEventHandler dropped=delegate(object s,DragEventArgs e) {if(Busy)return;var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&files.Length>0)SetInput(files[0]);};
        DragEnter+=enter; DragDrop+=dropped; drop.DragEnter+=enter; drop.DragDrop+=dropped;
    }
    Label LabelAt(string text,int x,int y,int w,int h,int size) {var l=new Label {Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("맑은 고딕",size),ForeColor=muted};Controls.Add(l);return l;}
    Button MakeButton(string text,int x,int y,int width) {var b=new Button {Text=text,Location=new Point(x,y),Size=new Size(width,42)};GUi.Button(b,surface);Controls.Add(b);return b;}
    public void SetInput(string path) {
        if(Busy)return;
        if(!File.Exists(path)||!Path.GetExtension(path).Equals(".mp4",StringComparison.OrdinalIgnoreCase)){MessageBox.Show("MP4 파일을 선택해 주세요.");return;}
        input=path;result=null;open.Enabled=false;bar.Value=0;
        fileLabel.Text=Path.GetFileName(path)+"\n"+(new FileInfo(path).Length/1000000.0).ToString("0.0")+" MB";
        start.Enabled=true;status.Text="준비 완료 · 배속을 선택하고 변환하세요.";
    }
    public void Cancel() {if(engine!=null&&Busy){engine.Cancelled=true;status.Text="변환을 취소하고 있습니다…";}}
    async Task Convert() {
        if(!rate.ValidateChildren())return;
        double speed=(double)rate.Value; string dest;
        using(var d=new SaveFileDialog {Filter="MP4 동영상|*.mp4",DefaultExt="mp4",AddExtension=true,InitialDirectory=Path.GetDirectoryName(input),FileName=Path.GetFileNameWithoutExtension(input)+"_"+speed.ToString("0.##",CultureInfo.InvariantCulture)+"x.mp4",OverwritePrompt=false}) {
            if(d.ShowDialog()!=DialogResult.OK)return; dest=d.FileName;
            if(File.Exists(dest)){MessageBox.Show("같은 이름의 파일이 있습니다. 다른 이름으로 저장해 주세요.");return;}
        }
        Busy=true;start.Enabled=select.Enabled=rate.Enabled=reset.Enabled=open.Enabled=false;cancel.Enabled=true;bar.Value=0;
        engine=new Engine();engine.Status=delegate(string text,int progress){if(!IsDisposed)BeginInvoke((Action)delegate {status.Text=text;bar.Value=Math.Max(0,Math.Min(100,progress));});};
        try {string info=await Task.Run(()=>engine.ChangeSpeed(input,dest,speed));result=dest;status.Text="완료 · "+info;bar.Value=100;open.Enabled=true;}
        catch(OperationCanceledException){status.Text="취소되었습니다.";bar.Value=0;}
        catch(Exception ex){status.Text="변환하지 못했습니다.";MessageBox.Show(ex.Message,"GIF Generator",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally {Busy=false;start.Enabled=select.Enabled=rate.Enabled=reset.Enabled=true;cancel.Enabled=false;}
    }
}
class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length>=4&&args[0]=="--speed") {
            try {var e=new Engine();var summary=e.ChangeSpeed(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),double.Parse(args[3],CultureInfo.InvariantCulture));File.WriteAllText(args[2]+".result.txt",summary);return 0;}catch(Exception ex){File.WriteAllText(args[2]+".error.txt",ex.ToString());return 1;}
        }
        if(args.Length>=3&&args[0]=="--convert") {
            try {var e=new Engine();if(args.Length>3)e.Limit=long.Parse(args[3]);e.Status=delegate(string s,int p){Console.WriteLine(s+" "+p+"%");};var summary=e.Convert(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));File.WriteAllText(args[2]+".result.txt",summary);return 0;}catch(Exception ex){File.WriteAllText(args[2]+".error.txt",ex.ToString());return 1;}
        }
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>=4&&args[0]=="--batch-test"){
            using(var batchForm=new MainForm()){
                batchForm.Shown+=async delegate {try{var files=new string[args.Length-2];Array.Copy(args,2,files,0,files.Length);await batchForm.ConvertBatchTest(files,args[1]);File.WriteAllText(Path.Combine(args[1],"batch-result.txt"),"done");}catch(Exception ex){File.WriteAllText(Path.Combine(args[1],"batch-error.txt"),ex.ToString());}finally{batchForm.Close();}};
                Application.Run(batchForm);
            }return 0;
        }
        var form=new MainForm();if(args.Length>0&&File.Exists(args[0]))form.SetInput(args[0]);
        if(args.Length==2&&args[0]=="--ui-batch-test"){
            string first=Path.Combine(Path.GetTempPath(),"GifBatchPreview-a-"+Guid.NewGuid().ToString("N")+".mp4"),second=Path.Combine(Path.GetTempPath(),"GifBatchPreview-b-"+Guid.NewGuid().ToString("N")+".mp4");
            try{File.WriteAllBytes(first,new byte[0]);File.WriteAllBytes(second,new byte[0]);form.SetInputs(new[]{first,second});form.Show();Application.DoEvents();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(args[1]);}form.Close();}finally{File.Delete(first);File.Delete(second);}return 0;
        }
        if(args.Length==2&&(args[0]=="--ui-test"||args[0]=="--ui-speed-test")) {form.Show();if(args[0]=="--ui-speed-test")form.SelectSpeedTab();Application.DoEvents();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(args[1]);}form.Close();return 0;}
        Application.Run(form);return 0;
    }
}
