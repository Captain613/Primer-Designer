using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RpaDesigner
{
    // All lanes share the original input template's one-based coordinate system.
    internal sealed class AmpliconView : Control
    {
        private static readonly Color ForwardColor = Color.FromArgb(0,119,117);
        private static readonly Color ReverseColor = Color.FromArgb(87,96,178);
        private static readonly Color SnpColor = Color.FromArgb(211,47,47);
        private PrimerPair pair;
        private DesignSettings settings;
        private SnpPrimerSet snp;
        private int snpPosition;
        private readonly ToolTip tip = new ToolTip();
        internal int TemplateLength { get; private set; }
        internal int LogicalHeight { get { return snp == null ? 164 : 234; } }

        public AmpliconView()
        {
            Name="templatePositionMap";DoubleBuffered=true;BackColor=Color.White;
            SetStyle(ControlStyles.ResizeRedraw,true);
            AccessibleName="引物在完整输入序列上的位置";
        }
        public void SetPair(PrimerPair value,DesignSettings options,int sequenceLength)
        {
            pair=value;settings=options;snp=null;snpPosition=0;
            TemplateLength=value==null?0:Math.Max(0,sequenceLength);UpdateDescription();
        }
        public void SetSnp(SnpPrimerSet value,int position,int sequenceLength)
        {
            pair=null;settings=null;snp=value;snpPosition=value==null?0:position;
            TemplateLength=value==null?0:Math.Max(0,sequenceLength);UpdateDescription();
        }
        internal float CoordinateToX(int position,float left,float right)
        {
            if(TemplateLength<=0)return left;
            if(TemplateLength==1)return (left+right)/2;
            int bounded=Math.Max(1,Math.Min(TemplateLength,position));
            return left+(bounded-1)*(right-left)/(TemplateLength-1);
        }
        private static string Range(Primer p){return p.Start+"–"+p.End;}
        private string ProductSummary()
        {
            if(pair!=null)return "扩增区间："+pair.AmpliconStart+"–"+pair.AmpliconEnd+"  ·  "+pair.AmpliconLength+" bp";
            if(snp!=null)return "等位扩增："+snp.ReferencePair.AmpliconStart+"–"+snp.ReferencePair.AmpliconEnd+"（"+snp.ReferencePair.AmpliconLength+" bp）\n对照扩增："+snp.ControlPair.AmpliconStart+"–"+snp.ControlPair.AmpliconEnd+"（"+snp.ControlPair.AmpliconLength+" bp）";
            return "";
        }
        private void UpdateDescription()
        {
            string text=TemplateLength>0?"完整输入序列：1–"+TemplateLength+" nt，5′→3′；所有位置按同一比例显示。\n"+ProductSummary():"选择候选后显示引物位置和方向。";
            if(pair!=null)text+="\nF："+Range(pair.Forward)+"（向右）\nR："+Range(pair.Reverse)+"（向左）";
            if(snp!=null)text+="\nSNP："+snpPosition+"\nF_control："+Range(snp.ControlForward)+"\nF_ref："+Range(snp.ReferenceForward)+"\nF_alt："+Range(snp.AlternateForward)+"\nR_common："+Range(snp.CommonReverse);
            AccessibleDescription=text;tip.SetToolTip(this,text);Invalidate();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)tip.Dispose();base.Dispose(disposing);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=g.DpiX/96F;
            if(TemplateLength<=0 || (pair==null && snp==null))
            {
                g.DrawString("选择候选后显示引物位置和方向",Font,Brushes.Gray,14*scale,38*scale);return;
            }
            float left=Math.Min(112*scale,ClientSize.Width*0.3F);
            float right=Math.Max(left+1,ClientSize.Width-24*scale);
            float axisY=38*scale;
            using(var small=new Font(Font.FontFamily,9F))
            {
                DrawText(g,"输入全序列  ·  "+TemplateLength.ToString("N0")+" nt  ·  按实际坐标比例",Font,Color.FromArgb(77,94,111),new RectangleF(4*scale,0,Math.Max(1,Width-8*scale),23*scale),StringAlignment.Near);
                using(var backbone=new Pen(Color.FromArgb(194,206,214),4*scale))g.DrawLine(backbone,left,axisY,right,axisY);
                PrimerPair outer=pair??snp.ControlPair;
                Highlight(g,outer.AmpliconStart,outer.AmpliconEnd,left,right,axisY,Color.FromArgb(163,193,214),8*scale);
                if(snp!=null)Highlight(g,snp.ReferencePair.AmpliconStart,snp.ReferencePair.AmpliconEnd,left,right,axisY,Color.FromArgb(115,174,171),4*scale);
                if(settings!=null && settings.TargetStart>0)Highlight(g,settings.TargetStart,settings.TargetEnd,left,right,axisY,Color.FromArgb(218,168,62),4*scale);
                using(var ticks=new Pen(Color.FromArgb(142,157,168),scale))
                {
                    g.DrawLine(ticks,left,axisY-5*scale,left,axisY+5*scale);
                    g.DrawLine(ticks,right,axisY-5*scale,right,axisY+5*scale);
                    if(right-left>300*scale && TemplateLength>4)
                        for(int i=1;i<4;i++)
                        {
                            int pos=1+(int)Math.Round((TemplateLength-1)*i/4.0);
                            float x=CoordinateToX(pos,left,right);
                            g.DrawLine(ticks,x,axisY-3*scale,x,axisY+3*scale);
                            CenteredText(g,pos.ToString(),small,Color.DimGray,x,44*scale,left,right,18*scale);
                        }
                }
                DrawText(g,"1  (5′)",small,Color.DimGray,new RectangleF(left,44*scale,80*scale,18*scale),StringAlignment.Near);
                DrawText(g,TemplateLength+"  (3′)",small,Color.DimGray,new RectangleF(Math.Max(left,right-100*scale),44*scale,Math.Min(100*scale,right-left),18*scale),StringAlignment.Far);
                if(snp!=null)
                {
                    float markX=CoordinateToX(snpPosition,left,right);
                    using(var marker=new Pen(SnpColor,scale))
                    {
                        marker.DashStyle=DashStyle.Dash;g.DrawLine(marker,markX,34*scale,markX,184*scale);
                    }
                    CenteredText(g,"SNP "+snpPosition,small,SnpColor,markX,18*scale,left,right,18*scale);
                    Lane(g,small,"F_control →",snp.ControlForward,false,Color.FromArgb(65,100,135),left,right,80*scale,scale);
                    Lane(g,small,"F_ref →",snp.ReferenceForward,false,ForwardColor,left,right,112*scale,scale);
                    Lane(g,small,"F_alt →",snp.AlternateForward,false,Color.FromArgb(168,93,166),left,right,144*scale,scale);
                    Lane(g,small,"R_common ←",snp.CommonReverse,true,ReverseColor,left,right,176*scale,scale);
                }
                else
                {
                    Lane(g,small,"F  5′ → 3′",pair.Forward,false,ForwardColor,left,right,80*scale,scale);
                    Lane(g,small,"R  3′ ← 5′",pair.Reverse,true,ReverseColor,left,right,112*scale,scale);
                }
                float footer=(snp==null?132:196)*scale;
                DrawText(g,ProductSummary(),small,Color.FromArgb(77,94,111),new RectangleF(4*scale,footer,Math.Max(1,Width-8*scale),(snp==null?26:36)*scale),StringAlignment.Near);
            }
        }
        private void Highlight(Graphics g,int start,int end,float left,float right,float y,Color color,float thickness)
        {
            using(var p=new Pen(color,thickness))g.DrawLine(p,CoordinateToX(start,left,right),y,CoordinateToX(end,left,right),y);
        }
        private void Lane(Graphics g,Font font,string label,Primer primer,bool reverse,Color color,float left,float right,float y,float scale)
        {
            float a=CoordinateToX(primer.Start,left,right),b=CoordinateToX(primer.End,left,right);
            DrawText(g,label,font,color,new RectangleF(4*scale,y-9*scale,Math.Max(1,left-12*scale),22*scale),StringAlignment.Near);
            CenteredText(g,Range(primer),font,color,(a+b)/2,y-20*scale,left,right,18*scale);
            float length=b-a,head=Math.Min(7*scale,length*0.6F),half=2*scale;
            using(var brush=new SolidBrush(color))
            {
                if(length<scale)g.FillRectangle(brush,Math.Min(a,right-scale),y-half,scale,half*2);
                else
                {
                    float tail=reverse?b:a,point=reverse?a:b,neck=reverse?a+head:b-head;
                    g.FillPolygon(brush,new PointF[]{new PointF(tail,y-half),new PointF(neck,y-half),new PointF(neck,y-4*scale),new PointF(point,y),new PointF(neck,y+4*scale),new PointF(neck,y+half),new PointF(tail,y+half)});
                }
            }
        }
        private static void CenteredText(Graphics g,string text,Font font,Color color,float center,float y,float left,float right,float height)
        {
            float width=Math.Min(right-left,g.MeasureString(text,font).Width+8);
            float x=Math.Max(left,Math.Min(right-width,center-width/2));
            g.FillRectangle(Brushes.White,x,y,Math.Max(1,width),height);
            DrawText(g,text,font,color,new RectangleF(x,y,Math.Max(1,width),height),StringAlignment.Center);
        }
        private static void DrawText(Graphics g,string text,Font font,Color color,RectangleF bounds,StringAlignment alignment)
        {
            using(var brush=new SolidBrush(color))using(var format=new StringFormat())
            {
                format.Alignment=alignment;format.Trimming=StringTrimming.EllipsisCharacter;
                g.DrawString(text,font,brush,bounds,format);
            }
        }
    }
}
