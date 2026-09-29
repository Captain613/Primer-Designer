using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using RpaDesigner;

internal static class UnlimitedUiCheck
{
    private static readonly List<string> errors=new List<string>();
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
                Handles(form);
                var mode=Field<ComboBox>(form,"designMode");
                string[] rpaNames={"primerMin","primerMax","gcMin","gcMax","productMin","productMax","productIdeal"};
                string[] rpaFlags={"PrimerMinUnlimited","PrimerMaxUnlimited","GcMinUnlimited","GcMaxUnlimited","AmpliconMinUnlimited","AmpliconMaxUnlimited","PreferredAmpliconUnlimited"};
                string[] lampNames={"lampRegionMin","lampRegionMax","lampGcMin","lampGcMax","lampSpanMin","lampSpanMax","lampCoreSpanMin","lampCoreSpanMax",
                    "lampF3TmMin","lampF3TmMax","lampB3TmMin","lampB3TmMax","lampF2TmMin","lampF2TmMax","lampB2TmMin","lampB2TmMax",
                    "lampF1cTmMin","lampF1cTmMax","lampB1cTmMin","lampB1cTmMax","lampLFTmMin","lampLFTmMax","lampLBTmMin","lampLBTmMax"};
                string[] lampFlags={"RegionMinUnlimited","RegionMaxUnlimited","GcMinUnlimited","GcMaxUnlimited","SpanMinUnlimited","SpanMaxUnlimited","CoreSpanMinUnlimited","CoreSpanMaxUnlimited",
                    "F3Tm.MinUnlimited","F3Tm.MaxUnlimited","B3Tm.MinUnlimited","B3Tm.MaxUnlimited","F2Tm.MinUnlimited","F2Tm.MaxUnlimited","B2Tm.MinUnlimited","B2Tm.MaxUnlimited",
                    "F1cTm.MinUnlimited","F1cTm.MaxUnlimited","B1cTm.MinUnlimited","B1cTm.MaxUnlimited","LFTm.MinUnlimited","LFTm.MaxUnlimited","LBTm.MinUnlimited","LBTm.MaxUnlimited"};
                for(int m=0;m<6;m++)
                {
                    mode.SelectedIndex=m;Application.DoEvents();
                    string[] names=m<2?rpaNames:lampNames,flags=m<2?rpaFlags:lampFlags;
                    string settings=m<2?"Settings":"LampSettings",reset=m<2?"ResetSettings":"ResetLampSettings";
                    Invoke(form,reset);
                    for(int i=0;i<names.Length;i++)
                    {
                        var number=Field<NumericUpDown>(form,names[i]);var option=Named<CheckBox>(form,names[i]+"Unlimited");
                        decimal before=number.Value;
                        Check(!option.Checked&&number.Enabled,"Finite field starts enabled: "+names[i]);
                        option.Checked=true;
                        object current=Invoke(form,settings);
                        Check(ReadFlag(current,flags[i]),"Checkbox maps to flag: "+flags[i]);
                        Check(!number.Enabled&&number.Value==before,"Unlimited preserves the dormant value: "+names[i]);
                        option.Checked=false;
                        Check(number.Enabled&&number.Value==before,"Unchecking restores value: "+names[i]);
                    }
                    foreach(string name in names)Named<CheckBox>(form,name+"Unlimited").Checked=true;
                    object all=Invoke(form,settings);
                    foreach(string flag in flags)Check(ReadFlag(all,flag),"All filters may be disabled together: "+flag);
                    Check(Field<NumericUpDown>(form,m<2?"pairCount":"lampCount").Value==10,"Candidate count stays finite and unchanged.");
                    if(m==5)
                    {
                        form.PerformLayout();Application.DoEvents();
                        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"unlimited-input.png"),ImageFormat.Png);}
                    }
                    Invoke(form,reset);
                    foreach(string name in names)Check(!Named<CheckBox>(form,name+"Unlimited").Checked&&Field<NumericUpDown>(form,name).Enabled,"Restore defaults clears unlimited: "+name);
                }
                mode.SelectedIndex=0;Named<CheckBox>(form,"gcMinUnlimited").Checked=true;
                mode.SelectedIndex=2;Check(!Named<CheckBox>(form,"lampGcMinUnlimited").Checked,"RPA and LAMP filters are independent.");
                Named<CheckBox>(form,"lampF1cTmMaxUnlimited").Checked=true;mode.SelectedIndex=1;
                Check(Named<CheckBox>(form,"gcMinUnlimited").Checked,"RPA filter choice survives mode switches.");
                mode.SelectedIndex=5;Check(Named<CheckBox>(form,"lampF1cTmMaxUnlimited").Checked,"LAMP filter choice survives mode switches.");
                Check(form.Controls.Find("pairCountUnlimited",true).Length==0&&form.Controls.Find("lampCountUnlimited",true).Length==0,"Output counts have no unlimited option.");
                Check(form.Controls.Find("lampPaTailUnlimited",true).Length==0,"PA tail remains a construction setting.");
                Check(!form.Visible,"Test never displays a desktop window.");
            }
        }
        catch(Exception ex){errors.Add(ex.ToString());}
        var report=new List<string>{"Unlimited filter UI checks","Assertions="+checks+"; failures="+errors.Count};
        foreach(string error in errors)report.Add("FAIL "+error);
        report.Add(errors.Count==0?"RESULT: PASS":"RESULT: FAIL");
        File.WriteAllLines(Path.Combine(output,"unlimited-ui-test-report.txt"),report.ToArray(),new UTF8Encoding(true));return errors.Count==0?0:1;
    }
    private static object Invoke(object instance,string name){return instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,null);}
    private static bool ReadFlag(object instance,string path){foreach(string part in path.Split('.'))instance=instance.GetType().GetField(part).GetValue(instance);return (bool)instance;}
    private static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}
    private static T Named<T>(Control parent,string name)where T:Control{var items=parent.Controls.Find(name,true);if(items.Length!=1)throw new Exception("Missing or duplicate control "+name);return (T)items[0];}
    private static void Handles(Control control){IntPtr ignored=control.Handle;foreach(Control child in control.Controls)Handles(child);}
    private static void Check(bool value,string message){checks++;if(!value)errors.Add(message);}
}
