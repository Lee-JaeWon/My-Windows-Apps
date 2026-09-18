using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Reflection;

[assembly: AssemblyTitle("Lab Server Monitor")]
[assembly: AssemblyProduct("Lab Server Monitor")]
[assembly: AssemblyVersion("1.0.2.0")]

public class ServerConfig {
    public string Host {get;set;}
    public string User {get;set;}
    public string HostKey {get;set;}
    public string PasswordFile {get;set;}
}
public class Config {public ServerConfig[] Servers {get;set;}}
public class Gpu {
    public string index {get;set;} public string name {get;set;}
    public double? used {get;set;} public double? total {get;set;}
    public double? utilization {get;set;}
    public double? temperature {get;set;}
}
public class Sample {
    public Gpu[] gpus {get;set;}
    public string gpu_error {get;set;} public string ram_error {get;set;}
    public double ram_total {get;set;} public double ram_used {get;set;}
}
public class ViewState {
    public Sample Data; public DateTime Received;
    public string State="연결 중…"; public bool Connected;
    public long Updates;
}
class Session : IDisposable {
    public readonly ServerConfig Config;
    readonly object gate=new object();
    readonly ManualResetEvent stop=new ManualResetEvent(false);
    readonly string configDirectory;
    readonly Thread worker;
    ViewState current=new ViewState(); Process active;
    public Session(ServerConfig config,string directory) {
        Config=config;configDirectory=directory;
        worker=new Thread(Work) {IsBackground=true,Name="Monitor "+config.Host};worker.Start();
    }
    static string Q(string s) {return "\""+s+"\"";}
    public ViewState Snapshot() {lock(gate)return new ViewState {Data=current.Data,Received=current.Received,State=current.State,Connected=current.Connected,Updates=current.Updates};}
    void SetState(string state,bool connected) {lock(gate){current.State=state;current.Connected=connected;if(!connected)current.Data=null;}}
    void Work() {
        while(!stop.WaitOne(0)) {
            string error="";
            try {
                SetState("연결 중…",false);
                string password=Path.Combine(configDirectory,Config.PasswordFile);
                if(!File.Exists(password)) {SetState("로그인 정보 파일 없음",false);if(stop.WaitOne(5000))break;continue;}
                using(var process=new Process()) {
                    process.StartInfo=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"plink.exe"),
                        "-ssh -batch -T -noagent -hostkey "+Q(Config.HostKey)+" -l "+Q(Config.User)+" -pwfile "+Q(password)+" "+Q(Config.Host)+" -m "+Q(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"collector.sh"))) {
                        UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
                        StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                    };
                    process.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e) {
                        if(e.Data==null || !e.Data.StartsWith("{"))return;
                        try {
                            var sample=new JavaScriptSerializer().Deserialize<Sample>(e.Data);
                            if(sample==null||sample.gpus==null)return;
                            lock(gate){current.Data=sample;current.Received=DateTime.UtcNow;current.Connected=true;current.State="연결됨";current.Updates++;}
                        } catch {}
                    };
                    process.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e) {if(e.Data!=null)lock(gate)error=e.Data;};
                    lock(gate){if(stop.WaitOne(0))break;process.Start();active=process;}
                    process.BeginOutputReadLine();process.BeginErrorReadLine();
                    DateTime began=DateTime.UtcNow;
                    while(!process.WaitForExit(250)) {
                        ViewState state=Snapshot();
                        DateTime latest=state.Received>began?state.Received:began;
                        if(stop.WaitOne(0)||(DateTime.UtcNow-latest).TotalSeconds>12) {
                            try {process.Kill();}catch{} break;
                        }
                    }
                    if(process.WaitForExit(2000))process.WaitForExit();
                    lock(gate)active=null;
                }
                string lower=error.ToLowerInvariant();
                SetState(lower.Contains("access denied")||lower.Contains("authentication")?"로그인 실패":
                    lower.Contains("host key")?"서버 키 확인 필요":"연결 안 됨 · 재시도 중",false);
            } catch {SetState("연결 안 됨 · 재시도 중",false);lock(gate)active=null;}
            if(stop.WaitOne(5000))break;
        }
    }
    public void Dispose() {
        stop.Set();lock(gate){if(active!=null)try{active.Kill();}catch{}}
        if(worker.Join(3000))stop.Dispose();
    }
}
class ServerCard : Control {
    readonly Session session;
    readonly Font hostFont=new Font("Segoe UI",16,FontStyle.Bold);
    readonly Font labelFont=new Font("Segoe UI",10);
    readonly Font valueFont=new Font("Segoe UI",15,FontStyle.Bold);
    readonly Font smallFont=new Font("맑은 고딕",9);
    readonly Color muted=Color.FromArgb(153,167,188),green=Color.FromArgb(78,220,163),purple=Color.FromArgb(158,139,255);
    public ServerCard(Session source) {session=source;DoubleBuffered=true;BackColor=Color.FromArgb(28,35,48);}
    void TextAt(Graphics g,string text,Font font,Color color,int x,int y,int width,int height) {
        TextRenderer.DrawText(g,text,font,new Rectangle(x,y,width,height),color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
    }
    void Bar(Graphics g,int y,double used,double total,Color color) {
        int w=Width-40;using(var b=new SolidBrush(Color.FromArgb(48,57,73)))g.FillRectangle(b,20,y,w,6);
        if(total>0)using(var b=new SolidBrush(color))g.FillRectangle(b,20,y,(int)(w*Math.Max(0,Math.Min(1,used/total))),6);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        var state=session.Snapshot();double age=(DateTime.UtcNow-state.Received).TotalSeconds;
        bool live=state.Connected&&age<4;var signal=live?green:Color.FromArgb(238,175,93);
        TextAt(g,session.Config.Host,hostFont,Color.White,20,16,Width-40,32);
        TextAt(g,session.Config.User,labelFont,muted,20,50,Width-40,22);
        using(var brush=new SolidBrush(signal))g.FillEllipse(brush,21,86,8,8);
        TextAt(g,state.Connected&&!live?"응답 지연":state.State,smallFont,signal,38,77,Width-60,25);
        var data=live?state.Data:null;int y=121;
        if(data!=null&&data.gpus.Length>0) {
            foreach(var gpu in data.gpus) {
                TextAt(g,"GPU "+gpu.index+"  ·  "+gpu.name,labelFont,muted,20,y,Width-40,24);
                string memory=gpu.used.HasValue&&gpu.total.HasValue?gpu.used.Value.ToString("0")+" MiB / "+gpu.total.Value.ToString("0")+" MiB":"메모리 정보 없음";
                TextAt(g,memory,valueFont,Color.White,20,y+27,Width-40,32);
                Bar(g,y+66,gpu.used??0,gpu.total??0,purple);
                TextAt(g,"GPU 사용률  "+(gpu.utilization.HasValue?gpu.utilization.Value.ToString("0")+"%":"—")+"   ·   온도  "+(gpu.temperature.HasValue?gpu.temperature.Value.ToString("0")+" °C":"—"),smallFont,green,20,y+75,Width-40,22);y+=108;
            }
        } else {
            TextAt(g,"GPU",labelFont,muted,20,y,Width-40,24);
            TextAt(g,data==null?"—":string.IsNullOrEmpty(data.gpu_error)?"GPU 없음":"GPU 조회 실패",valueFont,muted,20,y+27,Width-40,32);y+=94;
        }
        TextAt(g,"RAM",labelFont,muted,20,y,Width-40,24);
        bool ram=data!=null&&data.ram_total>0&&string.IsNullOrEmpty(data.ram_error);
        string ramText=ram?(data.ram_used/1024).ToString("0.0")+" GiB / "+(data.ram_total/1024).ToString("0.0")+" GiB":"—";
        TextAt(g,ramText,valueFont,Color.White,20,y+27,Width-40,32);
        Bar(g,y+66,ram?data.ram_used:0,ram?data.ram_total:0,green);
        TextAt(g,live?"마지막 갱신  "+state.Received.ToLocalTime().ToString("HH:mm:ss"):"상태는 SSH 연결 기준입니다.",smallFont,muted,20,Height-34,Width-40,24);
    }
    protected override void Dispose(bool disposing) {if(disposing){hostFont.Dispose();labelFont.Dispose();valueFont.Dispose();smallFont.Dispose();}base.Dispose(disposing);}
}
class MonitorForm : Form {
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=1000};
    readonly List<Session> sessions=new List<Session>();readonly List<ServerCard> cards=new List<ServerCard>();
    readonly FlowLayoutPanel area=new FlowLayoutPanel();
    public MonitorForm(Config config,string directory) {
        Text="Lab Server Monitor";ClientSize=new Size(980,610);MinimumSize=new Size(800,640);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(17,23,33);ForeColor=Color.White;Font=new Font("맑은 고딕",10);AutoScaleMode=AutoScaleMode.Dpi;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Controls.Add(new Label {Text="Lab Server Monitor",Location=new Point(24,18),Size=new Size(600,42),Font=new Font("Segoe UI",23,FontStyle.Bold)});
        Controls.Add(new Label {Text="1초마다 갱신  ·  GPU 메모리 / 사용률 / 온도 / RAM",Location=new Point(26,66),Size=new Size(600,25),ForeColor=Color.FromArgb(153,167,188)});
        area.Location=new Point(16,108);area.Size=new Size(ClientSize.Width-32,ClientSize.Height-122);area.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;area.AutoScroll=true;area.WrapContents=true;Controls.Add(area);
        foreach(var server in config.Servers){var session=new Session(server,directory);sessions.Add(session);var card=new ServerCard(session){Size=new Size(458,464),Margin=new Padding(8,0,8,12)};cards.Add(card);area.Controls.Add(card);}
        timer.Tick+=delegate {foreach(var card in cards)card.Invalidate();};timer.Start();
        FormClosed+=delegate {timer.Stop();timer.Dispose();foreach(var session in sessions)session.Dispose();};
    }
    public void SaveCheck(string path) {
        var states=new List<object>();
        foreach(var session in sessions)states.Add(new {host=session.Config.Host,state=session.Snapshot()});
        File.WriteAllText(path+".json",new JavaScriptSerializer().Serialize(states));
        using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path+".png");}
    }
}
class Program {
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr FindWindow(string className,string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindowAsync(IntPtr window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
    [STAThread] static int Main(string[] args) {
        bool first;
        using(var instance=new Mutex(true,"Local\\LabServerMonitor-jw",out first)) {
        if(!first) {var window=FindWindow(null,"Lab Server Monitor");if(window!=IntPtr.Zero){ShowWindowAsync(window,9);SetForegroundWindow(window);}return 0;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        // Keep this private installation's settings with the executable so launching
        // from Explorer uses the same files as launching from a terminal.
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings");
        if(!File.Exists(Path.Combine(directory,"servers.json")))
            directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LabServerMonitor");
        try {
            if(!File.Exists(Path.Combine(directory,"servers.json")))throw new Exception("서버 설정 파일이 없습니다. 앱 폴더의 settings\\servers.json을 확인해 주세요.");
            var config=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(Path.Combine(directory,"servers.json")));
            if(config==null||config.Servers==null||config.Servers.Length==0)throw new Exception("서버 설정이 없습니다.");
            using(var form=new MonitorForm(config,directory)) {
                if(args.Length==2&&args[0]=="--check") {
                    var finish=new System.Windows.Forms.Timer {Interval=10000};
                    finish.Tick+=delegate {finish.Stop();finish.Dispose();form.SaveCheck(args[1]);form.Close();};finish.Start();
                }
                Application.Run(form);
            }
            return 0;
        } catch(Exception ex) {
            if(args.Length==2&&args[0]=="--check")File.WriteAllText(args[1]+".error.txt",ex.Message);
            else MessageBox.Show("앱을 시작하지 못했습니다.\n"+ex.Message,"Lab Server Monitor");
            return 1;
        }
        }
    }
}
