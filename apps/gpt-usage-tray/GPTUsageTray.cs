using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("GPT Usage Tray")]
[assembly: AssemblyProduct("GPT Usage Tray")]
[assembly: AssemblyDescription("Codex usage in the Windows notification area")]
[assembly: AssemblyVersion("1.1.0.0")]

class UsageWindow {
    public string Name;
    public double Used;
    public double Remaining { get { return Math.Max(0, Math.Min(100, 100-Used)); } }
    public int? DurationMinutes;
    public DateTime? ResetLocal;
}

class UsageSnapshot {
    public List<UsageWindow> Windows=new List<UsageWindow>();
    public long? LifetimeTokens;
    public long? TodayTokens;
    public bool? Allowed;
    public DateTime Updated=DateTime.Now;
    public double? Remaining {
        get {
            UsageWindow weekly=null;
            foreach(var window in Windows) {
                if(!window.DurationMinutes.HasValue)continue;
                if(weekly==null||window.DurationMinutes.Value>weekly.DurationMinutes.Value)weekly=window;
            }
            if(weekly!=null)return weekly.Remaining;
            if(Windows.Count>0)return Windows[0].Remaining;
            return Allowed==false?(double?)0:null;
        }
    }
}

static class CodexUsageReader {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static Dictionary<string,object> Dict(object value) { return value as Dictionary<string,object>; }
    static object Value(Dictionary<string,object> source,string key) { object value; return source!=null&&source.TryGetValue(key,out value)?value:null; }
    static double Number(object value) { return Convert.ToDouble(value,CultureInfo.InvariantCulture); }
    static long Long(object value) { return Convert.ToInt64(value,CultureInfo.InvariantCulture); }

    public static UsageSnapshot Read() {
        string codex=FindCodex();
        using(var process=new Process()) {
            process.StartInfo=new ProcessStartInfo(codex,"app-server --listen stdio://") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
            };
            process.ErrorDataReceived+=delegate{};
            process.Start();process.BeginErrorReadLine();
            try {
                var initParams=new Dictionary<string,object> {
                    {"clientInfo",new Dictionary<string,object>{{"name","gpt-usage-tray"},{"title","GPT Usage Tray"},{"version","1.0.0"}}},
                    {"capabilities",new Dictionary<string,object>{{"experimentalApi",true},{"requestAttestation",false}}}
                };
                Request(process,1,"initialize",initParams,15000);
                var limits=Request(process,2,"account/rateLimits/read",new Dictionary<string,object>{{"excludeResetCreditDetails",true}},15000);
                var usage=Request(process,3,"account/usage/read",new Dictionary<string,object>(),15000);
                return Parse(limits,usage);
            } finally {
                try { if(!process.HasExited)process.Kill(); } catch {}
                try { process.WaitForExit(2000); } catch {}
            }
        }
    }

    static Dictionary<string,object> Request(Process process,int id,string method,object parameters,int timeoutMs) {
        var request=new Dictionary<string,object>{{"id",id},{"method",method},{"params",parameters}};
        process.StandardInput.WriteLine(Json.Serialize(request));process.StandardInput.Flush();
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<timeoutMs) {
            int remaining=Math.Max(1,timeoutMs-(int)watch.ElapsedMilliseconds);
            var task=process.StandardOutput.ReadLineAsync();
            if(!task.Wait(remaining))throw new TimeoutException("Codex 응답 시간이 초과되었습니다.");
            string line=task.Result;if(line==null)throw new IOException("Codex 연결이 종료되었습니다.");
            Dictionary<string,object> message;
            try {message=Json.Deserialize<Dictionary<string,object>>(line);} catch {continue;}
            object responseId;if(!message.TryGetValue("id",out responseId)||Convert.ToInt32(responseId)!=id)continue;
            object error;if(message.TryGetValue("error",out error))throw new Exception("Codex 사용량 조회 실패: "+Json.Serialize(error));
            return Dict(Value(message,"result"));
        }
        throw new TimeoutException("Codex 응답 시간이 초과되었습니다.");
    }

    static UsageSnapshot Parse(Dictionary<string,object> limits,Dictionary<string,object> usage) {
        var snapshot=new UsageSnapshot();
        object allowed=Value(limits,"ordinaryUsageAllowed");if(allowed!=null)snapshot.Allowed=Convert.ToBoolean(allowed);
        Dictionary<string,object> rate=null;
        var byId=Dict(Value(limits,"rateLimitsByLimitId"));
        if(byId!=null)rate=Dict(Value(byId,"codex"));
        if(rate==null)rate=Dict(Value(limits,"rateLimits"));
        if(rate!=null) {
            AddWindow(snapshot,Dict(Value(rate,"primary")));
            AddWindow(snapshot,Dict(Value(rate,"secondary")));
        }
        snapshot.Windows.Sort(delegate(UsageWindow a,UsageWindow b){return Nullable.Compare(a.DurationMinutes,b.DurationMinutes);});
        var summary=Dict(Value(usage,"summary"));
        object lifetime=Value(summary,"lifetimeTokens");if(lifetime!=null)snapshot.LifetimeTokens=Long(lifetime);
        var buckets=Value(usage,"dailyUsageBuckets") as ArrayList;
        if(buckets!=null)foreach(object item in buckets) {
            var bucket=Dict(item);string date=Convert.ToString(Value(bucket,"startDate"),CultureInfo.InvariantCulture);
            if(date==DateTime.Now.ToString("yyyy-MM-dd")) {object tokens=Value(bucket,"tokens");if(tokens!=null)snapshot.TodayTokens=Long(tokens);}
        }
        snapshot.Updated=DateTime.Now;return snapshot;
    }

    static void AddWindow(UsageSnapshot snapshot,Dictionary<string,object> source) {
        if(source==null||Value(source,"usedPercent")==null)return;
        var window=new UsageWindow {Used=Number(Value(source,"usedPercent"))};
        object duration=Value(source,"windowDurationMins");if(duration!=null)window.DurationMinutes=Convert.ToInt32(duration);
        if(window.DurationMinutes.HasValue)window.Name=window.DurationMinutes.Value<=360?"단기":window.DurationMinutes.Value>=10000?"주간":FormatDuration(window.DurationMinutes.Value);
        else window.Name="사용량";
        object reset=Value(source,"resetsAt");
        if(reset!=null)window.ResetLocal=DateTimeOffset.FromUnixTimeSeconds(Long(reset)).LocalDateTime;
        snapshot.Windows.Add(window);
    }

    static string FormatDuration(int minutes) {return minutes%1440==0?(minutes/1440)+"일":minutes%60==0?(minutes/60)+"시간":minutes+"분";}
    static string FindCodex() {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root=Path.Combine(local,"OpenAI","Codex","bin");
        if(Directory.Exists(root)) {
            FileInfo best=null;
            foreach(string file in Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories)) {var info=new FileInfo(file);if(best==null||info.LastWriteTimeUtc>best.LastWriteTimeUtc)best=info;}
            if(best!=null)return best.FullName;
        }
        string path=Environment.GetEnvironmentVariable("PATH")??"";
        foreach(string folder in path.Split(';')) {try {string file=Path.Combine(folder.Trim(),"codex.exe");if(File.Exists(file))return file;}catch{}}
        throw new FileNotFoundException("Codex가 설치되어 있지 않습니다.");
    }
}

class DetailsForm : Form {
    Label main,updated,tokens;Panel windows;Button refresh;
    readonly Color bg=Color.FromArgb(17,23,33),surface=Color.FromArgb(29,37,51),muted=Color.FromArgb(157,171,192);
    public event EventHandler RefreshRequested;
    public DetailsForm() {
        Text="GPT 사용량";ClientSize=new Size(440,360);MinimumSize=MaximumSize=Size;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=bg;ForeColor=Color.White;Font=new Font("맑은 고딕",10);ShowInTaskbar=true;
        main=new Label {Location=new Point(24,20),Size=new Size(390,38),Font=new Font("맑은 고딕",19,FontStyle.Bold),Text="사용량 확인 중…"};Controls.Add(main);
        updated=new Label {Location=new Point(26,62),Size=new Size(380,24),ForeColor=muted};Controls.Add(updated);
        windows=new Panel {Location=new Point(20,100),Size=new Size(400,150),BackColor=surface};Controls.Add(windows);
        tokens=new Label {Location=new Point(24,264),Size=new Size(390,38),ForeColor=muted};Controls.Add(tokens);
        refresh=new Button {Text="지금 새로고침",Location=new Point(274,310),Size=new Size(146,36),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(91,76,219),ForeColor=Color.White};refresh.FlatAppearance.BorderSize=0;Controls.Add(refresh);
        refresh.Click+=delegate {if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
    }
    public void Loading(){refresh.Enabled=false;updated.Text="Codex에서 최신 사용량을 읽고 있습니다…";}
    public void ShowError(string error){main.Text="사용량을 읽지 못했습니다";updated.Text=error;refresh.Enabled=true;windows.Controls.Clear();}
    public void UpdateData(UsageSnapshot snapshot) {
        refresh.Enabled=true;double? remaining=snapshot.Remaining;
        main.Text=remaining.HasValue?"주간 남은 사용량  "+remaining.Value.ToString("0")+"%":"사용량 정보 없음";
        updated.Text="마지막 갱신  "+snapshot.Updated.ToString("yyyy-MM-dd HH:mm:ss");
        windows.Controls.Clear();int y=12;
        foreach(var item in snapshot.Windows) {
            var label=new Label {Text=item.Name+"  "+item.Remaining.ToString("0")+"% 남음",Location=new Point(14,y),Size=new Size(190,24),ForeColor=Color.White};windows.Controls.Add(label);
            string reset=item.ResetLocal.HasValue?"초기화 "+item.ResetLocal.Value.ToString("MM/dd HH:mm"):"";
            var resetLabel=new Label {Text=reset,Location=new Point(210,y),Size=new Size(170,24),TextAlign=ContentAlignment.MiddleRight,ForeColor=muted};windows.Controls.Add(resetLabel);
            var track=new Panel {Location=new Point(14,y+28),Size=new Size(366,7),BackColor=Color.FromArgb(50,60,77)};windows.Controls.Add(track);
            var fill=new Panel {Location=Point.Empty,Size=new Size((int)(366*item.Remaining/100),7),BackColor=UsageContext.ColorFor(item.Remaining)};track.Controls.Add(fill);y+=60;
        }
        if(snapshot.Windows.Count==0)windows.Controls.Add(new Label {Text="현재 계정에서 한도 정보를 제공하지 않습니다.",Location=new Point(14,18),Size=new Size(365,30),ForeColor=muted});
        tokens.Text="오늘 토큰  "+FormatTokens(snapshot.TodayTokens)+"     누적 토큰  "+FormatTokens(snapshot.LifetimeTokens);
    }
    static string FormatTokens(long? value){if(!value.HasValue)return "—";if(value.Value>=1000000000)return (value.Value/1000000000.0).ToString("0.00")+"B";if(value.Value>=1000000)return (value.Value/1000000.0).ToString("0.0")+"M";if(value.Value>=1000)return (value.Value/1000.0).ToString("0.0")+"K";return value.Value.ToString();}
}

class UsageWidgetForm : Form {
    double? remaining;
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly System.Windows.Forms.Timer anchorTimer=new System.Windows.Forms.Timer();
    IntPtr taskbarHandle=IntPtr.Zero;
    public event EventHandler DetailsRequested;
    public event EventHandler RefreshRequested;
    public event EventHandler HideRequested;

    public UsageWidgetForm() {
        Text="GPT Usage Tray";ClientSize=new Size(44,44);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false;TopMost=false;BackColor=Color.Fuchsia;TransparencyKey=Color.Fuchsia;DoubleBuffered=true;Cursor=Cursors.Hand;
        menu.Items.Add("상세 보기",null,delegate {if(DetailsRequested!=null)DetailsRequested(this,EventArgs.Empty);});
        menu.Items.Add("지금 새로고침",null,delegate {if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);});
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("큰 위젯 숨기기",null,delegate {if(HideRequested!=null)HideRequested(this,EventArgs.Empty);});
        ContextMenuStrip=menu;
        MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&DetailsRequested!=null)DetailsRequested(this,EventArgs.Empty);};
        Shown+=delegate {AttachToTaskbar();};
        anchorTimer.Interval=3000;anchorTimer.Tick+=delegate {if(Visible)AttachToTaskbar();};anchorTimer.Start();
        ApplyCircleShape();
    }

    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x80;return value;}}

    public void UpdateData(UsageSnapshot snapshot) {
        remaining=snapshot.Remaining;
        Invalidate();
    }

    public void ShowError() {remaining=null;Invalidate();}

    public void AttachToTaskbar() {
        IntPtr bar=FindWindow("Shell_TrayWnd",null);if(bar==IntPtr.Zero)return;
        RECT barRect;if(!GetWindowRect(bar,out barRect))return;
        int barWidth=barRect.Right-barRect.Left,barHeight=barRect.Bottom-barRect.Top;
        int x,y;
        if(barWidth>=barHeight) {
            x=barRect.Left+8;y=barRect.Top+Math.Max(0,(barHeight-Height)/2);
        } else {
            x=barRect.Left+Math.Max(0,(barWidth-Width)/2);y=barRect.Bottom-Height-8;
        }
        if(taskbarHandle!=bar){taskbarHandle=bar;SetWindowLongPtr(Handle,-8,bar);}
        SetWindowPos(Handle,new IntPtr(-1),x,y,Width,Height,0x0010|0x0040);
    }

    void ApplyCircleShape() {
        IntPtr region=CreateEllipticRgn(0,0,Width+1,Height+1);
        Region=Region.FromHrgn(region);DeleteObject(region);
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.None;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        double value=remaining.HasValue?Math.Max(0,Math.Min(100,remaining.Value)):0;
        Color color=remaining.HasValue?UsageContext.ColorFor(value):Color.FromArgb(125,135,150);
        RectangleF ring=new RectangleF(4,4,36,36);
        using(var track=new Pen(Color.FromArgb(125,135,150),5)){track.StartCap=LineCap.Round;track.EndCap=LineCap.Round;g.DrawArc(track,ring,-90,359.8f);}
        if(remaining.HasValue&&value>0)using(var progress=new Pen(color,5)){progress.StartCap=LineCap.Round;progress.EndCap=LineCap.Round;g.DrawArc(progress,ring,-90,(float)(Math.Min(99.9,value)/100*359.8));}
        string percent=remaining.HasValue?Math.Round(value).ToString("0")+"%":"?";
        float percentSize=percent.Length>=4?11:14;
        using(var font=new Font("Segoe UI",percentSize,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var black=new SolidBrush(Color.Black))
        using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(percent,font,black,ring,format);
    }

    protected override void Dispose(bool disposing) {if(disposing){anchorTimer.Dispose();menu.Dispose();}base.Dispose(disposing);}
    [DllImport("gdi32.dll")]static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern IntPtr FindWindow(string className,string windowName);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr handle,out RECT rect);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtr",SetLastError=true)]static extern IntPtr SetWindowLongPtr(IntPtr handle,int index,IntPtr value);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
    [StructLayout(LayoutKind.Sequential)]struct RECT {public int Left,Top,Right,Bottom;}
}

class UsageContext : ApplicationContext {
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName="GPT Usage Tray";
    readonly NotifyIcon tray=new NotifyIcon();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly Control marshal=new Control();
    DetailsForm details;UsageWidgetForm widget;Icon dynamicIcon;bool refreshing;UsageSnapshot current;
    ToolStripMenuItem headline,windowLine,tokensLine,startup,largeWidget;
    bool firstSuccess=true;
    public UsageContext() {
        marshal.CreateControl();
        tray.Icon=SystemIcons.Application;tray.Text="GPT 사용량 확인 중…";tray.Visible=true;
        tray.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)ShowDetails();};
        var menu=new ContextMenuStrip();
        headline=new ToolStripMenuItem("사용량 확인 중…") {Enabled=false};menu.Items.Add(headline);
        windowLine=new ToolStripMenuItem("") {Enabled=false};menu.Items.Add(windowLine);
        tokensLine=new ToolStripMenuItem("") {Enabled=false};menu.Items.Add(tokensLine);menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("상세 보기",null,delegate{ShowDetails();});
        menu.Items.Add("지금 새로고침",null,delegate{Refresh();});
        largeWidget=new ToolStripMenuItem("작업표시줄 위젯 표시") {Checked=true,CheckOnClick=true};
        largeWidget.CheckedChanged+=delegate {if(widget!=null){if(largeWidget.Checked){widget.Show();widget.AttachToTaskbar();}else widget.Hide();}};menu.Items.Add(largeWidget);
        startup=new ToolStripMenuItem("Windows 시작 시 자동 실행") {Checked=IsStartupEnabled(),CheckOnClick=true};
        startup.CheckedChanged+=delegate {SetStartup(startup.Checked);};menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("종료",null,delegate{Exit();});tray.ContextMenuStrip=menu;
        timer.Interval=60000;timer.Tick+=delegate{Refresh();};timer.Start();
        SetStartup(true);startup.Checked=true;
        widget=new UsageWidgetForm();widget.DetailsRequested+=delegate{ShowDetails();};widget.RefreshRequested+=delegate{Refresh();};widget.HideRequested+=delegate{largeWidget.Checked=false;};widget.Show();
        Refresh();
        var promotionTimer=new System.Windows.Forms.Timer {Interval=6000};promotionTimer.Tick+=delegate {promotionTimer.Stop();PromoteInTaskbar();promotionTimer.Dispose();};promotionTimer.Start();
    }
    public static Color ColorFor(double remaining){
        remaining=Math.Max(0,Math.Min(100,remaining));
        if(remaining<50)return Blend(Color.FromArgb(239,68,68),Color.FromArgb(250,190,55),remaining/50.0);
        return Blend(Color.FromArgb(250,190,55),Color.FromArgb(76,222,128),(remaining-50)/50.0);
    }
    static Color Blend(Color from,Color to,double amount){return Color.FromArgb((int)(from.R+(to.R-from.R)*amount),(int)(from.G+(to.G-from.G)*amount),(int)(from.B+(to.B-from.B)*amount));}
    void Refresh() {
        if(refreshing)return;refreshing=true;if(details!=null)details.Loading();
        Task.Run(delegate{return CodexUsageReader.Read();}).ContinueWith(task=>marshal.BeginInvoke((Action)delegate {
            refreshing=false;
            if(task.IsFaulted) {string message=task.Exception.GetBaseException().Message;SetError(message);TrimWorkingSet();return;}
            current=task.Result;SetSnapshot(current);TrimWorkingSet();
        }));
    }
    void SetSnapshot(UsageSnapshot snapshot) {
        double? remaining=snapshot.Remaining;SetIcon(remaining);
        string shortText=remaining.HasValue?"GPT 주간 잔여 "+remaining.Value.ToString("0")+"%":"GPT 사용량 정보 없음";tray.Text=TrimTooltip(shortText);
        headline.Text=shortText;windowLine.Text=WindowSummary(snapshot);tokensLine.Text="오늘 "+FormatTokens(snapshot.TodayTokens)+" · 누적 "+FormatTokens(snapshot.LifetimeTokens);
        if(details!=null)details.UpdateData(snapshot);
        if(widget!=null)widget.UpdateData(snapshot);
        if(firstSuccess){firstSuccess=false;tray.BalloonTipTitle="GPT 사용량";tray.BalloonTipText=shortText+" · 아이콘을 클릭하면 상세 내용을 볼 수 있습니다.";tray.ShowBalloonTip(3500);}
    }
    void SetError(string error){SetIcon(null);tray.Text="GPT 사용량 조회 실패";headline.Text="사용량 조회 실패";windowLine.Text=error;tokensLine.Text="";if(details!=null)details.ShowError(error);if(widget!=null)widget.ShowError();}
    void SetIcon(double? remaining) {
        double value=remaining.HasValue?Math.Max(0,Math.Min(100,remaining.Value)):0;Color color=remaining.HasValue?ColorFor(value):Color.FromArgb(125,135,150);
        var bitmap=new Bitmap(64,64);using(var g=Graphics.FromImage(bitmap)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
            var ring=new RectangleF(4,4,56,56);
            using(var center=new SolidBrush(Color.FromArgb(245,18,23,33)))g.FillEllipse(center,7,7,50,50);
            using(var track=new Pen(Color.FromArgb(175,75,84,98),7)){track.StartCap=LineCap.Round;track.EndCap=LineCap.Round;g.DrawArc(track,ring,-90,359.8f);}
            if(remaining.HasValue&&value>0)using(var progress=new Pen(color,7)){progress.StartCap=LineCap.Round;progress.EndCap=LineCap.Round;g.DrawArc(progress,ring,-90,(float)(Math.Min(99.9,value)/100*359.8));}
            string text=remaining.HasValue?Math.Round(value).ToString("0"):"?";float fontSize=text.Length>=3?18:25;
            using(var font=new Font("Segoe UI",fontSize,FontStyle.Bold,GraphicsUnit.Pixel))using(var white=new SolidBrush(Color.White))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(text,font,white,new RectangleF(0,0,64,62),format);
        }
        IntPtr handle=bitmap.GetHicon();Icon next=(Icon)Icon.FromHandle(handle).Clone();DestroyIcon(handle);bitmap.Dispose();Icon old=dynamicIcon;dynamicIcon=next;tray.Icon=next;if(old!=null)old.Dispose();
    }
    string WindowSummary(UsageSnapshot snapshot){var parts=new List<string>();foreach(var w in snapshot.Windows)parts.Add(w.Name+" "+w.Remaining.ToString("0")+"%");return parts.Count>0?string.Join(" · ",parts.ToArray()):"한도 정보 없음";}
    static string FormatTokens(long? value){if(!value.HasValue)return "—";if(value.Value>=1000000000)return (value.Value/1000000000.0).ToString("0.00")+"B";if(value.Value>=1000000)return (value.Value/1000000.0).ToString("0.0")+"M";if(value.Value>=1000)return (value.Value/1000.0).ToString("0.0")+"K";return value.Value.ToString();}
    static string TrimTooltip(string text){return text.Length>63?text.Substring(0,63):text;}
    void ShowDetails(){if(details==null){details=new DetailsForm();details.RefreshRequested+=delegate{Refresh();};}if(current!=null)details.UpdateData(current);details.Show();details.WindowState=FormWindowState.Normal;details.Activate();}
    bool IsStartupEnabled(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null&&key.GetValue(RunName)!=null;}
    void SetStartup(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(enabled)key.SetValue(RunName,"\""+Application.ExecutablePath+"\"");else key.DeleteValue(RunName,false);}}
    void PromoteInTaskbar(){try {using(var root=Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings",true)){if(root==null)return;foreach(string name in root.GetSubKeyNames())using(var key=root.OpenSubKey(name,true)){if(key==null)continue;string path=Convert.ToString(key.GetValue("ExecutablePath"));if(!string.IsNullOrEmpty(path)&&string.Equals(Path.GetFullPath(path),Path.GetFullPath(Application.ExecutablePath),StringComparison.OrdinalIgnoreCase))key.SetValue("IsPromoted",1,RegistryValueKind.DWord);}}}catch{}}
    void Exit(){timer.Stop();tray.Visible=false;if(details!=null)details.Dispose();if(widget!=null)widget.Dispose();tray.Dispose();if(dynamicIcon!=null)dynamicIcon.Dispose();ExitThread();}
    static void TrimWorkingSet(){try{using(var process=Process.GetCurrentProcess())SetProcessWorkingSetSize(process.Handle,new IntPtr(-1),new IntPtr(-1));}catch{}}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();marshal.Dispose();}base.Dispose(disposing);}
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool DestroyIcon(IntPtr handle);
    [DllImport("kernel32.dll")]static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr minimum,IntPtr maximum);
}

class Program {
    [STAThread]static void Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--check") {
            try {var data=CodexUsageReader.Read();File.WriteAllText(args[1]+".txt","weekly_remaining="+(data.Remaining.HasValue?data.Remaining.Value.ToString("0.##"):"unknown")+Environment.NewLine+"windows="+data.Windows.Count+Environment.NewLine+"lifetime_tokens="+(data.LifetimeTokens.HasValue?data.LifetimeTokens.Value.ToString():"unknown"));Environment.ExitCode=0;}
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        if(args.Length==2&&args[0]=="--ui-test") {
            try {using(var form=new DetailsForm()){form.UpdateData(CodexUsageReader.Read());form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}form.Dispose();}Environment.ExitCode=0;}
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        if(args.Length==2&&args[0]=="--widget-test") {
            try {var sample=new UsageSnapshot();sample.Windows.Add(new UsageWindow{Name="주간",Used=85,DurationMinutes=10080});using(var form=new UsageWidgetForm()){form.UpdateData(sample);form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}}Environment.ExitCode=0;}
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        bool first;using(var mutex=new Mutex(true,"Local\\GPT-Usage-Tray",out first)){if(!first)return;Application.Run(new UsageContext());}
    }
}
