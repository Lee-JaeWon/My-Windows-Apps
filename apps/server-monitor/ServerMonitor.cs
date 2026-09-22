using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Text.RegularExpressions;

[assembly: AssemblyTitle("Lab Server Monitor")]
[assembly: AssemblyProduct("Lab Server Monitor")]
[assembly: AssemblyVersion("1.3.2.0")]

static class Ui {
    public static Color Background=Color.FromArgb(14,18,16), Surface=Color.FromArgb(27,35,30), Surface2=Color.FromArgb(35,46,39);
    public static Color Text=Color.FromArgb(244,247,245), Muted=Color.FromArgb(158,171,162), Accent=Color.FromArgb(113,190,126), Track=Color.FromArgb(55,68,59);
    public static int Hue=130;
    public static Color Hsl(double hue,double saturation,double lightness){hue=((hue%360)+360)%360;double c=(1-Math.Abs(2*lightness-1))*saturation,x=c*(1-Math.Abs(hue/60%2-1)),m=lightness-c/2;double r=0,g=0,b=0;if(hue<60){r=c;g=x;}else if(hue<120){r=x;g=c;}else if(hue<180){g=c;b=x;}else if(hue<240){g=x;b=c;}else if(hue<300){r=x;b=c;}else{r=c;b=x;}return Color.FromArgb((int)Math.Round((r+m)*255),(int)Math.Round((g+m)*255),(int)Math.Round((b+m)*255));}
    static Color Rotate(Color original){return Hsl(original.GetHue()+Hue-130,original.GetSaturation(),original.GetBrightness());}
    public static Color[] Palette(){return new[]{Background,Surface,Surface2,Text,Muted,Accent,Track};}
    public static void SetHue(int hue){Hue=Math.Max(0,Math.Min(359,hue));Background=Rotate(Color.FromArgb(14,18,16));Surface=Rotate(Color.FromArgb(27,35,30));Surface2=Rotate(Color.FromArgb(35,46,39));Text=Rotate(Color.FromArgb(244,247,245));Muted=Rotate(Color.FromArgb(158,171,162));Accent=Rotate(Color.FromArgb(113,190,126));Track=Rotate(Color.FromArgb(55,68,59));}
    public static void LoadTheme(string directory){int hue;try{if(int.TryParse(File.ReadAllText(Path.Combine(directory,"theme-hue.txt")),out hue)&&hue>=0&&hue<=359){SetHue(hue);return;}}catch{}SetHue(130);}
    public static void Recolor(Control control,Color[] before,Color[] after){for(int i=0;i<before.Length;i++){if(control.BackColor.ToArgb()==before[i].ToArgb()){control.BackColor=after[i];break;}}for(int i=0;i<before.Length;i++){if(control.ForeColor.ToArgb()==before[i].ToArgb()){control.ForeColor=after[i];break;}}foreach(Control child in control.Controls)Recolor(child,before,after);control.Invalidate();}
    public static GraphicsPath Round(Rectangle r,int radius) {
        var p=new GraphicsPath();int d=radius*2;
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static void StyleButton(Button b,Color color) {b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=color;b.ForeColor=Text;b.Cursor=Cursors.Hand;using(var p=Round(new Rectangle(0,0,b.Width,b.Height),10))b.Region=new Region(p);b.Resize+=delegate {using(var p=Round(new Rectangle(0,0,b.Width,b.Height),10))b.Region=new Region(p);};}
    public static void StyleTextBox(TextBox box) {box.BackColor=Surface2;box.ForeColor=Text;box.BorderStyle=BorderStyle.FixedSingle;}
}

class RoundedPanel : Panel {
    public Color FillColor=Ui.Surface;public int Radius=16;
    public RoundedPanel(){DoubleBuffered=true;BackColor=Ui.Background;}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Ui.Round(new Rectangle(0,0,Width-1,Height-1),Radius))using(var b=new SolidBrush(FillColor))e.Graphics.FillPath(b,p);}
}

enum ServerViewMode { List, One }

class HueSlider : Control {
    int hue=130;readonly ToolTip hint=new ToolTip();public event EventHandler HueChanged;
    public int Hue {get{return hue;}set{value=Math.Max(0,Math.Min(359,value));if(hue==value)return;hue=value;Invalidate();if(HueChanged!=null)HueChanged(this,EventArgs.Empty);}}
    public HueSlider(){Size=new Size(222,28);DoubleBuffered=true;BackColor=Ui.Background;Cursor=Cursors.Hand;TabStop=true;AccessibleName="앱 색조";AccessibleRole=AccessibleRole.Slider;hint.SetToolTip(this,"드래그로 색조 변경 · 방향키로 미세 조절 · Home으로 기본 초록색");}
    void Pick(int x){Hue=(int)Math.Round(Math.Max(0,Math.Min(1,(x-9.0)/Math.Max(1,Width-18)))*359);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();Capture=true;Pick(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture&&e.Button==MouseButtons.Left)Pick(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left)Capture=false;}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||key==Keys.Home||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left)Hue-=1;else if(e.KeyCode==Keys.Right)Hue+=1;else if(e.KeyCode==Keys.Home)Hue=130;else return;e.Handled=true;}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2;var r=new Rectangle(9,y-4,Math.Max(1,Width-18),8);using(var path=Ui.Round(r,4))using(var brush=new LinearGradientBrush(r,Color.Red,Color.Red,0f)){var colors=new Color[7];var positions=new float[7];for(int i=0;i<7;i++){colors[i]=Ui.Hsl(i*60,0.7,0.6);positions[i]=i/6f;}brush.InterpolationColors=new ColorBlend{Colors=colors,Positions=positions};g.FillPath(brush,path);}float x=9+(Width-18)*hue/359f;using(var b=new SolidBrush(Ui.Accent))g.FillEllipse(b,x-7,y-7,14,14);using(var p=new Pen(Ui.Text,2))g.DrawEllipse(p,x-7,y-7,14,14);if(Focused)ControlPaint.DrawFocusRectangle(g,ClientRectangle,Ui.Muted,BackColor);}
    protected override void Dispose(bool disposing){if(disposing)hint.Dispose();base.Dispose(disposing);}
}

class ModeToggle : Control {
    ServerViewMode mode=ServerViewMode.List;readonly Font font=new Font("Segoe UI",9,FontStyle.Bold);
    public event EventHandler ModeChanged;
    public ServerViewMode Mode {get{return mode;}set{if(mode==value)return;mode=value;Invalidate();if(ModeChanged!=null)ModeChanged(this,EventArgs.Empty);}}
    public ModeToggle(){Size=new Size(164,36);DoubleBuffered=true;Cursor=Cursors.Hand;BackColor=Ui.Background;}
    protected override void OnMouseUp(MouseEventArgs e){Mode=e.X<Width/2?ServerViewMode.List:ServerViewMode.One;base.OnMouseUp(e);}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Ui.Round(new Rectangle(0,0,Width-1,Height-1),12))using(var b=new SolidBrush(Ui.Surface))g.FillPath(b,p);int x=mode==ServerViewMode.List?3:Width/2;using(var p=Ui.Round(new Rectangle(x,3,Width/2-3,Height-6),10))using(var b=new SolidBrush(Ui.Accent))g.FillPath(b,p);Draw(g,"List",new Rectangle(0,0,Width/2,Height),mode==ServerViewMode.List?Color.FromArgb(15,26,18):Ui.Muted);Draw(g,"One",new Rectangle(Width/2,0,Width/2,Height),mode==ServerViewMode.One?Color.FromArgb(15,26,18):Ui.Muted);}
    void Draw(Graphics g,string text,Rectangle r,Color color){TextRenderer.DrawText(g,text,font,r,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);}
    protected override void Dispose(bool disposing){if(disposing)font.Dispose();base.Dispose(disposing);}
}

public class ServerConfig {
    public string Host {get;set;}
    public string User {get;set;}
    public string HostKey {get;set;}
    public string PasswordFile {get;set;}
}
public class Config {public ServerConfig[] Servers {get;set;}}
static class ConfigStore {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    public static string ConfigPath(string directory) {return Path.Combine(directory,"servers.json");}
    public static Config Load(string directory) {
        string path=ConfigPath(directory);if(!File.Exists(path))return new Config {Servers=new ServerConfig[0]};
        try {var value=Json.Deserialize<Config>(File.ReadAllText(path,Encoding.UTF8));if(value==null||value.Servers==null)value=new Config {Servers=new ServerConfig[0]};return value;}
        catch {return new Config {Servers=new ServerConfig[0]};}
    }
    public static void Save(string directory,IList<ServerConfig> servers) {
        Directory.CreateDirectory(directory);
        var config=new Config {Servers=new List<ServerConfig>(servers).ToArray()};
        File.WriteAllText(ConfigPath(directory),Json.Serialize(config),new UTF8Encoding(false));
    }
    public static void MigrateExisting(string directory) {
        Directory.CreateDirectory(directory);if(File.Exists(ConfigPath(directory)))return;
        string oldDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings");string oldConfig=ConfigPath(oldDirectory);
        if(!File.Exists(oldConfig))return;
        File.Copy(oldConfig,ConfigPath(directory),true);
        var config=Load(directory);foreach(var server in config.Servers) {
            if(server==null||string.IsNullOrWhiteSpace(server.PasswordFile))continue;
            string source=Path.Combine(oldDirectory,server.PasswordFile),target=Path.Combine(directory,server.PasswordFile);
            if(File.Exists(source)&&!File.Exists(target))File.Copy(source,target);
        }
    }
}

static class HostKeyLookup {
    public static string Fetch(string host,string user,string password) {
        string plink=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"plink.exe");if(!File.Exists(plink))throw new FileNotFoundException("Plink를 찾을 수 없습니다.",plink);
        string passwordFile=Path.Combine(Path.GetTempPath(),"LabServerMonitor-"+Guid.NewGuid().ToString("N")+".password.txt");
        try {
            File.WriteAllText(passwordFile,password,new UTF8Encoding(false));
            using(var process=new Process()) {
                process.StartInfo=new ProcessStartInfo(plink,"-ssh -v -batch -T -noagent -l "+Q(user)+" -pwfile "+Q(passwordFile)+" "+Q(host)+" exit") {
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                };
                process.Start();Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(15000)){try{process.Kill();}catch{}throw new TimeoutException("서버 연결 시간이 초과되었습니다.");}
                Task.WaitAll(output,error);string text=output.Result+Environment.NewLine+error.Result;
                Match match=Regex.Match(text,@"SHA256:[A-Za-z0-9+/]{20,}={0,2}");if(match.Success)return match.Value;
                if(text.IndexOf("Network error",StringComparison.OrdinalIgnoreCase)>=0)throw new Exception("서버에 연결할 수 없습니다. 주소와 네트워크를 확인해 주세요.");
                throw new Exception("SSH 서버 키 지문을 찾지 못했습니다.");
            }
        } finally {try{if(File.Exists(passwordFile))File.Delete(passwordFile);}catch{}}
    }
    static string Q(string value){return "\""+(value??"").Replace("\"","\\\"")+"\"";}
}

class LoginManagerForm : Form {
    readonly string directory;readonly List<ServerConfig> servers=new List<ServerConfig>();
    readonly ListBox list=new ListBox();readonly TextBox host=new TextBox(),user=new TextBox(),password=new TextBox(),hostKey=new TextBox();
    int selected=-1;public bool Changed {get;private set;}
    public LoginManagerForm(string configDirectory) {
        directory=configDirectory;Text="Login Manager";ClientSize=new Size(720,430);MinimumSize=MaximumSize=Size;StartPosition=FormStartPosition.CenterParent;
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;BackColor=Ui.Background;ForeColor=Ui.Text;Font=new Font("맑은 고딕",10);
        Controls.Add(new Label {Text="저장된 로그인",Location=new Point(20,18),Size=new Size(220,26),Font=new Font("맑은 고딕",12,FontStyle.Bold)});
        list.Location=new Point(20,52);list.Size=new Size(235,292);list.BackColor=Ui.Surface;list.ForeColor=Ui.Text;list.BorderStyle=BorderStyle.None;list.ItemHeight=30;Controls.Add(list);
        var add=new Button {Text="새 로그인",Location=new Point(20,356),Size=new Size(112,38)};var remove=new Button {Text="삭제",Location=new Point(143,356),Size=new Size(112,38)};Ui.StyleButton(add,Ui.Surface2);Ui.StyleButton(remove,Color.FromArgb(102,55,59));Controls.Add(add);Controls.Add(remove);
        int x=286;AddLabel("서버 주소",x,24);host.SetBounds(x,50,402,30);Ui.StyleTextBox(host);Controls.Add(host);
        AddLabel("사용자 이름",x,91);user.SetBounds(x,117,402,30);Ui.StyleTextBox(user);Controls.Add(user);
        AddLabel("비밀번호",x,158);password.SetBounds(x,184,330,30);password.UseSystemPasswordChar=true;Ui.StyleTextBox(password);Controls.Add(password);
        var showPassword=new CheckBox {Text="표시",Location=new Point(626,186),Size=new Size(62,28),ForeColor=Color.White};Controls.Add(showPassword);
        AddLabel("SSH 서버 키 지문",x,225);hostKey.SetBounds(x,251,292,30);Ui.StyleTextBox(hostKey);Controls.Add(hostKey);
        var lookup=new Button {Text="지문 조회",Location=new Point(588,249),Size=new Size(100,34)};Ui.StyleButton(lookup,Ui.Surface2);Controls.Add(lookup);
        Controls.Add(new Label {Text="예: SHA256:...  서버 관리자에게 확인한 지문을 입력하세요.",Location=new Point(x,286),Size=new Size(402,25),ForeColor=Color.FromArgb(153,167,188)});
        var save=new Button {Text="저장",Location=new Point(486,356),Size=new Size(96,38)};Ui.StyleButton(save,Ui.Accent);Controls.Add(save);
        var close=new Button {Text="닫기",Location=new Point(592,356),Size=new Size(96,38)};Ui.StyleButton(close,Ui.Surface2);Controls.Add(close);
        list.SelectedIndexChanged+=delegate {LoadSelected();};add.Click+=delegate {ClearFields();};remove.Click+=delegate {RemoveSelected();};
        save.Click+=delegate {SaveCurrent();};close.Click+=delegate {Close();};lookup.Click+=delegate {LookupHostKey(lookup);};showPassword.CheckedChanged+=delegate {password.UseSystemPasswordChar=!showPassword.Checked;};
        LoadList();
    }
    void AddLabel(string text,int x,int y){Controls.Add(new Label {Text=text,Location=new Point(x,y),Size=new Size(402,24),ForeColor=Ui.Muted});}
    void LoadList() {
        servers.Clear();servers.AddRange(ConfigStore.Load(directory).Servers);list.Items.Clear();
        foreach(var server in servers)list.Items.Add(server.Host+"  ·  "+server.User);
        if(list.Items.Count>0)list.SelectedIndex=0;else ClearFields();
    }
    void LoadSelected() {
        selected=list.SelectedIndex;if(selected<0||selected>=servers.Count)return;var item=servers[selected];
        host.Text=item.Host??"";user.Text=item.User??"";hostKey.Text=item.HostKey??"";password.Text="";
        if(!string.IsNullOrWhiteSpace(item.PasswordFile)){string path=Path.Combine(directory,item.PasswordFile);if(File.Exists(path))password.Text=File.ReadAllText(path);}
    }
    void ClearFields(){list.ClearSelected();selected=-1;host.Text="";user.Text="";password.Text="";hostKey.Text="";host.Focus();}
    void SaveCurrent() {
        string hostValue=host.Text.Trim(),userValue=user.Text.Trim(),keyValue=hostKey.Text.Trim();
        if(hostValue.Length==0||userValue.Length==0||password.Text.Length==0||keyValue.Length==0){MessageBox.Show(this,"서버 주소, 사용자 이름, 비밀번호, 서버 키 지문을 모두 입력해 주세요.","Login Manager");return;}
        ServerConfig item;
        if(selected>=0&&selected<servers.Count)item=servers[selected];else {item=new ServerConfig {PasswordFile="login-"+Guid.NewGuid().ToString("N")+".password.txt"};servers.Add(item);selected=servers.Count-1;}
        int savedIndex=selected;
        item.Host=hostValue;item.User=userValue;item.HostKey=keyValue;Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,item.PasswordFile),password.Text,new UTF8Encoding(false));
        ConfigStore.Save(directory,servers);Changed=true;LoadList();if(list.Items.Count>0)list.SelectedIndex=Math.Min(savedIndex,list.Items.Count-1);
    }
    void RemoveSelected() {
        if(selected<0||selected>=servers.Count)return;var item=servers[selected];
        if(MessageBox.Show(this,"이 로그인을 삭제할까요?", "Login Manager",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        servers.RemoveAt(selected);ConfigStore.Save(directory,servers);
        if(!string.IsNullOrWhiteSpace(item.PasswordFile)){string path=Path.Combine(directory,item.PasswordFile);if(File.Exists(path))File.Delete(path);}
        Changed=true;LoadList();
    }
    void LookupHostKey(Button button) {
        string hostValue=host.Text.Trim(),userValue=user.Text.Trim(),passwordValue=password.Text;
        if(hostValue.Length==0||userValue.Length==0||passwordValue.Length==0){MessageBox.Show(this,"서버 주소, 사용자 이름, 비밀번호를 먼저 입력해 주세요.","지문 조회");return;}
        button.Enabled=false;button.Text="조회 중…";
        Task.Run(delegate{return HostKeyLookup.Fetch(hostValue,userValue,passwordValue);}).ContinueWith(task=>BeginInvoke((Action)delegate {
            button.Enabled=true;button.Text="지문 조회";
            if(task.IsFaulted)MessageBox.Show(this,task.Exception.GetBaseException().Message,"지문 조회 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);
            else {hostKey.Text=task.Result;MessageBox.Show(this,"SSH 서버 키 지문을 가져왔습니다. 저장을 눌러 완료하세요.","지문 조회");}
        }));
    }
}
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
    public Session(ServerConfig config,Sample preview) {
        Config=config;configDirectory="";worker=null;current.Data=preview;current.Received=DateTime.UtcNow;current.Connected=true;current.State="연결됨";
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
        if(worker==null||worker.Join(3000))stop.Dispose();
    }
}
class ServerCard : Control {
    readonly Session session;ServerViewMode mode;
    readonly Font hostFont=new Font("Segoe UI",16,FontStyle.Bold), labelFont=new Font("Segoe UI",9), valueFont=new Font("Segoe UI",14,FontStyle.Bold), ringFont=new Font("Segoe UI",15,FontStyle.Bold), smallFont=new Font("맑은 고딕",9);
    public ServerCard(Session source,ServerViewMode viewMode) {session=source;mode=viewMode;DoubleBuffered=true;BackColor=Ui.Background;}
    public ServerViewMode Mode {get{return mode;}set{mode=value;Invalidate();}}
    public int DesiredHeight {get {var state=session.Snapshot();int count=state.Data!=null&&state.Data.gpus!=null?state.Data.gpus.Length:0;if(mode==ServerViewMode.One)return Math.Max(400,140+Math.Max(1,(count+3)/4)*170+78);return Math.Max(464,121+count*108+120);}}
    void TextAt(Graphics g,string text,Font font,Color color,int x,int y,int width,int height,TextFormatFlags extra) {TextRenderer.DrawText(g,text,font,new Rectangle(x,y,width,height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|extra);}
    void TextAt(Graphics g,string text,Font font,Color color,int x,int y,int width,int height){TextAt(g,text,font,color,x,y,width,height,TextFormatFlags.Left);}
    void Bar(Graphics g,int x,int y,int width,double used,double total,Color color) {using(var pen=new Pen(Ui.Track,7)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,x,y,x+width,y);}if(total>0)using(var pen=new Pen(color,7)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,x,y,x+(int)(width*Math.Max(0,Math.Min(1,used/total))),y);}}
    void Ring(Graphics g,Gpu gpu,Rectangle cell) {
        int diameter=Math.Min(94,cell.Width-34),x=cell.X+(cell.Width-diameter)/2,y=cell.Y+24;var ring=new Rectangle(x+7,y+7,diameter-14,diameter-14);
        double ratio=gpu.used.HasValue&&gpu.total.HasValue&&gpu.total.Value>0?Math.Max(0,Math.Min(1,gpu.used.Value/gpu.total.Value)):0;
        using(var pen=new Pen(Ui.Track,9)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawArc(pen,ring,-90,359.8f);}if(ratio>0)using(var pen=new Pen(Ui.Accent,9)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawArc(pen,ring,-90,(float)(359.8*ratio));}
        string percent=gpu.total.HasValue&&gpu.total.Value>0?(ratio*100).ToString("0")+"%":"—";TextAt(g,percent,ringFont,Ui.Text,x,y,diameter,diameter,TextFormatFlags.HorizontalCenter);
        TextAt(g,"GPU "+gpu.index,labelFont,Ui.Muted,cell.X,cell.Y,cell.Width,24,TextFormatFlags.HorizontalCenter);
        string memory=gpu.used.HasValue&&gpu.total.HasValue?(gpu.used.Value/1024).ToString("0.0")+" / "+(gpu.total.Value/1024).ToString("0.0")+" GB":"메모리 —";
        TextAt(g,memory,smallFont,Ui.Text,cell.X,cell.Y+121,cell.Width,22,TextFormatFlags.HorizontalCenter);
        string stats=(gpu.utilization.HasValue?"Usage "+gpu.utilization.Value.ToString("0")+"%":"Usage —")+"   ·   "+(gpu.temperature.HasValue?gpu.temperature.Value.ToString("0")+"°C":"—");
        TextAt(g,stats,smallFont,Ui.Accent,cell.X,cell.Y+143,cell.Width,22,TextFormatFlags.HorizontalCenter);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Ui.Round(new Rectangle(0,0,Width-1,Height-1),18))using(var b=new SolidBrush(Ui.Surface))g.FillPath(b,p);
        var state=session.Snapshot();double age=(DateTime.UtcNow-state.Received).TotalSeconds;bool live=state.Connected&&age<4;var signal=live?Ui.Accent:Color.FromArgb(232,177,92);var data=live?state.Data:null;
        TextAt(g,session.Config.Host,hostFont,Ui.Text,24,18,Width-48,31);TextAt(g,session.Config.User,labelFont,Ui.Muted,24,49,Width-48,21);
        using(var brush=new SolidBrush(signal))g.FillEllipse(brush,25,84,8,8);TextAt(g,state.Connected&&!live?"응답 지연":state.State,smallFont,signal,42,75,Width-66,25);
        if(mode==ServerViewMode.One)PaintOne(g,data,live);else PaintList(g,data,live);
    }
    void PaintList(Graphics g,Sample data,bool live) {
        int y=118;if(data!=null&&data.gpus!=null&&data.gpus.Length>0)foreach(var gpu in data.gpus){TextAt(g,"GPU "+gpu.index+"  ·  "+gpu.name,labelFont,Ui.Muted,24,y,Width-48,24);string memory=gpu.used.HasValue&&gpu.total.HasValue?gpu.used.Value.ToString("0")+" MiB / "+gpu.total.Value.ToString("0")+" MiB":"메모리 정보 없음";TextAt(g,memory,valueFont,Ui.Text,24,y+27,Width-48,32);Bar(g,25,y+69,Width-50,gpu.used??0,gpu.total??0,Ui.Accent);TextAt(g,"Usage  "+(gpu.utilization.HasValue?gpu.utilization.Value.ToString("0")+"%":"—")+"   ·   "+(gpu.temperature.HasValue?gpu.temperature.Value.ToString("0")+"°C":"—"),smallFont,Ui.Accent,24,y+77,Width-48,22);y+=108;}
        else {TextAt(g,"GPU",labelFont,Ui.Muted,24,y,Width-48,24);TextAt(g,data==null?"—":"GPU 없음",valueFont,Ui.Muted,24,y+27,Width-48,32);y+=94;}
        PaintRam(g,data,y);PaintFooter(g,live);
    }
    void PaintOne(Graphics g,Sample data,bool live) {
        int y=112;if(data!=null&&data.gpus!=null&&data.gpus.Length>0){int cols=Math.Min(4,data.gpus.Length),cellWidth=(Width-48)/cols;for(int i=0;i<data.gpus.Length;i++){int row=i/4,col=i%4;Ring(g,data.gpus[i],new Rectangle(24+col*cellWidth,y+row*170,cellWidth,168));}y+=((data.gpus.Length+3)/4)*170+8;}else{TextAt(g,data==null?"GPU 데이터를 기다리는 중입니다.":"GPU 없음",valueFont,Ui.Muted,24,y,Width-48,72,TextFormatFlags.HorizontalCenter);y+=94;}PaintRam(g,data,y);PaintFooter(g,live);
    }
    void PaintRam(Graphics g,Sample data,int y){bool ram=data!=null&&data.ram_total>0&&string.IsNullOrEmpty(data.ram_error);string text=ram?"RAM   "+(data.ram_used/1024).ToString("0.0")+" / "+(data.ram_total/1024).ToString("0.0")+" GiB":"RAM   —";TextAt(g,text,smallFont,Ui.Muted,24,y,Width-48,25);Bar(g,25,y+32,Width-50,ram?data.ram_used:0,ram?data.ram_total:0,Ui.Accent);}
    void PaintFooter(Graphics g,bool live){var state=session.Snapshot();TextAt(g,live?"마지막 갱신  "+state.Received.ToLocalTime().ToString("HH:mm:ss"):"상태는 SSH 연결 기준입니다.",smallFont,Ui.Muted,24,Height-37,Width-48,24);}
    protected override void Dispose(bool disposing){if(disposing){hostFont.Dispose();labelFont.Dispose();valueFont.Dispose();ringFont.Dispose();smallFont.Dispose();}base.Dispose(disposing);}
}
class MonitorForm : Form {
    readonly HueSlider hueSlider=new HueSlider();readonly System.Windows.Forms.Timer themeSaveTimer=new System.Windows.Forms.Timer {Interval=350};
    readonly Panel hueEditor=new Panel();readonly Button hueButton=new Button();bool hueEditorOpen;
    readonly Label titleLabel=new Label(),subtitleLabel=new Label();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=1000};readonly List<Session> sessions=new List<Session>();readonly List<ServerCard> cards=new List<ServerCard>();
    readonly FlowLayoutPanel area=new FlowLayoutPanel();readonly string directory;readonly Sample previewData;readonly ModeToggle toggle=new ModeToggle();ServerViewMode mode=ServerViewMode.List;
    public MonitorForm(string configDirectory):this(configDirectory,null){}
    public MonitorForm(string configDirectory,Sample preview) {
        directory=configDirectory;Ui.LoadTheme(directory);previewData=preview;Text="Lab Server Monitor";ClientSize=new Size(1000,680);MinimumSize=new Size(820,670);StartPosition=FormStartPosition.CenterScreen;BackColor=Ui.Background;ForeColor=Ui.Text;Font=new Font("맑은 고딕",10);AutoScaleMode=AutoScaleMode.Dpi;Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
        titleLabel.Text="Lab Server Monitor";titleLabel.Location=new Point(24,43);titleLabel.Size=new Size(560,42);titleLabel.Font=new Font("Segoe UI",23,FontStyle.Bold);titleLabel.AutoEllipsis=true;Controls.Add(titleLabel);
        subtitleLabel.Text="GPU와 RAM 상태를 1초마다 확인합니다";subtitleLabel.Location=new Point(26,87);subtitleLabel.Size=new Size(560,25);subtitleLabel.ForeColor=Ui.Muted;subtitleLabel.AutoEllipsis=true;Controls.Add(subtitleLabel);
        var loginManager=new Button {Text="Login Manager",Size=new Size(116,36),Location=new Point(ClientSize.Width-140,32),Anchor=AnchorStyles.Top|AnchorStyles.Right};Ui.StyleButton(loginManager,Ui.Surface2);Controls.Add(loginManager);
        toggle.Location=new Point(ClientSize.Width-328,32);toggle.Anchor=AnchorStyles.Top|AnchorStyles.Right;toggle.BackColor=Ui.Background;Controls.Add(toggle);
        hueButton.Text="Color";hueButton.Location=new Point(ClientSize.Width-140,78);hueButton.Size=new Size(116,32);hueButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;Ui.StyleButton(hueButton,Ui.Surface2);Controls.Add(hueButton);hueButton.Click+=delegate{ToggleHueEditor();};
        hueEditor.Location=new Point(ClientSize.Width-328,122);hueEditor.Size=new Size(304,44);hueEditor.BackColor=Ui.Surface;hueEditor.Anchor=AnchorStyles.Top|AnchorStyles.Right;hueEditor.Visible=false;Controls.Add(hueEditor);
        hueSlider.Location=new Point(10,8);hueSlider.Size=new Size(242,28);hueSlider.BackColor=Ui.Surface;hueSlider.Hue=Ui.Hue;hueEditor.Controls.Add(hueSlider);
        var resetHue=new Button {Text="↺",Location=new Point(262,8),Size=new Size(32,28),AccessibleName="기본 초록색 복원"};Ui.StyleButton(resetHue,Ui.Surface2);resetHue.Click+=delegate{hueSlider.Hue=130;};hueEditor.Controls.Add(resetHue);
        hueSlider.HueChanged+=delegate{var before=Ui.Palette();Ui.SetHue(hueSlider.Hue);Ui.Recolor(this,before,Ui.Palette());themeSaveTimer.Stop();themeSaveTimer.Start();};themeSaveTimer.Tick+=delegate{themeSaveTimer.Stop();SaveTheme();};
        area.Location=new Point(16,126);area.Size=new Size(ClientSize.Width-32,ClientSize.Height-142);area.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;area.AutoScroll=true;area.WrapContents=true;area.FlowDirection=FlowDirection.LeftToRight;area.TabStop=true;area.BackColor=Ui.Background;Controls.Add(area);
        loginManager.Click+=delegate {using(var manager=new LoginManagerForm(directory)){manager.ShowDialog(this);if(manager.Changed)ReloadServers();}};toggle.ModeChanged+=delegate {mode=toggle.Mode;SaveMode();LayoutCards();};area.Resize+=delegate {LayoutCards();};
        LoadMode();ReloadServers();timer.Tick+=delegate {LayoutCards();foreach(var card in cards)card.Invalidate();};timer.Start();FormClosed+=delegate {if(themeSaveTimer.Enabled)SaveTheme();themeSaveTimer.Stop();themeSaveTimer.Dispose();timer.Stop();timer.Dispose();foreach(var session in sessions)session.Dispose();};
    }
    void SaveTheme(){try{Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"theme-hue.txt"),Ui.Hue.ToString());}catch{}}
    void ToggleHueEditor(){int shift=hueEditor.Height+Math.Max(8,hueEditor.Height/4);SuspendLayout();hueEditorOpen=!hueEditorOpen;hueEditor.Visible=hueEditorOpen;area.Top+=hueEditorOpen?shift:-shift;area.Height+=hueEditorOpen?-shift:shift;hueButton.Text=hueEditorOpen?"Color ▴":"Color";ResumeLayout(true);if(hueEditorOpen)hueSlider.Focus();}
    public void PreviewHue(int hue){if(!hueEditorOpen)ToggleHueEditor();int expandedTop=area.Top;ToggleHueEditor();if(hueEditorOpen||area.Top>=expandedTop)throw new Exception("Color editor collapse failed");ToggleHueEditor();if(!hueEditorOpen||area.Top!=expandedTop||(Visible&&!hueEditor.Visible))throw new Exception("Color editor expand failed");hueSlider.Hue=hue;SaveTheme();}
    string ModePath {get{return Path.Combine(directory,"view-mode.txt");}}
    protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(titleLabel==null||subtitleLabel==null||toggle==null||toggle.Parent==null)return;int gap=Math.Max(12,titleLabel.Left/2);int right=toggle.Left-gap;titleLabel.Width=Math.Max(1,right-titleLabel.Left);subtitleLabel.Width=Math.Max(1,right-subtitleLabel.Left);}
    void LoadMode(){try{if(File.Exists(ModePath)&&File.ReadAllText(ModePath).Trim()=="One")mode=ServerViewMode.One;}catch{}toggle.Mode=mode;}
    void SaveMode(){try{Directory.CreateDirectory(directory);File.WriteAllText(ModePath,mode.ToString());}catch{}}
    void LayoutCards(){if(cards.Count==0)return;int available=Math.Max(1,area.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-8);int columns=mode==ServerViewMode.List&&available>=772?2:1;int width=Math.Max(1,available/columns-16);foreach(var card in cards){card.Mode=mode;card.Width=width;int desired=card.DesiredHeight;if(card.Height!=desired)card.Height=desired;card.Margin=new Padding(8,0,8,14);}}
    void ReloadServers(){foreach(var session in sessions)session.Dispose();sessions.Clear();cards.Clear();area.Controls.Clear();var config=ConfigStore.Load(directory);foreach(var server in config.Servers){if(server==null)continue;var session=previewData==null?new Session(server,directory):new Session(server,previewData);sessions.Add(session);var card=new ServerCard(session,mode){Size=new Size(458,464)};card.MouseEnter+=delegate{area.Focus();};cards.Add(card);area.Controls.Add(card);}if(config.Servers.Length==0)area.Controls.Add(new Label {Text="저장된 로그인이 없습니다. ‘Login Manager’를 눌러 서버를 추가하세요.",AutoSize=false,Size=new Size(700,80),Margin=new Padding(18),Font=new Font("맑은 고딕",13),ForeColor=Ui.Muted});LayoutCards();}
    void SetModeCore(ServerViewMode value){mode=value;toggle.Mode=value;LayoutCards();}
    public void ShowOnePreview(){SetModeCore(ServerViewMode.One);}
    public void SaveCheck(string path){var states=new List<object>();foreach(var session in sessions)states.Add(new {host=session.Config.Host,state=session.Snapshot()});File.WriteAllText(path+".json",new JavaScriptSerializer().Serialize(states));using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path+".png");}}
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
        if(args.Length==2&&args[0]=="--login-ui-test") {
            string previewDirectory=Path.Combine(Path.GetTempPath(),"LabServerMonitor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(previewDirectory);
            try {using(var form=new LoginManagerForm(previewDirectory)){form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}}}
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());return 1;}return 0;
        }
        if(args.Length==2&&(args[0]=="--one-ui-test"||args[0]=="--narrow-ui-test"||args[0]=="--theme-ui-test")) {
            string previewDirectory=Path.Combine(Path.GetTempPath(),"LabServerMonitor-One-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(previewDirectory);
            try {
                var server=new ServerConfig {Host="gpu-lab.example",User="researcher",HostKey="preview",PasswordFile="preview.txt"};ConfigStore.Save(previewDirectory,new []{server});
                var gpus=new List<Gpu>();for(int i=0;i<8;i++)gpus.Add(new Gpu {index=i.ToString(),name="NVIDIA GPU",used=8200+i*1350,total=49152,utilization=18+i*9,temperature=47+i*2});
                var sample=new Sample {gpus=gpus.ToArray(),ram_used=96256,ram_total=256000};
                using(var form=new MonitorForm(previewDirectory,sample)){form.ShowOnePreview();form.Show();Application.DoEvents();if(args[0]=="--narrow-ui-test"||args[0]=="--theme-ui-test"){form.Width=form.MinimumSize.Width;Application.DoEvents();}if(args[0]=="--theme-ui-test"){form.PreviewHue(260);Application.DoEvents();}using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}form.Close();}
                if(args[0]=="--theme-ui-test"){Ui.SetHue(130);using(var restored=new MonitorForm(previewDirectory,sample)){if(Ui.Hue!=260)throw new Exception("Theme persistence failed");restored.PreviewHue(130);if(Ui.Background!=Color.FromArgb(14,18,16)||Ui.Accent!=Color.FromArgb(113,190,126))throw new Exception("Default palette restoration failed");restored.Show();restored.Close();}File.WriteAllText(args[1]+".result.txt","theme persistence and default restoration passed");}
            } catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());return 1;}finally{try{Directory.Delete(previewDirectory,true);}catch{}}return 0;
        }
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LabServerMonitor");
        try {
            ConfigStore.MigrateExisting(directory);
            if(args.Length==2&&args[0]=="--fingerprint-test") {
                var config=ConfigStore.Load(directory);if(config.Servers.Length==0)throw new Exception("테스트할 로그인이 없습니다.");var server=config.Servers[0];
                string savedPassword=File.ReadAllText(Path.Combine(directory,server.PasswordFile));string fingerprint=HostKeyLookup.Fetch(server.Host,server.User,savedPassword);
                File.WriteAllText(args[1],"matched="+string.Equals(fingerprint,server.HostKey,StringComparison.Ordinal));return 0;
            }
            using(var form=new MonitorForm(directory)) {
                if(args.Length==2&&args[0]=="--check") {
                    var finish=new System.Windows.Forms.Timer {Interval=10000};
                    finish.Tick+=delegate {finish.Stop();finish.Dispose();form.SaveCheck(args[1]);form.Close();};finish.Start();
                }
                Application.Run(form);
            }
            return 0;
        } catch(Exception ex) {
            if(args.Length==2&&(args[0]=="--check"||args[0]=="--fingerprint-test"))File.WriteAllText(args[1]+".error.txt",ex.Message);
            else MessageBox.Show("앱을 시작하지 못했습니다.\n"+ex.Message,"Lab Server Monitor");
            return 1;
        }
        }
    }
}
