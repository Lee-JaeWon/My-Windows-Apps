using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("My Windows Apps Setup")]
[assembly: AssemblyProduct("My Windows Apps")]
[assembly: AssemblyDescription("Installer for GIF Generator, Lab Server Monitor and GPT Usage Tray")]
[assembly: AssemblyVersion("1.0.3.0")]

class SetupForm : Form {
    readonly CheckBox gif=new CheckBox(),monitor=new CheckBox(),usage=new CheckBox();
    readonly CheckBox removeLabData=new CheckBox();readonly Button install=new Button(),uninstall=new Button();readonly ProgressBar progress=new ProgressBar();readonly Label status=new Label();
    readonly Color background=Color.FromArgb(17,23,33),surface=Color.FromArgb(29,37,51),muted=Color.FromArgb(165,178,197);
    public SetupForm() {
        Text="My Windows Apps 설치 및 삭제";ClientSize=new Size(610,550);MinimumSize=MaximumSize=Size;StartPosition=FormStartPosition.CenterScreen;
        FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;BackColor=background;ForeColor=Color.White;Font=new Font("맑은 고딕",10);Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Controls.Add(new Label {Text="My Windows Apps",Location=new Point(30,24),Size=new Size(540,42),Font=new Font("Segoe UI",24,FontStyle.Bold)});
        Controls.Add(new Label {Text="설치하거나 삭제할 앱을 선택하세요",Location=new Point(32,69),Size=new Size(540,26),ForeColor=muted});
        AddOption(gif,30,112,"GIF Generator","MP4를 50MB·999프레임 이하 GIF로 변환하고 MP4 배속을 조절합니다.");
        AddOption(monitor,30,210,"Lab Server Monitor","서버 GPU 메모리·사용률·온도와 RAM을 1초마다 확인합니다.");
        AddOption(usage,30,308,"GPT Usage Tray","Codex Pro 주간 잔여 사용량을 작업표시줄 왼쪽에 표시합니다.");
        gif.Checked=monitor.Checked=usage.Checked=true;
        removeLabData.Text="삭제할 때 Lab 로그인 정보도 함께 삭제";removeLabData.Location=new Point(31,397);removeLabData.Size=new Size(290,28);removeLabData.ForeColor=muted;Controls.Add(removeLabData);
        progress.Location=new Point(30,435);progress.Size=new Size(290,18);progress.Maximum=3;Controls.Add(progress);
        status.Location=new Point(31,463);status.Size=new Size(290,58);status.ForeColor=muted;status.Text="앱 파일과 필요한 도구가 모두 설치 파일 안에 포함되어 있습니다.";Controls.Add(status);
        install.Text="선택한 앱 설치";install.Location=new Point(335,435);install.Size=new Size(116,54);install.BackColor=Color.FromArgb(91,76,219);install.ForeColor=Color.White;install.FlatStyle=FlatStyle.Flat;install.FlatAppearance.BorderSize=0;Controls.Add(install);
        uninstall.Text="선택한 앱 삭제";uninstall.Location=new Point(465,435);uninstall.Size=new Size(116,54);uninstall.BackColor=Color.FromArgb(178,65,82);uninstall.ForeColor=Color.White;uninstall.FlatStyle=FlatStyle.Flat;uninstall.FlatAppearance.BorderSize=0;Controls.Add(uninstall);
        install.Click+=delegate {BeginInstall();};uninstall.Click+=delegate {BeginUninstall();};
    }
    void AddOption(CheckBox box,int x,int y,string title,string description) {
        var panel=new Panel {Location=new Point(x,y),Size=new Size(550,82),BackColor=surface};Controls.Add(panel);
        box.Location=new Point(17,14);box.Size=new Size(24,24);box.FlatStyle=FlatStyle.Flat;panel.Controls.Add(box);
        panel.Controls.Add(new Label {Text=title,Location=new Point(52,11),Size=new Size(470,27),Font=new Font("맑은 고딕",13,FontStyle.Bold)});
        panel.Controls.Add(new Label {Text=description,Location=new Point(53,43),Size=new Size(475,25),ForeColor=muted});
        panel.Click+=delegate {box.Checked=!box.Checked;};
    }
    void BeginInstall() {
        if(!gif.Checked&&!monitor.Checked&&!usage.Checked){MessageBox.Show(this,"설치할 앱을 하나 이상 선택해 주세요.","My Windows Apps 설치");return;}
        bool installGif=gif.Checked,installMonitor=monitor.Checked,installUsage=usage.Checked;
        SetBusy(true);progress.Maximum=(installGif?1:0)+(installMonitor?1:0)+(installUsage?1:0);progress.Value=0;status.Text="설치를 준비하고 있습니다…";
        Task.Run(delegate {
            try {
                int completed=0;
                if(installGif){SetStatus("GIF Generator 설치 중…",completed);InstallPackage("Payload.Gif.zip","GIFGenerator","GIF Generator.exe","GIF Generator",false);completed++;SetStatus("GIF Generator 설치 완료",completed);}
                if(installMonitor){SetStatus("Lab Server Monitor 설치 중…",completed);InstallPackage("Payload.Monitor.zip","LabServerMonitor","Lab Server Monitor.exe","Lab Server Monitor",false);completed++;SetStatus("Lab Server Monitor 설치 완료",completed);}
                if(installUsage){SetStatus("GPT Usage Tray 설치 중…",completed);string executable=InstallPackage("Payload.Usage.zip","GPTUsageTray","GPT Usage Tray.exe","GPT Usage Tray",true);completed++;SetStatus("GPT Usage Tray 설치 완료",completed);TryStart(executable);}
                BeginInvoke((Action)delegate {status.Text="설치가 완료되었습니다. 바탕화면의 아이콘으로 실행하세요.";SetBusy(false);});
            } catch(Exception ex) {BeginInvoke((Action)delegate {status.Text="설치 중 오류가 발생했습니다.";SetBusy(false);MessageBox.Show(this,ex.Message,"설치 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);});}
        });
    }
    void BeginUninstall() {
        if(!gif.Checked&&!monitor.Checked&&!usage.Checked){MessageBox.Show(this,"삭제할 앱을 하나 이상 선택해 주세요.","My Windows Apps 삭제");return;}
        bool removeGif=gif.Checked,removeMonitor=monitor.Checked,removeUsage=usage.Checked,removeCredentials=removeLabData.Checked;
        SetBusy(true);progress.Maximum=(removeGif?1:0)+(removeMonitor?1:0)+(removeUsage?1:0);progress.Value=0;status.Text="삭제를 준비하고 있습니다…";
        Task.Run(delegate {
            try {
                int completed=0;
                if(removeGif){SetStatus("GIF Generator 삭제 중…",completed);UninstallPackage("GIFGenerator","GIF Generator",false);RemoveInstallDirectory("Gif50");completed++;SetStatus("GIF Generator 삭제 완료",completed);}
                if(removeMonitor){SetStatus("Lab Server Monitor 삭제 중…",completed);UninstallPackage("LabServerMonitor","Lab Server Monitor",false);if(removeCredentials)RemoveLabSettings();completed++;SetStatus("Lab Server Monitor 삭제 완료",completed);}
                if(removeUsage){SetStatus("GPT Usage Tray 삭제 중…",completed);UninstallPackage("GPTUsageTray","GPT Usage Tray",true);completed++;SetStatus("GPT Usage Tray 삭제 완료",completed);}
                BeginInvoke((Action)delegate {status.Text="선택한 앱 삭제가 완료되었습니다.";SetBusy(false);});
            } catch(Exception ex){BeginInvoke((Action)delegate {status.Text="삭제 중 오류가 발생했습니다.";SetBusy(false);MessageBox.Show(this,ex.Message,"삭제 실패",MessageBoxButtons.OK,MessageBoxIcon.Error);});}
        });
    }
    void SetBusy(bool busy){install.Enabled=!busy;uninstall.Enabled=!busy;gif.Enabled=monitor.Enabled=usage.Enabled=removeLabData.Enabled=!busy;}
    void SetStatus(string text,int value){BeginInvoke((Action)delegate {status.Text=text;progress.Value=Math.Max(0,Math.Min(progress.Maximum,value));});}
    public static string InstallPackage(string resource,string folder,string executableName,string shortcutName,bool startWithWindows) {
        string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",folder);
        string targetExecutable=Path.Combine(target,executableName);StopInstalled(target);Directory.CreateDirectory(target);
        string temporary=Path.Combine(Path.GetTempPath(),"MyWindowsAppsSetup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        try {
            string zipPath=Path.Combine(temporary,"payload.zip");WriteResource(resource,zipPath);string extracted=Path.Combine(temporary,"files");Directory.CreateDirectory(extracted);ZipFile.ExtractToDirectory(zipPath,extracted);CopyTree(extracted,target);
        } finally {try{Directory.Delete(temporary,true);}catch{}}
        CreateShortcut(shortcutName,targetExecutable,target);
        if(startWithWindows)using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))key.SetValue("GPT Usage Tray","\""+targetExecutable+"\"");
        return targetExecutable;
    }
    public static void UninstallPackage(string folder,string shortcutName,bool removeStartup) {
        string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",folder);StopInstalled(target);RemoveInstallDirectory(folder);
        string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),shortcutName+".lnk");if(File.Exists(shortcut))File.Delete(shortcut);
        if(removeStartup)using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))key.DeleteValue("GPT Usage Tray",false);
    }
    public static void RemoveInstallDirectory(string folder) {
        string programs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"),target=Path.Combine(programs,folder);
        StopInstalled(target);DeleteDirectoryInside(programs,target);
    }
    public static void RemoveLabSettings() {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),target=Path.Combine(local,"LabServerMonitor");DeleteDirectoryInside(local,target);
    }
    static void DeleteDirectoryInside(string root,string target) {
        string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,fullTarget=Path.GetFullPath(target);
        if(!fullTarget.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("안전하지 않은 삭제 경로입니다.");
        if(Directory.Exists(fullTarget))Directory.Delete(fullTarget,true);
    }
    static void WriteResource(string name,string path) {
        using(Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if(input==null)throw new Exception("설치 파일 내부 구성 요소를 찾을 수 없습니다: "+name);
            using(var output=File.Create(path))input.CopyTo(output);
        }
    }
    static void CopyTree(string source,string destination) {
        foreach(string directory in Directory.GetDirectories(source,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(destination,directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)));
        foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
            string relative=file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar),target=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
        }
    }
    static void StopInstalled(string directory) {
        string prefix=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        foreach(var process in Process.GetProcesses())try {string path=process.MainModule.FileName;if(path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){process.CloseMainWindow();if(!process.WaitForExit(1500)){process.Kill();process.WaitForExit(1500);}}}catch{};
    }
    static void CreateShortcut(string name,string executable,string workingDirectory) {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),name+".lnk");Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type),shortcut=null;
        try {
            shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});Type shortcutType=shortcut.GetType();
            shortcutType.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{executable});shortcutType.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{workingDirectory});
            shortcutType.InvokeMember("IconLocation",BindingFlags.SetProperty,null,shortcut,new object[]{executable+",0"});shortcutType.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
        } finally {if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
    }
    static void TryStart(string executable){try{Process.Start(new ProcessStartInfo(executable){WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=true});}catch{}}
}

class Program {
    [STAThread]static void Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--ui-test") {
            try {using(var form=new SetupForm()){form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}}}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());}return;
        }
        if(args.Length==2&&args[0]=="--install-all") {
            try {
                SetupForm.InstallPackage("Payload.Gif.zip","GIFGenerator","GIF Generator.exe","GIF Generator",false);
                SetupForm.InstallPackage("Payload.Monitor.zip","LabServerMonitor","Lab Server Monitor.exe","Lab Server Monitor",false);
                SetupForm.InstallPackage("Payload.Usage.zip","GPTUsageTray","GPT Usage Tray.exe","GPT Usage Tray",true);
                File.WriteAllText(args[1],"ok");
            } catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());}return;
        }
        if(args.Length==2&&args[0]=="--uninstall-all") {
            try {
                SetupForm.UninstallPackage("GIFGenerator","GIF Generator",false);SetupForm.RemoveInstallDirectory("Gif50");
                SetupForm.UninstallPackage("LabServerMonitor","Lab Server Monitor",false);
                SetupForm.UninstallPackage("GPTUsageTray","GPT Usage Tray",true);
                File.WriteAllText(args[1],"ok");
            } catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());}return;
        }
        Application.Run(new SetupForm());
    }
}
