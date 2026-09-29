using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace RpaDesigner
{
    // A drawing primitive carries input-template coordinates only. In particular,
    // a chemical C3 block never acquires a fictitious genomic coordinate.
    internal sealed class LampMapSegment
    {
        internal string Role { get; private set; }
        internal string SourceName { get; private set; }
        internal string Kind { get; private set; }
        internal int? TemplateStart { get; private set; }
        internal int? TemplateEnd { get; private set; }
        internal bool Reverse { get; private set; }
        internal string Description { get; private set; }

        internal LampMapSegment(string role,string source,string kind,int? start,int? end,bool reverse,string description)
        {Role=role;SourceName=source;Kind=kind;TemplateStart=start;TemplateEnd=end;Reverse=reverse;Description=description;}
    }

    // The complete, linear input template is the fixed reference for every lane.
    // The two parts of a composite oligo are never collapsed into one interval.
    internal sealed class LampPositionView : Control
    {
        private static readonly Color Ink=Color.FromArgb(35,53,69);
        private static readonly Color Muted=Color.FromArgb(97,111,123);
        private static readonly Color SnpRed=Color.FromArgb(211,47,47);
        private static readonly Color TailOrange=Color.FromArgb(190,125,29);
        private static readonly Color ActiveGreen=Color.FromArgb(0,125,96);
        private readonly ToolTip tip=new ToolTip {AutoPopDelay=18000,InitialDelay=250,ReshowDelay=80};
        private readonly List<LampMapSegment> segments=new List<LampMapSegment>();
        private readonly List<MapLane> lanes=new List<MapLane>();
        private readonly List<HitArea> hitAreas=new List<HitArea>();
        private LampPrimerSet selectedSet;
        private LampDesignResult selectedResult;
        private string hoverText="";
        private bool pa;

        private sealed class MapLane
        {
            internal string Label,Summary,Description;
            internal Color Color;
            internal readonly List<LampMapSegment> Parts=new List<LampMapSegment>();
            internal bool IsActivation;
        }
        private sealed class HitArea
        {
            internal RectangleF Bounds;
            internal string Text;
        }
        private sealed class PlotLayout
        {
            internal float Scale,Left,Right,TopAxis,BottomAxis,LaneTop,FooterTop,Height;
            internal readonly List<float> RowHeights=new List<float>();
        }

        internal int TemplateLength {get;private set;}
        internal int SnpPosition {get;private set;}
        internal int CandidateRank {get{return selectedSet==null?0:selectedSet.Rank;}}
        internal LampMapSegment[] Segments {get{return segments.ToArray();}}
        internal int LogicalHeight
        {
            get
            {
                using(var g=Graphics.FromHwnd(IntPtr.Zero))
                using(var small=new Font(Font.FontFamily,9F))
                {
                    PlotLayout layout=MeasureLayout(g,small);
                    return (int)Math.Ceiling(layout.Height/layout.Scale);
                }
            }
        }

        public LampPositionView()
        {
            Name="lampTemplatePositionMap";BackColor=Color.White;ForeColor=Ink;DoubleBuffered=true;
            SetStyle(ControlStyles.ResizeRedraw,true);
            AccessibleName="LAMP 引物在完整双链输入模板上的位置";
            UpdateDescription();
        }

        public void SetLamp(LampPrimerSet set,LampDesignResult result)
        {
            selectedSet=set;selectedResult=result;segments.Clear();lanes.Clear();hitAreas.Clear();hoverText="";
            TemplateLength=set==null || result==null || result.Input==null || result.Input.Sequence==null?0:result.Input.Sequence.Length;
            SnpPosition=TemplateLength>0 && result.Snp!=null?result.Snp.Position:0;
            pa=TemplateLength>0 && set.BIP!=null && set.BIP.RnaIndex>=0;
            if(TemplateLength>0)
            {
                AddLane("F3",set.F3,Color.FromArgb(0,119,117));
                AddLane("FIP",set.FIP,Color.FromArgb(0,134,126));
                AddLane("LF",set.LF,Color.FromArgb(74,122,151));
                AddLane("BIP",set.BIP,Color.FromArgb(106,89,167));
                AddLane("LB",set.LB,Color.FromArgb(147,107,160));
                AddLane("B3",set.B3,Color.FromArgb(86,97,167));
            }
            UpdateDescription();Invalidate();
        }

        internal float CoordinateToX(int position,float left,float right)
        {
            if(TemplateLength<=0)return left;
            if(TemplateLength==1)return (left+right)/2F;
            int bounded=Math.Max(1,Math.Min(TemplateLength,position));
            return left+(bounded-1)*(right-left)/(TemplateLength-1);
        }

        private static string Range(int start,int end){return start+"–"+end;}
        private static string Direction(bool reverse){return reverse?"←":"→";}
        private string AlleleLabel(string role)
        {
            return selectedResult.Snp!=null && selectedSet.AlternateInner!=null && selectedSet.SpecificInner==role?" ref / alt":"";
        }
        private void AddLane(string role,LampOligo oligo,Color color)
        {
            if(oligo==null)return;
            var lane=new MapLane {Label=role+AlleleLabel(role),Color=color};
            var summary=new List<string>();var description=new StringBuilder();
            description.Append(role).Append(AlleleLabel(role)).Append("；订购拼接顺序（5′→3′）：");
            for(int i=0;i<oligo.Regions.Count;i++)
            {
                LampRegion region=oligo.Regions[i];
                if(i>0)description.Append(" + ");description.Append(region.Name);
                string kind=oligo.RnaIndex>=0 && region.Name=="B2"?"precursor":"DNA";
                string name=region.Name+(kind=="precursor"?" 前体":"");
                string text=role+" / "+name+"：输入正链坐标 "+Range(region.Start,region.End)+"（两端含）；5′→3′ "+(region.Reverse?"向左；相对输入序列为反向互补。":"向右；相对输入序列为同向。");
                if(region.Name=="F1c" || region.Name=="B1c")text+=" 此为成环区段，图中位置不表示与另一内区段同时退火在线性模板上。";
                var part=new LampMapSegment(role,region.Name,kind,region.Start,region.End,region.Reverse,text);
                lane.Parts.Add(part);segments.Add(part);
                summary.Add(name+" "+Direction(region.Reverse)+" "+Range(region.Start,region.End));
            }
            lane.Summary=String.Join("   +   ",summary.ToArray());
            description.Append("。\n");foreach(var part in lane.Parts)description.AppendLine(part.Description);
            if(AlleleLabel(role).Length>0)description.AppendLine("ref / alt 使用相同位置，两种等位内引物分别用于两个独立反应。");
            lane.Description=description.ToString().Trim();lanes.Add(lane);
            if(oligo.RnaIndex>=0)AddActivationLane(role,oligo);
        }

        private void AddActivationLane(string role,LampOligo oligo)
        {
            var lane=new MapLane {Label="切后 B2 / 尾",Color=ActiveGreen,IsActivation=true};
            LampRegion precursor=null,active=null;
            foreach(var r in oligo.Regions)if(r.Name=="B2")precursor=r;
            foreach(var r in oligo.ActivatedRegions)if(r.Name=="B2")active=r;
            if(precursor==null || active==null)return;
            var activePart=new LampMapSegment(role,"B2","active",active.Start,active.End,active.Reverse,
                "切后有效 B2："+Range(active.Start,active.End)+"；5′→3′ 向左；其 3′-OH 位于输入正链坐标 "+active.Start+"。两种等位前体切后有效序列相同，SNP 不在有效 B2 内。");
            var rna=new LampMapSegment(role,"RNA","RNA",oligo.RnaTemplatePosition,oligo.RnaTemplatePosition,precursor.Reverse,
                "RNA 对应输入正链 SNP "+oligo.RnaTemplatePosition+"；位于 BIP 反向区段，RNA 碱基与所选等位配对（按引物方向为互补碱基）。RNase H2 在 RNA 的 5′ 侧切割。");
            int tailStart=precursor.Start,tailEnd=oligo.RnaTemplatePosition-1;
            string tailText="会被切除的 DNA 尾：对应输入正链范围 "+Range(tailStart,tailEnd)+"（"+oligo.ActivationTailLength+" nt）；5′→3′ 向左。尾末位在输入坐标 "+oligo.TailMismatchPosition+" 引入人为非互补碱基，此范围表示模板对应位置，不表示尾段全部匹配。";
            var tail=new LampMapSegment(role,"DNA tail","tail",tailStart,tailEnd,precursor.Reverse,tailText);
            var block=new LampMapSegment(role,"C3","C3",null,null,precursor.Reverse,"3′ C3 为前体末端化学阻断修饰，没有输入模板坐标；与 RNA + DNA 尾一同随阻断片段移除。");
            // Paint every object in this list; C3 is deliberately rendered only in text.
            lane.Parts.Add(activePart);lane.Parts.Add(tail);lane.Parts.Add(rna);lane.Parts.Add(block);
            segments.AddRange(lane.Parts);
            lane.Summary="有效 B2 ← "+Range(active.Start,active.End)+"；尾 ← "+Range(tailStart,tailEnd)+"；RNA "+oligo.RnaTemplatePosition+"；3′ C3（修饰）";
            lane.Description=activePart.Description+"\n"+tail.Description+"\n"+rna.Description+"\n"+block.Description;
            lanes.Add(lane);
        }

        private string Footer()
        {
            if(selectedSet==null)return "选择 LAMP 候选后显示完整输入双链模板与各引物区段。";
            string text="浅色区：F3…B3 "+Range(selectedSet.SpanStart,selectedSet.SpanEnd)+"；图为线性输入模板，不是 LAMP 产物。\n"
                +"箭头为区段 5′→3′ 方向；虚线仅示同一引物的拼接关系。\n"
                +"F1c / B1c 是成环段，图示不表示两个内区段同时退火在线性模板上。";
            if(SnpPosition>0)text+="\nref / alt 为两个独立等位反应；红色标记 SNP"+(pa?" / RNA，橙色为切除尾；C3 为修饰。":"。");
            return text;
        }
        private void UpdateDescription()
        {
            var text=new StringBuilder("完整输入双链模板；所有坐标均为输入正链 1-based 闭区间。\n");
            if(TemplateLength>0)
            {
                text.Append("输入序列 1–").Append(TemplateLength).Append(" nt，所有候选使用同一完整输入比例。\n");
                if(SnpPosition>0)text.Append("SNP ").Append(SnpPosition).Append(" [").Append(selectedResult.Snp.ReferenceAllele).Append(">").Append(selectedResult.Snp.AlternateAllele).Append("]\n");
                foreach(var lane in lanes)text.AppendLine(lane.Description);
            }
            text.Append(Footer());AccessibleDescription=text.ToString();tip.SetToolTip(this,AccessibleDescription);
        }

        private PlotLayout MeasureLayout(Graphics g,Font small)
        {
            float scale=g.DpiX/96F;
            var layout=new PlotLayout {Scale=scale};
            float width=Math.Max(1,ClientSize.Width);
            layout.Left=Math.Min(102*scale,width*0.35F);layout.Right=Math.Max(layout.Left+1,width-26*scale);
            layout.TopAxis=50*scale;layout.BottomAxis=62*scale;layout.LaneTop=91*scale;
            float y=layout.LaneTop;
            foreach(var lane in lanes)
            {
                float textHeight=g.MeasureString(lane.Summary,small,new SizeF(Math.Max(1,layout.Right-layout.Left),10000)).Height;
                float height=Math.Max(32*scale,textHeight+15*scale);
                layout.RowHeights.Add(height);y+=height;
            }
            layout.FooterTop=y+5*scale;
            float footerHeight=g.MeasureString(Footer(),small,new SizeF(Math.Max(1,width-16*scale),10000)).Height;
            layout.Height=layout.FooterTop+footerHeight+8*scale;
            if(TemplateLength<=0)layout.Height=120*scale;
            return layout;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);hitAreas.Clear();Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var small=new Font(Font.FontFamily,9F))
            using(var laneFont=new Font(Font.FontFamily,9F,FontStyle.Bold))
            {
                PlotLayout layout=MeasureLayout(g,small);float s=layout.Scale,left=layout.Left,right=layout.Right;
                if(TemplateLength<=0)
                {
                    DrawText(g,Footer(),small,Muted,new RectangleF(10*s,18*s,Math.Max(1,Width-20*s),80*s));return;
                }
                DrawText(g,"完整输入双链模板 · "+TemplateLength.ToString("N0")+" nt · 同一坐标比例",small,Ink,new RectangleF(6*s,1*s,Math.Max(1,Width-12*s),20*s));
                if(SnpPosition>0)
                    DrawText(g,"SNP "+SnpPosition+"  ["+selectedResult.Snp.ReferenceAllele+">"+selectedResult.Snp.AlternateAllele+"]",small,SnpRed,new RectangleF(6*s,23*s,Math.Max(1,Width-12*s),20*s));
                else DrawText(g,"普通 LAMP · 未标注 SNP",small,Muted,new RectangleF(6*s,23*s,Math.Max(1,Width-12*s),20*s));
                PaintTemplate(g,small,layout);
                float y=layout.LaneTop;
                for(int i=0;i<lanes.Count;i++)
                {
                    PaintLane(g,small,laneFont,lanes[i],y,layout.RowHeights[i],layout);y+=layout.RowHeights[i];
                }
                if(SnpPosition>0)
                {
                    float x=CoordinateToX(SnpPosition,left,right);
                    using(var marker=new Pen(Color.FromArgb(150,SnpRed),s))
                    {marker.DashStyle=DashStyle.Dash;g.DrawLine(marker,x,layout.TopAxis-6*s,x,layout.FooterTop-4*s);}
                    AddHit(new RectangleF(x-5*s,layout.TopAxis-6*s,10*s,layout.FooterTop-layout.TopAxis),"SNP：输入正链坐标 "+SnpPosition+" ["+selectedResult.Snp.ReferenceAllele+">"+selectedResult.Snp.AlternateAllele+"]。红线在所有引物行采用相同输入坐标。");
                }
                DrawText(g,Footer(),small,Muted,new RectangleF(6*s,layout.FooterTop,Math.Max(1,Width-12*s),layout.Height-layout.FooterTop));
            }
        }

        private void PaintTemplate(Graphics g,Font font,PlotLayout l)
        {
            float s=l.Scale,left=l.Left,right=l.Right;
            float a=CoordinateToX(selectedSet.SpanStart,left,right),b=CoordinateToX(selectedSet.SpanEnd,left,right);
            using(var shade=new SolidBrush(Color.FromArgb(220,236,237)))
                g.FillRectangle(shade,a,l.TopAxis-6*s,Math.Max(s,b-a),l.BottomAxis-l.TopAxis+12*s);
            using(var backbone=new Pen(Color.FromArgb(151,172,184),2*s))
            {
                g.DrawLine(backbone,left,l.TopAxis,right,l.TopAxis);g.DrawLine(backbone,left,l.BottomAxis,right,l.BottomAxis);
                int bars=Math.Max(2,Math.Min(40,(int)((right-left)/(17*s))));
                using(var rung=new Pen(Color.FromArgb(208,219,225),s))
                    for(int i=0;i<=bars;i++){float x=left+(right-left)*i/bars;g.DrawLine(rung,x,l.TopAxis+2*s,x,l.BottomAxis-2*s);}
            }
            DrawText(g,"输入正链 5′",font,Muted,new RectangleF(6*s,l.TopAxis-9*s,Math.Max(1,left-11*s),18*s));
            DrawText(g,"互补链   3′",font,Muted,new RectangleF(6*s,l.BottomAxis-2*s,Math.Max(1,left-11*s),18*s));
            DrawText(g,"3′",font,Muted,new RectangleF(right+5*s,l.TopAxis-9*s,22*s,18*s));
            DrawText(g,"5′",font,Muted,new RectangleF(right+5*s,l.BottomAxis-2*s,22*s,18*s));
            DrawText(g,"1",font,Muted,new RectangleF(left,l.BottomAxis+11*s,50*s,18*s));
            DrawText(g,TemplateLength.ToString(),font,Muted,new RectangleF(Math.Max(left,right-90*s),l.BottomAxis+11*s,Math.Min(90*s,right-left),18*s),StringAlignment.Far);
            if(right-left>350*s && TemplateLength>4)
            {
                int position=1+(TemplateLength-1)/2;float x=CoordinateToX(position,left,right);
                DrawText(g,position.ToString(),font,Muted,new RectangleF(x-45*s,l.BottomAxis+11*s,90*s,18*s),StringAlignment.Center);
            }
            AddHit(new RectangleF(left,l.TopAxis-8*s,right-left,l.BottomAxis-l.TopAxis+22*s),"线性双链输入模板，正链 5′→3′ 向右，互补链 5′→3′ 向左。所有区段坐标按输入正链记录。F3…B3 范围："+Range(selectedSet.SpanStart,selectedSet.SpanEnd)+"。引物箭头只说明序列相对于输入坐标的方向，不表示引物与同向模板链结合。");
        }

        private void PaintLane(Graphics g,Font font,Font labelFont,MapLane lane,float top,float height,PlotLayout l)
        {
            float s=l.Scale,left=l.Left,right=l.Right,y=top+height-8*s;
            if(lanes.IndexOf(lane)%2==0)
                using(var wash=new SolidBrush(Color.FromArgb(248,250,251)))g.FillRectangle(wash,3*s,top,Math.Max(1,Width-6*s),height);
            DrawText(g,lane.Label,labelFont,lane.Color,new RectangleF(6*s,top+2*s,left-12*s,height-2*s));
            DrawText(g,lane.Summary,font,lane.Color,new RectangleF(left,top,Math.Max(1,right-left),height-11*s));
            // The broad hit target makes precise coordinates available even when a
            // long input compresses several 20-nt regions to the same screen pixel.
            AddHit(new RectangleF(3*s,top,Math.Max(1,Width-6*s),height),lane.Description);
            if(!lane.IsActivation && lane.Parts.Count>1)
            {
                for(int i=1;i<lane.Parts.Count;i++)
                {
                    var first=lane.Parts[i-1];var second=lane.Parts[i];
                    int from=first.Reverse?first.TemplateStart.Value:first.TemplateEnd.Value;
                    int to=second.Reverse?second.TemplateEnd.Value:second.TemplateStart.Value;
                    float x1=CoordinateToX(from,left,right),x2=CoordinateToX(to,left,right);
                    using(var connector=new Pen(Color.FromArgb(135,lane.Color),s))
                    {connector.DashStyle=DashStyle.Dash;g.DrawLine(connector,x1,y+4*s,x2,y+4*s);}
                }
            }
            foreach(var part in lane.Parts)
            {
                if(!part.TemplateStart.HasValue || !part.TemplateEnd.HasValue)continue;
                float a=CoordinateToX(part.TemplateStart.Value,left,right),b=CoordinateToX(part.TemplateEnd.Value,left,right);
                if(part.Kind=="RNA")
                {
                    using(var brush=new SolidBrush(SnpRed))g.FillEllipse(brush,a-3*s,y-3*s,6*s,6*s);
                }
                else DrawArrow(g,a,b,y,part.Reverse,part.Kind=="active"?ActiveGreen:part.Kind=="tail"?TailOrange:lane.Color,s);
            }
        }
        private static void DrawArrow(Graphics g,float start,float end,float y,bool reverse,Color color,float scale)
        {
            float length=Math.Max(0,end-start);
            using(var brush=new SolidBrush(color))
            {
                if(length<scale)
                {
                    // Do not stretch a short region into a false genomic interval.
                    // Its direction remains explicit in the lane text and tooltip.
                    g.FillRectangle(brush,(start+end)/2-scale/2,y-3*scale,scale,6*scale);return;
                }
                float head=Math.Min(6*scale,length*0.6F),tail=reverse?end:start,tip=reverse?start:end,neck=reverse?start+head:end-head;
                g.FillPolygon(brush,new PointF[]{new PointF(tail,y-1.5F*scale),new PointF(neck,y-1.5F*scale),new PointF(neck,y-4*scale),new PointF(tip,y),new PointF(neck,y+4*scale),new PointF(neck,y+1.5F*scale),new PointF(tail,y+1.5F*scale)});
            }
        }
        private void AddHit(RectangleF bounds,string text){hitAreas.Add(new HitArea {Bounds=bounds,Text=text});}
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);string text=AccessibleDescription;
            // Lane descriptions take priority over the thin SNP hit area so the
            // full composite/activation explanation is always available there.
            for(int i=0;i<hitAreas.Count;i++)if(hitAreas[i].Bounds.Contains(e.Location)){text=hitAreas[i].Text;break;}
            if(text!=hoverText){hoverText=text;tip.SetToolTip(this,text);}
        }
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverText="";tip.Hide(this);}
        protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
        private static void DrawText(Graphics g,string text,Font font,Color color,RectangleF bounds,StringAlignment alignment=StringAlignment.Near)
        {
            using(var brush=new SolidBrush(color))using(var format=new StringFormat())
            {format.Alignment=alignment;format.Trimming=StringTrimming.None;g.DrawString(text,font,brush,bounds,format);}
        }
    }
}
