using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
[assembly: AssemblyVersion("1.3.0.0")]

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

static class WidgetColors {
    public static bool Single;public static int Hue=130;
    static readonly string Settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GPTUsageTray","widget-color.txt");
    static WidgetColors(){try{string[] values=File.ReadAllLines(Settings);int hue;if(values.Length==2&&int.TryParse(values[1],out hue)&&hue>=0&&hue<=359){Single=values[0]=="single";Hue=hue;}}catch{}}
    public static void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(Settings));File.WriteAllLines(Settings,new[]{Single?"single":"rainbow",Hue.ToString()});}catch{}}
    public static Color FromHue(double hue){hue=((hue%360)+360)%360;double c=0.64,x=c*(1-Math.Abs(hue/60%2-1)),m=0.22,r=0,g=0,b=0;if(hue<60){r=c;g=x;}else if(hue<120){r=x;g=c;}else if(hue<180){g=c;b=x;}else if(hue<240){g=x;b=c;}else if(hue<300){r=x;b=c;}else{r=c;b=x;}return Color.FromArgb((int)((r+m)*255),(int)((g+m)*255),(int)((b+m)*255));}
}
class WidgetHueSlider : Control {
    int hue;public event EventHandler ValueChanged;
    public int Hue{get{return hue;}set{value=Math.Max(0,Math.Min(359,value));if(value==hue)return;hue=value;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    public WidgetHueSlider(){DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;AccessibleName="위젯 단일 색상";AccessibleRole=AccessibleRole.Slider;BackColor=DetailsUi.Surface;}
    void Pick(int x){Hue=(int)(Math.Max(0,Math.Min(1,(x-10.0)/Math.Max(1,Width-20)))*359);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();Capture=true;Pick(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture&&e.Button==MouseButtons.Left)Pick(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left)Capture=false;}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left){Hue--;e.Handled=true;}if(e.KeyCode==Keys.Right){Hue++;e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2;var r=new Rectangle(10,y-4,Math.Max(1,Width-20),8);using(var p=DetailsUi.Round(r,4))using(var b=new LinearGradientBrush(r,Color.Red,Color.Red,0f)){var colors=new Color[7];var positions=new float[7];for(int i=0;i<7;i++){colors[i]=WidgetColors.FromHue(i*60);positions[i]=i/6f;}b.InterpolationColors=new ColorBlend{Colors=colors,Positions=positions};g.FillPath(b,p);}float x=10+(Width-20)*hue/359f;using(var b=new SolidBrush(WidgetColors.FromHue(hue)))g.FillEllipse(b,x-7,y-7,14,14);using(var p=new Pen(Color.White,2))g.DrawEllipse(p,x-7,y-7,14,14);if(Focused)ControlPaint.DrawFocusRectangle(g,ClientRectangle);}
}

static class DetailsUi {
    public static readonly Color Background=Color.FromArgb(14,18,16),Surface=Color.FromArgb(27,35,30),Surface2=Color.FromArgb(35,46,39),Text=Color.FromArgb(244,247,245),Muted=Color.FromArgb(158,171,162),Accent=Color.FromArgb(113,190,126);
    public static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static void Button(Button b){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=Accent;b.ForeColor=Color.FromArgb(15,26,18);b.Cursor=Cursors.Hand;using(var p=Round(new Rectangle(0,0,b.Width,b.Height),11))b.Region=new Region(p);}
}
class DetailsCard : Panel {
    public DetailsCard(){DoubleBuffered=true;BackColor=DetailsUi.Surface;Resize+=delegate{using(var p=DetailsUi.Round(new Rectangle(0,0,Width,Height),18))Region=new Region(p);};}
}
class DetailsBar : Control {
    double remaining;public double Remaining{get{return remaining;}set{remaining=Math.Max(0,Math.Min(100,value));Invalidate();}}
    public DetailsBar(){DoubleBuffered=true;BackColor=DetailsUi.Surface;Height=10;}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2;using(var p=new Pen(DetailsUi.Surface2,7)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,4,y,Width-4,y);}if(remaining>0)using(var p=new Pen(UsageContext.ColorFor(remaining),7)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,4,y,4+(int)((Width-8)*remaining/100),y);}}
}

class DetailsForm : Form {
    readonly System.Windows.Forms.Timer colorSaveTimer=new System.Windows.Forms.Timer {Interval=350};
    public event EventHandler ColorsChanged;
    Label main,updated,tokens;Panel windows;Button refresh;
    readonly Color bg=DetailsUi.Background,surface=DetailsUi.Surface,muted=DetailsUi.Muted;
    public event EventHandler RefreshRequested;
    public DetailsForm() {
        Text="GPT 사용량";ClientSize=new Size(480,550);MinimumSize=MaximumSize=Size;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=bg;ForeColor=DetailsUi.Text;Font=new Font("맑은 고딕",10);ShowInTaskbar=true;
        Controls.Add(new Label {Text="GPT Usage",Location=new Point(26,22),Size=new Size(420,34),Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=DetailsUi.Text});
        main=new Label {Location=new Point(27,62),Size=new Size(425,42),Font=new Font("맑은 고딕",22,FontStyle.Bold),Text="사용량 확인 중…",ForeColor=DetailsUi.Text};Controls.Add(main);
        updated=new Label {Location=new Point(29,108),Size=new Size(420,24),ForeColor=muted};Controls.Add(updated);
        windows=new DetailsCard {Location=new Point(24,145),Size=new Size(432,158)};Controls.Add(windows);
        tokens=new Label {Location=new Point(27,317),Size=new Size(425,28),ForeColor=muted};Controls.Add(tokens);
        var colors=new DetailsCard {Location=new Point(24,354),Size=new Size(432,128)};Controls.Add(colors);
        var rainbow=new RadioButton {Text="무지개 모드",Location=new Point(18,12),Size=new Size(170,28),Checked=!WidgetColors.Single};var single=new RadioButton {Text="단일색 선택",Location=new Point(224,12),Size=new Size(184,28),Checked=WidgetColors.Single};colors.Controls.Add(rainbow);colors.Controls.Add(single);
        var hue=new WidgetHueSlider {Location=new Point(16,47),Size=new Size(398,30),Hue=WidgetColors.Hue};colors.Controls.Add(hue);
        var colorHint=new Label {Location=new Point(18,88),Size=new Size(396,25),ForeColor=muted};colors.Controls.Add(colorHint);
        Action updateColors=delegate{WidgetColors.Single=single.Checked;WidgetColors.Hue=hue.Hue;colorHint.Text=single.Checked?"선택한 색상을 유지합니다 · 변경 사항 자동 저장":"잔여량에 따라 무지개 색상이 자동으로 바뀝니다";windows.Invalidate(true);colorSaveTimer.Stop();colorSaveTimer.Start();if(ColorsChanged!=null)ColorsChanged(this,EventArgs.Empty);};
        rainbow.CheckedChanged+=delegate{if(rainbow.Checked)updateColors();};single.CheckedChanged+=delegate{if(single.Checked)updateColors();};hue.ValueChanged+=delegate{single.Checked=true;updateColors();};colorHint.Text=single.Checked?"선택한 색상을 유지합니다 · 변경 사항 자동 저장":"잔여량에 따라 무지개 색상이 자동으로 바뀝니다";
        colorSaveTimer.Tick+=delegate{colorSaveTimer.Stop();WidgetColors.Save();};
        refresh=new Button {Text="지금 새로고침",Location=new Point(306,496),Size=new Size(150,40)};DetailsUi.Button(refresh);Controls.Add(refresh);
        refresh.Click+=delegate {if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
    }
    public void Loading(){refresh.Enabled=false;updated.Text="Codex에서 최신 사용량을 읽고 있습니다…";}
    public void ShowError(string error){main.Text="사용량을 읽지 못했습니다";updated.Text=error;refresh.Enabled=true;windows.Controls.Clear();}
    public void UpdateData(UsageSnapshot snapshot) {
        refresh.Enabled=true;double? remaining=snapshot.Remaining;
        main.Text=remaining.HasValue?"주간 남은 사용량  "+remaining.Value.ToString("0")+"%":"사용량 정보 없음";
        updated.Text="마지막 갱신  "+snapshot.Updated.ToString("yyyy-MM-dd HH:mm:ss");
        windows.Controls.Clear();int y=14;
        foreach(var item in snapshot.Windows) {
            var label=new Label {Text=item.Name+"  "+item.Remaining.ToString("0")+"% 남음",Location=new Point(18,y),Size=new Size(205,24),ForeColor=DetailsUi.Text,BackColor=surface,Font=new Font("맑은 고딕",10,FontStyle.Bold)};windows.Controls.Add(label);
            string reset=item.ResetLocal.HasValue?"초기화 "+item.ResetLocal.Value.ToString("MM/dd HH:mm"):"";
            var resetLabel=new Label {Text=reset,Location=new Point(225,y),Size=new Size(186,24),TextAlign=ContentAlignment.MiddleRight,ForeColor=muted,BackColor=surface};windows.Controls.Add(resetLabel);
            windows.Controls.Add(new DetailsBar {Location=new Point(18,y+31),Size=new Size(393,10),Remaining=item.Remaining});y+=64;
        }
        if(snapshot.Windows.Count==0)windows.Controls.Add(new Label {Text="현재 계정에서 한도 정보를 제공하지 않습니다.",Location=new Point(18,20),Size=new Size(395,30),ForeColor=muted,BackColor=surface});
        tokens.Text="오늘 토큰  "+FormatTokens(snapshot.TodayTokens)+"     누적 토큰  "+FormatTokens(snapshot.LifetimeTokens);
    }
    static string FormatTokens(long? value){if(!value.HasValue)return "—";if(value.Value>=1000000000)return (value.Value/1000000000.0).ToString("0.00")+"B";if(value.Value>=1000000)return (value.Value/1000000.0).ToString("0.0")+"M";if(value.Value>=1000)return (value.Value/1000.0).ToString("0.0")+"K";return value.Value.ToString();}
    protected override void Dispose(bool disposing){if(disposing){if(colorSaveTimer.Enabled)WidgetColors.Save();colorSaveTimer.Dispose();}base.Dispose(disposing);}
}

class UsageWidgetForm : Form {
    double? remaining;
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly System.Windows.Forms.Timer anchorTimer=new System.Windows.Forms.Timer();
    IntPtr taskbarHandle=IntPtr.Zero;
    public event EventHandler DetailsRequested;
    public event EventHandler RefreshRequested;
    public event EventHandler ExitRequested;

    public UsageWidgetForm() {
        Text="GPT Usage Tray";ClientSize=new Size(44,44);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false;TopMost=false;BackColor=Color.Black;DoubleBuffered=true;Cursor=Cursors.Hand;
        menu.Items.Add("상세 보기",null,delegate {if(DetailsRequested!=null)DetailsRequested(this,EventArgs.Empty);});
        menu.Items.Add("지금 새로고침",null,delegate {if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);});
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료",null,delegate {if(ExitRequested!=null)ExitRequested(this,EventArgs.Empty);});
        ContextMenuStrip=menu;
        MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&DetailsRequested!=null)DetailsRequested(this,EventArgs.Empty);};
        Shown+=delegate {AttachToTaskbar();RenderLayered();};
        anchorTimer.Interval=3000;anchorTimer.Tick+=delegate {if(Visible)AttachToTaskbar();};anchorTimer.Start();
        ApplyCircleShape();
    }

    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x80|0x80000;return value;}}

    public void UpdateData(UsageSnapshot snapshot) {
        remaining=snapshot.Remaining;
        RenderLayered();
    }

    public void ShowError() {remaining=null;RenderLayered();}

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
        SetWindowPos(Handle,new IntPtr(-1),x,y,Width,Height,0x0010|0x0040);RenderLayered();
    }

    void ApplyCircleShape() {
        IntPtr region=CreateEllipticRgn(0,0,Width+1,Height+1);
        Region=Region.FromHrgn(region);DeleteObject(region);
    }

    public Bitmap CreateGaugeBitmap() {
        var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);using(Graphics g=Graphics.FromImage(bitmap)) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        // Layered windows pass clicks through alpha-zero pixels. An imperceptible
        // interior fill makes the whole circle clickable, including around digits.
        using(var hitArea=new SolidBrush(Color.FromArgb(1,0,0,0)))g.FillEllipse(hitArea,1,1,Width-2,Height-2);
        double value=remaining.HasValue?Math.Max(0,Math.Min(100,remaining.Value)):0;
        Color color=remaining.HasValue?UsageContext.ColorFor(value):Color.FromArgb(125,135,150);
        RectangleF ring=new RectangleF(4,4,36,36);
        using(var track=new Pen(Color.FromArgb(125,135,150),5)){track.StartCap=LineCap.Round;track.EndCap=LineCap.Round;g.DrawArc(track,ring,-90,359.8f);}
        if(remaining.HasValue&&value>0)using(var progress=new Pen(color,5)){progress.StartCap=LineCap.Round;progress.EndCap=LineCap.Round;g.DrawArc(progress,ring,-90,(float)(Math.Min(99.9,value)/100*359.8));}
        string percent=remaining.HasValue?Math.Round(value).ToString("0")+"%":"?";
        float percentSize=percent.Length>=4?11:14;
        using(var font=new Font("Segoe UI",percentSize,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var textBrush=new SolidBrush(ReadTaskbarTextColor()))
        using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(percent,font,textBrush,ring,format);
        }return bitmap;
    }

    static Color ReadTaskbarTextColor() {
        try {using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){object value=key==null?null:key.GetValue("SystemUsesLightTheme");bool light=value==null||Convert.ToInt32(value)!=0;return light?Color.Black:Color.White;}}
        catch{return Color.Black;}
    }

    void RenderLayered() {
        if(!IsHandleCreated||IsDisposed)return;
        using(var bitmap=CreateGaugeBitmap()) {
            IntPtr screenDc=GetDC(IntPtr.Zero),memoryDc=CreateCompatibleDC(screenDc),hBitmap=bitmap.GetHbitmap(Color.FromArgb(0)),oldBitmap=IntPtr.Zero;
            try {
                oldBitmap=SelectObject(memoryDc,hBitmap);
                var destination=new POINT {X=Left,Y=Top};var size=new SIZE {Width=Width,Height=Height};var source=new POINT();
                var blend=new BLENDFUNCTION {BlendOp=0,BlendFlags=0,SourceConstantAlpha=255,AlphaFormat=1};
                UpdateLayeredWindow(Handle,screenDc,ref destination,ref size,memoryDc,ref source,0,ref blend,2);
            } finally {
                if(oldBitmap!=IntPtr.Zero)SelectObject(memoryDc,oldBitmap);DeleteObject(hBitmap);DeleteDC(memoryDc);ReleaseDC(IntPtr.Zero,screenDc);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) {}

    protected override void Dispose(bool disposing) {if(disposing){anchorTimer.Dispose();menu.Dispose();}base.Dispose(disposing);}
    [DllImport("gdi32.dll")]static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern IntPtr FindWindow(string className,string windowName);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr handle,out RECT rect);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtr",SetLastError=true)]static extern IntPtr SetWindowLongPtr(IntPtr handle,int index,IntPtr value);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr handle);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr handle,IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr handle,IntPtr destinationDc,ref POINT destination,ref SIZE size,IntPtr sourceDc,ref POINT source,int colorKey,ref BLENDFUNCTION blend,int flags);
    [StructLayout(LayoutKind.Sequential)]struct RECT {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct POINT {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct SIZE {public int Width,Height;}
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct BLENDFUNCTION {public byte BlendOp,BlendFlags,SourceConstantAlpha,AlphaFormat;}
}

class UsageContext : ApplicationContext {
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName="GPT Usage Tray";
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly Control marshal=new Control();
    DetailsForm details;UsageWidgetForm widget;bool refreshing;UsageSnapshot current;
    public UsageContext() {
        marshal.CreateControl();
        timer.Interval=60000;timer.Tick+=delegate{Refresh();};timer.Start();
        SetStartup(true);
        widget=new UsageWidgetForm();widget.DetailsRequested+=delegate{ShowDetails();};widget.RefreshRequested+=delegate{Refresh();};widget.ExitRequested+=delegate{Exit();};widget.Show();
        Refresh();
    }
    public static Color ColorFor(double remaining){
        remaining=Math.Max(0,Math.Min(100,remaining));
        return WidgetColors.FromHue(WidgetColors.Single?WidgetColors.Hue:remaining*3);
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
        if(details!=null)details.UpdateData(snapshot);
        if(widget!=null)widget.UpdateData(snapshot);
    }
    void SetError(string error){if(details!=null)details.ShowError(error);if(widget!=null)widget.ShowError();}
    void ShowDetails(){if(details==null){details=new DetailsForm();details.RefreshRequested+=delegate{Refresh();};details.ColorsChanged+=delegate{if(current!=null)widget.UpdateData(current);};}if(current!=null)details.UpdateData(current);details.Show();details.WindowState=FormWindowState.Normal;details.Activate();}
    void SetStartup(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(enabled)key.SetValue(RunName,"\""+Application.ExecutablePath+"\"");else key.DeleteValue(RunName,false);}}
    void Exit(){timer.Stop();if(details!=null)details.Dispose();if(widget!=null)widget.Dispose();ExitThread();}
    static void TrimWorkingSet(){try{using(var process=Process.GetCurrentProcess())SetProcessWorkingSetSize(process.Handle,new IntPtr(-1),new IntPtr(-1));}catch{}}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();marshal.Dispose();}base.Dispose(disposing);}
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
            try {var sample=new UsageSnapshot();sample.Windows.Add(new UsageWindow{Name="주간",Used=85,DurationMinutes=10080});using(var form=new UsageWidgetForm()){form.UpdateData(sample);using(var bitmap=form.CreateGaugeBitmap())bitmap.Save(args[1]);}Environment.ExitCode=0;}
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        bool first;using(var mutex=new Mutex(true,"Local\\GPT-Usage-Tray",out first)){if(!first)return;Application.Run(new UsageContext());}
    }
}
