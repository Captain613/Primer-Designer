using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RpaDesigner;

// Hidden custom-control rendering only: no Show(), desktop automation, dialogs or clipboard.
// The hard-coded 301 nt fixture is intentionally artificial and tests diagram geometry only.
internal static class LampPositionMapCheck
{
    private static readonly List<string> notes=new List<string>();
    private static readonly List<string> errors=new List<string>();
    private static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static string output;
    private static float scale;
    private static int checks;
    private static Exception dispatchError;
    private static readonly Color snpRed=Color.FromArgb(211,47,47);

    [STAThread]
    private static int Main(string[] args)
    {
        output=args.Length==0?AppDomain.CurrentDomain.BaseDirectory:Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){dispatchError=e.Exception;};
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls=true;
            Type type=typeof(MainForm).Assembly.GetType("RpaDesigner.LampPositionView",true);
            using(Control map=(Control)Activator.CreateInstance(type,true))
            {
                map.Font=new Font("Microsoft YaHei UI",10F);
                IntPtr handle=map.Handle;
                using(Graphics g=map.CreateGraphics())scale=g.DpiX/96F;
                notes.Add("Assembly="+typeof(MainForm).Assembly.Location+"; DPI="+(scale*96F));
                Check(map.Name=="lampTemplatePositionMap","Position map has a stable accessible control name.");
                var ordinary=Fixture("ordinary",true,301,0);
                Set(map,ordinary.Sets[0],ordinary,1000);
                Check(Int(map,"TemplateLength")==301,"Template scale uses full 301 nt input, not 260 nt F3..B3 target.");
                Near(X(map,1,10,610),10,"First base is full-template left endpoint.");
                Near(X(map,151,10,610),310,"Base 151 is the 301 nt template midpoint.");
                Near(X(map,301,10,610),610,"Last input base is full-template right endpoint.");
                Near(X(map,21,10,610),50,"F3 starts at a proportionally internal coordinate.");
                Near(X(map,280,10,610),568,"B3 ends before the full-template endpoint.");
                CheckFixtureSegments(map,"ordinary",true);
                using(Bitmap bitmap=Render(map,"lamp-ordinary-301nt.png"))CheckDrawing(bitmap,"Ordinary full template");

                var asFip=Fixture("FIP",true,301,0);Set(map,asFip.Sets[0],asFip,1000);
                CheckFixtureSegments(map,"FIP",true);
                CheckSnp(map,80,"AS-FIP SNP coincides with forward F2 3-prime end.");
                using(Bitmap bitmap=Render(map,"lamp-as-fip-301nt.png")){CheckDrawing(bitmap,"AS-FIP");CheckRed(bitmap,"AS-FIP SNP marker");CheckPaintedSnp(bitmap,80,301);}
                var asBip=Fixture("BIP",true,301,0);Set(map,asBip.Sets[0],asBip,1000);
                CheckFixtureSegments(map,"BIP",true);
                CheckSnp(map,221,"AS-BIP SNP coincides with reverse B2 3-prime end.");
                using(Bitmap bitmap=Render(map,"lamp-as-bip-301nt.png")){CheckDrawing(bitmap,"AS-BIP");CheckRed(bitmap,"AS-BIP SNP marker");CheckPaintedSnp(bitmap,221,301);}

                var pa=Fixture("PA",true,301,0);Set(map,pa.Sets[0],pa,1000);
                CheckFixtureSegments(map,"PA",true);CheckSnp(map,221,"PA RNA site remains at template base 221.");
                using(Bitmap bitmap=Render(map,"lamp-pa-301nt.png")){CheckDrawing(bitmap,"PA-LAMP");CheckRed(bitmap,"PA SNP/RNA marker");CheckPaintedSnp(bitmap,221,301);}
                int withLoops=Int(map,"LogicalHeight");
                var noLoops=Fixture("PA",false,301,0);Set(map,noLoops.Sets[0],noLoops,1000);
                CheckFixtureSegments(map,"PA",false);
                Check(Int(map,"LogicalHeight")<=withLoops,"Missing optional loop primers do not leave stale extra rows.");
                Set(map,noLoops.Sets[0],noLoops,420);
                using(Bitmap bitmap=Render(map,"lamp-pa-narrow-no-loops.png")){CheckDrawing(bitmap,"Narrow PA view");CheckRed(bitmap,"Narrow PA SNP");}

                var longTemplate=Fixture("PA",false,20000,9800);Set(map,longTemplate.Sets[0],longTemplate,1000);
                Check(Int(map,"TemplateLength")==20000,"Long template preserves full 20,000 nt length.");
                Near(X(map,1,0,19999),0,"20,000 nt first base maps to start.");
                Near(X(map,10000,0,19999),9999,"20,000 nt internal base retains its exact coordinate fraction.");
                Near(X(map,20000,0,19999),19999,"20,000 nt final base maps to end.");
                CheckSnp(map,10021,"Long template maps shifted PA RNA/SNP coordinate.");
                using(Bitmap bitmap=Render(map,"lamp-pa-20000nt.png")){CheckDrawing(bitmap,"Long PA template");CheckRed(bitmap,"Long-template SNP");CheckPaintedSnp(bitmap,10021,20000);}

                Set(map,ordinary.Sets[0],ordinary,1000);
                CheckFixtureSegments(map,"ordinary",true);
                Check(Int(map,"TemplateLength")==301,"Switching to an ordinary candidate removes stale long-template length.");
                CheckSnp(map,0,"Switching from PA to ordinary clears SNP state.");
                using(Bitmap bitmap=Render(map,"lamp-ordinary-after-pa.png"))Check(Count(bitmap,snpRed)==0,"Ordinary map has no stale red SNP or RNA graphics.");
                Set(map,null,null,1000);
                Check(Int(map,"TemplateLength")==0,"Clearing removes stored template length.");
                Check(Segments(map).Count==0,"Clearing removes every old primer/chemical segment.");
                CheckSnp(map,0,"Clearing removes SNP guide.");
                using(Bitmap bitmap=Render(map,"lamp-cleared.png"))Check(Count(bitmap,snpRed)==0,"Cleared map has no old SNP graphics.");
                Check(map.FindForm()==null,"Custom map has never been attached to a visible desktop window.");
                CheckActualCandidates(map);
            }
            Check(dispatchError==null,"No UI exception was dispatched.");
        }
        catch(Exception ex){errors.Add(ex.ToString());}
        notes.Add("Assertions="+checks+"; failures="+errors.Count);
        foreach(string error in errors)notes.Add("FAIL "+error);
        notes.Add(errors.Count==0?"RESULT: PASS":"RESULT: FAIL");
        notes.Add("Scope: preset coordinate fixtures and real synthetic-input candidates verify full-template geometry, strand/segment state, mode changes and custom-control drawing. They do not validate amplification, RNA cleavage or SNP selectivity. No desktop window or clipboard was used.");
        File.WriteAllLines(Path.Combine(output,"lamp-position-map-test-report.txt"),notes.ToArray(),new UTF8Encoding(true));
        return errors.Count==0?0:1;
    }

    private static void CheckActualCandidates(Control map)
    {
        string dna=SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
        foreach(string mode in new string[]{"ordinary","FIP","BIP","PA"})
        {
            LampDesignSettings settings=Broad();settings.IncludeLoops=true;
            LampDesignResult result;
            if(mode=="ordinary")result=LampDesignEngine.Design(SequenceParser.Parse(dna),settings,null,CancellationToken.None);
            else
            {
                settings.SnpOrientation=mode=="FIP"?"FIP":"BIP";
                settings.SnpMethod=mode=="PA"?"PA-LAMP":"AS-LAMP";
                int position=mode=="PA"?333:300;
                SnpInput snp=SnpParser.Parse(dna.Substring(0,position-1)+"[A>C]"+dna.Substring(position));
                result=LampDesignEngine.DesignSnp(snp,settings,null,CancellationToken.None);
            }
            Check(result.Sets.Count>0,"Real synthetic-input engine candidate exists for "+mode+".");
            if(result.Sets.Count==0)continue;
            for(int i=0;i<Math.Min(2,result.Sets.Count);i++)
            {
                Set(map,result.Sets[i],result,i==0?1000:420);
                Check(Int(map,"TemplateLength")==dna.Length,"Real "+mode+" candidate "+i+" passes full input length.");
                CheckSnp(map,result.Snp==null?0:result.Snp.Position,"Real "+mode+" candidate has current SNP coordinate.");
                using(Bitmap bitmap=Render(map,"lamp-real-"+mode.ToLowerInvariant()+"-candidate"+i+".png"))CheckDrawing(bitmap,"Real "+mode+" candidate "+i);
            }
        }
    }
    private static LampDesignSettings Broad(){return new LampDesignSettings{RegionMin=20,RegionMax=20,SpanMin=120,SpanMax=300,MaxSets=2,GcMin=20,GcMax=80,AnnealTmMin=35,AnnealTmMax=85,InnerTmMin=35,InnerTmMax=85,IncludeLoops=false};}

    private static LampDesignResult Fixture(string mode,bool loops,int length,int offset)
    {
        var set=new LampPrimerSet {Rank=1,Score=80,SpanStart=21+offset,SpanEnd=280+offset,SpanLength=260,ReferenceTemplate=new string('A',260),AlternateTemplate=new string('A',260)};
        set.F3=Oligo("F3",Region("F3",21,40,false,offset));
        set.B3=Oligo("B3",Region("B3",261,280,true,offset));
        set.FIP=Oligo("FIP",Region("F1c",121,140,true,offset),Region("F2",61,80,false,offset));
        set.BIP=Oligo("BIP",Region("B1c",161,180,false,offset),Region("B2",221,240,true,offset));
        if(loops){set.LF=Oligo("LF",Region("LF",91,110,true,offset));set.LB=Oligo("LB",Region("LB",191,210,false,offset));}
        var result=new LampDesignResult{Input=new ParsedSequence{Name="Artificial diagram fixture",Sequence=new string('A',length)},Settings=Broad()};
        result.Settings.IncludeLoops=loops;
        if(mode!="ordinary")
        {
            int pos=(mode=="FIP"?80:221)+offset;
            result.Snp=SnpParser.Parse(new string('A',pos-1)+"[A>C]"+new string('A',length-pos));result.Input=result.Snp.Reference;
            set.SpecificInner=mode=="FIP"?"FIP":"BIP";result.Settings.SnpOrientation=set.SpecificInner;
            LampOligo specific=mode=="FIP"?set.FIP:set.BIP;
            if(mode=="PA")
            {
                result.Settings.SnpMethod="PA-LAMP";
                set.BIP=Oligo("BIP",Region("B1c",161,180,false,offset),Region("B2",216,240,true,offset));specific=set.BIP;
                specific.ActivatedRegions.Add(Region("B1c",161,180,false,offset));specific.ActivatedRegions.Add(Region("B2",222,240,true,offset));
                specific.ActivatedSequence=new string('A',20)+new string('T',19);specific.ActivatedMetrics=new Primer{Sequence=specific.ActivatedSequence};
                specific.RnaIndex=39;specific.RnaTemplatePosition=pos;specific.ActivationTailLength=5;specific.ThreePrimeBlock="C3";
                specific.SnpIndex=39;
                specific.TailMismatchPosition=216+offset;specific.TailMismatchBase='G';specific.TailMismatchTemplateBase='A';specific.TailMismatchOriginalBase='T';
                specific.Sequence=specific.Sequence.Substring(0,44)+"G";
            }
            else specific.SnpIndex=specific.Sequence.Length-1;
            set.AlternateInner=Clone(specific);set.AlternateInner.Name=specific.Name+"_alt";
            char replacement=mode=="FIP"?'C':'G';
            char[] seq=set.AlternateInner.Sequence.ToCharArray();seq[specific.SnpIndex]=replacement;set.AlternateInner.Sequence=new string(seq);
        }
        result.Sets.Add(set);return result;
    }
    private static LampRegion Region(string name,int start,int end,bool reverse,int offset){return new LampRegion{Name=name,Start=start+offset,End=end+offset,Reverse=reverse,Sequence=new string(reverse?'T':'A',end-start+1),Gc=0,Tm=60};}
    private static LampOligo Oligo(string name,params LampRegion[] regions)
    {
        var value=new LampOligo{Name=name};var sequence=new StringBuilder();foreach(var r in regions){value.Regions.Add(r);sequence.Append(r.Sequence);}value.Sequence=sequence.ToString();value.Metrics=new Primer{Sequence=value.Sequence};return value;
    }
    private static LampOligo Clone(LampOligo p)
    {
        var value=Oligo(p.Name,p.Regions.ToArray());value.Sequence=p.Sequence;value.SnpIndex=p.SnpIndex;value.RnaIndex=p.RnaIndex;value.RnaTemplatePosition=p.RnaTemplatePosition;value.ActivationTailLength=p.ActivationTailLength;value.ThreePrimeBlock=p.ThreePrimeBlock;value.ActivatedSequence=p.ActivatedSequence;value.ActivatedMetrics=p.ActivatedMetrics;value.ActivatedRegions.AddRange(p.ActivatedRegions);value.TailMismatchPosition=p.TailMismatchPosition;return value;
    }
    private static void Set(Control map,LampPrimerSet set,LampDesignResult result,int width){map.Width=(int)Math.Ceiling(width*scale);map.GetType().GetMethod("SetLamp",flags).Invoke(map,new object[]{set,result});map.Height=(int)Math.Ceiling(Int(map,"LogicalHeight")*scale);map.PerformLayout();Application.DoEvents();}
    private static int Int(Control map,string name){return Convert.ToInt32(map.GetType().GetProperty(name,flags).GetValue(map,null));}
    private static float X(Control map,int position,float left,float right){return Convert.ToSingle(map.GetType().GetMethod("CoordinateToX",flags).Invoke(map,new object[]{position,left,right}));}
    private static void Near(float actual,float expected,string text){Check(!Single.IsNaN(actual)&&!Single.IsInfinity(actual)&&Math.Abs(actual-expected)<0.01F,text+" (actual "+actual+", expected "+expected+")");}
    private static void Check(bool pass,string text){checks++;notes.Add((pass?"PASS ":"FAIL ")+text);if(!pass)errors.Add(text);}
    private static Bitmap Render(Control map,string name){map.Refresh();Application.DoEvents();var bitmap=new Bitmap(map.Width,map.Height);map.DrawToBitmap(bitmap,map.ClientRectangle);bitmap.Save(Path.Combine(output,name),ImageFormat.Png);return bitmap;}
    private static int Count(Bitmap bitmap,Color color){int total=0,argb=color.ToArgb();for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==argb)total++;return total;}
    private static void CheckRed(Bitmap bitmap,string name){Check(Count(bitmap,snpRed)>20,name+" is visibly rendered in red.");}
    private static void CheckDrawing(Bitmap bitmap,string name)
    {
        int painted=0;for(int y=0;y<bitmap.Height;y+=2)for(int x=0;x<bitmap.Width;x+=2){Color c=bitmap.GetPixel(x,y);if(c.A>0&&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))>35)painted++;}
        Check(painted>80,name+" draws colored template/primer primitives rather than an empty native-control snapshot.");
        int backbone=Color.FromArgb(151,172,184).ToArgb();
        foreach(int y in new int[]{(int)Math.Round(50*scale),(int)Math.Round(62*scale)})
        {
            int line=0;for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==backbone)line++;
            Check(line>100,name+" includes a complete backbone at double-strand row "+y+".");
        }
    }
    private static void CheckPaintedSnp(Bitmap bitmap,int position,int length)
    {
        int backbone=Color.FromArgb(151,172,184).ToArgb(),left=-1,right=-1,y=(int)Math.Round(62*scale);
        for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==backbone){if(left<0)left=x;right=x;}
        Check(left>=0&&right>left,"SNP paint check finds full-template backbone endpoints.");if(left<0||right<=left)return;
        int expected=(int)Math.Round(left+(position-1)*(right-left)/(double)(length-1)),hits=0;
        for(int py=(int)(44*scale);py<bitmap.Height;py++)for(int px=Math.Max(0,expected-3);px<=Math.Min(bitmap.Width-1,expected+3);px++)
        {Color c=bitmap.GetPixel(px,py);if(c.R>150&&c.R-c.G>35&&c.R-c.B>35)hits++;}
        Check(hits>20,"SNP guide is painted at input coordinate "+position+" of "+length+", not at a candidate-relative fraction.");
    }
    private static void CheckSnp(Control map,int expected,string description){Check(Int(map,"SnpPosition")==expected,description);}
    private static void CheckFixtureSegments(Control map,string mode,bool loops)
    {
        List<object> segments=Segments(map);
        Segment(segments,"F3","F3",21,40,false,null);
        Segment(segments,"B3","B3",261,280,true,null);
        Segment(segments,"FIP","F1c",121,140,true,null);
        Segment(segments,"FIP","F2",61,80,false,null);
        Segment(segments,"BIP","B1c",161,180,false,null);
        Segment(segments,"BIP","B2",mode=="PA"?216:221,240,true,mode=="PA"?"precursor":null);
        if(loops){Segment(segments,"LF","LF",91,110,true,null);Segment(segments,"LB","LB",191,210,false,null);}
        else Check(!HasRole(segments,"LF")&&!HasRole(segments,"LB"),"Absent optional LF/LB primers have no stale segments.");
        foreach(object item in segments)
        {
            string role=Text(item,"Role"),kind=Text(item,"Kind");
            object start=Value(item,"TemplateStart"),end=Value(item,"TemplateEnd");
            if(kind!="C3")Check(start!=null&&end!=null&&Convert.ToInt32(start)>=1&&Convert.ToInt32(end)<=301&&Convert.ToInt32(start)<=Convert.ToInt32(end),"Mapped segment has valid inclusive input coordinates: "+role+" "+kind);
            if(mode!="PA")Check(kind!="RNA"&&kind!="C3"&&kind!="tail"&&kind!="active","Non-PA diagram contains no stale chemical/activation overlays.");
        }
        Check(mode=="ordinary"?!map.AccessibleDescription.Contains("ref / alt"):map.AccessibleDescription.Contains("ref / alt"),"Shared allele-position rows explicitly identify ref / alt only in SNP modes.");
        if(mode=="PA")
        {
            Segment(segments,"BIP","B2",222,240,true,"active");
            Segment(segments,"BIP",null,216,220,true,"tail");
            Segment(segments,"BIP",null,221,221,true,"RNA");
            int blocks=0;
            foreach(object item in segments)if(Text(item,"Kind")=="C3")
            {
                blocks++;Check(Value(item,"TemplateStart")==null&&Value(item,"TemplateEnd")==null,"C3 is an unpositioned chemical modification, never a DNA coordinate.");
                Check(Text(item,"Description").IndexOf("C3",StringComparison.Ordinal)>=0,"C3 modification retains its readable identity.");
            }
            Check(blocks==1&&map.AccessibleDescription.Contains("ref / alt"),"Shared reference/alternate PA position row identifies its common C3 block.");
        }
    }
    private static List<object> Segments(Control map){var result=new List<object>();foreach(object item in (IEnumerable)map.GetType().GetProperty("Segments",flags).GetValue(map,null))result.Add(item);return result;}
    private static object Value(object item,string name){FieldInfo f=item.GetType().GetField(name,flags);if(f!=null)return f.GetValue(item);return item.GetType().GetProperty(name,flags).GetValue(item,null);}
    private static string Text(object item,string name){return Convert.ToString(Value(item,name));}
    private static bool MatchesRole(object item,string role){string current=Text(item,"Role");return current==role||current.StartsWith(role+"_",StringComparison.Ordinal);}
    private static bool HasRole(List<object> items,string role){foreach(object item in items)if(MatchesRole(item,role))return true;return false;}
    private static void Segment(List<object> items,string role,string source,int start,int end,bool reverse,string kind)
    {
        bool found=false;
        foreach(object item in items)
        {
            if(!MatchesRole(item,role)||(source!=null&&Text(item,"SourceName")!=source)||(kind!=null&&Text(item,"Kind")!=kind))continue;
            object a=Value(item,"TemplateStart"),b=Value(item,"TemplateEnd");
            if(a!=null&&b!=null&&Convert.ToInt32(a)==start&&Convert.ToInt32(b)==end&&Convert.ToBoolean(Value(item,"Reverse"))==reverse){found=true;break;}
        }
        Check(found,role+" "+source+" "+kind+" preserves independently specified span "+start+".."+end+" and "+(reverse?"reverse":"forward")+" synthesis direction.");
    }
}
