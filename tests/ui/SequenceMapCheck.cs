using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RpaDesigner;

// Hidden-control checks: no Show(), desktop automation, dialogs or clipboard.
// Fabricated coordinate fixtures are for diagram layout only, not primer design.
internal static class SequenceMapCheck
{
    private static readonly List<string> notes = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static readonly Color forwardColor = Color.FromArgb(0,119,117);
    private static readonly Color reverseColor = Color.FromArgb(87,96,178);
    private static int checks;
    private static Exception dispatchError;
    private static string output;
    private static float scale;

    [STAThread]
    private static int Main(string[] args)
    {
        output=args.Length==0?AppDomain.CurrentDomain.BaseDirectory:Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){dispatchError=e.Exception;};
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls=true;
            Type diagramType=typeof(MainForm).Assembly.GetType("RpaDesigner.AmpliconView",true);
            using(var diagram=(Control)Activator.CreateInstance(diagramType,true))
            {
                diagram.Font=new Font("Microsoft YaHei UI",10F);
                IntPtr handle=diagram.Handle;
                using(Graphics graphics=diagram.CreateGraphics())scale=graphics.DpiX/96F;
                notes.Add("Assembly="+typeof(MainForm).Assembly.Location);
                notes.Add("DPI="+(scale*96F));
                SetSize(diagram,980,254);
                PrimerPair pair=Pair(45,75,161,192);
                Invoke(diagram,"SetPair",pair,new DesignSettings(),301);
                Check(TemplateLength(diagram)==301,"Full template has 301 nt even though product has 148 bp.");
                CheckNear(X(diagram,1,10,610),10,"First base maps to start of full sequence.");
                CheckNear(X(diagram,151,10,610),310,"Middle base maps to full-template midpoint.");
                CheckNear(X(diagram,301,10,610),610,"Last base maps to end of full sequence.");
                CheckNear(X(diagram,45,10,610),98,"F starts 44 bases into the full template, not at the left edge.");
                CheckNear(X(diagram,192,10,610),392,"R ends 191 bases into the full template, not at the right edge.");
                using(Bitmap image=Render(diagram,"ordinary-301nt.png")){CheckPrimerPixels(image,"Ordinary 301 nt");CheckPaintedPositions(image,301,pair,80,112,"Ordinary 301 nt");}

                float before=X(diagram,151,20,1220);
                Invoke(diagram,"SetPair",Pair(101,132,221,251),new DesignSettings(),301);
                Check(TemplateLength(diagram)==301,"Candidate selection does not replace template length with product length.");
                CheckNear(X(diagram,151,20,1220),before,"The same template coordinate stays fixed when the candidate changes.");
                using(Bitmap image=Render(diagram,"ordinary-other-candidate.png"))CheckPrimerPixels(image,"Second candidate");

                var set=Snp();
                Invoke(diagram,"SetSnp",set,151,301);
                Check(TemplateLength(diagram)==301,"SNP diagram uses reference-template base count.");
                CheckNear(X(diagram,151,10,610),310,"SNP 151 maps to full-template midpoint rather than control-product fraction.");
                CheckNear(X(diagram,set.ReferenceForward.End,10,610),X(diagram,151,10,610),"Reference allele primer 3-prime end aligns with SNP.");
                CheckNear(X(diagram,set.AlternateForward.End,10,610),X(diagram,151,10,610),"Alternate allele primer 3-prime end aligns with SNP.");
                using(Bitmap image=Render(diagram,"snp-301nt.png")){CheckPrimerPixels(image,"SNP 301 nt");CheckPaintedPositions(image,301,set.ReferencePair,112,176,"SNP 301 nt");CheckSnpPixels(image);}

                Invoke(diagram,"SetPair",Pair(9870,9900,9990,10021),new DesignSettings(),20000);
                CheckNear(X(diagram,1,0,19999),0,"20,000 nt template starts at base 1.");
                CheckNear(X(diagram,10000,0,19999),9999,"Long-template positions retain true proportion.");
                CheckNear(X(diagram,20000,0,19999),19999,"20,000 nt template reaches its full end.");
                using(Bitmap image=Render(diagram,"ordinary-20000nt.png"))CheckPrimerPixels(image,"Long template");

                Invoke(diagram,"SetPair",Pair(1,31,271,301),new DesignSettings(),301);
                using(Bitmap image=Render(diagram,"ordinary-both-ends.png"))CheckPrimerPixels(image,"Both boundary primers");
                SetSize(diagram,420,254);
                Invoke(diagram,"SetPair",pair,new DesignSettings(),301);
                using(Bitmap image=Render(diagram,"ordinary-narrow.png")){CheckPrimerPixels(image,"Narrow ordinary view");CheckPaintedPositions(image,301,pair,80,112,"Narrow ordinary view");}
                Invoke(diagram,"SetSnp",set,151,301);
                using(Bitmap image=Render(diagram,"snp-narrow.png")){CheckPrimerPixels(image,"Narrow SNP view");CheckPaintedPositions(image,301,set.ReferencePair,112,176,"Narrow SNP view");CheckSnpPixels(image);}

                Invoke(diagram,"SetPair",null,null,0);
                Check(TemplateLength(diagram)==0,"Clearing removes stored template length.");
                using(Bitmap image=Render(diagram,"cleared.png"))
                {
                    Check(CountColor(image,forwardColor)==0&&CountColor(image,reverseColor)==0,"Clearing removes old forward and reverse graphics.");
                }
                Invoke(diagram,"SetPair",Pair(1,1,1,1),new DesignSettings(),1);
                CheckNear(X(diagram,1,10,610),310,"Single-base coordinate maps safely to the axis midpoint.");
                Invoke(diagram,"SetSnp",null,151,301);
                Check(TemplateLength(diagram)==0,"The null SNP entry point also clears template state.");
                CheckNear(X(diagram,1,10,610),10,"Empty template mapping remains finite.");
                Check(!diagram.Visible || diagram.FindForm()==null,"Diagram has not been displayed in a desktop window.");
            }
            CheckResultIntegration();
            CheckMinimumMainWindow();
            Check(dispatchError==null,"Hidden UI dispatched without exceptions.");
        }
        catch(Exception ex){errors.Add(ex.ToString());}
        notes.Add("Assertions="+checks+"; failures="+errors.Count);
        foreach(string error in errors)notes.Add("FAIL "+error);
        notes.Add(errors.Count==0?"RESULT: PASS":"RESULT: FAIL");
        notes.Add("Scope: coordinate fixtures and hidden DrawToBitmap verify diagram mapping, mode switches, template-length propagation and reset. They do not validate biological amplification or design scores. Hidden native RichTextBox controls may omit text from DrawToBitmap; their real TextLength and layout are checked separately.");
        File.WriteAllLines(Path.Combine(output,"sequence-map-test-report.txt"),notes.ToArray(),new UTF8Encoding(true));
        Console.WriteLine("Assertions="+checks+"; failures="+errors.Count);
        foreach(string error in errors)Console.WriteLine("FAIL "+error);
        return errors.Count==0?0:1;
    }

    private static void CheckResultIntegration()
    {
        using(var host=new Form {ClientSize=new Size((int)(1000*scale),(int)(760*scale)),ShowInTaskbar=false,Font=new Font("Microsoft YaHei UI",10F)})
        using(var details=new ResultDetailsView {Dock=DockStyle.Fill})
        {
            host.Controls.Add(details);
            Handles(host);Layout(host);Application.DoEvents();
            PrimerPair pair=Pair(45,75,161,192);
            var result=new DesignResult {Input=new ParsedSequence {Name="301 nt diagram fixture",Sequence=new string('A',301)},Settings=new DesignSettings()};
            result.Pairs.Add(pair);
            details.ShowPair(pair,result);Handles(host);Layout(host);Application.DoEvents();
            Control diagram=(Control)typeof(ResultDetailsView).GetField("diagram",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(details);
            Control lampDiagram=Field<Control>(details,"lampDiagram");
            Panel diagramHost=Field<Panel>(details,"diagramHost");
            Check(Object.ReferenceEquals(diagram.Parent,diagramHost),"RPA diagram is hosted in the shared position-map container.");
            Check(TemplateLength(diagram)==301,"ResultDetailsView passes ordinary input length to diagram.");
            Check(TemplateLength(lampDiagram)==0,"Ordinary result has no stale LAMP template state.");
            using(Bitmap image=Render(host,"results-ordinary.png")){}
            SnpInput snpInput=SnpParser.Parse(new string('A',150)+"[A>C]"+new string('A',150));
            var snpResult=new SnpDesignResult {Input=snpInput,Settings=new SnpDesignSettings()};
            SnpPrimerSet set=Snp();snpResult.Sets.Add(set);
            details.ShowSnp(set,snpResult);Handles(host);Layout(host);Application.DoEvents();
            Check(TemplateLength(diagram)==301,"SNP annotated text counts as 301 bases rather than 305 characters.");
            Check(TemplateLength(lampDiagram)==0,"RPA SNP result keeps the LAMP map cleared.");
            using(Bitmap image=Render(host,"results-snp.png")){}
            details.ShowNoResults(new LampDesignResult {Input=new ParsedSequence {Name="Empty LAMP layout fixture",Sequence=new string('A',301)},Settings=new LampDesignSettings()});
            Layout(host);Application.DoEvents();
            Check(TemplateLength(lampDiagram)==0,"A LAMP no-result view has no stale candidate/template map.");
            Check(Field<TableLayoutPanel>(details,"root").RowStyles[2].Height==0,"A LAMP no-result view collapses the unused map row only.");
            details.ShowEmpty("Input changed","Prior candidate graphics must clear.");Layout(host);Application.DoEvents();
            Check(TemplateLength(diagram)==0,"ResultDetailsView clears template state when input becomes invalidated.");
            Check(TemplateLength(lampDiagram)==0,"Invalidating input also clears the LAMP position map.");
            Check(!host.Visible,"Integration host was never shown.");
        }
    }

    private static Primer Primer(int start,int end)
    {
        return new Primer {Start=start,End=end,Sequence=new string('A',end-start+1),Gc=0,Tm=55};
    }
    private static PrimerPair Pair(int forwardStart,int forwardEnd,int reverseStart,int reverseEnd)
    {
        return new PrimerPair {Rank=1,Forward=Primer(forwardStart,forwardEnd),Reverse=Primer(reverseStart,reverseEnd),AmpliconStart=forwardStart,AmpliconEnd=reverseEnd,AmpliconLength=reverseEnd-forwardStart+1,AmpliconSequence=new string('A',reverseEnd-forwardStart+1),Score=88.8};
    }
    private static SnpPrimerSet Snp()
    {
        PrimerPair reference=Pair(121,151,221,251),alternate=Pair(121,151,221,251),control=Pair(81,111,221,251);
        alternate.Forward.Sequence=new string('A',30)+"C";
        alternate.AmpliconSequence=alternate.AmpliconSequence.Substring(0,30)+"C"+alternate.AmpliconSequence.Substring(31);
        return new SnpPrimerSet {Rank=1,ReferenceForward=reference.Forward,AlternateForward=alternate.Forward,CommonReverse=reference.Reverse,ControlForward=control.Forward,ReferencePair=reference,AlternatePair=alternate,ControlPair=control,AlternateControlAmpliconSequence=control.AmpliconSequence.Substring(0,70)+"C"+control.AmpliconSequence.Substring(71),Score=82.1};
    }
    private static void SetSize(Control control,int width,int height){control.Size=new Size((int)(width*scale),(int)(height*scale));}
    private static void Invoke(Control control,string method,params object[] args){control.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(control,args);Application.DoEvents();}
    private static int TemplateLength(Control control){return (int)control.GetType().GetProperty("TemplateLength",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(control,null);}
    private static float X(Control control,int position,float left,float right){return Convert.ToSingle(control.GetType().GetMethod("CoordinateToX",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(control,new object[]{position,left,right}));}
    private static void CheckNear(float actual,float expected,string description){Check(!Single.IsNaN(actual)&&!Single.IsInfinity(actual)&&Math.Abs(actual-expected)<0.01F,description+" (actual="+actual+", expected="+expected+")");}
    private static void Check(bool condition,string description){checks++;notes.Add((condition?"PASS ":"FAIL ")+description);if(!condition)errors.Add(description);}
    private static Bitmap Render(Control control,string name){control.Refresh();Application.DoEvents();var bitmap=new Bitmap(control.Width,control.Height);control.DrawToBitmap(bitmap,control.ClientRectangle);bitmap.Save(Path.Combine(output,name),ImageFormat.Png);return bitmap;}
    private static int CountColor(Bitmap bitmap,Color color){int total=0,target=color.ToArgb();for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==target)total++;return total;}
    private static void CheckPrimerPixels(Bitmap bitmap,string label){Check(CountColor(bitmap,forwardColor)>0,label+" draws forward primer in teal.");Check(CountColor(bitmap,reverseColor)>0,label+" draws reverse primer in purple.");}
    private static Point AxisBounds(Bitmap bitmap)
    {
        int y=(int)Math.Round(38*scale),left=-1,right=-1;
        Color gray=Color.FromArgb(194,206,214);
        for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==gray.ToArgb()){if(left<0)left=x;right=x;}
        Check(left>=0&&right>left,"Complete template backbone is present beyond the product highlight.");
        return new Point(left,right);
    }
    private static void CheckPaintedPositions(Bitmap bitmap,int length,PrimerPair pair,int forwardY,int reverseY,string label)
    {
        Point axis=AxisBounds(bitmap);
        CheckArrow(bitmap,axis,length,pair.Forward,forwardY,forwardColor,label+" F");
        CheckArrow(bitmap,axis,length,pair.Reverse,reverseY,reverseColor,label+" R");
    }
    private static void CheckArrow(Bitmap bitmap,Point axis,int length,Primer primer,int logicalY,Color color,string label)
    {
        int y=(int)Math.Round(logicalY*scale),start=-1,end=-1;
        for(int x=Math.Max(0,axis.X);x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==color.ToArgb()){if(start<0)start=x;end=x;}
        float expectedStart=axis.X+(primer.Start-1)*(axis.Y-axis.X)/(float)(length-1);
        float expectedEnd=axis.X+(primer.End-1)*(axis.Y-axis.X)/(float)(length-1);
        Check(start>=0&&Math.Abs(start-expectedStart)<=3*scale&&Math.Abs(end-expectedEnd)<=3*scale,label+" painted span follows complete-template fraction (paint="+start+".."+end+", expected="+expectedStart+".."+expectedEnd+").");
    }
    private static void CheckSnpPixels(Bitmap bitmap)
    {
        Point axis=AxisBounds(bitmap);
        int expected=(axis.X+axis.Y)/2,hits=0;
        Color red=Color.FromArgb(211,47,47);
        for(int y=(int)(64*scale);y<(int)(180*scale);y++)
            for(int x=expected-3;x<=expected+3;x++)if(bitmap.GetPixel(x,y).ToArgb()==red.ToArgb())hits++;
        Check(hits>20,"SNP red guide is painted at the middle of the complete 301 nt sequence.");
    }
    private static void CheckMinimumMainWindow()
    {
        using(var form=new MainForm())
        {
            Handles(form);Layout(form);Application.DoEvents();
            form.Size=form.MinimumSize;
            Field<ComboBox>(form,"designMode").SelectedIndex=1;
            var result=new SnpDesignResult {Input=SnpParser.Parse(new string('A',150)+"[A>C]"+new string('A',150)),Settings=new SnpDesignSettings()};
            var set=Snp();result.Sets.Add(set);
            typeof(MainForm).GetField("snpResult",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,result);
            var grid=Field<DataGridView>(form,"grid");grid.Rows.Add("#1","82.1","131 / 171");grid.CurrentCell=grid.Rows[0].Cells[0];
            var details=Field<ResultDetailsView>(form,"detailsView");details.ShowSnp(set,result);
            Field<Label>(form,"resultInfo").Text="UI coordinate fixture | 301 nt | SNP 151 | fabricated sequences for layout checks";
            Field<TabControl>(form,"tabs").SelectedIndex=1;
            Handles(form);Layout(form);Application.DoEvents();
            var diagram=Field<Control>(details,"diagram");
            var diagramHost=Field<Panel>(details,"diagramHost");
            var tabs=Field<TabControl>(details,"detailTabs");
            var cards=Field<FlowLayoutPanel>(details,"primers");
            Check(diagram.Height>=(int)(234*scale)-2,"Minimum main window preserves the SNP diagram's intended height.");
            Check(Object.ReferenceEquals(diagram.Parent,diagramHost),"RPA map remains inside the shared map host at minimum size.");
            Check(diagramHost.Bottom<=tabs.Top+2,"Position-map host does not overlap primer-card tabs.");
            Check(tabs.Height>=(int)(100*scale),"Minimum main window leaves a usable primer-card viewport.");
            Check(cards.Controls.Count==4&&cards.AutoScroll,"Four SNP primer cards remain in a scrollable list.");
            Check(cards.DisplayRectangle.Height>cards.ClientRectangle.Height,"At minimum window size the full card list has scrollable vertical extent.");
            int sequenceCount=0;
            foreach(Control card in cards.Controls)
                foreach(Control child in card.Controls)
                    if(child is RichTextBox)
                    {
                        sequenceCount++;
                        var box=(RichTextBox)child;
                        Check(box.TextLength==31,"Primer sequence survives map layout: "+box.Name);
                        Check(box.Left>=0&&box.Right<=card.ClientSize.Width&&box.Top>=0&&box.Bottom<=card.ClientSize.Height,"Primer sequence control fits its card: "+box.Name);
                    }
            Check(sequenceCount==4,"All four primer sequence controls remain present.");
            using(Bitmap image=Render(form,"main-minimum-snp.png")){}
            int contentHeight=cards.DisplayRectangle.Height;
            cards.AutoScrollPosition=new Point(0,contentHeight);
            Layout(form);Application.DoEvents();
            if(cards.AutoScrollPosition.Y<0)
            {
                Control last=cards.Controls[cards.Controls.Count-1];
                Check(last.Bottom<=cards.ClientSize.Height+cards.Padding.Bottom+2,"Last primer card is reachable by scrolling at minimum window size.");
                using(Bitmap image=Render(form,"main-minimum-snp-scrolled.png")){}
            }
            else notes.Add("LIMIT: hidden form does not apply native AutoScrollPosition; AutoScroll, positive viewport and full content extent verified.");
            Check(!form.Visible,"MainForm was never shown on the desktop.");
        }
    }
    private static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
    private static void Handles(Control control){IntPtr handle=control.Handle;foreach(Control child in control.Controls)Handles(child);}
    private static void Layout(Control control){control.PerformLayout();foreach(Control child in control.Controls)Layout(child);}
}
