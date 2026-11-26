using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Net;
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
[assembly: AssemblyDescription("ChatGPT, Claude and Gemini usage in the Windows taskbar")]
[assembly: AssemblyVersion("1.5.2.0")]

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
    public static bool IsAvailable() {try {FindCodex();return true;} catch {return false;}}
}

static class ClaudeUsageReader {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static Dictionary<string,object> Dict(object value) { return value as Dictionary<string,object>; }
    static object Value(Dictionary<string,object> source,string key) { object value; return source!=null&&source.TryGetValue(key,out value)?value:null; }
    static double Number(object value) { return Convert.ToDouble(value,CultureInfo.InvariantCulture); }
    static long Long(object value) { return Convert.ToInt64(value,CultureInfo.InvariantCulture); }

    const string UsageUrl="https://api.anthropic.com/api/oauth/usage";
    const string TokenUrl="https://platform.claude.com/v1/oauth/token";
    const string ClientId="9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    // The usage endpoint rate-limits aggressively (HTTP 429), so fetch at most every few minutes,
    // back off after a 429 and keep showing the last good snapshot in the meantime.
    const int FetchIntervalMinutes=5,MaxBackoffMinutes=30;
    static UsageSnapshot cached;static DateTime nextFetch=DateTime.MinValue;static int backoffMinutes=FetchIntervalMinutes;

    public static UsageSnapshot Read() {
        if(DateTime.Now<nextFetch) {
            if(cached!=null)return cached;
            throw new Exception("Claude 사용량 조회 제한(HTTP 429) · "+nextFetch.ToString("HH:mm")+" 이후 다시 시도합니다");
        }
        try {
            cached=Fetch();backoffMinutes=FetchIntervalMinutes;nextFetch=DateTime.Now.AddMinutes(FetchIntervalMinutes);return cached;
        } catch(ClaudeApiException ex) {
            if(ex.Status!=429)throw;
            int wait=ex.RetryAfterSeconds>0?Math.Max(1,(ex.RetryAfterSeconds+59)/60):backoffMinutes;
            backoffMinutes=Math.Min(MaxBackoffMinutes,backoffMinutes*2);nextFetch=DateTime.Now.AddMinutes(wait);
            if(cached!=null)return cached;
            throw new Exception("Claude 사용량 조회 제한(HTTP 429) · "+nextFetch.ToString("HH:mm")+" 이후 다시 시도합니다");
        }
    }

    static UsageSnapshot Fetch() {
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        string path=FindCredentials();
        var oauth=Dict(Value(LoadCredentials(path),"claudeAiOauth"));
        string token=Convert.ToString(Value(oauth,"accessToken"),CultureInfo.InvariantCulture);
        if(string.IsNullOrEmpty(token))throw new Exception("Claude Code에 로그인되어 있지 않습니다. 터미널에서 claude를 실행해 로그인해 주세요.");
        object expires=Value(oauth,"expiresAt");
        if(expires!=null&&Long(expires)<DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds())token=RefreshToken(path);
        try {return Parse(Request("GET",UsageUrl,token,null));}
        catch(ClaudeApiException ex) {
            if(ex.Status!=401)throw;
            return Parse(Request("GET",UsageUrl,RefreshToken(path),null));
        }
    }

    class ClaudeApiException : Exception {
        public readonly int Status,RetryAfterSeconds;
        public ClaudeApiException(int status,int retryAfterSeconds,string message):base(message){Status=status;RetryAfterSeconds=retryAfterSeconds;}
    }

    static Dictionary<string,object> Request(string method,string url,string token,string body) {
        var request=(HttpWebRequest)WebRequest.Create(url);
        request.Method=method;request.Timeout=15000;request.ReadWriteTimeout=15000;request.Accept="application/json";request.UserAgent="GPT-Usage-Tray/1.4";
        request.Headers["anthropic-beta"]="oauth-2025-04-20";
        if(token!=null)request.Headers["Authorization"]="Bearer "+token;
        if(body!=null) {
            byte[] bytes=Encoding.UTF8.GetBytes(body);request.ContentType="application/json";request.ContentLength=bytes.Length;
            using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
        }
        try {
            using(var response=(HttpWebResponse)request.GetResponse())
            using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))
                return Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
        } catch(WebException ex) {
            var response=ex.Response as HttpWebResponse;if(response==null)throw new Exception("Claude 서버에 연결하지 못했습니다: "+ex.Message);
            int status=(int)response.StatusCode,retryAfter;
            if(!int.TryParse(response.Headers["Retry-After"],NumberStyles.Integer,CultureInfo.InvariantCulture,out retryAfter))retryAfter=0;
            response.Close();
            throw new ClaudeApiException(status,retryAfter,"Claude 사용량 조회 실패 (HTTP "+status+")");
        }
    }

    static string RefreshToken(string path) {
        var root=LoadCredentials(path);var oauth=Dict(Value(root,"claudeAiOauth"));
        string refresh=Convert.ToString(Value(oauth,"refreshToken"),CultureInfo.InvariantCulture);
        if(string.IsNullOrEmpty(refresh))throw new Exception("Claude Code 로그인이 만료되었습니다. 터미널에서 claude를 실행해 다시 로그인해 주세요.");
        Dictionary<string,object> result;
        try {result=Request("POST",TokenUrl,null,Json.Serialize(new Dictionary<string,object>{{"grant_type","refresh_token"},{"refresh_token",refresh},{"client_id",ClientId}}));}
        catch(ClaudeApiException ex) {
            if(ex.Status==429)throw;
            if(ex.Status==400||ex.Status==401)throw new Exception("Claude Code 로그인이 만료되었습니다. 터미널에서 claude를 실행해 다시 로그인해 주세요.");
            throw new Exception("Claude Code 로그인 갱신에 실패했습니다 (HTTP "+ex.Status+")");
        }
        string access=Convert.ToString(Value(result,"access_token"),CultureInfo.InvariantCulture);
        if(string.IsNullOrEmpty(access))throw new Exception("Claude Code 로그인 갱신에 실패했습니다.");
        oauth["accessToken"]=access;
        string newRefresh=Convert.ToString(Value(result,"refresh_token"),CultureInfo.InvariantCulture);if(!string.IsNullOrEmpty(newRefresh))oauth["refreshToken"]=newRefresh;
        object expiresIn=Value(result,"expires_in");if(expiresIn!=null)oauth["expiresAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+(long)(Number(expiresIn)*1000);
        // Save the rotated tokens back so Claude Code keeps working with the same login.
        string temporary=path+".tray.tmp";File.WriteAllText(temporary,Json.Serialize(root),new UTF8Encoding(false));
        File.Copy(temporary,path,true);File.Delete(temporary);
        return access;
    }

    static Dictionary<string,object> LoadCredentials(string path) {
        try {return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));}
        catch {throw new Exception("Claude Code 로그인 정보를 읽지 못했습니다.");}
    }

    static UsageSnapshot Parse(Dictionary<string,object> usage) {
        var snapshot=new UsageSnapshot();
        AddWindow(snapshot,Dict(Value(usage,"five_hour")),300);
        AddWindow(snapshot,Dict(Value(usage,"seven_day")),10080);
        snapshot.Windows.Sort(delegate(UsageWindow a,UsageWindow b){return Nullable.Compare(a.DurationMinutes,b.DurationMinutes);});
        snapshot.Updated=DateTime.Now;return snapshot;
    }

    static void AddWindow(UsageSnapshot snapshot,Dictionary<string,object> source,int minutes) {
        if(source==null||Value(source,"utilization")==null)return;
        var window=new UsageWindow {Used=Number(Value(source,"utilization")),DurationMinutes=minutes};
        window.Name=window.DurationMinutes.Value<=360?"단기":window.DurationMinutes.Value>=10000?"주간":FormatDuration(window.DurationMinutes.Value);
        object reset=Value(source,"resets_at");DateTimeOffset resetAt;
        if(reset!=null&&DateTimeOffset.TryParse(Convert.ToString(reset,CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out resetAt))window.ResetLocal=resetAt.LocalDateTime;
        snapshot.Windows.Add(window);
    }

    static string FormatDuration(int minutes) {return minutes%1440==0?(minutes/1440)+"일":minutes%60==0?(minutes/60)+"시간":minutes+"분";}
    static string FindCredentials() {
        string folder=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if(string.IsNullOrEmpty(folder))folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude");
        string file=Path.Combine(folder,".credentials.json");
        if(!File.Exists(file))throw new FileNotFoundException("Claude Code에 로그인되어 있지 않습니다. 터미널에서 claude를 실행해 로그인해 주세요.");
        return file;
    }
    public static bool IsAvailable() {try {FindCredentials();return true;} catch {return false;}}
}

// Gemini (Google AI Pro) quota comes from the local Antigravity language server, which
// publishes its port and CSRF token in ~/.gemini/antigravity/daemon/hub_<port>.json.
static class AntigravityUsageReader {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static Dictionary<string,object> Dict(object value) { return value as Dictionary<string,object>; }
    static object Value(Dictionary<string,object> source,string key) { object value; return source!=null&&source.TryGetValue(key,out value)?value:null; }
    static string Text(object value) { return Convert.ToString(value,CultureInfo.InvariantCulture); }
    static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

    public static bool IsAvailable() {
        return Directory.Exists(Path.Combine(Home,".gemini","antigravity"))||Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","antigravity"));
    }

    public static UsageSnapshot Read() {
        string port,token;
        if(!FindServer(out port,out token))throw new Exception("Antigravity가 실행 중일 때만 Gemini 사용량을 읽을 수 있습니다.");
        var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port+"/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary");
        request.Method="POST";request.Proxy=null;request.Timeout=10000;request.ReadWriteTimeout=10000;request.ContentType="application/json";
        request.Headers["X-Codeium-Csrf-Token"]=token;request.Headers["Connect-Protocol-Version"]="1";
        byte[] body=Encoding.UTF8.GetBytes("{}");request.ContentLength=body.Length;
        Dictionary<string,object> result;
        try {
            using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
            using(var response=(HttpWebResponse)request.GetResponse())
            using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))
                result=Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
        } catch(WebException ex) {
            var response=ex.Response as HttpWebResponse;
            throw new Exception(response==null?"Antigravity에 연결하지 못했습니다.":"Gemini 사용량 조회 실패 (HTTP "+(int)response.StatusCode+")");
        }
        return Parse(Dict(Value(result,"response")));
    }

    static bool FindServer(out string port,out string token) {
        port=token=null;
        foreach(string name in new[]{"antigravity","antigravity-ide"}) {
            string folder=Path.Combine(Home,".gemini",name,"daemon");if(!Directory.Exists(folder))continue;
            var files=new List<FileInfo>(new DirectoryInfo(folder).GetFiles("hub_*.json"));
            files.Sort(delegate(FileInfo a,FileInfo b){return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);});
            foreach(var file in files) {
                try {
                    var hub=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file.FullName,Encoding.UTF8));
                    object pid=Value(hub,"pid"),httpPort=Value(hub,"httpPort"),csrf=Value(hub,"csrfToken");
                    if(pid==null||httpPort==null||csrf==null)continue;
                    // Hub files of closed sessions stay behind; only trust one whose server is still running.
                    using(var process=Process.GetProcessById(Convert.ToInt32(pid,CultureInfo.InvariantCulture)))
                        if(!process.ProcessName.StartsWith("language_server",StringComparison.OrdinalIgnoreCase))continue;
                    port=Text(httpPort);token=Text(csrf);return true;
                } catch {}
            }
        }
        return false;
    }

    static UsageSnapshot Parse(Dictionary<string,object> summary) {
        var snapshot=new UsageSnapshot();
        var groups=Value(summary,"groups") as ArrayList;
        if(groups!=null)foreach(object group in groups) {
            var buckets=Value(Dict(group),"buckets") as ArrayList;if(buckets==null)continue;
            foreach(object item in buckets) {
                var bucket=Dict(item);string id=Text(Value(bucket,"bucketId"));
                if(id=="gemini-5h")AddWindow(snapshot,bucket,300);else if(id=="gemini-weekly")AddWindow(snapshot,bucket,10080);
            }
        }
        // Fall back to the first Gemini group's 5h/weekly windows if the bucket ids change.
        if(snapshot.Windows.Count==0&&groups!=null)foreach(object group in groups) {
            var source=Dict(group);var buckets=Value(source,"buckets") as ArrayList;
            if(buckets==null||Text(Value(source,"displayName")).IndexOf("Gemini",StringComparison.OrdinalIgnoreCase)<0)continue;
            foreach(object item in buckets) {
                var bucket=Dict(item);string window=Text(Value(bucket,"window"));
                if(window=="5h")AddWindow(snapshot,bucket,300);else if(window=="weekly")AddWindow(snapshot,bucket,10080);
            }
            if(snapshot.Windows.Count>0)break;
        }
        snapshot.Windows.Sort(delegate(UsageWindow a,UsageWindow b){return Nullable.Compare(a.DurationMinutes,b.DurationMinutes);});
        snapshot.Updated=DateTime.Now;return snapshot;
    }

    static void AddWindow(UsageSnapshot snapshot,Dictionary<string,object> bucket,int minutes) {
        if(snapshot.Windows.Exists(delegate(UsageWindow item){return item.DurationMinutes==minutes;}))return;
        // Protobuf JSON omits zero values, so a bucket without a fraction has nothing left.
        object fraction=Value(bucket,"remainingFraction");
        var window=new UsageWindow {Used=100-(fraction==null?0:Convert.ToDouble(fraction,CultureInfo.InvariantCulture))*100,DurationMinutes=minutes,Name=minutes==300?"단기":"주간"};
        object reset=Value(bucket,"resetTime");DateTimeOffset resetAt;
        if(reset!=null&&DateTimeOffset.TryParse(Text(reset),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out resetAt))window.ResetLocal=resetAt.LocalDateTime;
        snapshot.Windows.Add(window);
    }
}

class UsageProvider {
    public static readonly Color Warning=Color.FromArgb(239,68,68),Missing=Color.FromArgb(125,135,150);
    public const double WarningBelow=20;
    public string Key,Name,Source,SetupHint;public Color DefaultColor;
    public Func<bool> IsAvailable;public Func<UsageSnapshot> Reader;
    public bool Available,Loading;public UsageSnapshot Snapshot;public string Error;
    public Color Color {get{return AiColors.ColorFor(this);}}
    public static List<UsageProvider> CreateAll() {
        return new List<UsageProvider> {
            new UsageProvider {Key="gpt",Name="GPT",Source="Codex",DefaultColor=Color.FromArgb(16,163,127),IsAvailable=CodexUsageReader.IsAvailable,Reader=CodexUsageReader.Read,
                SetupHint="Codex 앱을 설치하고 ChatGPT 계정으로 로그인하면\n자동으로 표시됩니다."},
            new UsageProvider {Key="claude",Name="Claude",Source="Claude Code",DefaultColor=Color.FromArgb(217,119,87),IsAvailable=ClaudeUsageReader.IsAvailable,Reader=ClaudeUsageReader.Read,
                SetupHint="Claude Code CLI를 설치하고 터미널에서 claude를 실행해\n로그인하면 자동으로 표시됩니다.\nClaude 데스크톱 앱 로그인만으로는 표시되지 않습니다.\n\nclaude 명령을 찾지 못하면 아래 파일을 실행하세요.\n%USERPROFILE%\\.local\\bin\\claude.exe"},
            new UsageProvider {Key="gemini",Name="Gemini",Source="Antigravity",DefaultColor=Color.FromArgb(66,133,244),IsAvailable=AntigravityUsageReader.IsAvailable,Reader=AntigravityUsageReader.Read,
                SetupHint="Antigravity를 설치하고 Google 계정으로 로그인하면\n자동으로 표시됩니다.\n\nGemini 사용량은 Antigravity가 실행 중일 때 읽습니다."}
        };
    }
}

static class AiColors {
    static readonly string Settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GPTUsageTray","ai-colors.txt");
    static readonly Dictionary<string,int> hues=new Dictionary<string,int>();
    static AiColors(){try{foreach(string line in File.ReadAllLines(Settings)){string[] parts=line.Split('=');int hue;if(parts.Length==2&&int.TryParse(parts[1],out hue)&&hue>=0&&hue<=359)hues[parts[0]]=hue;}}catch{}}
    public static int? Hue(string key){int hue;return hues.TryGetValue(key,out hue)?(int?)hue:null;}
    public static void SetHue(string key,int? hue){if(hue.HasValue)hues[key]=hue.Value;else hues.Remove(key);}
    public static void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(Settings));var lines=new List<string>();foreach(var pair in hues)lines.Add(pair.Key+"="+pair.Value);File.WriteAllLines(Settings,lines.ToArray());}catch{}}
    public static Color ColorFor(UsageProvider provider){int? hue=Hue(provider.Key);return hue.HasValue?WidgetColors.FromHue(hue.Value):provider.DefaultColor;}
}

static class WidgetColors {
    public static string Mode="remaining";public static int Hue=130,EndHue=0;
    public static readonly Color StartColor=Color.FromArgb(76,222,128),EndColor=Color.FromArgb(239,68,68);
    static readonly string Settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GPTUsageTray","widget-color.txt");
    static WidgetColors(){try{string[] values=File.ReadAllLines(Settings);int hue,end;if(values.Length>=2&&int.TryParse(values[1],out hue)&&hue>=0&&hue<=359){Mode=values[0]=="single"?"single":values[0]=="custom"?"custom":"remaining";Hue=hue;EndHue=values.Length>=3&&int.TryParse(values[2],out end)&&end>=0&&end<=359?end:0;if(Mode=="single")EndHue=Hue;}}catch{}}
    public static void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(Settings));File.WriteAllLines(Settings,new[]{Mode,Hue.ToString(),EndHue.ToString()});}catch{}}
    public static Color FromHue(double hue){hue=((hue%360)+360)%360;double c=0.64,x=c*(1-Math.Abs(hue/60%2-1)),m=0.22,r=0,g=0,b=0;if(hue<60){r=c;g=x;}else if(hue<120){r=x;g=c;}else if(hue<180){g=c;b=x;}else if(hue<240){g=x;b=c;}else if(hue<300){r=x;b=c;}else{r=c;b=x;}return Color.FromArgb((int)((r+m)*255),(int)((g+m)*255),(int)((b+m)*255));}
}
class WidgetHueSlider : Control {
    public Color? PreviewColor;
    int hue;public event EventHandler ValueChanged;
    public int Hue{get{return hue;}set{value=Math.Max(0,Math.Min(359,value));if(value==hue)return;hue=value;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    public WidgetHueSlider(){DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;AccessibleName="위젯 단일 색상";AccessibleRole=AccessibleRole.Slider;BackColor=DetailsUi.Surface;}
    void Pick(int x){Hue=(int)(Math.Max(0,Math.Min(1,(x-10.0)/Math.Max(1,Width-20)))*359);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();Capture=true;Pick(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture&&e.Button==MouseButtons.Left)Pick(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left)Capture=false;}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left){Hue--;e.Handled=true;}if(e.KeyCode==Keys.Right){Hue++;e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2;var r=new Rectangle(10,y-4,Math.Max(1,Width-20),8);using(var p=DetailsUi.Round(r,4))using(var b=new LinearGradientBrush(r,Color.Red,Color.Red,0f)){var colors=new Color[7];var positions=new float[7];for(int i=0;i<7;i++){colors[i]=WidgetColors.FromHue(i*60);positions[i]=i/6f;}b.InterpolationColors=new ColorBlend{Colors=colors,Positions=positions};g.FillPath(b,p);}float x=10+(Width-20)*hue/359f;using(var b=new SolidBrush(PreviewColor??WidgetColors.FromHue(hue)))g.FillEllipse(b,x-7,y-7,14,14);using(var p=new Pen(Color.White,2))g.DrawEllipse(p,x-7,y-7,14,14);if(!Enabled)using(var b=new SolidBrush(Color.FromArgb(140,BackColor)))g.FillRectangle(b,ClientRectangle);if(Focused&&Enabled)ControlPaint.DrawFocusRectangle(g,ClientRectangle);}
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
    Color barColor=DetailsUi.Accent;public Color BarColor{get{return barColor;}set{barColor=value;Invalidate();}}
    public DetailsBar(){DoubleBuffered=true;BackColor=DetailsUi.Surface;Height=10;}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2;using(var p=new Pen(DetailsUi.Surface2,7)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,4,y,Width-4,y);}if(remaining>0)using(var p=new Pen(barColor,7)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,4,y,4+(int)((Width-8)*remaining/100),y);}}
}

class AiTabButton : Control {
    public readonly UsageProvider Provider;bool selected;
    public bool Selected{get{return selected;}set{selected=value;Invalidate();}}
    public AiTabButton(UsageProvider provider){Provider=provider;DoubleBuffered=true;Cursor=Cursors.Hand;BackColor=DetailsUi.Background;ForeColor=DetailsUi.Muted;Font=new Font("맑은 고딕",9.5f,FontStyle.Bold);AccessibleRole=AccessibleRole.PageTab;AccessibleName=provider.Name;}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        if(selected)using(var p=DetailsUi.Round(new Rectangle(0,0,Width-1,Height-1),(Height-1)/2))using(var b=new SolidBrush(DetailsUi.Surface2))g.FillPath(b,p);
        // Not-yet-connected AIs stay clickable (they open setup guidance) but look dimmed.
        bool dim=!Provider.Available;
        Color dot=dim?Color.FromArgb(95,Provider.Color):Provider.Color,text=selected?DetailsUi.Text:dim?Color.FromArgb(92,102,96):DetailsUi.Muted;
        Size size=TextRenderer.MeasureText(g,Provider.Name,Font,Size.Empty,TextFormatFlags.NoPadding);
        int x=(Width-(7+5+size.Width))/2;
        using(var b=new SolidBrush(dot))g.FillEllipse(b,x,Height/2f-3.5f,7,7);
        TextRenderer.DrawText(g,Provider.Name,Font,new Point(x+12,(Height-size.Height)/2),text,TextFormatFlags.NoPadding);
    }
}

class DetailsForm : Form {
    readonly System.Windows.Forms.Timer colorSaveTimer=new System.Windows.Forms.Timer {Interval=350};
    public event EventHandler ColorsChanged;
    Label title,main,updated,colorHint;Panel windows;Button refresh;
    RadioButton brandColor,pickColor;WidgetHueSlider hue;bool syncing;
    readonly List<AiTabButton> tabs=new List<AiTabButton>();
    UsageProvider current;
    readonly Color bg=DetailsUi.Background,surface=DetailsUi.Surface,muted=DetailsUi.Muted;
    public event EventHandler RefreshRequested;
    public UsageProvider Current{get{return current;}}
    public DetailsForm(IList<UsageProvider> providers) {
        Text="AI 사용량";ClientSize=new Size(480,523);MinimumSize=MaximumSize=Size;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=bg;ForeColor=DetailsUi.Text;Font=new Font("맑은 고딕",10);ShowInTaskbar=true;
        title=new Label {Text="GPT Usage",Location=new Point(26,18),Size=new Size(222,44),Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=DetailsUi.Text};Controls.Add(title);
        int x=456-providers.Count*66-(providers.Count-1)*4;
        foreach(var provider in providers){var tab=new AiTabButton(provider){Location=new Point(x,25),Size=new Size(66,30)};tab.Click+=delegate{ShowProvider(tab.Provider);};tabs.Add(tab);Controls.Add(tab);x+=70;}
        main=new Label {Location=new Point(27,62),Size=new Size(425,42),Font=new Font("맑은 고딕",22,FontStyle.Bold),Text="사용량 확인 중…",ForeColor=DetailsUi.Text};Controls.Add(main);
        updated=new Label {Location=new Point(29,108),Size=new Size(420,24),ForeColor=muted,AutoEllipsis=true};Controls.Add(updated);
        windows=new DetailsCard {Location=new Point(24,145),Size=new Size(432,138)};Controls.Add(windows);
        var colors=new DetailsCard {Location=new Point(24,297),Size=new Size(432,158)};Controls.Add(colors);
        brandColor=new RadioButton {Text="기본 색상",Location=new Point(18,16),Size=new Size(120,28)};
        pickColor=new RadioButton {Text="색상 직접 선택",Location=new Point(140,16),Size=new Size(160,28)};
        colors.Controls.AddRange(new Control[]{brandColor,pickColor});
        colors.Controls.Add(new Label {Text="점·밑줄 색상",Location=new Point(18,62),Size=new Size(90,30),TextAlign=ContentAlignment.MiddleLeft,ForeColor=muted});
        hue=new WidgetHueSlider {Location=new Point(108,62),Size=new Size(306,30),AccessibleName="AI 색상"};colors.Controls.Add(hue);
        colorHint=new Label {Location=new Point(18,112),Size=new Size(396,25),ForeColor=muted};colors.Controls.Add(colorHint);
        brandColor.CheckedChanged+=delegate{if(syncing||!brandColor.Checked||current==null)return;AiColors.SetHue(current.Key,null);OnColorChanged();};
        pickColor.CheckedChanged+=delegate{if(syncing||!pickColor.Checked||current==null)return;AiColors.SetHue(current.Key,hue.Hue);OnColorChanged();};
        hue.ValueChanged+=delegate{if(syncing||current==null)return;AiColors.SetHue(current.Key,hue.Hue);OnColorChanged();};
        colorSaveTimer.Tick+=delegate{colorSaveTimer.Stop();AiColors.Save();};
        refresh=new Button {Text="지금 새로고침",Location=new Point(306,469),Size=new Size(150,40)};DetailsUi.Button(refresh);Controls.Add(refresh);
        refresh.Click+=delegate {if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        ShowProvider(providers[0]);
    }
    public void ShowProvider(UsageProvider provider) {
        current=provider;foreach(var tab in tabs)tab.Selected=tab.Provider==provider;
        title.Text=provider.Name+" Usage";Text=provider.Name+" 사용량";
        SyncColors();Render();
    }
    public void ProviderChanged(UsageProvider provider){foreach(var tab in tabs)tab.Invalidate();if(provider==current)Render();}
    void SyncColors(){
        syncing=true;int? picked=AiColors.Hue(current.Key);
        brandColor.Checked=!picked.HasValue;pickColor.Checked=picked.HasValue;
        hue.Hue=picked.HasValue?picked.Value:(int)Math.Round(current.DefaultColor.GetHue());hue.PreviewColor=picked.HasValue?null:(Color?)current.DefaultColor;hue.Enabled=picked.HasValue;hue.Invalidate();
        colorHint.Text=picked.HasValue?"작업표시줄의 "+current.Name+" 점과 밑줄에 적용됩니다 · 자동 저장":current.Name+" 기본 색상을 사용합니다";
        syncing=false;
    }
    void OnColorChanged(){SyncColors();foreach(var tab in tabs)tab.Invalidate();Render();colorSaveTimer.Stop();colorSaveTimer.Start();if(ColorsChanged!=null)ColorsChanged(this,EventArgs.Empty);}
    void Render() {
        var provider=current;refresh.Enabled=!provider.Loading;main.ForeColor=DetailsUi.Text;
        if(!provider.Available) {
            main.Text="연결되지 않음";updated.Text=provider.Name+" 사용량을 읽을 수 있는 프로그램이 없습니다";windows.Controls.Clear();
            windows.Controls.Add(new Label {Text=provider.SetupHint,Location=new Point(18,14),Size=new Size(395,118),ForeColor=muted,BackColor=surface});return;
        }
        if(provider.Snapshot==null) {
            if(provider.Error!=null&&!provider.Loading){ShowError(provider.Error);return;}
            main.Text="사용량 확인 중…";updated.Text=provider.Source+"에서 최신 사용량을 읽고 있습니다…";windows.Controls.Clear();AddUnavailableFiveHourRow();return;
        }
        UpdateData(provider.Snapshot);
        if(provider.Loading)updated.Text=provider.Source+"에서 최신 사용량을 읽고 있습니다…";
        else if(provider.Error!=null)updated.Text="마지막 갱신 "+provider.Snapshot.Updated.ToString("HH:mm:ss")+" · "+provider.Error;
    }
    void AddUnavailableFiveHourRow(){
        windows.Controls.Add(new Label {Text="5시간 한도 · 정보 없음",Location=new Point(18,14),Size=new Size(395,24),ForeColor=muted,BackColor=surface});
        windows.Controls.Add(new DetailsBar {Location=new Point(18,45),Size=new Size(393,10),Remaining=0,Enabled=false});
    }
    void ShowError(string error){main.Text="사용량을 읽지 못했습니다";updated.Text=error;windows.Controls.Clear();AddUnavailableFiveHourRow();}
    void UpdateData(UsageSnapshot snapshot) {
        double? remaining=snapshot.Remaining;
        main.Text=remaining.HasValue?"주간 남은 사용량  "+remaining.Value.ToString("0")+"%":"사용량 정보 없음";
        if(remaining.HasValue&&remaining.Value<UsageProvider.WarningBelow)main.ForeColor=UsageProvider.Warning;
        updated.Text="마지막 갱신  "+snapshot.Updated.ToString("yyyy-MM-dd HH:mm:ss");
        windows.Controls.Clear();int y=14;
        bool hasFiveHour=snapshot.Windows.Exists(delegate(UsageWindow item){return item.DurationMinutes==300;});
        if(!hasFiveHour){AddUnavailableFiveHourRow();y+=64;}
        foreach(var item in snapshot.Windows) {
            var label=new Label {Text=(item.DurationMinutes==300?"5시간":item.Name)+"  "+item.Remaining.ToString("0")+"% 남음",Location=new Point(18,y),Size=new Size(205,24),ForeColor=DetailsUi.Text,BackColor=surface,Font=new Font("맑은 고딕",10,FontStyle.Bold)};windows.Controls.Add(label);
            string reset=item.ResetLocal.HasValue?"초기화 "+item.ResetLocal.Value.ToString("MM/dd HH:mm"):"";
            var resetLabel=new Label {Text=reset,Location=new Point(225,y),Size=new Size(186,24),TextAlign=ContentAlignment.MiddleRight,ForeColor=muted,BackColor=surface};windows.Controls.Add(resetLabel);
            windows.Controls.Add(new DetailsBar {Location=new Point(18,y+31),Size=new Size(393,10),Remaining=item.Remaining,BarColor=current.Color});y+=64;
        }
        if(snapshot.Windows.Count==0)windows.Controls.Add(new Label {Text="현재 계정에서 한도 정보를 제공하지 않습니다.",Location=new Point(18,y),Size=new Size(395,30),ForeColor=muted,BackColor=surface});
    }
    protected override void Dispose(bool disposing){if(disposing){if(colorSaveTimer.Enabled)AiColors.Save();colorSaveTimer.Dispose();}base.Dispose(disposing);}
}

class UsageWidgetForm : Form {
    // One chip per connected AI: colored dot + remaining number, with a remaining-amount underline.
    const int ChipWidth=54,ChipGap=10,Pad=2,WidgetHeight=44;
    const float DotSize=9f,LineWidth=6f,LineGap=7f,MinNumberGap=6f,MaxFontSize=20f,MinFontSize=14f;
    // The number sits centered between the dot's right edge and the underline's right end.
    const float NumberLeft=1+DotSize,NumberRight=ChipWidth-0.5f;
    static float fontSize,digitBottom,lineY;

    // Pick the largest font (up to MaxFontSize) whose widest rendered value from 0 to 100 still
    // leaves MinNumberGap on both sides, then center the digits and underline vertically.
    static void EnsureLayout() {
        if(fontSize>0)return;
        float digitHeight=0;
        using(var family=new FontFamily("Segoe UI")) {
            for(float size=MaxFontSize;size>=MinFontSize;size-=0.5f) {
                float widest=0;
                for(int value=0;value<=100;value++)using(var path=new GraphicsPath()) {
                    path.AddString(value.ToString(CultureInfo.InvariantCulture),family,(int)FontStyle.Bold,size,PointF.Empty,StringFormat.GenericTypographic);
                    widest=Math.Max(widest,path.GetBounds().Width);
                }
                fontSize=size;
                if(widest+MinNumberGap*2<=NumberRight-NumberLeft)break;
            }
            using(var path=new GraphicsPath()) {path.AddString("0",family,(int)FontStyle.Bold,fontSize,PointF.Empty,StringFormat.GenericTypographic);digitHeight=path.GetBounds().Height;}
        }
        float top=(WidgetHeight-(digitHeight+LineGap+LineWidth))/2;
        digitBottom=top+digitHeight;lineY=digitBottom+LineGap+LineWidth/2;
    }
    readonly List<UsageProvider> shown=new List<UsageProvider>();
    string renderedKey;
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly System.Windows.Forms.Timer anchorTimer=new System.Windows.Forms.Timer();
    IntPtr taskbarHandle=IntPtr.Zero;
    public event Action<UsageProvider> DetailsRequested;
    public event EventHandler RefreshRequested;
    public event EventHandler ExitRequested;

    public UsageWidgetForm() {
        Text="GPT Usage Tray";ClientSize=new Size(ChipWidth+Pad*2,WidgetHeight);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false;TopMost=false;BackColor=Color.Black;DoubleBuffered=true;Cursor=Cursors.Hand;
        menu.Items.Add("상세 보기",null,delegate {if(DetailsRequested!=null)DetailsRequested(null);});
        menu.Items.Add("지금 새로고침",null,delegate {if(RefreshRequested!=null)RefreshRequested(this,EventArgs.Empty);});
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료",null,delegate {if(ExitRequested!=null)ExitRequested(this,EventArgs.Empty);});
        ContextMenuStrip=menu;
        MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&DetailsRequested!=null)DetailsRequested(ProviderAt(e.X));};
        Shown+=delegate {AttachToTaskbar();RenderLayered();};
        anchorTimer.Interval=3000;anchorTimer.Tick+=delegate {if(Visible)AttachToTaskbar();};anchorTimer.Start();
    }

    protected override bool ShowWithoutActivation {get{return true;}}
    protected override void OnHandleDestroyed(EventArgs e){renderedKey=null;base.OnHandleDestroyed(e);}
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x80|0x80000;return value;}}

    public void UpdateProviders(IList<UsageProvider> providers) {
        shown.Clear();foreach(var provider in providers)if(provider.Available)shown.Add(provider);
        int count=Math.Max(1,shown.Count),width=Pad*2+count*ChipWidth+(count-1)*ChipGap;
        if(ClientSize.Width!=width){ClientSize=new Size(width,WidgetHeight);if(IsHandleCreated)AttachToTaskbar();}
        RenderLayered();
    }

    // Every point of the widget belongs to the nearest chip, so the whole rectangle around
    // each AI (including the gaps) opens that AI's details.
    UsageProvider ProviderAt(int x) {
        if(shown.Count==0)return null;
        int index=(x-Pad+ChipGap/2)/(ChipWidth+ChipGap);
        return shown[Math.Max(0,Math.Min(shown.Count-1,index))];
    }

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

    public Bitmap CreateGaugeBitmap() {return CreateGaugeBitmap(ReadTaskbarTextColor());}

    Bitmap CreateGaugeBitmap(Color textColor) {
        var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);using(Graphics g=Graphics.FromImage(bitmap)) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        // Layered windows pass clicks through alpha-zero pixels. An imperceptible fill makes
        // the whole widget clickable, including the space around the digits and between chips.
        using(var hitArea=new SolidBrush(Color.FromArgb(1,0,0,0)))g.FillRectangle(hitArea,0,0,Width,Height);
        if(shown.Count==0)DrawChip(g,Pad,UsageProvider.Missing,null,"?",textColor);
        for(int i=0;i<shown.Count;i++) {
            var provider=shown[i];double? value=provider.Snapshot==null?null:provider.Snapshot.Remaining;
            if(value.HasValue)value=Math.Max(0,Math.Min(100,value.Value));
            DrawChip(g,Pad+i*(ChipWidth+ChipGap),provider.Color,value,value.HasValue?Math.Round(value.Value).ToString("0"):"–",textColor);
        }
        }return bitmap;
    }

    static void DrawChip(Graphics g,int x,Color color,double? remaining,string text,Color textColor) {
        EnsureLayout();
        Color number=!remaining.HasValue?UsageProvider.Missing:remaining.Value<UsageProvider.WarningBelow?UsageProvider.Warning:textColor;
        float digitHeight;
        using(var font=new Font("Segoe UI",fontSize,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var brush=new SolidBrush(number))
        using(var digits=new GraphicsPath())
        using(var path=new GraphicsPath())
        using(var transform=new Matrix()) {
            // Position by visible outlines (not font boxes): vertically on a digit so "–" and "?" sit
            // at the same height as numbers, horizontally centered between the dot and the line end.
            digits.AddString("0",font.FontFamily,(int)font.Style,font.Size,PointF.Empty,StringFormat.GenericTypographic);
            RectangleF reference=digits.GetBounds();digitHeight=reference.Height;
            path.AddString(text,font.FontFamily,(int)font.Style,font.Size,PointF.Empty,StringFormat.GenericTypographic);
            RectangleF bounds=path.GetBounds();
            transform.Translate(x+(NumberLeft+NumberRight)/2-(bounds.X+bounds.Width/2),digitBottom-reference.Bottom);path.Transform(transform);g.FillPath(brush,path);
        }
        using(var dot=new SolidBrush(color))g.FillEllipse(dot,x+1,digitBottom-digitHeight/2-DotSize/2,DotSize,DotSize);
        float left=x+LineWidth/2+0.5f,right=x+ChipWidth-LineWidth/2-0.5f;
        using(var track=new Pen(UsageProvider.Missing,LineWidth)){track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,left,lineY,right,lineY);}
        if(remaining.HasValue&&remaining.Value>0)using(var fill=new Pen(color,LineWidth)){fill.StartCap=fill.EndCap=LineCap.Round;g.DrawLine(fill,left,lineY,left+(float)((right-left)*remaining.Value/100),lineY);}
    }

    static Color ReadTaskbarTextColor() {
        try {using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){object value=key==null?null:key.GetValue("SystemUsesLightTheme");bool light=value==null||Convert.ToInt32(value)!=0;return light?Color.Black:Color.White;}}
        catch{return Color.Black;}
    }

    void RenderLayered() {
        if(!IsHandleCreated||IsDisposed)return;
        Color textColor=ReadTaskbarTextColor();
        // Keep only the last successful render's inputs, not a cached bitmap.
        var key=new StringBuilder(Size.ToString()+textColor.ToArgb());
        foreach(var provider in shown)key.Append('|').Append(provider.Key).Append(provider.Color.ToArgb()).Append(':').Append(provider.Snapshot==null||!provider.Snapshot.Remaining.HasValue?"-":Math.Round(provider.Snapshot.Remaining.Value,1).ToString(CultureInfo.InvariantCulture));
        if(key.ToString()==renderedKey)return;
        using(var bitmap=CreateGaugeBitmap(textColor)) {
            IntPtr screenDc=GetDC(IntPtr.Zero),memoryDc=CreateCompatibleDC(screenDc),hBitmap=bitmap.GetHbitmap(Color.FromArgb(0)),oldBitmap=IntPtr.Zero;
            try {
                oldBitmap=SelectObject(memoryDc,hBitmap);
                var destination=new POINT {X=Left,Y=Top};var size=new SIZE {Width=Width,Height=Height};var source=new POINT();
                var blend=new BLENDFUNCTION {BlendOp=0,BlendFlags=0,SourceConstantAlpha=255,AlphaFormat=1};
                if(UpdateLayeredWindow(Handle,screenDc,ref destination,ref size,memoryDc,ref source,0,ref blend,2))renderedKey=key.ToString();
            } finally {
                if(oldBitmap!=IntPtr.Zero)SelectObject(memoryDc,oldBitmap);DeleteObject(hBitmap);DeleteDC(memoryDc);ReleaseDC(IntPtr.Zero,screenDc);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) {}

    protected override void Dispose(bool disposing) {if(disposing){anchorTimer.Dispose();menu.Dispose();}base.Dispose(disposing);}
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
    readonly List<UsageProvider> providers=UsageProvider.CreateAll();
    DetailsForm details;UsageWidgetForm widget;
    public UsageContext() {
        marshal.CreateControl();
        // Decide the widget layout up front (cheap local checks); values arrive with the first refresh.
        foreach(var provider in providers)provider.Available=IsAvailable(provider);
        timer.Interval=60000;timer.Tick+=delegate{Refresh();};timer.Start();
        SetStartup(true);
        widget=new UsageWidgetForm();widget.DetailsRequested+=ShowDetails;widget.RefreshRequested+=delegate{Refresh();};widget.ExitRequested+=delegate{Exit();};widget.UpdateProviders(providers);widget.Show();
        Refresh();
    }
    static bool IsAvailable(UsageProvider provider){try{return provider.IsAvailable();}catch{return false;}}
    void Refresh() {
        foreach(var item in providers) {
            var provider=item;if(provider.Loading)continue;provider.Loading=true;
            if(details!=null)details.ProviderChanged(provider);
            Task.Run(delegate {
                // Availability is re-checked every refresh, so an AI appears as soon as you log in to it.
                bool available=IsAvailable(provider);UsageSnapshot snapshot=null;string error=null;
                if(available){try{snapshot=provider.Reader();}catch(Exception ex){error=ex.GetBaseException().Message;}}
                marshal.BeginInvoke((Action)delegate {
                    provider.Loading=false;provider.Available=available;
                    // On a temporary failure keep the last good snapshot and show the error next to it.
                    if(snapshot!=null){provider.Snapshot=snapshot;provider.Error=null;}
                    else if(available)provider.Error=error;
                    else{provider.Snapshot=null;provider.Error=null;}
                    if(widget!=null)widget.UpdateProviders(providers);
                    if(details!=null)details.ProviderChanged(provider);
                    TrimWorkingSet();
                });
            });
        }
    }
    void ShowDetails(UsageProvider provider) {
        if(details==null){details=new DetailsForm(providers);details.RefreshRequested+=delegate{Refresh();};details.ColorsChanged+=delegate{widget.UpdateProviders(providers);};}
        if(provider==null)provider=details.Current.Available?details.Current:providers.Find(delegate(UsageProvider item){return item.Available;})??providers[0];
        details.ShowProvider(provider);details.Show();details.WindowState=FormWindowState.Normal;details.Activate();
    }
    void SetStartup(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(enabled)key.SetValue(RunName,"\""+Application.ExecutablePath+"\"");else key.DeleteValue(RunName,false);}}
    void Exit(){timer.Stop();if(details!=null)details.Dispose();if(widget!=null)widget.Dispose();ExitThread();}
    static void TrimWorkingSet(){try{using(var process=Process.GetCurrentProcess())SetProcessWorkingSetSize(process.Handle,new IntPtr(-1),new IntPtr(-1));}catch{}}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();marshal.Dispose();}base.Dispose(disposing);}
    [DllImport("kernel32.dll")]static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr minimum,IntPtr maximum);
}

class Program {
    static List<UsageProvider> ReadAll() {
        var providers=UsageProvider.CreateAll();
        foreach(var provider in providers) {
            try {provider.Available=provider.IsAvailable();} catch {provider.Available=false;}
            if(provider.Available)try {provider.Snapshot=provider.Reader();} catch(Exception ex) {provider.Error=ex.GetBaseException().Message;}
        }
        return providers;
    }
    [STAThread]static void Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--check") {
            try {
                var text=new StringBuilder();
                foreach(var provider in ReadAll()) {
                    var data=provider.Snapshot;
                    text.AppendLine(provider.Key+": available="+provider.Available+" weekly_remaining="+(data!=null&&data.Remaining.HasValue?data.Remaining.Value.ToString("0.##",CultureInfo.InvariantCulture):"unknown")+" windows="+(data==null?0:data.Windows.Count)+(provider.Error!=null?" error="+provider.Error:""));
                }
                File.WriteAllText(args[1]+".txt",text.ToString());Environment.ExitCode=0;
            }
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        if(args.Length==2&&args[0]=="--ui-test") {
            try {
                var providers=ReadAll();
                using(var form=new DetailsForm(providers)){form.Show();foreach(var provider in providers){form.ShowProvider(provider);Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.ChangeExtension(args[1],null)+"-"+provider.Key+".png");}}form.Dispose();}
                Environment.ExitCode=0;
            }
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        if(args.Length==2&&args[0]=="--widget-test") {
            try {
                var providers=UsageProvider.CreateAll();double[] used={35,85,0};
                for(int i=0;i<providers.Count;i++){providers[i].Available=true;providers[i].Snapshot=new UsageSnapshot();providers[i].Snapshot.Windows.Add(new UsageWindow{Name="주간",Used=used[i],DurationMinutes=10080});}
                using(var form=new UsageWidgetForm()){form.UpdateProviders(providers);using(var bitmap=form.CreateGaugeBitmap())bitmap.Save(args[1]);}Environment.ExitCode=0;
            }
            catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
        }
        bool first;using(var mutex=new Mutex(true,"Local\\GPT-Usage-Tray",out first)){if(!first)return;Application.Run(new UsageContext());}
    }
}
