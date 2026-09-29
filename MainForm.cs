using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RpaDesigner
{
    public sealed partial class MainForm : Form
    {
        private readonly Color ink = Color.FromArgb(26,43,62);
        private readonly Color teal = Color.FromArgb(0,119,117);
        private TextBox sequenceBox;
        private ResultDetailsView detailsView;
        private Label inputInfo, status, resultInfo;
        private TabControl tabs;
        private DataGridView grid;
        private Label preferredProductLabel;
        private NumericUpDown primerMin, primerMax, gcMin, gcMax, productMin, productMax, productIdeal, pairCount, targetStart, targetEnd;
        private CheckBox targetCheck;
        private ComboBox designMode, mismatchMode;
        private Label modeHint;
        private Button design, cancel, export, copy;
        private ProgressBar progress;
        private Control inputPanel;
        private DesignResult result;
        private SnpDesignResult snpResult;
        private CancellationTokenSource cancellation;
        private bool initializing = true;
        private bool busy;

        public MainForm()
        {
            SuspendLayout();
            // All controls below are created in 96-DPI logical pixels. Apply one
            // explicit geometry scale after the complete tree exists; automatic
            // scaling during programmatic construction can miss later children.
            AutoScaleMode = AutoScaleMode.None;
            Text = "RPA / LAMP 引物设计助手 · v0.19";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(244,247,249);
            ForeColor = ink;
            ClientSize = new Size(1190,850);
            MinimumSize = new Size(1060,780);
            StartPosition = FormStartPosition.CenterScreen;
            var shell = new TableLayoutPanel { Dock=DockStyle.Fill, RowCount=3, ColumnCount=1, Padding=new Padding(18,12,18,10) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
            Controls.Add(shell);
            var header = new TableLayoutPanel {Name="appHeader",Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,RowCount=2,ColumnCount=2,Padding=new Padding(0,0,0,8)};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = new Label {Name="appTitle",Text="RPA / LAMP  引物设计助手",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,18,FontStyle.Bold),ForeColor=ink};
            header.Controls.Add(title,0,0);
            header.Controls.Add(new Label {Text="粘贴序列 → 设置扩增范围 → 比较候选引物",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F),ForeColor=Color.FromArgb(90,106,120),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(3,4,3,0)},0,1);
            header.Controls.Add(new Label {Name="versionLabel",Text="本地设计  /  v0.19",AutoSize=true,Dock=DockStyle.Fill,ForeColor=teal,TextAlign=ContentAlignment.MiddleRight,Margin=new Padding(14,0,3,0)},1,0);
            shell.Controls.Add(header,0,0);
            tabs = new TabControl {Dock=DockStyle.Fill,Padding=new Point(18,7)};
            shell.Controls.Add(tabs,0,1);
            BuildInput(); BuildResults(); BuildHelp(); BuildBlast();
            var footer = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};
            footer.RowCount=1;footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,200));
            status = new Label {Text="准备就绪 · 本地设计；在线 BLAST 需另行确认",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(90,106,120)};
            progress = new ProgressBar {Dock=DockStyle.Fill,Minimum=0,Maximum=100,Margin=new Padding(6,11,0,9)};
            footer.Controls.Add(status,0,0);footer.Controls.Add(progress,1,0);shell.Controls.Add(footer,0,2);
            FormClosing += delegate { if(cancellation!=null)cancellation.Cancel();if(blastCancellation!=null)blastCancellation.Cancel(); };
            ScaleForDisplay();
            ResumeLayout(true);
            inputPanel.Parent.PerformLayout();
            initializing=false;
        }
        private void ScaleForDisplay()
        {
            using(var graphics=CreateGraphics())
            {
                float scale=graphics.DpiX/96F;
                if(Math.Abs(scale-1F)>0.001F)Scale(new SizeF(scale,graphics.DpiY/96F));
            }
            Rectangle area=Screen.FromControl(this).WorkingArea;
            Size limit=new Size(Math.Max(640,area.Width-24),Math.Max(480,area.Height-24));
            MinimumSize=new Size(Math.Min(MinimumSize.Width,limit.Width),Math.Min(MinimumSize.Height,limit.Height));
            Size=new Size(Math.Min(Width,limit.Width),Math.Min(Height,limit.Height));
            StartPosition=FormStartPosition.Manual;
            Location=new Point(area.Left+Math.Max(0,(area.Width-Width)/2),area.Top+Math.Max(0,(area.Height-Height)/2));
        }
        private Button ButtonFor(string text, EventHandler action)
        {
            var b = new Button { Text=text,AutoSize=true,Height=32,MinimumSize=new Size(95,32),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(0,0,10,0),Padding=new Padding(8,2,8,2),Cursor=Cursors.Hand};
            b.FlatAppearance.BorderColor=Color.FromArgb(209,219,225);b.Click+=action;return b;
        }
        private NumericUpDown Number(int value,int min,int max)
        {
            var n=new NumericUpDown {Minimum=min,Maximum=max,Value=value,Dock=DockStyle.Fill,Margin=new Padding(0,3,0,3),ThousandsSeparator=false};
            n.ValueChanged += delegate { InputChanged(); };return n;
        }
        private void Field(TableLayoutPanel table,string name,Control c,int col,int row)
        {
            table.Controls.Add(new Label {Text=name,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(2,0,0,0)},col,row);table.Controls.Add(WithUnlimitedOption(c,name),col+1,row);
        }
        private bool IsSnpMode { get { return designMode!=null && (designMode.SelectedIndex==1 || designMode.SelectedIndex>=3); } }
        private void BuildResults()
        {
            var page=new TabPage("02  候选结果") {BackColor=Color.White,Padding=new Padding(12)};tabs.TabPages.Add(page);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,36));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.Controls.Add(root);
            resultInfo=new Label {Text="设计完成后，在左侧选择候选，右侧查看分区详情。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};root.Controls.Add(resultInfo,0,0);
            var split=new SplitContainer {Name="resultSplit",Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,SplitterWidth=8,Size=new Size(1050,520),Panel1MinSize=210,Panel2MinSize=580,SplitterDistance=235};root.Controls.Add(split,0,1);
            var candidatePanel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
            candidatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            candidatePanel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));candidatePanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));candidatePanel.RowStyles.Add(new RowStyle(SizeType.Absolute,52));split.Panel1.Controls.Add(candidatePanel);
            candidatePanel.Controls.Add(new Label {Text="候选列表",Dock=DockStyle.Fill,Font=new Font(Font,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
            grid=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.FixedSingle,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EnableHeadersVisualStyles=false};
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(234,242,244);grid.ColumnHeadersDefaultCellStyle.ForeColor=ink;grid.ColumnHeadersHeight=44;grid.RowTemplate.Height=42;
            grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(222,241,238);grid.DefaultCellStyle.SelectionForeColor=ink;grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,251);
            string[] names={"候选","分数*","产物 bp"};
            foreach(string name in names) {int i=grid.Columns.Add(name,name);grid.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}
            grid.Columns[0].FillWeight=60;grid.Columns[1].FillWeight=65;grid.Columns[2].FillWeight=110;
            grid.CurrentCellChanged+=delegate {ShowPair();};candidatePanel.Controls.Add(grid,0,1);
            candidatePanel.Controls.Add(new Label {Text="* 分数用于候选排序\r\n不代表扩增成功率或 SNP 选择性",Dock=DockStyle.Fill,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9F),TextAlign=ContentAlignment.MiddleLeft},0,2);
            detailsView=new ResultDetailsView {Dock=DockStyle.Fill,Margin=Padding.Empty};split.Panel2.Controls.Add(detailsView);
            detailsView.CopyCompleted+=delegate(string message){status.Text=message;};
            var tools=new FlowLayoutPanel {Name="resultActions",Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(0,8,0,0),WrapContents=true};
            copy=ButtonFor("复制选中引物",CopyPair);copy.Enabled=false;tools.Controls.Add(copy);
            export=ButtonFor("导出结果…",Export);export.Enabled=false;tools.Controls.Add(export);
            var blastLink=ButtonFor("BLAST 特异性…",delegate{tabs.SelectedTab=blastPage;LoadBlastCandidate();});blastLink.Name="openBlast";tools.Controls.Add(blastLink);
            tools.Controls.Add(new Label {Text="HTML 彩色报告 / CSV / TXT / FASTA",AutoSize=true,Margin=new Padding(10,8,0,0),ForeColor=Color.DimGray});root.Controls.Add(tools,0,2);
        }
        private void BuildHelp()
        {
            var page=new TabPage("03  使用与依据") {BackColor=Color.White,Padding=new Padding(22)};tabs.TabPages.Add(page);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,36));page.Controls.Add(root);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var help = new TextBox {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Color.White,Font=new Font("Microsoft YaHei UI",11)};
            help.Text="从序列开始\r\n\r\n1. 粘贴序列，或导入一条 FASTA / TXT。可先载入随机演示序列了解操作。\r\n2. 设置引物长度和产物范围。若要覆盖 SNP 或已有靶点，勾选“包围指定靶区”，填写输入正链上的坐标；普通 RPA 模式不用于等位基因判别；SNP 请选择专用模式。\r\n3. 点击“开始设计”，在候选结果中比较不同区域的引物对。\r\n4. 复制引物，或导出 CSV（Excel 可打开）、TXT（完整参数与说明）、FASTA。\r\n\r\n默认参数依据\r\n\r\nTwistAmp Assay Design Manual（INASDM Rev 1，§2）建议 30–35 nt 引物、30–70% GC 和 100–200 bp 产物。大于 500 bp 应结合体系另行评估。参数范围只是候选筛选依据，不能保证反应性能。\r\n\r\n输入约定\r\n\r\n空白和数字行号会被移除；U 转为 T 并提示。IUPAC 歧义位点保留原始坐标，含歧义位点的引物窗口被跳过；产物内部可能保留歧义。每次只接受一条序列。输入为线性模板，长度上限 20,000 nt。\r\n\r\n如何理解结果\r\n\r\n" + ReportWriter.Method + "\r\n\r\n连续同聚物超过 5 nt 的窗口会被排除；评分偏好较少重复和互补的候选。分数越高越优先筛选。长序列搜索会按区域保留候选并裁剪配对，界面和完整报告会提示，因此不是所有组合的穷举最优解。具体权重和筛选记录见程序附带说明及每次 TXT 报告。\r\n\r\n引物设计在本机运行。可选 NCBI 在线 BLAST 仅在启用并确认上传后发送候选序列与联系邮箱，不上传完整模板或原始标题。BLAST 结果单独查看和导出；设计报告本身不包含数据库特异性验证结论。";
            help.Text=help.Text.Replace("普通 RPA 模式不用于等位基因判别；SNP 请选择专用模式。","该选项只包围区域；SNP 候选请使用 SNP 模式。").Replace("如何理解结果","如何理解普通扩增结果");
            help.Text+="\r\n\r\n分区结果界面（v0.4）\r\n\r\n左侧点击候选组；右侧位置图和内容同步切换。引物清单中每条引物独立展示序列、坐标、长度、GC 和 Tm；扩增产物页分别列出各反应的完整产物；评价与说明页查看结构指标、错配策略和筛选提示。卡片上的“复制序列”仅复制这一条完整序列；底部按钮复制整组引物。切换候选时保留当前详情页，方便比较。";
            help.Text+="\r\n\r\nSNP 红色标记（v0.3）\r\n\r\n结果中的 SNP 碱基显示为红色：包括两条等位正向引物及全部四种产物序列。共用引物不覆盖 SNP，因此不标红；人为附加错配另列位置和替换碱基；文本报告中也用 * 注释，不混作 SNP。导出选择 HTML 可保存红色标记，浏览器可直接打开。TXT、CSV、FASTA 不支持字符颜色，仍为纯文本。复制所选引物同时提供纯文本和富文本；粘贴到支持富文本的编辑器并保留源格式时可显示红色。";
            help.Text+="\r\n\r\nSNP 选择性扩增模式（v0.2）\r\n\r\n选择“RPA SNP 选择性扩增”，把一处 SNP 写为 [参考碱基>替代碱基]，例如 [A>C] 或 [G>T]。支持 A/C/G/T 中任意两种不同碱基；标记前后分别为用户定义的参考与替代等位基因，不自动代表正常或致病。标记占一个碱基，坐标自动计算，无需填写靶区。\r\n每组输出 F_ref、F_alt、R_common、F_control 四条引物。两条等位正向引物 3′ 最末端位于 SNP；共用引物避开该 SNP。输入必须包含足够上下游序列。\r\n默认不加人为错配；可选倒数第 2 或第 3 位附加错配探索。在该位点枚举三种其他 DNA 碱基，按序列质量排序；不是指定其中某一种必然最有选择性。引物替换的碱基会进入相应预期产物。\r\n\r\n"+SnpReportWriter.Method+"\r\n\r\n详细研究依据与软件范围见随附使用说明。";
            help.Text="PA-LAMP 引物激活型扩增\r\n\r\n"+LampReportWriter.PaMethod+"\r\n\r\n"+LampReportWriter.PaScoring+"\r\n原始研究："+LampReportWriter.PaEvidenceUrl+"\r\n\r\n普通 LAMP / AS-LAMP / PA-LAMP\r\n\r\n在“设计模式”选择普通 LAMP、AS-LAMP 或 PA-LAMP。普通 LAMP 每组含 F3、B3、FIP、BIP 四条核心引物；勾选环引物后，尝试增加合适的 LF / LB，找不到环引物时仍可输出核心组。FIP 按 F1c + F2 拼接，BIP 按 B1c + B2 拼接，订购序列不含分隔符。\r\n\r\nAS-LAMP 使用同样的 [参考碱基>替代碱基] 输入方式。可自动比较 FIP / BIP，或指定一种方向；每组有两条等位内引物及共用引物。两种等位反应分开使用，每种反应只用相应的一条等位内引物。BIP 为反向引物时，其 SNP 末端显示输入等位碱基的互补碱基。人为错配不是 SNP，另行注明。LAMP 模式不额外生成 RPA 式的对照正向引物。\r\n\r\n长度参数针对单个结合片段，不是拼接后的整条 FIP / BIP。Tm 按 F3、B3、F2、B2、F1c、B1c、LF、LB 分别设置；每项上下限可独立取消。F2c 是 F2 的互补区，相同条件的完全匹配双链模型下 Tm 相同。FIP/BIP 不使用整条拼接序列的单一 Tm。LAMP 页面展示 F3 至 B3 的原始靶区模板，两种等位分别标红；LAMP 实际会形成茎环及串联重复产物，没有一条固定长度的完整产物序列。人为错配不改写这里展示的原始模板。\r\n\r\n"+LampReportWriter.Method+"\r\n\r\n"+LampReportWriter.Scoring+"\r\n\r\n官方设计说明："+LampReportWriter.ManualUrl+"\r\nAS-LAMP 原始研究："+LampReportWriter.EvidenceUrl+"\r\n\r\n──────── RPA 使用说明 ────────\r\n\r\n"+help.Text;
            help.Text="mLAMP 人工错配型扩增（Ren 2019）\r\n\r\n"+LampReportWriter.MLampMethod+"\r\n\r\n选择“mLAMP · 人工错配型（Ren 2019）”，以 [参考碱基>替代碱基] 标注一处 SNP。SNP 判别方向固定为 FIP，SNP 对应 F2 的 3′ 最末位。SNP 策略默认倒数第 3 位人工错配；可切换为无人工错配（论文对照）或倒数第 2 位（论文比较）。程序在所选位点枚举其余三种 DNA 碱基并排序，具体替换碱基仍需实验比较。\r\n默认每个反应使用 FIP、BIP、F3、B3 四条核心引物，两种等位反应分开。可手动勾选环引物，属于文献外扩展。恢复 LAMP 默认会恢复 FIP、倒数第 3 位人工错配和不加环引物；离开此模式会还原进入前的错配、环引物及可选方向设置。\r\n原始研究："+LampReportWriter.MLampEvidenceUrl+"\r\n\r\n"+help.Text;
            help.Text="RPA SNP / mLAMP 序列颜色（v0.17）\r\n\r\n红色为 SNP 碱基，蓝色为人为错配碱基。引物卡片、单条及整组富文本复制、HTML 报告均保留颜色；无人工错配方案不显示蓝色。RPA 两种等位反应产物中由引物引入的对应碱基也标蓝；对照产物和原始模板不标蓝。TXT / CSV / FASTA 不保存颜色。\r\n\r\n"+help.Text;
            help.Text="筛选参数无限制（v0.17）\r\n\r\n勾选某个数字旁的“无限制”，即可关闭该项筛选；取消勾选后恢复原数值。上下限可分别选择。RPA 的偏好产物长度选择无限制时，不再按产物长度偏好评分。候选数量保持现有设置。恢复默认参数会取消无限制。\r\n无限制不取消模板边界、LAMP 六区顺序及间距、结构筛选或方法构型。放开长度后程序可能采样搜索，并在结果中注明；不保证穷举所有组合。靶区坐标继续通过“包围指定靶区”开关控制，PA 尾部长度和 SNP 策略继续按所选方法设置。\r\n\r\n"+help.Text;
            help.Text="可选 NCBI 在线 BLAST（v0.17）\r\n\r\n完成设计并选中一组候选后，点击“BLAST 特异性…”或打开第 04 页。默认关闭；勾选启用、填写联系邮箱、选择数据库后，提交前会列出实际上传的匿名 FASTA，须确认才发送。仅上传当前候选的引物/结合区段，不上传完整模板或原始 FASTA 标题。\r\n默认检索 RefSeq 参考基因组全部物种。预期登录号仅作本地标注，不限制搜索；同一染色体仍需核对具体坐标。覆盖率、一致率与组合跨度是本地解释阈值。LAMP FIP/BIP 分区比对，PA-LAMP 查询切后有效 DNA，不评估 RNase H2 或 C3。\r\n结果包含单条命中和位置关系相容的候选组合，不能作为特异性通过证明。失败、超时、缺失查询及无命中分别提示。可导出完整 TXT、命中 CSV 和原始 XML；这些结果与原设计报告分开。取消只停止本地等待，不能撤回已提交数据。\r\n\r\n"+help.Text;
            help.Text="默认参数与区段 Tm\r\n\r\nLAMP 当前默认是较宽的候选搜索范围：各结合片段 18–27 nt、GC 35–70%；F3 / B3 / F2 / B2 各为 55–65 °C，F1c / B1c / LF / LB 各为 60–70 °C；F2..B2 跨度 110–190 bp、F3..B3 跨度 120–300 bp（均含两端）。各区段 Tm 可独立修改；排序仍偏好前四区段接近 60 °C、后四区段接近 65 °C。\r\nPrimerExplorer V5 的较窄参考目标为前四区段 59–61 °C、后四区段 64–66 °C、GC 40–65% 和 F2..B2 跨度 120–160 bp。由于 Tm 计算模型和位点序列的差异，把这些目标全部当成硬性筛选边界容易漏掉可供实验筛选的候选。\r\n参考计算条件：Na⁺ 50 mM、Mg²⁺ 4 mM、寡核苷酸 100 nM。Mg²⁺ 按网站公式换算等效 Na⁺；程序保留 SantaLucia 1998 最近邻参数及熵盐校正，与 PrimerExplorer 的公式不同。这些是计算参考条件，不是建议实验配方。\r\nRPA：30–35 nt、GC 30–70%、产物 100–200 bp，保留符合 TwistDx 指南的默认值；Tm 仅供参考，不作为 RPA 硬筛选。\r\n官方来源：PrimerExplorer V5 设计手册正文第 1–3 页 https://primerexplorer.jp/e/v5_manual/pdf/PrimerExplorerV5_Manual_1.pdf ；Tm 附录 https://primerexplorer.jp/e/v3_manual/03.html ；TwistDx / Abbott 设计手册 https://www.globalpointofcare.abbott/us/en/lp/twistdx/support.html 。\r\n\r\n"+help.Text;
            root.Controls.Add(help,0,0);
            var link = new LinkLabel {Text="打开官方 TwistAmp 引物设计手册（联网）",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
            link.LinkClicked+=delegate {try {System.Diagnostics.Process.Start(ReportWriter.ManualUrl);}catch(Exception ex){MessageBox.Show(this,ex.Message,"无法打开链接");}};root.Controls.Add(link,0,1);
        }
        private void ResetSettings()
        {
            ClearUnlimited(primerMin,primerMax,gcMin,gcMax,productMin,productMax,productIdeal);
            primerMin.Value=30;primerMax.Value=35;gcMin.Value=30;gcMax.Value=70;productMin.Value=100;productMax.Value=200;productIdeal.Value=150;pairCount.Value=10;targetCheck.Checked=false;mismatchMode.SelectedIndex=0;
        }
        private void InputChanged()
        {
            if(initializing || busy) return;
            ClearBlastResults();
            if(result!=null || snpResult!=null || lampResult!=null) {result=null;snpResult=null;lampResult=null;grid.Rows.Clear();detailsView.ShowEmpty("输入已更新","输入或参数已更改，请重新设计。");resultInfo.Text="输入已更新，旧结果已清除。";export.Enabled=false;copy.Enabled=false;}
            if(status!=null)status.Text="输入或参数已更新 · 点击开始设计";
        }
        private DesignSettings Settings()
        {
            return new DesignSettings {PrimerMin=(int)primerMin.Value,PrimerMax=(int)primerMax.Value,GcMin=(double)gcMin.Value,GcMax=(double)gcMax.Value,AmpliconMin=(int)productMin.Value,AmpliconMax=(int)productMax.Value,PreferredAmplicon=(int)productIdeal.Value,MaxPairs=(int)pairCount.Value,TargetStart=targetCheck.Checked&&!IsSnpMode?(int)targetStart.Value:0,TargetEnd=targetCheck.Checked&&!IsSnpMode?(int)targetEnd.Value:0,
                PrimerMinUnlimited=Unlimited(primerMin),PrimerMaxUnlimited=Unlimited(primerMax),GcMinUnlimited=Unlimited(gcMin),GcMaxUnlimited=Unlimited(gcMax),
                AmpliconMinUnlimited=Unlimited(productMin),AmpliconMaxUnlimited=Unlimited(productMax),PreferredAmpliconUnlimited=Unlimited(productIdeal)};
        }
        private void LoadSequence(object sender,EventArgs e)
        {
            using(var dialog=new OpenFileDialog {Title="导入单条序列",Filter="序列文件|*.fasta;*.fa;*.fna;*.txt|所有文件|*.*"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try {if(new FileInfo(dialog.FileName).Length>1000000)throw new ArgumentException("文件大于 1 MB，请提取单条靶区序列后再导入。");sequenceBox.Text=File.ReadAllText(dialog.FileName,Encoding.UTF8);}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"导入失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
        private void StartDesign(object sender,EventArgs e)
        {
            if(busy||blastBusy)return;
            bool snpMode=IsSnpMode,lampMode=IsLampMode;
            ParsedSequence input=null;SnpInput snpInput=null;
            try
            {
                if(snpMode)snpInput=SnpParser.Parse(sequenceBox.Text);
                else
                {
                    foreach(string line in sequenceBox.Text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'))
                        if(!line.TrimStart('\uFEFF',' ','\t').StartsWith(">",StringComparison.Ordinal) && line.IndexOf('[')>=0 && line.IndexOf(']')>=0)
                            throw new ArgumentException("检测到 SNP 标记。请切换为相应的 RPA 或 LAMP SNP 选择性扩增模式。");
                    input=SequenceParser.Parse(sequenceBox.Text);
                }
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"请检查输入",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
            var settings=Settings();
            var snpSettings=new SnpDesignSettings {Base=settings,ExtraMismatchFromThreePrime=mismatchMode.SelectedIndex==0?0:mismatchMode.SelectedIndex+1};
            var lampOptions=LampSettings();
            ClearBlastResults();
            result=null;snpResult=null;lampResult=null;grid.Rows.Clear();detailsView.ShowEmpty("正在设计…","正在筛选候选。完成后可分别查看引物、序列与评价。");export.Enabled=false;copy.Enabled=false;
            resultInfo.Text="正在设计，结果将在完成后显示…";
            cancellation=new CancellationTokenSource();busy=true;SetInputEnabled(false);progress.Value=0;status.Text="开始筛选…";
            var worker=new BackgroundWorker();worker.WorkerReportsProgress=true;
            worker.DoWork+=delegate(object s,DoWorkEventArgs args)
            {
                Action<int,string> report=delegate(int percent,string message){worker.ReportProgress(Math.Max(0,Math.Min(100,percent)),message);};
                if(lampMode)args.Result=snpMode?LampDesignEngine.DesignSnp(snpInput,lampOptions,report,cancellation.Token):LampDesignEngine.Design(input,lampOptions,report,cancellation.Token);
                else args.Result=snpMode?(object)SnpDesignEngine.Design(snpInput,snpSettings,report,cancellation.Token):DesignEngine.Design(input,settings,report,cancellation.Token);
            };
            worker.ProgressChanged+=delegate(object s,ProgressChangedEventArgs args){if(IsDisposed)return;progress.Value=args.ProgressPercentage;status.Text=Convert.ToString(args.UserState);};
            worker.RunWorkerCompleted+=delegate(object s,RunWorkerCompletedEventArgs args)
            {
                busy=false;worker.Dispose();if(cancellation!=null){cancellation.Dispose();cancellation=null;}if(IsDisposed)return;SetInputEnabled(true);
                if(args.Error!=null) {progress.Value=0;if(args.Error is OperationCanceledException){status.Text="已取消设计";resultInfo.Text="设计已取消";detailsView.ShowEmpty("已取消设计","可以调整输入后重新开始。");return;}status.Text="设计未完成";resultInfo.Text="设计未完成";detailsView.ShowEmpty("设计未完成",args.Error.Message);MessageBox.Show(this,args.Error.Message,"请检查参数或序列",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                progress.Value=100;
                string[] labels=lampMode?new string[]{"组","分数*","靶区 bp"}:snpMode?new string[]{"组","分数*","等位/对照 bp"}:new string[]{"候选","分数*","产物 bp"};
                for(int i=0;i<labels.Length;i++)grid.Columns[i].HeaderText=labels[i];
                if(lampMode)
                {
                    lampResult=(LampDesignResult)args.Result;
                    foreach(var p in lampResult.Sets)grid.Rows.Add("#"+p.Rank,p.Score.ToString("0.0"),p.SpanLength);
                    resultInfo.Text=LampReportWriter.ModeName(lampResult)+(snpMode?" · SNP "+snpInput.Position+" ["+snpInput.ReferenceAllele+">"+snpInput.AlternateAllele+"]":"")+"  |  "+lampResult.Sets.Count+" 组"+(lampResult.SearchTruncated?"  |  有限搜索":"")+"  |  靶区长度非完整产物长度";
                }
                else if(snpMode)
                {
                    snpResult=(SnpDesignResult)args.Result;
                    foreach(var p in snpResult.Sets)grid.Rows.Add("#"+p.Rank,p.Score.ToString("0.0"),p.ReferencePair.AmpliconLength+" / "+p.ControlPair.AmpliconLength);
                    resultInfo.Text="SNP "+snpResult.Input.Position+" ["+snpResult.Input.ReferenceAllele+">"+snpResult.Input.AlternateAllele+"]  |  "+snpResult.Sets.Count+" 组 × 4 条引物"+(snpResult.SearchTruncated?"  |  有限搜索":"")+"  |  红色碱基 = SNP  |  *分数不代表选择性";
                }
                else
                {
                    result=(DesignResult)args.Result;
                    foreach(var p in result.Pairs)grid.Rows.Add("#"+p.Rank,p.Score.ToString("0.0"),p.AmpliconLength);
                    resultInfo.Text=result.Input.Name+"  |  "+result.Input.Sequence.Length+" nt  |  "+result.Pairs.Count+" 对候选"+(result.SearchTruncated?"  |  已裁剪搜索，见报告":"")+"  |  *分数非成功率";
                }
                status.Text=grid.Rows.Count>0?"设计完成 · 请比较候选并进行实验筛选":"没有满足条件的候选 · 查看说明后调整参数";
                export.Enabled=true;copy.Enabled=grid.Rows.Count>0;
                copy.Text=lampMode?"复制此组 LAMP 引物":snpMode?"复制选中组 4 条引物":"复制选中引物";
                if(grid.Rows.Count>0){grid.Rows[0].Selected=true;grid.CurrentCell=grid.Rows[0].Cells[0];ShowPair();}
                else if(lampMode)detailsView.ShowNoResults(lampResult);
                else if(snpMode)detailsView.ShowNoResults(snpResult);
                else detailsView.ShowNoResults(result);
                tabs.SelectedIndex=1;
            };
            worker.RunWorkerAsync();
        }
        private void SetInputEnabled(bool enabled)
        {
            foreach(Control c in inputPanel.Controls)c.Enabled=enabled;
            design.Parent.Enabled=true;design.Enabled=enabled;cancel.Enabled=!enabled;
        }
        private PrimerPair SelectedPair()
        {
            if(result==null || grid.CurrentRow==null)return null;int index=grid.CurrentRow.Index;return index>=0 && index<result.Pairs.Count?result.Pairs[index]:null;
        }
        private void ShowPair()
        {
            if(lampResult!=null)
            {
                var set=SelectedLampSet();if(set==null || detailsView==null)return;
                detailsView.ShowLamp(set,lampResult);return;
            }
            if(snpResult!=null)
            {
                var set=SelectedSnpSet();if(set==null || detailsView==null)return;
                detailsView.ShowSnp(set,snpResult);return;
            }
            var p=SelectedPair();if(p==null || detailsView==null)return;
            detailsView.ShowPair(p,result);
        }
        private void CopyPair(object sender,EventArgs e)
        {
            if(lampResult!=null){CopyLampSet();return;}
            if(snpResult!=null)
            {
                var set=SelectedSnpSet();if(set==null)return;
                try
                {
                    using(var formatted=new RichTextBox {Font=new Font("Consolas",12F),ForeColor=ink})
                    {
                        ApplyHighlights(formatted,SnpReportWriter.HighlightedOrderingText(set,snpResult.Input),"");
                        var data=new DataObject();data.SetData(DataFormats.UnicodeText,SnpReportWriter.OrderingText(set,snpResult.Input));data.SetData(DataFormats.Rtf,formatted.Rtf);Clipboard.SetDataObject(data,true);
                    }
                    status.Text="已复制 4 条引物；富文本可保留 SNP 红色及人为错配蓝色";
                }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"复制失败");}return;
            }
            var p=SelectedPair();if(p==null)return;
            try {Clipboard.SetText("RPA_"+p.Rank+"_F\t"+p.Forward.Sequence+"\r\nRPA_"+p.Rank+"_R\t"+p.Reverse.Sequence);status.Text="已复制 F / R 引物序列（均为 5′→3′）";}catch(Exception ex){MessageBox.Show(this,ex.Message,"复制失败");}
        }
        private void Export(object sender,EventArgs e)
        {
            if(lampResult!=null){ExportLamp();return;}
            if(result==null && snpResult==null)return;
            bool snpExport=snpResult!=null;
            using(var dialog=new SaveFileDialog {Title="导出 RPA 候选结果",Filter=snpExport?"彩色 SNP 与错配报告 (*.html)|*.html|完整文本报告 (*.txt)|*.txt|Excel 可读表格 (*.csv)|*.csv|引物 FASTA (*.fasta)|*.fasta":"完整报告 (*.txt)|*.txt|Excel 可读表格 (*.csv)|*.csv|引物 FASTA (*.fasta)|*.fasta",FileName=(snpExport?"SNP_候选引物_":"RPA_候选引物_")+DateTime.Now.ToString("yyyyMMdd_HHmmss"),AddExtension=true,DefaultExt=snpExport?"html":"txt"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try
                {
                    int format=snpExport?dialog.FilterIndex-1:dialog.FilterIndex;
                    string content=snpExport?(format==0?SnpReportWriter.Html(snpResult):format==2?SnpReportWriter.Csv(snpResult):format==3?SnpReportWriter.Fasta(snpResult):SnpReportWriter.TextReport(snpResult)):(format==2?ReportWriter.Csv(result):format==3?ReportWriter.Fasta(result):ReportWriter.TextReport(result));
                    File.WriteAllText(dialog.FileName,content,new UTF8Encoding(format!=3));status.Text="已导出："+dialog.FileName;
                }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
        private SnpPrimerSet SelectedSnpSet()
        {
            if(snpResult==null || grid.CurrentRow==null)return null;
            int i=grid.CurrentRow.Index;return i>=0 && i<snpResult.Sets.Count?snpResult.Sets[i]:null;
        }
        internal static void ApplyHighlights(RichTextBox box,HighlightedReport report,string suffix)
        {
            box.Text=report.Text+(suffix??"").Replace("\r\n","\n").Replace('\r','\n');
            box.SelectAll();box.SelectionColor=box.ForeColor;
            foreach(var span in report.MismatchHighlights)
            {
                if(span.Start<0 || span.Length!=1 || span.Start>box.TextLength-span.Length)throw new ArgumentException("人为错配颜色标记超出序列范围。");
                box.Select(span.Start,span.Length);box.SelectionColor=Color.FromArgb(37,99,235);
            }
            foreach(var span in report.SnpHighlights)
            {
                if(span.Start<0 || span.Length!=1 || span.Start>box.TextLength-span.Length)throw new ArgumentException("SNP 颜色标记超出序列范围。");
                box.Select(span.Start,span.Length);box.SelectionColor=Color.FromArgb(211,47,47);
            }
            box.Select(0,0);box.ScrollToCaret();
        }
    }
}
