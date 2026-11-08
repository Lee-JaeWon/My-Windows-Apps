using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

class DetailsUiChecks {
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static DetailsButton Button(DetailsForm form,string text){return form.Controls.OfType<DetailsButton>().First(b=>b.Text==text);}
    [STAThread]static int Main(string[] args) {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        // Isolate persistence tests from the real widget preferences.
        typeof(WidgetColors).GetField("Settings",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Path.Combine(output,"colors.txt"));
        try {
            Require(DetailsFonts.Get(14,false).FontFamily.Name=="Pretendard","Embedded Korean font did not load.");
            Color light,dark;
            using(var form=new DetailsForm(false)) {
                form.Show();Application.DoEvents();light=form.BackColor;
                int changes=0,refreshes=0;form.ColorsChanged+=delegate{changes++;};form.RefreshRequested+=delegate{refreshes++;};
                Button(form,"잔여량").PerformClick();
                var sliders=form.Controls.OfType<WidgetHueSlider>().ToArray();
                Require(sliders.All(s=>!s.Enabled),"Remaining mode must disable manual sliders.");
                Button(form,"커스텀").PerformClick();Require(sliders.All(s=>s.Enabled),"Custom mode must enable both endpoints.");
                int prior=changes;sliders[0].Hue=278;sliders[1].Hue=31;
                Require(WidgetColors.Hue==278&&WidgetColors.EndHue==31&&changes==prior+2,"Custom endpoint edits must notify once each.");
                Button(form,"단일색").PerformClick();sliders[1].Hue=172;
                Require(WidgetColors.Hue==172&&WidgetColors.EndHue==172&&sliders[0].Hue==172,"Single-color edits must synchronize both endpoints.");
                form.Loading();Require(!Button(form,"새로고침").Enabled,"Refresh must be disabled while loading.");
                form.ShowError("Offline test");Require(Button(form,"새로고침").Enabled,"Failed requests must allow retry.");
                Button(form,"새로고침").PerformClick();Require(refreshes==1,"Refresh callback was lost.");
                var sample=new UsageSnapshot();sample.Windows.Add(new UsageWindow{Name="주간",Used=0,DurationMinutes=10080});
                sample.Windows.Add(new UsageWindow{Name="단기",Used=100,DurationMinutes=300});form.UpdateData(sample);
                foreach(float scale in new[]{1f,1.5f,2f}) {
                    form.ClientSize=new Size((int)(660*scale),(int)(584*scale));Application.DoEvents();
                    foreach(Control control in form.Controls)Require(form.ClientRectangle.Contains(control.Bounds),"A control overflows at scale "+scale);
                    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"limits-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));}
                }
                form.Close();Require(!form.IsDisposed&&!form.Visible,"Closing details must hide it, not close the app.");
                form.Show();form.Hide();
            }
            string[] saved=File.ReadAllLines(Path.Combine(output,"colors.txt"));Require(saved[0]=="single"&&saved[1]=="172"&&saved[2]=="172","Pending color edits must be persisted on disposal.");
            using(var form=new DetailsForm(true)){dark=form.BackColor;form.Show();form.UpdateData(new UsageSnapshot());Application.DoEvents();}
            Require(light!=dark,"Light and dark themes must differ.");
            Console.WriteLine("PASS: embedded fonts, both themes, endpoint editing, persistence, refresh/error states, close/reopen, and 100/150/200% layout.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
