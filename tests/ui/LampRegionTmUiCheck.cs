using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using RpaDesigner;

// Hidden, offline checks for independently editable LAMP region ranges.
internal static class LampRegionTmUiCheck
{
    private static readonly string[] roles={"F3","B3","F2","B2","F1c","B1c","LF","LB"};
    private static readonly List<string> errors=new List<string>();
    private static readonly List<string> notes=new List<string>();
    private static readonly MethodInfo visibleState=typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic);
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new MainForm())
            {
                Handles(form);Layout(form);Application.DoEvents();
                var mode=Field<ComboBox>(form,"designMode");mode.SelectedIndex=2;
                var pages=Named<TabControl>(form,"lampParameterTabs");
                Check(pages.TabPages.Count==2&&pages.TabPages[0].Text=="长度与 GC"&&pages.TabPages[1].Text=="各区段 Tm","Two clearly named parameter pages exist.");
                var defaults=new LampDesignSettings();CheckDefaults(form,defaults);
                foreach(string role in roles)
                {
                    Invoke(form,"ResetLampSettings");
                    Number(form,role,"Min").Value=40;Number(form,role,"Max").Value=80;
                    Named<CheckBox>(form,"lamp"+role+"TmMinUnlimited").Checked=true;
                    var settings=Settings(form);
                    foreach(string other in roles)
                    {
                        LampTmRange range=settings.GetTm(other),standard=defaults.GetTm(other);
                        Check(other==role?range.Min==40&&range.Max==80&&range.MinUnlimited&&!range.MaxUnlimited:
                            range.Min==standard.Min&&range.Max==standard.Max&&!range.MinUnlimited&&!range.MaxUnlimited,
                            role+" edit and one-sided unlimited affect only the selected region; inspected "+other+".");
                    }
                }
                Invoke(form,"ResetLampSettings");
                Field<NumericUpDown>(form,"lampCoreSpanMax").Value=155;
                Named<CheckBox>(form,"lampCoreSpanMinUnlimited").Checked=true;
                var spans=Settings(form);
                Check(spans.CoreSpanMax==155&&spans.CoreSpanMinUnlimited&&!spans.CoreSpanMaxUnlimited&&spans.SpanMin==110&&spans.SpanMax==350,
                    "F2-B2 span and its one-sided unlimited remain independent of F3-B3 span.");
                mode.SelectedIndex=4;mode.SelectedIndex=5;mode.SelectedIndex=2;
                Check(Settings(form).CoreSpanMax==155&&Settings(form).CoreSpanMinUnlimited,"Region and span choices survive mode switches.");
                Invoke(form,"ResetLampSettings");CheckDefaults(form,defaults);

                Size regular=form.Size,minimum=form.MinimumSize;
                using(Graphics g=form.CreateGraphics())notes.Add("DPI="+g.DpiX+"; default="+regular+"; minimum="+minimum);
                foreach(Size size in new[]{regular,minimum})
                {
                    form.Size=size;
                    foreach(int selected in new[]{2,3,4,5})
                    {
                        mode.SelectedIndex=selected;
                        foreach(int page in new[]{0,1})
                        {
                            pages.SelectedIndex=page;Layout(form);Application.DoEvents();
                            string state="size="+size+" mode="+selected+" page="+page;
                            var group=Field<GroupBox>(form,"lampSettingsGroup");CheckLayout(group,state);
                            using(Graphics g=form.CreateGraphics())Check(Named<Button>(form,"restoreLampDefaults").Height<=50*g.DpiY/96F,
                                state+" restore action retains a normal button height.");
                            CheckFixedActions(form,state);
                            if(size==minimum&&selected==2)
                            {
                                Save(group,Path.Combine(output,page==0?"lamp-ranges-minimum.png":"lamp-region-tm-minimum.png"));
                                if(page==1)Save(form,Path.Combine(output,"lamp-input-minimum.png"));
                            }
                        }
                    }
                }
                Check(!form.Visible,"Test never shows a desktop window.");
                Check(!Field<CheckBox>(form,"blastEnabled").Checked,"Online BLAST remains disabled by default.");
            }
        }
        catch(Exception ex){errors.Add(ex.ToString());}
        notes.Add("Assertions="+checks+"; failures="+errors.Count);
        foreach(string error in errors)notes.Add("FAIL "+error);
        notes.Add(errors.Count==0?"RESULT: PASS":"RESULT: FAIL");
        File.WriteAllLines(Path.Combine(output,"lamp-region-tm-ui-test-report.txt"),notes.ToArray(),new UTF8Encoding(true));
        return errors.Count==0?0:1;
    }
    private static void CheckDefaults(MainForm form,LampDesignSettings defaults)
    {
        var settings=Settings(form);
        Check(settings.RegionMin==17&&settings.RegionMax==30&&settings.GcMin==30&&settings.GcMax==75,"Default LAMP search bounds are 17-30 nt and 30-75 percent GC.");
        Check(settings.SpanMin==110&&settings.SpanMax==350&&settings.CoreSpanMin==100&&settings.CoreSpanMax==220,
            "Two separate default span definitions are present.");
        foreach(string role in roles)
        {
            LampTmRange value=settings.GetTm(role),expected=defaults.GetTm(role);
            bool inner=role=="F1c"||role=="B1c"||role=="LF"||role=="LB";
            Check(expected.Min==(inner?58:52)&&expected.Max==(inner?72:68),"Shared default Tm search band is intentionally broad: "+role);
            Check(value.Min==expected.Min&&value.Max==expected.Max&&!value.MinUnlimited&&!value.MaxUnlimited,"Initial/reset bounds match shared broad search defaults: "+role);
            Check(Number(form,role,"Min").Enabled&&Number(form,role,"Max").Enabled,"Reset enables both numeric bounds: "+role);
        }
        Check(settings.MonovalentMilliMolar==50&&settings.MagnesiumMilliMolar==4&&settings.OligoNanoMolar==100,"UI uses the reference Tm calculation conditions.");
    }
    private static void CheckLayout(Control parent,string path)
    {
        if(parent is NumericUpDown||parent is ComboBox)return;
        var tab=parent as TabControl;
        foreach(Control child in parent.Controls)
        {
            if(tab!=null&&child!=tab.SelectedTab)continue;
            if(!(bool)visibleState.Invoke(child,new object[]{2}))continue;
            string name=path+"/"+(String.IsNullOrEmpty(child.Name)?child.GetType().Name:child.Name);
            Rectangle allowed=parent.ClientRectangle,bounds=child.Bounds;
            Check(bounds.Width>0&&bounds.Height>0&&bounds.Left>=allowed.Left-3&&bounds.Top>=allowed.Top-3&&bounds.Right<=allowed.Right+3&&bounds.Bottom<=allowed.Bottom+3,
                name+" fits parent: bounds="+bounds+" allowed="+allowed);
            if(child is Label||child is Button||child is CheckBox)
            {
                int available=Math.Max(1,child.ClientSize.Width-child.Padding.Horizontal-(child is CheckBox?24:0));
                using(Graphics g=child.CreateGraphics())
                {
                    Size measured=TextRenderer.MeasureText(g,child.Text,child.Font,new Size(available,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl);
                    Check(measured.Height<=child.ClientSize.Height-child.Padding.Vertical+2&&measured.Width<=available+2,
                        name+" text fits: measured="+measured+" available="+available+"x"+child.ClientSize.Height+" text="+child.Text.Replace("\r\n"," / "));
                }
            }
            CheckLayout(child,name);
        }
    }
    private static void CheckFixedActions(MainForm form,string state)
    {
        var scroll=Field<Panel>(form,"inputScroll");var button=Field<Button>(form,"design");var page=Field<TabControl>(form,"tabs").TabPages[0];
        Rectangle before=new Rectangle(page.PointToClient(button.PointToScreen(Point.Empty)),button.Size);
        Check(!scroll.Contains(button)&&page.ClientRectangle.Contains(before),state+" start action is fixed and inside the viewport: "+before);
        scroll.AutoScrollPosition=new Point(0,scroll.AutoScrollMinSize.Height);Layout(form);Application.DoEvents();
        Rectangle after=new Rectangle(page.PointToClient(button.PointToScreen(Point.Empty)),button.Size);
        Check(before==after,state+" scrolling parameters preserves the start button position.");
        scroll.AutoScrollPosition=Point.Empty;Layout(form);Application.DoEvents();
    }
    private static LampDesignSettings Settings(MainForm form){return (LampDesignSettings)Invoke(form,"LampSettings");}
    private static NumericUpDown Number(MainForm form,string role,string bound){return Field<NumericUpDown>(form,"lamp"+role+"Tm"+bound);}
    private static object Invoke(object value,string name){return value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);}
    private static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
    private static T Named<T>(Control parent,string name)where T:Control{Control[] matches=parent.Controls.Find(name,true);if(matches.Length!=1)throw new Exception("Missing/duplicate "+name);return (T)matches[0];}
    private static void Handles(Control control){IntPtr ignored=control.Handle;foreach(Control child in control.Controls)Handles(child);}
    private static void Layout(Control control){control.PerformLayout();foreach(Control child in control.Controls)Layout(child);}
    private static void Save(Control control,string path){using(var image=new Bitmap(control.Width,control.Height)){control.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,ImageFormat.Png);}}
    private static void Check(bool value,string description){checks++;if(!value)errors.Add(description);}
}
