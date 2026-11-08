using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Details-only styling: the taskbar widget never uses these fonts or colors.
static class DetailsFonts {
    static readonly PrivateFontCollection collection=new PrivateFontCollection();
    static readonly List<IntPtr> memory=new List<IntPtr>();
    static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
    static DetailsFonts() {
        foreach(string name in new[]{"Fonts.Regular","Fonts.Bold"}) {
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if(stream==null)continue;
                byte[] data=new byte[stream.Length];int read=0,count;
                while(read<data.Length&&(count=stream.Read(data,read,data.Length-read))>0)read+=count;
                IntPtr buffer=Marshal.AllocCoTaskMem(data.Length);Marshal.Copy(data,0,buffer,data.Length);
                memory.Add(buffer);collection.AddMemoryFont(buffer,data.Length);
            }
        }
    }
    public static Font Get(int pixels,bool bold) {
        string key=pixels+":"+bold;Font font;
        if(!fonts.TryGetValue(key,out font)) {
            font=collection.Families.Length>0?new Font(collection.Families[0],pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel):new Font("맑은 고딕",pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
            fonts.Add(key,font);
        }
        return font;
    }
}

sealed class DetailsPalette {
    public readonly bool Dark;
    public readonly Color Background,Surface,Weekly,Text,Muted,Border,Track,Accent,Selected,Button,ButtonText,Good,Error;
    public DetailsPalette(bool dark) {
        Dark=dark;
        Background=Hex(dark?0x191A1E:0xFAFAFB);Surface=Hex(dark?0x232429:0xFFFFFF);
        Weekly=Hex(dark?0x282531:0xF6F3FC);Text=Hex(dark?0xF1F1F4:0x232329);
        Muted=Hex(dark?0xA6A6B3:0x74747F);Border=Hex(dark?0x383840:0xE1E1E8);
        Track=Hex(dark?0x34353D:0xE9E9EF);Accent=Hex(dark?0xAD94F5:0x8061CB);
        Selected=Hex(dark?0x41404A:0xFFFFFF);Button=Hex(dark?0xEAEAF0:0x29292F);
        ButtonText=Hex(dark?0x25252B:0xFFFFFF);Good=Hex(dark?0x73C994:0x338454);Error=Hex(dark?0xF1AD83:0xA85220);
    }
    static Color Hex(int value){return Color.FromArgb((value>>16)&255,(value>>8)&255,value&255);}
}

static class DetailsUi {
    public static GraphicsPath Round(RectangleF r,float radius) {
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var p=new GraphicsPath();
        if(d<=0){p.AddRectangle(r);return p;}
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static void Fill(Graphics g,RectangleF r,float radius,Color color) {using(var p=Round(r,radius))using(var b=new SolidBrush(color))g.FillPath(b,p);}
    public static void Text(Graphics g,string text,RectangleF r,int pixels,bool bold,Color color,bool right) {
        using(var b=new SolidBrush(color))using(var format=new StringFormat(StringFormat.GenericTypographic)) {
            format.Alignment=right?StringAlignment.Far:StringAlignment.Near;format.LineAlignment=StringAlignment.Center;
            format.Trimming=StringTrimming.EllipsisCharacter;format.FormatFlags=StringFormatFlags.NoWrap;
            g.DrawString(text,DetailsFonts.Get(pixels,bold),b,r,format);
        }
    }
}

sealed class DetailsButton : Button {
    public DetailsPalette Palette;
    public bool Selected,Primary;
    public DetailsButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;}
    protected override void OnPaint(PaintEventArgs e) {
        var p=Palette;if(p==null)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAlias;
        g.Clear(BackColor);float scale=Height/36f;g.ScaleTransform(scale,scale);float w=Width/scale;
        if(Primary||Selected)DetailsUi.Fill(g,new RectangleF(0,0,w,36),8,Primary?p.Button:p.Selected);
        if(Focused&&ShowFocusCues)using(var pen=new Pen(p.Accent,1))using(var path=DetailsUi.Round(new RectangleF(2,2,w-4,32),7))g.DrawPath(pen,path);
        Color color=!Enabled?p.Muted:Primary?p.ButtonText:Selected?p.Accent:p.Muted;
        using(var b=new SolidBrush(color))using(var f=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(Text,DetailsFonts.Get(14,Selected||Primary),b,new RectangleF(0,0,w,36),f);
    }
}

class WidgetHueSlider : Control {
    public Color? PreviewColor;
    int hue;public event EventHandler ValueChanged;
    public int Hue{get{return hue;}set{value=Math.Max(0,Math.Min(359,value));if(value==hue)return;hue=value;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    public WidgetHueSlider(){DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;AccessibleName="위젯 단일 색상";AccessibleRole=AccessibleRole.Slider;}
    void Pick(int x){double inset=Height/3.0;Hue=(int)(Math.Max(0,Math.Min(1,(x-inset)/Math.Max(1,Width-2*inset)))*359);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();Capture=true;Pick(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture&&e.Button==MouseButtons.Left)Pick(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left)Capture=false;}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left){Hue--;e.Handled=true;}if(e.KeyCode==Keys.Right){Hue++;e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=Height/30f,y=Height/2f,inset=10*s;
        var r=new RectangleF(inset,y-2.5f*s,Math.Max(1,Width-2*inset),5*s);
        using(var path=DetailsUi.Round(r,2.5f*s))using(var b=new LinearGradientBrush(r,Color.Red,Color.Red,0f)) {
            var colors=new Color[7];var positions=new float[7];for(int i=0;i<7;i++){colors[i]=WidgetColors.FromHue(i*60);positions[i]=i/6f;}
            b.InterpolationColors=new ColorBlend{Colors=colors,Positions=positions};g.FillPath(b,path);
        }
        float x=inset+(Width-2*inset)*hue/359f;
        using(var b=new SolidBrush(PreviewColor??WidgetColors.FromHue(hue)))g.FillEllipse(b,x-6*s,y-6*s,12*s,12*s);
        using(var p=new Pen(Color.White,2*s))g.DrawEllipse(p,x-6*s,y-6*s,12*s,12*s);
        if(!Enabled)using(var b=new SolidBrush(Color.FromArgb(160,BackColor)))g.FillRectangle(b,ClientRectangle);
        if(Focused&&Enabled)ControlPaint.DrawFocusRectangle(g,ClientRectangle);
    }
}

class DetailsForm : Form {
    const int CanvasWidth=660,CanvasHeight=584;
    readonly System.Windows.Forms.Timer colorSaveTimer=new System.Windows.Forms.Timer{Interval=350};
    readonly DetailsButton remainingMode=new DetailsButton{Text="잔여량",AccessibleName="잔여량에 따른 기본 색상"};
    readonly DetailsButton single=new DetailsButton{Text="단일색",AccessibleName="단일 색상"};
    readonly DetailsButton custom=new DetailsButton{Text="커스텀",AccessibleName="시작과 끝 색상 선택"};
    readonly DetailsButton refresh=new DetailsButton{Text="새로고침",Primary=true};
    readonly WidgetHueSlider hue=new WidgetHueSlider{AccessibleName="100% 색상"},endHue=new WidgetHueSlider{AccessibleName="0% 색상"};
    readonly ToolTip tips=new ToolTip();
    DetailsPalette palette;
    bool syncing,loading;
    string error;
    UsageSnapshot snapshot;
    readonly bool? themeOverride;
    public event EventHandler ColorsChanged;
    public event EventHandler RefreshRequested;
    public DetailsForm():this(null){}
    internal DetailsForm(bool? darkPreview) {
        themeOverride=darkPreview;Text="GPT Usage";ShowIcon=false;ClientSize=new Size(CanvasWidth,CanvasHeight);
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        DoubleBuffered=true;ShowInTaskbar=true;
        Controls.AddRange(new Control[]{remainingMode,single,custom,hue,endHue,refresh});
        remainingMode.TabIndex=0;single.TabIndex=1;custom.TabIndex=2;hue.TabIndex=3;endHue.TabIndex=4;refresh.TabIndex=5;
        remainingMode.Click+=delegate{SelectMode("remaining");};single.Click+=delegate{SelectMode("single");};custom.Click+=delegate{SelectMode("custom");};
        hue.ValueChanged+=delegate{if(syncing)return;WidgetColors.Hue=hue.Hue;if(WidgetColors.Mode=="single")WidgetColors.EndHue=hue.Hue;ColorsEdited();};
        endHue.ValueChanged+=delegate{if(syncing)return;WidgetColors.EndHue=endHue.Hue;if(WidgetColors.Mode=="single")WidgetColors.Hue=endHue.Hue;ColorsEdited();};
        colorSaveTimer.Tick+=delegate{colorSaveTimer.Stop();WidgetColors.Save();};
        refresh.Click+=delegate{if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        SystemEvents.UserPreferenceChanged+=ThemeChanged;
        ApplyTheme();SyncColors();PlaceControls();
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyTheme();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)ApplyTheme();}
    protected override void OnResize(EventArgs e){base.OnResize(e);if(refresh!=null)PlaceControls();Invalidate();}
    void PlaceControls() {
        float s=ClientSize.Width/(float)CanvasWidth;
        SetBounds(remainingMode,31,337,108,36,s);SetBounds(single,139,337,108,36,s);SetBounds(custom,247,337,108,36,s);
        SetBounds(hue,166,394,466,30,s);SetBounds(endHue,166,438,466,30,s);SetBounds(refresh,522,532,110,36,s);
    }
    static void SetBounds(Control c,int x,int y,int w,int h,float s){c.SetBounds((int)Math.Round(x*s),(int)Math.Round(y*s),(int)Math.Round(w*s),(int)Math.Round(h*s));}
    static bool WindowsDark() {
        try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))return key!=null&&Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;}catch{return false;}
    }
    void ThemeChanged(object sender,UserPreferenceChangedEventArgs e) {
        if(IsDisposed||!IsHandleCreated)return;
        try{BeginInvoke((Action)delegate{if(!IsDisposed)ApplyTheme();});}catch(InvalidOperationException){}
    }
    void ApplyTheme() {
        palette=new DetailsPalette(themeOverride??WindowsDark());BackColor=palette.Background;ForeColor=palette.Text;
        foreach(var button in new[]{remainingMode,single,custom,refresh}){button.Palette=palette;button.BackColor=button.Primary?palette.Background:palette.Track;button.Invalidate();}
        hue.BackColor=endHue.BackColor=palette.Background;
        if(IsHandleCreated){int dark=palette.Dark?1:0;try{if(DwmSetWindowAttribute(Handle,20,ref dark,4)!=0)DwmSetWindowAttribute(Handle,19,ref dark,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
        Invalidate(true);
    }
    void SelectMode(string mode){WidgetColors.Mode=mode;if(mode=="single")WidgetColors.EndHue=WidgetColors.Hue;ColorsEdited();}
    void SyncColors() {
        syncing=true;bool remaining=WidgetColors.Mode=="remaining";
        remainingMode.Selected=remaining;single.Selected=WidgetColors.Mode=="single";custom.Selected=WidgetColors.Mode=="custom";
        hue.PreviewColor=remaining?(Color?)WidgetColors.StartColor:null;endHue.PreviewColor=remaining?(Color?)WidgetColors.EndColor:null;
        hue.Hue=remaining?(int)Math.Round(WidgetColors.StartColor.GetHue()):WidgetColors.Hue;
        endHue.Hue=remaining?(int)Math.Round(WidgetColors.EndColor.GetHue()):WidgetColors.EndHue;
        hue.Enabled=endHue.Enabled=!remaining;syncing=false;Invalidate(true);
    }
    void ColorsEdited(){SyncColors();colorSaveTimer.Stop();colorSaveTimer.Start();if(ColorsChanged!=null)ColorsChanged(this,EventArgs.Empty);}
    public void Loading(){loading=true;refresh.Enabled=false;Invalidate();}
    public void ShowError(string message){error=message;loading=false;refresh.Enabled=true;tips.SetToolTip(this,message);Invalidate();}
    public void UpdateData(UsageSnapshot data){snapshot=data;error=null;loading=false;refresh.Enabled=true;tips.SetToolTip(this,null);Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);if(palette==null)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAlias;
        float scale=ClientSize.Width/(float)CanvasWidth;g.ScaleTransform(scale,scale);var p=palette;
        DrawText(g,"사용량",28,22,260,38,26,true,p.Text,false);
        string state=loading?"갱신 중":error!=null?"연결 확인":snapshot==null?"대기 중":"동기화됨";
        Color status=error!=null?p.Error:loading||snapshot==null?p.Muted:p.Good;
        using(var b=new SolidBrush(status))g.FillEllipse(b,547,37,6,6);
        DrawText(g,state,560,25,72,30,13,false,p.Muted,true);
        string detail=loading?"최신 사용량을 확인하고 있습니다…":error!=null?"갱신하지 못했습니다. 새로고침으로 다시 시도하세요.":snapshot==null?"사용량 정보를 기다리고 있습니다.":"마지막 업데이트 "+snapshot.Updated.ToString("HH:mm")+" · 1분마다 갱신";
        DrawText(g,detail,28,65,604,24,13,false,error!=null?p.Error:p.Muted,false);
        UsageWindow weekly=null,five=null;
        if(snapshot!=null)foreach(var item in snapshot.Windows) {
            if(item.DurationMinutes==300){five=item;continue;}
            if(weekly==null||Nullable.Compare(item.DurationMinutes,weekly.DurationMinutes)>0)weekly=item;
        }
        DrawCard(g,new RectangleF(28,110,294,164),weekly==null?"주간 한도":weekly.Name=="주간"?"주간 한도":weekly.Name+" 한도",weekly,true);
        DrawCard(g,new RectangleF(338,110,294,164),"5시간 한도",five,false);
        DrawText(g,"위젯 색상",28,296,300,28,19,true,p.Text,false);
        DrawText(g,"자동 저장",530,298,102,24,12,false,p.Muted,true);
        DetailsUi.Fill(g,new RectangleF(28,334,330,42),10,p.Track);
        DrawText(g,"100% 색상",28,394,102,30,14,true,p.Text,false);DrawText(g,"0% 색상",28,438,102,30,14,true,p.Text,false);
        DetailsUi.Fill(g,new RectangleF(133,398,22,22),5,hue.PreviewColor??WidgetColors.FromHue(hue.Hue));
        DetailsUi.Fill(g,new RectangleF(133,442,22,22),5,endHue.PreviewColor??WidgetColors.FromHue(endHue.Hue));
        string hint=WidgetColors.Mode=="remaining"?"잔여량이 줄면 초록 → 노랑 → 빨강으로 바뀝니다":WidgetColors.Mode=="single"?"잔여량과 관계없이 선택한 색상을 유지합니다":"남은 양에 따라 두 색 사이로 바뀝니다";
        DrawText(g,hint,28,478,604,24,13,false,p.Muted,false);
        using(var pen=new Pen(p.Border))g.DrawLine(pen,28,518,632,518);
        DrawText(g,"설정은 자동으로 저장됩니다",28,535,450,30,12,false,p.Muted,false);
    }
    void DrawCard(Graphics g,RectangleF r,string title,UsageWindow window,bool weekly) {
        var p=palette;DetailsUi.Fill(g,r,14,weekly?p.Weekly:p.Surface);
        using(var pen=new Pen(p.Border))using(var path=DetailsUi.Round(r,14))g.DrawPath(pen,path);
        DrawText(g,title,r.X+20,r.Y+16,r.Width-40,26,17,true,p.Text,false);
        // Failed refreshes never present stale percentages as a fresh reading.
        bool available=window!=null&&error==null;
        string number=available?window.Remaining.ToString("0")+"%":"—";
        DrawText(g,number,r.X+20,r.Y+49,r.Width-40,53,44,true,p.Text,false);
        if(available) {
            float width=g.MeasureString(number,DetailsFonts.Get(44,true),PointF.Empty,StringFormat.GenericTypographic).Width;
            DrawText(g,"남음",r.X+26+width,r.Y+70,44,24,14,false,p.Muted,false);
        }
        var track=new RectangleF(r.X+20,r.Y+115,r.Width-40,6);DetailsUi.Fill(g,track,3,p.Track);
        if(available&&window.Remaining>0)DetailsUi.Fill(g,new RectangleF(track.X,track.Y,track.Width*(float)window.Remaining/100,track.Height),3,p.Accent);
        string reset=error!=null?"연결 상태를 확인해주세요":window==null?"한도 정보 없음":window.ResetLocal.HasValue?window.ResetLocal.Value.ToString("MM/dd HH:mm")+" 초기화":"초기화 시각 정보 없음";
        DrawText(g,reset,r.X+20,r.Y+132,r.Width-40,22,12,false,p.Muted,false);
    }
    static void DrawText(Graphics g,string text,float x,float y,float w,float h,int size,bool bold,Color color,bool right){DetailsUi.Text(g,text,new RectangleF(x,y,w,h),size,bold,color,right);}
    protected override void Dispose(bool disposing) {
        if(disposing){SystemEvents.UserPreferenceChanged-=ThemeChanged;if(colorSaveTimer.Enabled)WidgetColors.Save();colorSaveTimer.Dispose();tips.Dispose();}
        base.Dispose(disposing);
    }
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
