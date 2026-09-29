using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RpaDesigner
{
    // A sequence is always its own selectable control. Metadata never becomes
    // part of the synthesized oligo or of the SNP character offsets.
    public sealed class ResultDetailsView : UserControl
    {
        private static readonly Color Ink = Color.FromArgb(26,43,62);
        private static readonly Color Teal = Color.FromArgb(0,119,117);
        private readonly Label heading, subheading;
        private readonly TableLayoutPanel root;
        private readonly AmpliconView diagram;
        private readonly LampPositionView lampDiagram;
        private readonly Panel diagramHost;
        private readonly TabControl detailTabs;
        private readonly CardStack primers, products, assessment;
        private bool fittingHeader;
        private bool fittingDiagram;
        private bool lampMode;
        private static readonly float ScreenScale = ReadScreenScale();
        public event Action<string> CopyCompleted;

        private static float ReadScreenScale()
        {
            using(var g=Graphics.FromHwnd(IntPtr.Zero))return Math.Max(1F,g.DpiX/96F);
        }
        private static int Px(int value){return (int)Math.Ceiling(value*ScreenScale);}
        private static int TextHeight(string text,Font font,int width)
        {
            return TextRenderer.MeasureText(text,font,new Size(Math.Max(1,width),Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+Px(3);
        }

        public ResultDetailsView()
        {
            AutoScaleMode=AutoScaleMode.None;BackColor=Color.White;ForeColor=Ink;
            root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,106));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Controls.Add(root);
            heading=new Label {Name="candidateHeading",Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",13,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
            subheading=new Label {Name="candidateSubheading",Dock=DockStyle.Fill,ForeColor=Color.DimGray,TextAlign=ContentAlignment.MiddleLeft};
            diagram=new AmpliconView {Dock=DockStyle.Fill,Margin=Padding.Empty};
            lampDiagram=new LampPositionView {Visible=false,Margin=Padding.Empty};
            diagramHost=new Panel {Name="diagramHost",Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty,BackColor=Color.White};
            diagramHost.Controls.Add(diagram);diagramHost.Controls.Add(lampDiagram);
            diagramHost.Layout+=delegate{FitDiagramContent();};
            detailTabs=new TabControl {Name="detailTabs",Dock=DockStyle.Fill,Padding=new Point(16,7),Margin=Padding.Empty};
            root.Controls.Add(heading,0,0);root.Controls.Add(subheading,0,1);root.Controls.Add(diagramHost,0,2);root.Controls.Add(detailTabs,0,3);
            primers=Page("primerPage","引物清单");products=Page("productPage","扩增产物");assessment=Page("assessmentPage","评价与说明");
            ShowEmpty("等待设计结果","完成设计后，左侧显示候选列表；在此分别查看引物、扩增产物和评价信息。");
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);FitHeader();
        }
        private void FitHeader()
        {
            if(fittingHeader || heading==null || subheading==null || root==null)return;
            fittingHeader=true;
            try
            {
                int width=Math.Max(Px(120),root.ClientSize.Width-Px(12));
                heading.Margin=new Padding(Px(3),0,Px(3),0);subheading.Margin=heading.Margin;
                root.RowStyles[0].Height=Math.Max(Px(32),TextHeight(heading.Text,heading.Font,width)+Px(4));
                root.RowStyles[1].Height=Math.Max(Px(28),TextHeight(subheading.Text,subheading.Font,width)+Px(3));
                if(lampMode)
                {
                    FitDiagramContent();
                    int available=root.ClientSize.Height-(int)root.RowStyles[0].Height-(int)root.RowStyles[1].Height;
                    root.RowStyles[2].Height=lampDiagram.TemplateLength==0?0:Math.Min(Px(lampDiagram.LogicalHeight),Math.Max(Px(100),available-Px(180)));
                }
                else root.RowStyles[2].Height=Px(diagram.LogicalHeight);
                FitDiagramContent();
                detailTabs.Padding=new Point(Px(16),Px(7));
            }
            finally{fittingHeader=false;}
        }
        private void FitDiagramContent()
        {
            if(fittingDiagram||diagramHost==null||lampDiagram==null)return;
            fittingDiagram=true;
            try
            {
                if(!lampMode){diagramHost.AutoScrollMinSize=Size.Empty;return;}
                // Measure wrapped text at the current width. A scrollbar may
                // change that width, so allow one additional measurement pass.
                for(int pass=0;pass<2;pass++)
                {
                    int width=Math.Max(1,diagramHost.ClientSize.Width);
                    lampDiagram.Width=width;
                    int height=lampDiagram.TemplateLength==0?0:Px(lampDiagram.LogicalHeight);
                    lampDiagram.Height=Math.Max(1,height);
                    diagramHost.AutoScrollMinSize=new Size(0,height);
                    if(width==diagramHost.ClientSize.Width)break;
                }
                lampDiagram.Location=diagramHost.AutoScrollPosition;
            }
            finally{fittingDiagram=false;}
        }
        private CardStack Page(string name,string title)
        {
            var page=new TabPage(title) {Name=name,BackColor=Color.FromArgb(244,247,249),Padding=Padding.Empty};
            var stack=new CardStack {Dock=DockStyle.Fill};page.Controls.Add(stack);detailTabs.TabPages.Add(page);return stack;
        }
        private static string N(double v){return v.ToString("0.0");}
        private void ClearCards()
        {
            foreach(var stack in new CardStack[]{primers,products,assessment})stack.ClearCards();
        }
        private void SetLampMode(bool value)
        {
            lampMode=value;diagram.Visible=!value;lampDiagram.Visible=value;
            diagramHost.AutoScrollPosition=Point.Empty;
            if(value)diagram.SetPair(null,null,0);
            else lampDiagram.SetLamp(null,null);
            detailTabs.TabPages[1].Text=value?"靶区模板":"扩增产物";
            FitHeader();
        }
        public void ShowEmpty(string title,string message)
        {
            SetLampMode(false);ClearCards();heading.Text=title;subheading.Text="引物与产物按分区展示";diagram.SetPair(null,null,0);
            detailTabs.SelectedIndex=0;
            primers.AddCard(new InfoCard("引物清单",message));
            products.AddCard(new InfoCard("扩增产物","暂无可展示的扩增产物。"));
            assessment.AddCard(new InfoCard("评价与说明","暂无候选评价。设计完成后可查看结构指标、输入提示和搜索说明。"));
            FitHeader();
        }
        public void ShowNoResults(SnpDesignResult r)
        {
            ShowEmpty("未找到符合条件的候选组","当前条件下没有候选。请在“评价与说明”查看筛选原因，调整参数后重新设计。");
            assessment.ClearCards();InputNotes(r.Input.Reference.Warnings,r.Notes,r.Settings.Base,r.SearchTruncated,true);
            assessment.AddCard(new InfoCard("SNP 设计策略",SnpReportWriter.Strategy(r.Settings.ExtraMismatchFromThreePrime)));
            assessment.AddCard(new InfoCard("如何理解候选",SnpReportWriter.Method));
            assessment.AddCard(new InfoCard("评分依据",SnpReportWriter.Scoring));detailTabs.SelectedIndex=2;
        }
        public void ShowNoResults(DesignResult r)
        {
            ShowEmpty("未找到符合条件的候选引物","当前条件下没有候选。请在“评价与说明”查看筛选原因，调整参数后重新设计。");
            assessment.ClearCards();InputNotes(r.Input.Warnings,r.Notes,r.Settings,r.SearchTruncated);
            assessment.AddCard(new InfoCard("如何理解候选",ReportWriter.Method));detailTabs.SelectedIndex=2;
        }
        public void ShowSnp(SnpPrimerSet set,SnpDesignResult r)
        {
            SetLampMode(false);ClearCards();var input=r.Input;
            heading.Text="候选组 #"+set.Rank+"    ·    排序分数 "+N(set.Score)+"（非选择性）";
            subheading.Text="SNP "+input.Position+" ["+input.ReferenceAllele+">"+input.AlternateAllele+"]    ·    4 条引物 / 3 种独立反应    ·    红色 = SNP · 蓝色 = 人为错配";
            diagram.SetSnp(set,input.Position,input.Reference.Sequence.Length);
            string mismatch=set.ExtraMismatchPosition>0?"附加人为错配：模板第 "+set.ExtraMismatchPosition+" 位 "+set.ExtraMismatchTemplateBase+" → "+set.ExtraMismatchPrimerBase+"（蓝色）":"仅 SNP 末端差异；无附加人为错配。";
            AddPrimer("F_ref","F_ref · "+input.ReferenceAllele+" 等位正向",set.ReferenceForward,input.Position-set.ReferenceForward.Start,mismatch,SnpReportWriter.MismatchIndex(set.ReferenceForward,set));
            AddPrimer("F_alt","F_alt · "+input.AlternateAllele+" 等位正向",set.AlternateForward,input.Position-set.AlternateForward.Start,mismatch,SnpReportWriter.MismatchIndex(set.AlternateForward,set));
            AddPrimer("R_common","R_common · 共用反向",set.CommonReverse,-1,"用于两种等位反应和对照反应；不覆盖当前 SNP。");
            AddPrimer("F_control","F_control · 对照正向",set.ControlForward,-1,"不区分当前 SNP；与 R_common 配对作为独立对照。");
            products.AddCard(new InfoCard("3 种独立反应 · 4 条预期产物序列","两种等位反应分别扩增；同一对照反应列出参考、替代模板的两种产物。以下为发生扩增时的预期正链（5′→3′），含合成引物引入的碱基。"));
            AddProduct("ref","参考等位反应 · "+input.ReferenceAllele,"F_ref + R_common",set.ReferencePair,set.ReferencePair.AmpliconSequence,input.Position,SnpReportWriter.ProductMismatchIndex(set.ReferencePair,set));
            AddProduct("alt","替代等位反应 · "+input.AlternateAllele,"F_alt + R_common",set.AlternatePair,set.AlternatePair.AmpliconSequence,input.Position,SnpReportWriter.ProductMismatchIndex(set.AlternatePair,set));
            AddProduct("control_ref","对照反应 · 参考模板 "+input.ReferenceAllele,"F_control + R_common",set.ControlPair,set.ControlPair.AmpliconSequence,input.Position);
            AddProduct("control_alt","对照反应 · 替代模板 "+input.AlternateAllele,"F_control + R_common",set.ControlPair,set.AlternateControlAmpliconSequence,input.Position);
            AddStructure(new string[]{"F_ref","F_alt","R_common","F_control"},new Primer[]{set.ReferenceForward,set.AlternateForward,set.CommonReverse,set.ControlForward});
            AddReactions(new string[]{"参考等位反应","替代等位反应","共用对照反应"},new PrimerPair[]{set.ReferencePair,set.AlternatePair,set.ControlPair});
            assessment.AddCard(new InfoCard("SNP 设计策略",SnpReportWriter.Strategy(r.Settings.ExtraMismatchFromThreePrime)+"\r\n"+mismatch));
            AddNotes("候选组提示",set.Notes);
            AddPrimerNotes(new string[]{"F_ref","F_alt","R_common","F_control"},new Primer[]{set.ReferenceForward,set.AlternateForward,set.CommonReverse,set.ControlForward});
            AddNotes("参考反应提示",set.ReferencePair.Warnings);AddNotes("替代反应提示",set.AlternatePair.Warnings);AddNotes("对照反应提示",set.ControlPair.Warnings);
            InputNotes(input.Reference.Warnings,r.Notes,r.Settings.Base,r.SearchTruncated,true);
            assessment.AddCard(new InfoCard("如何理解 SNP 候选",SnpReportWriter.Method));
            assessment.AddCard(new InfoCard("评分依据",SnpReportWriter.Scoring));
            FitHeader();
        }
        public void ShowPair(PrimerPair pair,DesignResult r)
        {
            SetLampMode(false);ClearCards();heading.Text="候选 #"+pair.Rank+"    ·    排序分数 "+N(pair.Score)+"（非成功率）";
            subheading.Text="普通 RPA    ·    2 条引物    ·    产物 "+pair.AmpliconLength+" bp    ·    序列均为 5′→3′";
            diagram.SetPair(pair,r.Settings,r.Input.Sequence.Length);
            AddPrimer("F","F · 正向引物",pair.Forward,-1,"与 R 配对扩增。");
            AddPrimer("R","R · 反向引物",pair.Reverse,-1,"已取反向互补；此处显示合成序列。");
            products.AddCard(new InfoCard("预期扩增产物","显示输入正链方向 5′→3′ 的预期产物，包含两端引物结合区。"));
            AddProduct("main","普通 RPA 扩增产物","F + R",pair,pair.AmpliconSequence,0);
            AddStructure(new string[]{"F","R"},new Primer[]{pair.Forward,pair.Reverse});
            AddReactions(new string[]{"F + R"},new PrimerPair[]{pair});
            AddPrimerNotes(new string[]{"F","R"},new Primer[]{pair.Forward,pair.Reverse});AddNotes("配对提示",pair.Warnings);
            InputNotes(r.Input.Warnings,r.Notes,r.Settings,r.SearchTruncated);
            assessment.AddCard(new InfoCard("如何理解候选",ReportWriter.Method));
            assessment.AddCard(new InfoCard("评分依据",ReportWriter.Scoring));
            FitHeader();
        }
        public void ShowNoResults(LampDesignResult r)
        {
            ShowEmpty("未找到符合条件的 LAMP 候选组","当前条件下没有候选。请在“评价与说明”查看筛选原因，调整参数后重新设计。");
            SetLampMode(true);subheading.Text=LampReportWriter.ModeName(r)+" · 六区段组合搜索";
            products.ClearCards();products.AddCard(new InfoCard("靶区模板","没有候选组，因此暂无 F3 至 B3 的靶区模板区间。"));
            assessment.ClearCards();LampInputNotes(r);
            detailTabs.SelectedIndex=2;FitHeader();
        }
        public void ShowLamp(LampPrimerSet set,LampDesignResult r)
        {
            SetLampMode(true);ClearCards();var oligos=LampReportWriter.Oligos(set);
            lampDiagram.SetLamp(set,r);
            heading.Text=(r.Snp==null?"LAMP":r.Settings.SnpMethod)+" 候选组 #"+set.Rank+"    ·    排序分数 "+N(set.Score)+"（非成功率/选择性）";
            subheading.Text=LampReportWriter.ModeName(r)+(r.Snp==null?"":(" · SNP "+r.Snp.Position+" ["+r.Snp.ReferenceAllele+">"+r.Snp.AlternateAllele+"] · "+(LampReportWriter.IsPa(r)?"BIP RNA/C3 激活":set.SpecificInner+" 末端区分")+" · 红色 = SNP"))+(LampReportWriter.IsMLamp(r)?" · 蓝色 = 人为错配":"")+"    ·    "+oligos.Count+" 条订购引物";
            if(LampReportWriter.IsPa(r))primers.AddCard(new InfoCard("PA-LAMP 修饰与订购","[rA/rC/rG/rU] 表示单个 RNA，[C3] 表示 3′ C3 封闭；需 RNase H2 激活。复制按钮保留完整修饰标记。FASTA 序列行为 DNA 等效序列，订购请使用卡片或 TXT/CSV/HTML 中的修饰序列。"));
            primers.AddCard(new InfoCard(r.Snp==null?"此组 LAMP 反应":"两种独立等位反应",LampReportWriter.Reactions(set,r)));
            foreach(LampOligo p in oligos)
            {
                string name=LampReportWriter.OligoName(p,set,r);
                primers.AddCard(new SequenceCard("primer_"+name,name+"    5′→3′",LampReportWriter.OligoMetadata(p),p.OrderingSequence,p.OrderingSnpIndex,false,LampReportWriter.OligoNote(p,set,r),CopySequence,LampReportWriter.MismatchIndex(p,set,r)));
            }
            products.AddCard(new InfoCard("靶区模板与 LAMP 产物","以下显示输入正链中 F3 至 B3 的原始靶区模板，便于检查区段位置。LAMP 形成茎环及串联、分支产物，此处不作为固定长度的最终扩增产物。附加人为错配没有写入原始模板。"));
            products.AddCard(new InfoCard("六区段布局",LampReportWriter.LayoutText(set)));
            string metadata="正链坐标 "+set.SpanStart+"–"+set.SpanEnd+"    ·    "+set.SpanLength+" nt    ·    5′→3′";
            int snpIndex=r.Snp==null?-1:r.Snp.Position-set.SpanStart;
            string note=r.Snp==null?"原始输入模板区间。":"红色为 SNP（此区间第 "+(snpIndex+1)+" 位）；未加入人为错配。";
            products.AddCard(new SequenceCard("template_ref","靶区模板 · "+(r.Snp==null?"输入":"参考等位基因 "+r.Snp.ReferenceAllele),metadata,set.ReferenceTemplate,snpIndex,true,note,CopySequence));
            if(r.Snp!=null)products.AddCard(new SequenceCard("template_alt","靶区模板 · 替代等位基因 "+r.Snp.AlternateAllele,metadata,set.AlternateTemplate,snpIndex,true,note,CopySequence));
            var table=new MetricCard(LampReportWriter.IsPa(r)?"前体 DNA 等效结构指标（nt）":"完整订购引物结构指标（nt）",new string[]{"引物","发卡茎","自互补","自身 3′","串联重复"},oligos.Count);
            foreach(LampOligo p in oligos)
            {
                if(p.Metrics==null)continue;
                var m=p.Metrics;string name=LampReportWriter.OligoName(p,set,r);
                table.AddRow(name,m.Hairpin,m.SelfComplement,m.SelfThreePrime,m.TandemRepeat);
                AddNotes(name+" 提示",m.Warnings);
            }
            assessment.AddCard(table);AddNotes("候选组提示",set.Notes);
            if(set.ExtraMismatchPosition>0)assessment.AddCard(new InfoCard("附加人为错配",LampReportWriter.MismatchText(set,r)));
            LampInputNotes(r);FitHeader();
        }
        private void LampInputNotes(LampDesignResult r)
        {
            AddNotes("输入提示",r.Input.Warnings);AddNotes("搜索与筛选说明",r.Notes);
            assessment.AddCard(new InfoCard("本次 LAMP 参数",LampReportWriter.Params(r.Settings)+"\n搜索裁剪："+(r.SearchTruncated?"是（有限搜索）":"否")+"。"));
            assessment.AddCard(new InfoCard("评价依据",LampReportWriter.ScoringFor(r)));
            assessment.AddCard(new InfoCard("扩增方式与设计说明",LampReportWriter.MethodFor(r)));
        }
        private void AddPrimer(string key,string title,Primer primer,int snpIndex,string note,int mismatchIndex=-1)
        {
            string meta=primer.Sequence.Length+" nt    GC "+N(primer.Gc)+"%    Tm ≈ "+N(primer.Tm)+" °C    坐标 "+primer.Start+"–"+primer.End;
            primers.AddCard(new SequenceCard("primer_"+key,title+"    5′→3′",meta,primer.Sequence,snpIndex,false,note,CopySequence,mismatchIndex));
        }
        private void AddProduct(string key,string title,string reaction,PrimerPair pair,string sequence,int snp,int mismatchIndex=-1)
        {
            string meta=reaction+"    ·    "+pair.AmpliconLength+" bp    ·    坐标 "+pair.AmpliconStart+"–"+pair.AmpliconEnd+"    ·    5′→3′";
            string note=snp>0?"红色为 SNP（产物第 "+(snp-pair.AmpliconStart+1)+" 位）。":"包含两端引物结合区。";
            if(mismatchIndex>=0)note+=" 蓝色为合成引物引入的碱基（产物第 "+(mismatchIndex+1)+" 位）。";
            products.AddCard(new SequenceCard("product_"+key,title,meta,sequence,snp>0?snp-pair.AmpliconStart:-1,true,note,CopySequence,mismatchIndex));
        }
        private void CopySequence(RichTextBox sequence,Button button)
        {
            try
            {
                var data=new DataObject();data.SetData(DataFormats.UnicodeText,sequence.Text);data.SetData(DataFormats.Rtf,sequence.Rtf);Clipboard.SetDataObject(data,true);
                if(CopyCompleted!=null)CopyCompleted("已复制此条完整序列（5′→3′）；支持富文本的编辑器可保留序列颜色标记");
            }
            catch(Exception ex){MessageBox.Show(FindForm(),ex.Message,"复制失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        private void AddStructure(string[] names,Primer[] values)
        {
            var table=new MetricCard("单引物结构指标（nt）",new string[]{"引物","发卡茎","自互补","自身 3′","串联重复"},values.Length);
            for(int i=0;i<values.Length;i++){var p=values[i];table.AddRow(names[i],p.Hairpin,p.SelfComplement,p.SelfThreePrime,p.TandemRepeat);}
            assessment.AddCard(table);
        }
        private void AddReactions(string[] names,PrimerPair[] pairs)
        {
            var table=new MetricCard("反应配对指标（互补长度单位：nt）",new string[]{"独立反应","产物 bp","连续互补","3′ 互补"},pairs.Length);
            for(int i=0;i<pairs.Length;i++){var p=pairs[i];table.AddRow(names[i],p.AmpliconLength,p.CrossComplement,p.CrossThreePrime);}
            assessment.AddCard(table);
        }
        private void AddPrimerNotes(string[] names,Primer[] values){for(int i=0;i<values.Length;i++)AddNotes(names[i]+" 提示",values[i].Warnings);}
        private void AddNotes(string title,List<string> values)
        {
            if(values.Count>0)assessment.AddCard(new InfoCard(title,String.Join("\r\n",values.ToArray())));
        }
        private void InputNotes(List<string> warnings,List<string> notes,DesignSettings s,bool truncated,bool snp=false)
        {
            AddNotes("输入提示",warnings);AddNotes("搜索与筛选说明",notes);
            assessment.AddCard(new InfoCard("本次参数",ReportWriter.Params(s,snp)+"\r\n靶区："+(s.TargetStart>0?s.TargetStart+"–"+s.TargetEnd:"未单独指定")+"；搜索裁剪："+(truncated?"是（有限搜索）":"否")+"。\r\n坐标均为输入正链 1-based 闭区间。Tm 是经验估算，不是 RPA 反应温度；结构指标为连续互补启发式。"));
        }

        private sealed class CardStack : FlowLayoutPanel
        {
            public CardStack(){AutoScroll=true;FlowDirection=FlowDirection.TopDown;WrapContents=false;Padding=new Padding(10);Margin=Padding.Empty;BackColor=Color.FromArgb(244,247,249);}
            public void AddCard(Control card){card.Margin=new Padding(0,0,0,Px(10));Controls.Add(card);FitCards();}
            public void ClearCards(){SuspendLayout();try{while(Controls.Count>0){var c=Controls[0];Controls.RemoveAt(0);c.Dispose();}AutoScrollPosition=Point.Empty;}finally{ResumeLayout();}}
            protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);FitCards();}
            private void FitCards(){Padding=new Padding(Px(10));int width=Math.Max(Px(100),ClientSize.Width-Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-Px(2));foreach(Control c in Controls)c.Width=width;}
        }
        private sealed class SequenceCard : Panel
        {
            private readonly Label title,meta,note;
            private readonly Button copy;
            private readonly RichTextBox sequence;
            private readonly bool product;
            public SequenceCard(string name,string heading,string metadata,string text,int snpIndex,bool isProduct,string footnote,Action<RichTextBox,Button> onCopy,int mismatchIndex=-1)
            {
                product=isProduct;BackColor=Color.White;BorderStyle=BorderStyle.FixedSingle;Padding=new Padding(Px(12));
                title=new Label {Text=heading,ForeColor=Teal,Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold)};
                meta=new Label {Text=metadata,ForeColor=Ink};note=new Label {Text=footnote,ForeColor=Color.DimGray,Font=new Font("Microsoft YaHei UI",9F)};
                copy=new Button {Text="复制序列",FlatStyle=FlatStyle.Flat,BackColor=Color.White,Cursor=Cursors.Hand,TabStop=true};copy.FlatAppearance.BorderColor=Color.FromArgb(215,225,230);
                sequence=new RichTextBox {Name=name,AccessibleName=heading+"序列",ReadOnly=true,WordWrap=true,DetectUrls=false,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(246,249,250),ForeColor=Ink,Font=new Font("Consolas",12F),ScrollBars=RichTextBoxScrollBars.Vertical,HideSelection=false};
                var highlighted=new HighlightedReport {Text=text};if(snpIndex>=0)highlighted.SnpHighlights.Add(new ReportHighlight {Start=snpIndex,Length=1});
                if(mismatchIndex>=0)highlighted.MismatchHighlights.Add(new ReportHighlight {Start=mismatchIndex,Length=1});
                MainForm.ApplyHighlights(sequence,highlighted,"");
                copy.Click+=delegate{onCopy(sequence,copy);};
                Controls.Add(title);Controls.Add(meta);Controls.Add(sequence);Controls.Add(note);Controls.Add(copy);
                Width=Px(700);PerformLayout();
            }
            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);if(title==null)return;
                Padding=new Padding(Px(12));int pad=Padding.Left,w=Math.Max(Px(80),ClientSize.Width-Padding.Horizontal);
                Size buttonText=TextRenderer.MeasureText(copy.Text,copy.Font);
                int buttonWidth=Math.Max(Px(96),buttonText.Width+Px(24)),buttonHeight=Math.Max(Px(32),buttonText.Height+Px(12));
                int titleWidth=w-buttonWidth-Px(12),y;
                if(titleWidth<Px(130))
                {
                    int titleHeight=TextHeight(title.Text,title.Font,w);
                    title.SetBounds(pad,pad,w,titleHeight);copy.SetBounds(pad,pad+titleHeight+Px(5),Math.Min(w,buttonWidth),buttonHeight);
                    y=copy.Bottom+Px(7);
                }
                else
                {
                    int headerHeight=Math.Max(buttonHeight,TextHeight(title.Text,title.Font,titleWidth));
                    title.SetBounds(pad,pad,titleWidth,headerHeight);copy.SetBounds(pad+w-buttonWidth,pad,buttonWidth,buttonHeight);
                    y=pad+headerHeight+Px(5);
                }
                int metaHeight=TextHeight(meta.Text,meta.Font,w);
                meta.SetBounds(pad,y,w,metaHeight);y+=metaHeight+Px(7);
                int charWidth=Math.Max(1,TextRenderer.MeasureText("ACGTACGTAC",sequence.Font,new Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPadding).Width/10);
                int columns=Math.Max(1,(w-SystemInformation.VerticalScrollBarWidth-Px(8))/charWidth);
                int lines=(sequence.TextLength+columns-1)/columns;
                int sequenceHeight=sequence.Font.Height*(product?4:Math.Max(1,lines))+Px(10);
                sequence.SetBounds(pad,y,w,sequenceHeight);y+=sequenceHeight+Px(7);
                int noteHeight=TextHeight(note.Text,note.Font,w);
                note.SetBounds(pad,y,w,noteHeight);int height=y+noteHeight+pad+2;if(Height!=height)Height=height;
            }
        }
        private sealed class InfoCard : Panel
        {
            private readonly Label title,body;
            public InfoCard(string heading,string text)
            {
                BackColor=Color.White;BorderStyle=BorderStyle.FixedSingle;Padding=new Padding(Px(12));
                title=new Label {Text=heading,Font=new Font("Microsoft YaHei UI",10F,FontStyle.Bold),ForeColor=Ink};
                body=new Label {Text=text,ForeColor=Color.FromArgb(85,100,114)};
                Controls.Add(title);Controls.Add(body);Width=Px(700);PerformLayout();
            }
            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);if(title==null)return;Padding=new Padding(Px(12));int w=Math.Max(Px(80),ClientSize.Width-Padding.Horizontal),pad=Padding.Left;
                int titleHeight=TextHeight(title.Text,title.Font,w);
                int bodyHeight=TextHeight(body.Text,body.Font,w)+Px(3);
                title.SetBounds(pad,pad,w,titleHeight);body.SetBounds(pad,pad+titleHeight+Px(5),w,bodyHeight);
                int height=pad*2+titleHeight+Px(5)+bodyHeight+2;if(Height!=height)Height=height;
            }
        }
        private sealed class MetricCard : Panel
        {
            private readonly DataGridView table;
            private readonly Label title;
            private bool fitting;
            public MetricCard(string heading,string[] columns,int rows)
            {
                BackColor=Color.White;BorderStyle=BorderStyle.FixedSingle;Padding=new Padding(Px(12));
                title=new Label {Text=heading,Font=new Font("Microsoft YaHei UI",10F,FontStyle.Bold)};
                table=new DataGridView {ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EnableHeadersVisualStyles=false,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,ScrollBars=ScrollBars.None};
                table.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(234,242,244);table.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;
                table.DefaultCellStyle.WrapMode=DataGridViewTriState.True;table.DefaultCellStyle.Padding=new Padding(Px(3));
                table.DefaultCellStyle.SelectionBackColor=Color.FromArgb(222,241,238);table.DefaultCellStyle.SelectionForeColor=Ink;
                foreach(string name in columns){int i=table.Columns.Add(name,name);table.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}
                table.Columns[0].FillWeight=150;Controls.Add(table);Controls.Add(title);Width=Px(700);PerformLayout();
            }
            public void AddRow(params object[] values){table.Rows.Add(values);table.ClearSelection();PerformLayout();}
            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);if(table==null || title==null || fitting)return;fitting=true;
                try
                {
                    Padding=new Padding(Px(12));int pad=Padding.Left,w=Math.Max(Px(80),ClientSize.Width-Padding.Horizontal);
                    int titleHeight=TextHeight(title.Text,title.Font,w);
                    title.SetBounds(pad,pad,w,titleHeight);table.SetBounds(pad,pad+titleHeight+Px(7),w,Math.Max(Px(32),table.Height));
                    int headerHeight=Px(32);
                    foreach(DataGridViewColumn col in table.Columns)
                        headerHeight=Math.Max(headerHeight,TextHeight(col.HeaderText,table.Font,Math.Max(1,col.Width-Px(12)))+Px(10));
                    table.ColumnHeadersHeight=headerHeight;
                    int tableHeight=headerHeight+2;
                    foreach(DataGridViewRow row in table.Rows)
                    {
                        int rowHeight=Px(32);
                        foreach(DataGridViewCell cell in row.Cells)
                            rowHeight=Math.Max(rowHeight,TextHeight(Convert.ToString(cell.Value),table.Font,Math.Max(1,cell.OwningColumn.Width-Px(12)))+Px(10));
                        row.Height=rowHeight;tableHeight+=rowHeight;
                    }
                    table.Height=tableHeight;int height=table.Bottom+pad+2;if(Height!=height)Height=height;
                }
                finally{fitting=false;}
            }
        }
    }
}
