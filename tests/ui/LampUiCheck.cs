using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RpaDesigner;

// In-process UI regression check. No Show(), desktop automation, dialogs or clipboard.
internal static class LampUiCheck
{
    private static readonly List<string> notes = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<string> layout = new List<string>();
    private static Exception dispatchError;
    private static readonly Color red = Color.FromArgb(211,47,47);
    private static float dpi;
    private static bool baseline;
    private static int checks;
    private static readonly HashSet<string> scrollCases = new HashSet<string>();
    private static readonly MethodInfo visibleState = typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic);

    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(args[0]);
        baseline = args.Length > 1 && args[1] == "baseline";
        Directory.CreateDirectory(output);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { dispatchError = e.Exception; };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            using (var form = new MainForm())
            {
                Handles(form); LayoutTree(form); Application.DoEvents();
                using (Graphics g = form.CreateGraphics()) dpi = g.DpiX;
                notes.Add("Assembly=" + typeof(MainForm).Assembly.Location);
                notes.Add("Graphics.DpiX=" + dpi + "; Font=" + form.Font + "; Font.Height=" + form.Font.Height + "; WorkArea=" + Screen.PrimaryScreen.WorkingArea + "; Form.Size=" + form.Size + "; MinimumSize=" + form.MinimumSize);
                Check(dpi >= 120, "Manifest-aware test measures actual high-DPI desktop (expected 144 on this host).");
                Check(Screen.FromControl(form).WorkingArea.Contains(form.Bounds),"Initial window is fully contained in the display work area.");
                notes.Add("Initial window Bounds="+form.Bounds+"; StartPosition="+form.StartPosition);
                var mode = Field<ComboBox>(form,"designMode");
                var mismatch = Field<ComboBox>(form,"mismatchMode");
                var tabs = Field<TabControl>(form,"tabs");
                Size normal = form.Size;
                Size work = Screen.PrimaryScreen.WorkingArea.Size;
                Size small = new Size(Math.Min(work.Width,form.MinimumSize.Width),Math.Min(work.Height,form.MinimumSize.Height));
                notes.Add("Size cases: default="+normal+"; minimum="+small);
                foreach (Size size in new Size[] {normal, small})
                {
                    form.Size = size;
                    tabs.SelectedIndex = 0;
                    foreach (int m in new int[] {0,1,2,3,4,5})
                    {
                        mode.SelectedIndex = m;
                        foreach (int strategy in (m == 1 || m == 3 || m == 5) ? new int[]{0,1,2} : new int[]{0})
                        {
                            mismatch.SelectedIndex = strategy;
                            string state = "input_" + form.Width + "x" + form.Height + "_mode" + m + "_strategy" + strategy;
                            LayoutTree(form); Application.DoEvents();
                            Check(OwnVisible(mismatch)==(m!=4),"AS mismatch selector is locally visible outside PA mode only.");
                            Check(OwnVisible(Field<Label>(form,"paStrategyText"))==(m==4),"Activation strategy label is locally visible in PA mode only.");
                            foreach(string field in new string[]{"lampPaTail","lampPaTailLabel","lampPaTailHint"})
                                Check(OwnVisible(Field<Control>(form,field))==(m==4),field+" has the correct local visibility for mode "+m+".");
                            Check(OwnVisible(Field<GroupBox>(form,"lampSettingsGroup"))==(m>=2),"LAMP parameter group visibility follows the chosen mode.");
                            Check(OwnVisible(Field<GroupBox>(form,"rpaSettingsGroup"))==(m<2),"RPA parameter group visibility follows the chosen mode.");
                            CheckTree(form, state);
                            if(m==4)
                            {
                                Check(!mismatch.Enabled,"PA-LAMP does not expose AS-LAMP mismatch choices.");
                                Check(!Field<ComboBox>(form,"lampOrientation").Enabled,"PA-LAMP fixes the activation design to BIP.");
                                Check(Field<NumericUpDown>(form,"lampPaTail").Enabled,"PA-LAMP exposes its activation-tail setting.");
                            }
                            if(!baseline&&m==1&&strategy==2)Render(form,Path.Combine(output,state+".png"));
                            if(!baseline&&m==1&&strategy==2&&size==small)CheckInputScroll(form,tabs.SelectedTab,output);
                        }
                    }
                }
                Check(mode.Items.Count == 6 && mode.Items[1].ToString() == "RPA SNP 选择性扩增" && mode.Items[3].ToString().Contains("AS-LAMP") && mode.Items[4].ToString().Contains("PA-LAMP") && mode.Items[5].ToString().Contains("mLAMP"), "AS-LAMP, PA-LAMP and mLAMP each have a distinct mode.");
                notes.Add("Input cases exercised: all six modes including distinct AS/PA/mLAMP; applicable second/third-position mismatch strategies; PA-only controls and local visibility; default/minimum window sizes. TextRenderer measurements cover visible title, labels, action buttons and every combo option.");
                if (baseline)
                {
                    Render(form,Path.Combine(output,"baseline-input.png"));
                }
                else
                {
                    form.Size = normal;
                    var input = Field<TextBox>(form,"sequenceBox");
                    var grid = Field<DataGridView>(form,"grid");
                    var view = Field<Control>(form,"detailsView");
                    var detailTabs = Named<TabControl>(view,"detailTabs");
                    mode.SelectedIndex = 1; mismatch.SelectedIndex = 0;
                    input.Text = SnpReportWriter.ExampleFasta(); Run(form);
                    var snp = Field<SnpDesignResult>(form,"snpResult");
                    Check(snp != null && snp.Sets.Count > 1 && grid.Rows.Count == snp.Sets.Count,"SNP design populates candidate groups.");
                    if(snp != null && snp.Sets.Count > 1)
                    {
                        CheckSnp(view,snp.Sets[0],snp.Input);
                        grid.CurrentCell=grid.Rows[1].Cells[0]; Application.DoEvents();
                        CheckSnp(view,snp.Sets[1],snp.Input);
                        foreach(Size size in new Size[]{normal,small})
                        {
                            form.Size=size;
                            for(int p=0;p<3;p++)
                            {
                                detailTabs.SelectedIndex=p;
                                LayoutTree(form);Application.DoEvents();
                                CheckTree(form,"snp_results_"+form.Width+"x"+form.Height+"_page"+p);
                            }
                        }
                    }
                    foreach(int lampMode in new int[]{2,3,4})
                    {
                        mode.SelectedIndex=lampMode;mismatch.SelectedIndex=0;
                        CheckClearedLampMap(view,"Changing to LAMP mode "+lampMode);
                        input.Text=lampMode>=3?LampReportWriter.ExampleSnpFasta():LampReportWriter.ExampleFasta();
                        if(lampMode==4){form.Size=normal;tabs.SelectedIndex=0;LayoutTree(form);Application.DoEvents();Render(form,Path.Combine(output,"pa-lamp-input.png"));}
                        foreach(int direction in lampMode==3?new int[]{1,2}:new int[]{0})
                        {
                            Field<ComboBox>(form,"lampOrientation").SelectedIndex=direction;
                            Run(form);var lamp=Field<LampDesignResult>(form,"lampResult");
                            Check(lamp!=null&&lamp.Sets.Count>0,"LAMP mode "+lampMode+" direction "+direction+" produces candidates through the worker.");
                            if(lamp!=null&&lamp.Sets.Count>0)
                            {
                                Check(Field<DesignResult>(form,"result")==null&&Field<SnpDesignResult>(form,"snpResult")==null,"LAMP has no stale RPA result.");
                                CheckLamp(view,lamp.Sets[0],lamp);
                                if(lampMode==4)
                                {
                                    Check(LampReportWriter.IsPa(lamp),"Mode 4 worker returns a PA-LAMP result.");
                                    Check(lamp.Settings.ExtraMismatchFromThreePrime==0,"PA result cannot retain an AS-LAMP extra mismatch.");
                                    Check(lamp.Sets[0].BIP.OrderingSequence.Contains("[C3]")&&lamp.Sets[0].BIP.OrderingSequence.Contains("[r"),"PA UI is backed by an RNA/C3 modified oligo.");
                                }
                                if(lamp.Sets.Count>1){grid.CurrentCell=grid.Rows[1].Cells[0];Application.DoEvents();CheckLamp(view,lamp.Sets[1],lamp);}
                                foreach(Size size in new Size[]{normal,small})
                                {
                                    form.Size=size;
                                    for(int p=0;p<3;p++)
                                    {
                                        detailTabs.SelectedIndex=p;LayoutTree(form);Application.DoEvents();
                                        string state="lamp_"+lampMode+"_direction"+direction+"_size"+size+"_page"+p;
                                        CheckTree(form,state);CheckLampViewport(view,state);
                                        if(p==0)Render(form,Path.Combine(output,"lamp-map-mode"+lampMode+"-direction"+direction+"-"+(size==small?"minimum":"default")+".png"));
                                    }
                                }
                                if(lampMode==4){form.Size=normal;detailTabs.SelectedIndex=0;LayoutTree(form);Application.DoEvents();Render(form,Path.Combine(output,"pa-lamp-results.png"));}
                                if(direction==2)
                                {
                                    mismatch.SelectedIndex=2;Run(form);lamp=Field<LampDesignResult>(form,"lampResult");
                                    Check(lamp!=null&&lamp.Sets.Count>0,"BIP SNP with third-from-end mismatch completes in the UI.");
                                    if(lamp!=null&&lamp.Sets.Count>0)
                                    {
                                        CheckLamp(view,lamp.Sets[0],lamp);form.Size=normal;
                                        for(int p=0;p<3;p++){detailTabs.SelectedIndex=p;LayoutTree(form);Application.DoEvents();CheckTree(form,"lamp_BIP_mismatch3_page"+p);}
                                        detailTabs.SelectedIndex=0;LayoutTree(form);Render(form,Path.Combine(output,"lamp-snp-results.png"));
                                    }
                                }
                            }
                        }
                    }
                    mode.SelectedIndex=0; input.Text=ReportWriter.ExampleFasta(); Run(form);
                    var ordinary=Field<DesignResult>(form,"result");
                    Check(ordinary!=null&&ordinary.Pairs.Count>0,"Ordinary design succeeds after SNP.");
                    if(ordinary!=null&&ordinary.Pairs.Count>0)
                    {
                        CheckBox(view,"primer_F",ordinary.Pairs[0].Forward.Sequence,-1);
                        CheckBox(view,"primer_R",ordinary.Pairs[0].Reverse.Sequence,-1);
                        CheckBox(view,"product_main",ordinary.Pairs[0].AmpliconSequence,-1);
                        for(int p=0;p<3;p++){detailTabs.SelectedIndex=p;LayoutTree(form);Application.DoEvents();CheckTree(form,"ordinary_results_page"+p);}
                    }
                    Check(Field<LampDesignResult>(form,"lampResult")==null,"Switching to RPA discards prior LAMP result.");
                    CheckClearedLampMap(view,"Returning to ordinary RPA");
                    Check(!OwnVisible(Field<Control>(view,"lampDiagram"))&&OwnVisible(Field<Control>(view,"diagram")),"RPA displays its original map and hides the LAMP map.");
                    tabs.SelectedIndex=0;mode.SelectedIndex=3;mismatch.SelectedIndex=2;
                    CheckClearedLampMap(view,"Changing inputs after the RPA result");
                    form.Size=normal;LayoutTree(form);Application.DoEvents();
                    Render(form,Path.Combine(output,"lamp-input.png"));
                    notes.Add("Result cases exercised: RPA SNP and AS/PA-LAMP candidates with exact per-character SNP coloring; RNA/C3 order text and RTF copy-payload round-trip; LAMP map candidate binding and positive map/detail viewports on three tabs at default/minimum size; ordinary design after PA with no residual SNP color or LAMP map state.");
                }
                Check(!form.Visible,"No desktop window was displayed during testing.");
            }
        }
        catch(Exception ex){errors.Add(ex.ToString());}
        notes.Add("Assertions checked="+checks+"; failures="+errors.Count);
        foreach(string e in errors) notes.Add("FAIL " + e);
        notes.Add(baseline ? "BASELINE: " + errors.Count + " findings" : errors.Count==0 ? "RESULT: PASS" : "RESULT: FAIL ("+errors.Count+")");
        notes.Add("Limit: hidden DrawToBitmap can omit native RichTextBox/DataGridView rendering. Text sizes, container bounds and exact per-character SNP colors are inspected in native controls; no visible-window gesture test performed.");
        File.WriteAllLines(Path.Combine(output,"lamp-ui-test-report.txt"),notes.ToArray(),new UTF8Encoding(true));
        File.WriteAllLines(Path.Combine(output,"lamp-ui-layout-bounds.txt"),layout.ToArray(),new UTF8Encoding(true));
        return baseline||errors.Count==0?0:1;
    }

    private static void CheckTree(Control parent,string path)
    {
        if(parent is DataGridView||parent is NumericUpDown||parent is RichTextBox||parent is TextBox||parent is ComboBox)return;
        var tab=parent as TabControl;
        var scroll=parent as ScrollableControl;
        Rectangle allowed=parent.ClientRectangle;
        if(scroll!=null&&scroll.AutoScroll)
        {
            // WinForms DisplayRectangle subtracts ScrollableControl padding;
            // children include it. Compare against the full scroll extent.
            Rectangle extent=scroll.DisplayRectangle;
            extent.Width+=scroll.Padding.Horizontal;
            extent.Height+=scroll.Padding.Vertical;
            allowed=Rectangle.Union(allowed,extent);
        }
        foreach(Control child in parent.Controls)
        {
            if(tab!=null&&child!=tab.SelectedTab)continue;
            // Control.Visible includes hidden ancestors and is false throughout
            // this in-process test. STATE_VISIBLE records only the control's own
            // requested visibility. Verify it separately before skipping layout.
            if(!OwnVisible(child)){layout.Add(path+"/"+child.Name+" skipped: own Visible state=false");continue;}
            var owner=parent.FindForm() as MainForm;
            int active=owner==null?0:Field<ComboBox>(owner,"designMode").SelectedIndex;
            if(child.Name=="lampSettings"&&active<2||child.Name=="rpaSettings"&&active>=2)continue;
            if(child.GetType().Name=="AmpliconView"&&active>=2)continue;
            string name=String.IsNullOrEmpty(child.Name)?child.GetType().Name:child.Name;
            string here=path+"/"+name;
            layout.Add(here+" text="+child.Text.Replace("\r","").Replace("\n"," / ")+" bounds="+child.Bounds+" parentClient="+parent.ClientRectangle+" allowed="+allowed+" fontHeight="+child.Font.Height+" preferred="+child.GetPreferredSize(Size.Empty));
            if(!(child is TabPage)&&!(child is SplitterPanel))
            {
                Rectangle b=child.Bounds;
                Check(b.Width>0&&b.Height>0,here+" has positive bounds.");
                Check(b.Left>=allowed.Left-3&&b.Top>=allowed.Top-3&&b.Right<=allowed.Right+3&&b.Bottom<=allowed.Bottom+3,here+" fits parent bounds "+b+" allowed="+allowed);
            }
            CheckText(child,here);
            CheckTree(child,here);
        }
    }
    private static void CheckInputScroll(MainForm form,TabPage page,string output)
    {
        var action=Field<Button>(form,"design");
        var cancel=Field<Button>(form,"cancel");
        var scroll=Field<Panel>(form,"inputScroll");
        Control root=Field<Control>(form,"inputPanel");
        var shell=Named<TableLayoutPanel>(page,"inputPageShell");
        Check(!page.AutoScroll,"Input page keeps its action bar outside the scrolling region.");
        Check(scroll.AutoScroll&&root.Parent==scroll,"Input content scrolls inside its dedicated panel.");
        Check(scroll.AutoScrollMinSize.Height>=root.MinimumSize.Height,"Input scroll panel explicitly declares its complete content height.");
        Check(shell.RowCount==2&&scroll.Parent==shell&&shell.GetRow(scroll)==0,"Input shell reserves a separate fixed row for its actions.");
        Check(action.Parent==cancel.Parent&&action.Parent.Parent==shell&&shell.GetRow(action.Parent)==1,
            "Start and cancel share the fixed action row below the content.");
        Check(!scroll.Contains(action)&&!scroll.Contains(cancel),"Neither action button belongs to the scrolling content subtree.");
        scroll.AutoScrollPosition=Point.Empty;
        LayoutTree(form);Application.DoEvents();
        Rectangle startBefore=new Rectangle(page.PointToClient(action.PointToScreen(Point.Empty)),action.Size);
        Rectangle cancelBefore=new Rectangle(page.PointToClient(cancel.PointToScreen(Point.Empty)),cancel.Size);
        Check(page.ClientRectangle.Contains(startBefore),"Start-design button is fully visible before scrolling at minimum window size.");
        Check(page.ClientRectangle.Contains(cancelBefore),"Cancel button is fully visible before scrolling at minimum window size.");
        int scrollBottom=page.PointToClient(scroll.PointToScreen(new Point(0,scroll.Height))).Y;
        Check(startBefore.Top>=scrollBottom&&cancelBefore.Top>=scrollBottom,"Fixed actions lie below the content viewport instead of covering its controls.");
        scroll.AutoScrollPosition=new Point(0,Math.Max(scroll.AutoScrollMinSize.Height,root.Height));
        LayoutTree(form);Application.DoEvents();
        Rectangle startAfter=new Rectangle(page.PointToClient(action.PointToScreen(Point.Empty)),action.Size);
        Rectangle cancelAfter=new Rectangle(page.PointToClient(cancel.PointToScreen(Point.Empty)),cancel.Size);
        Check(startAfter==startBefore&&cancelAfter==cancelBefore,"Scrolling input parameters does not move either action button.");
        Check(page.ClientRectangle.Contains(startAfter)&&page.ClientRectangle.Contains(cancelAfter),"Both fixed actions remain fully within the page viewport after scrolling.");
        notes.Add("Minimum input scroll position="+scroll.AutoScrollPosition+"; fixed start="+startAfter+"; fixed cancel="+cancelAfter+"; pageViewport="+page.ClientRectangle);
        if(scroll.AutoScrollPosition.Y<0)
        {
            Check(root.Bottom<=scroll.ClientSize.Height-scroll.Padding.Bottom+2,"Scrolling reaches the full bottom of the input content.");
            Render(form,Path.Combine(output,"input-minimum-scrolled-bottom.png"));
        }
        else if(root.Height+scroll.Padding.Vertical>scroll.ClientSize.Height)
            notes.Add("LIMIT: hidden ancestor prevents native scroll offset. Dedicated scroll extent and fixed-action hierarchy/viewport coordinates verified before and after the scroll request.");
        scroll.AutoScrollPosition=Point.Empty;LayoutTree(form);Application.DoEvents();
    }
    private static void CheckText(Control c,string name)
    {
        if(String.IsNullOrEmpty(c.Text))return;
        var label=c as Label;
        var button=c as Button;
        var combo=c as ComboBox;
        if(label!=null||button!=null)
        {
            int width=Math.Max(1,c.ClientSize.Width-c.Padding.Horizontal);
            int height=c.ClientSize.Height-c.Padding.Vertical;
            using(Graphics g=c.CreateGraphics())
            {
                TextFormatFlags flags=TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl;
                Size text=TextRenderer.MeasureText(g,c.Text,c.Font,new Size(width,Int32.MaxValue),flags);
                layout.Add(name+" TEXT measure="+text+" available="+width+"x"+height);
                Check(height>=text.Height-2,name+" text height fits: "+height+" >= "+text.Height+" text="+c.Text.Replace("\r\n"," / "));
                Check(width>=text.Width-2,name+" text width fits: "+width+" >= "+text.Width);
            }
        }
        if(combo!=null)
        {
            using(Graphics g=c.CreateGraphics())
            foreach(object item in combo.Items)
            {
                Size measured=TextRenderer.MeasureText(g,item.ToString(),combo.Font,new Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.SingleLine);
                Check(combo.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-6>=measured.Width-2,name+" full option fits width: "+item);
                Check(combo.ClientSize.Height>=measured.Height,name+" full option fits height: "+item);
            }
        }
    }
    private static void CheckSnp(Control view,SnpPrimerSet set,SnpInput input)
    {
        CheckBox(view,"primer_F_ref",set.ReferenceForward.Sequence,input.Position-set.ReferenceForward.Start);
        CheckBox(view,"primer_F_alt",set.AlternateForward.Sequence,input.Position-set.AlternateForward.Start);
        CheckBox(view,"primer_R_common",set.CommonReverse.Sequence,-1);
        CheckBox(view,"primer_F_control",set.ControlForward.Sequence,-1);
        CheckBox(view,"product_ref",set.ReferencePair.AmpliconSequence,input.Position-set.ReferencePair.AmpliconStart);
        CheckBox(view,"product_alt",set.AlternatePair.AmpliconSequence,input.Position-set.AlternatePair.AmpliconStart);
        CheckBox(view,"product_control_ref",set.ControlPair.AmpliconSequence,input.Position-set.ControlPair.AmpliconStart);
        CheckBox(view,"product_control_alt",set.AlternateControlAmpliconSequence,input.Position-set.ControlPair.AmpliconStart);
    }
    private static void CheckLamp(Control view,LampPrimerSet set,LampDesignResult r)
    {
        var expected=new List<string>();
        foreach(var p in LampReportWriter.Oligos(set))
        {
            string name="primer_"+LampReportWriter.OligoName(p,set,r);expected.Add(name);
            CheckBox(view,name,p.OrderingSequence,p.OrderingSnpIndex);
        }
        CheckBox(view,"template_ref",set.ReferenceTemplate,r.Snp==null?-1:r.Snp.Position-set.SpanStart);
        if(r.Snp!=null)CheckBox(view,"template_alt",set.AlternateTemplate,r.Snp.Position-set.SpanStart);
        var detailTabs=Named<TabControl>(view,"detailTabs");
        Check(detailTabs.TabPages[1].Text.Contains("靶区模板"),"LAMP template tab does not label a fixed amplification product.");
        var map=Field<Control>(view,"lampDiagram");
        Check(map.Name=="lampTemplatePositionMap"&&OwnVisible(map),"LAMP shows its dedicated template-position map.");
        Check(!OwnVisible(Field<Control>(view,"diagram")),"The RPA AmpliconView stays hidden for LAMP results.");
        Check(Object.ReferenceEquals(Field<LampPrimerSet>(map,"selectedSet"),set)&&Object.ReferenceEquals(Field<LampDesignResult>(map,"selectedResult"),r),"LAMP map binds the same selected candidate and result as the sequence cards.");
        Check(MapTemplateLength(map)==r.Input.Sequence.Length,"LAMP map receives the full input-template length.");
        if(LampReportWriter.IsPa(r))CheckPaCopy(set,r);
        notes.Add("Checked LAMP candidate #"+set.Rank+" direction="+set.SpecificInner+" oligos="+expected.Count+" exact SNP colors.");
    }
    private static void CheckLampViewport(Control view,string state)
    {
        Control map=Field<Control>(view,"lampDiagram");
        Panel host=Field<Panel>(view,"diagramHost");
        TabControl details=Field<TabControl>(view,"detailTabs");
        float scale=dpi/96F;
        int logicalHeight=(int)map.GetType().GetProperty("LogicalHeight",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(map,null);
        Check(Object.ReferenceEquals(map.Parent,host)&&OwnVisible(host)&&OwnVisible(map),state+" keeps the LAMP map in its visible scroll host.");
        Check(host.AutoScroll&&map.Height>=Math.Round(logicalHeight*scale)-2,state+" preserves full LAMP map height instead of compressing tracks.");
        Check(host.ClientSize.Height>=80*scale-2,state+" retains a usable map viewport.");
        Check(details.Height>=180*scale-2,state+" reserves at least 180 logical pixels for primer/detail tabs.");
        Check(host.Bottom<=details.Top+2,state+" map host does not overlap detail tabs.");
        Check(map.Width>0&&map.Right<=host.ClientRectangle.Right+2,state+" map width fits its scroll host.");
        if(map.Height>host.ClientSize.Height)
            Check(Math.Max(host.DisplayRectangle.Height,host.AutoScrollMinSize.Height)>=map.Height,state+" complete tall map has scrollable vertical extent.");
        if(state.EndsWith("_page0",StringComparison.Ordinal))CheckStandaloneLampScroll(view,map,state);
    }
    private static void CheckStandaloneLampScroll(Control integratedView,Control integratedMap,string state)
    {
        LampPrimerSet set=Field<LampPrimerSet>(integratedMap,"selectedSet");
        LampDesignResult result=Field<LampDesignResult>(integratedMap,"selectedResult");
        string key=(result.Snp==null?"LAMP":result.Settings.SnpMethod)+"_"+integratedView.ClientSize;
        if(!scrollCases.Add(key))return;
        // A hidden Form prevents AutoScrollPosition from moving native children.
        // The same parentless UserControl has managed Visible=true in the parking
        // window, so this tests real native scrolling without displaying a Form.
        using(var probe=new ResultDetailsView {Size=integratedView.ClientSize,Font=integratedView.Font})
        {
            Handles(probe);probe.ShowLamp(set,result);Handles(probe);LayoutTree(probe);Application.DoEvents();
            Panel host=Field<Panel>(probe,"diagramHost");Control map=Field<Control>(probe,"lampDiagram");
            Check(probe.Parent==null&&probe.FindForm()==null&&host.Visible,state+" standalone scroll probe is a visible control subtree with no desktop Form.");
            Point originalScroll=host.AutoScrollPosition;
            try
            {
                host.AutoScrollPosition=new Point(0,Math.Max(map.Height,host.AutoScrollMinSize.Height));
                LayoutTree(probe);Application.DoEvents();
                Check(map.Bottom<=host.ClientRectangle.Bottom+2&&(map.Height<=host.ClientSize.Height||host.AutoScrollPosition.Y<0),state+" actual scrolling reaches the complete map's bottom.");
                Check(host.AutoScrollPosition.X==0&&!host.HorizontalScroll.Visible&&map.Left>=-2&&map.Right<=host.ClientRectangle.Right+2,state+" scrolling the map introduces no horizontal overflow.");
                notes.Add("Native scroll "+key+": offset="+host.AutoScrollPosition+"; map bottom="+map.Bottom+"; viewport bottom="+host.ClientRectangle.Bottom+".");
            }
            finally
            {
                // The getter is negative; the setter expects a positive distance.
                host.AutoScrollPosition=new Point(-originalScroll.X,-originalScroll.Y);
                LayoutTree(probe);Application.DoEvents();
            }
        }
    }
    private static void CheckClearedLampMap(Control view,string state)
    {
        Control map=Field<Control>(view,"lampDiagram");
        Check(MapTemplateLength(map)==0&&Field<LampPrimerSet>(map,"selectedSet")==null&&Field<LampDesignResult>(map,"selectedResult")==null,state+" clears prior LAMP candidate and template state.");
    }
    private static int MapTemplateLength(Control map){return (int)map.GetType().GetProperty("TemplateLength",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(map,null);}
    private static void CheckPaCopy(LampPrimerSet set,LampDesignResult r)
    {
        HighlightedReport report=LampReportWriter.HighlightedOrderingText(set,r);
        using(var source=new RichTextBox())using(var pasted=new RichTextBox())
        {
            typeof(MainForm).GetMethod("ApplyHighlights",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source,report,""});
            var data=new DataObject();data.SetData(DataFormats.UnicodeText,source.Text);data.SetData(DataFormats.Rtf,source.Rtf);
            Check((string)data.GetData(DataFormats.UnicodeText)==report.Text,"PA copy payload retains complete modified ordering text.");
            pasted.Rtf=(string)data.GetData(DataFormats.Rtf);
            Check(pasted.Text==report.Text,"PA rich-text copy round-trip retains RNA and C3 annotations.");
            var expected=new HashSet<int>();foreach(var mark in report.SnpHighlights)expected.Add(mark.Start);
            for(int i=0;i<pasted.TextLength;i++)
            {
                pasted.Select(i,1);
                if((pasted.SelectionColor.ToArgb()==red.ToArgb())!=expected.Contains(i)){errors.Add("PA rich-text copy has incorrect SNP color at "+i);break;}
            }
            Check(expected.Count==2,"PA copied candidate group colors only the two allele-specific RNA bases.");
        }
    }
    private static void CheckBox(Control view,string name,string sequence,int snpIndex)
    {
        var box=Named<RichTextBox>(view,name);
        Check(box.ReadOnly&&box.Text==sequence,name+" contains the complete expected sequence.");
        for(int i=0;i<box.TextLength;i++){box.Select(i,1);if((box.SelectionColor.ToArgb()==red.ToArgb())!=(i==snpIndex)){errors.Add(name+" wrong SNP color at "+i);break;}}
        box.Select(0,0);
    }
    private static void Run(MainForm form)
    {
        dispatchError=null;WindowsFormsSynchronizationContext.AutoInstall=false;
        SynchronizationContext.SetSynchronizationContext(new HandleContext(form));
        typeof(MainForm).GetMethod("StartDesign",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{null,EventArgs.Empty});
        var timer=Stopwatch.StartNew();
        while(Field<bool>(form,"busy")){Application.DoEvents();if(dispatchError!=null)throw dispatchError;if(timer.ElapsedMilliseconds>30000)throw new TimeoutException("Design timed out.");Thread.Sleep(5);}
        Application.DoEvents();if(dispatchError!=null)throw dispatchError;
    }
    private sealed class HandleContext:SynchronizationContext
    {
        private readonly Control control;
        public HandleContext(Control value){control=value;}
        public override void Post(SendOrPostCallback callback,object state){try{control.BeginInvoke(new Action(delegate{try{callback(state);}catch(Exception ex){dispatchError=ex;}}));}catch(Exception ex){dispatchError=ex;}}
    }
    private static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
    private static bool OwnVisible(Control control){if(visibleState==null)throw new InvalidOperationException("Control.GetState unavailable; local visibility cannot be verified.");return (bool)visibleState.Invoke(control,new object[]{2});}
    private static T Named<T>(Control root,string name)where T:Control {Control[] matches=root.Controls.Find(name,true);if(matches.Length!=1)throw new Exception("Missing/duplicate control "+name);return (T)matches[0];}
    private static void Check(bool ok,string text){checks++;if(!ok)errors.Add(text);}
    private static void Handles(Control c){IntPtr h=c.Handle;foreach(Control child in c.Controls)Handles(child);}
    private static void LayoutTree(Control c){c.PerformLayout();foreach(Control child in c.Controls)LayoutTree(child);c.PerformLayout();}
    private static void Render(MainForm form,string file){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(file,ImageFormat.Png);}}
}
