using System;
using System.Drawing;
using System.Windows.Forms;

namespace RpaDesigner
{
    public sealed partial class MainForm
    {
        private Panel inputScroll;

        private void BuildInput()
        {
            var page=new TabPage("01  输入与参数") {BackColor=Color.White,Padding=new Padding(12,10,12,8)};tabs.TabPages.Add(page);
            var pageShell=new TableLayoutPanel {Name="inputPageShell",Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
            pageShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            pageShell.RowStyles.Add(new RowStyle(SizeType.Percent,100));pageShell.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.Controls.Add(pageShell);
            inputScroll=new Panel {Name="inputScroll",Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(4,2,4,8),Margin=Padding.Empty};pageShell.Controls.Add(inputScroll,0,0);
            var root=new TableLayoutPanel {Name="inputLayout",ColumnCount=1,RowCount=6,MinimumSize=new Size(880,440),Margin=Padding.Empty};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,112));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,224));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inputScroll.Controls.Add(root);inputPanel=root;

            var modeTools=new FlowLayoutPanel {Name="modeToolbar",Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(0,0,0,7),Margin=Padding.Empty};
            modeTools.Controls.Add(new Label {Text="设计模式",AutoSize=true,Margin=new Padding(0,7,10,0),ForeColor=teal});
            designMode=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=320,DropDownWidth=390,Margin=new Padding(0,1,18,0)};
            designMode.Items.AddRange(new object[]{"普通 RPA 扩增","RPA SNP 选择性扩增","普通 LAMP 扩增","AS-LAMP · 等位基因特异性","PA-LAMP · 引物激活型","mLAMP · 人工错配型（Ren 2019）"});
            designMode.SelectedIndex=0;modeTools.Controls.Add(designMode);
            modeTools.Controls.Add(new Label {Text="SNP 策略",AutoSize=true,Margin=new Padding(0,7,10,0),ForeColor=teal});
            mismatchMode=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=350,Enabled=false,Margin=new Padding(0,1,0,0)};
            mismatchMode.Items.AddRange(new object[]{"仅 SNP 末端差异（基线）","倒数第 2 位增加错配（探索）","倒数第 3 位增加错配（探索）"});mismatchMode.SelectedIndex=0;
            mismatchMode.SelectedIndexChanged+=delegate{InputChanged();};modeTools.Controls.Add(mismatchMode);
            paStrategyText=new Label {Text="RNA / C3 修饰 · RNase H2 激活",AutoSize=true,Visible=false,Margin=new Padding(0,7,0,0),ForeColor=teal};modeTools.Controls.Add(paStrategyText);root.Controls.Add(modeTools,0,0);

            var tools=new FlowLayoutPanel {Name="inputToolbar",Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(0,0,0,5),Margin=Padding.Empty};
            tools.Controls.Add(new Label {Text="输入序列",AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,8,16,0)});
            tools.Controls.Add(ButtonFor("导入 FASTA / TXT",LoadSequence));
            tools.Controls.Add(ButtonFor("载入演示",delegate{sequenceBox.Text=IsLampMode?(IsSnpMode?LampReportWriter.ExampleSnpFasta():LampReportWriter.ExampleFasta()):(IsSnpMode?SnpReportWriter.ExampleFasta():ReportWriter.ExampleFasta());}));
            tools.Controls.Add(ButtonFor("清空",delegate{sequenceBox.Clear();}));
            tools.Controls.Add(new Label {Text="单条 FASTA / 纯序列 · 最多 20,000 nt",AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(4,8,0,0)});root.Controls.Add(tools,0,1);
            sequenceBox=new TextBox {Name="sequenceInput",AccessibleName="DNA 序列输入",Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,WordWrap=true,Font=new Font("Consolas",11F),AcceptsReturn=true,MaxLength=1000000,MinimumSize=new Size(0,96),BackColor=Color.FromArgb(248,250,251),BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,0,0,3)};
            sequenceBox.TextChanged+=delegate{inputInfo.Text="当前输入 "+sequenceBox.Text.Length.ToString("N0")+" 个字符 · 自动整理空白与行号 · SNP 标记计为 1 nt";InputChanged();};root.Controls.Add(sequenceBox,0,2);
            inputInfo=new Label {Text="输入 5′→3′ 正链；SNP 标记示例：[A>C] 或 [G>T]",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F),ForeColor=Color.DimGray,Margin=new Padding(0,3,0,8)};root.Controls.Add(inputInfo,0,3);
            var settingsHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};root.Controls.Add(settingsHost,0,4);
            BuildRpaSettings(settingsHost);BuildLampSettings(settingsHost);
            modeHint=new Label {Text="未指定靶区时扫描完整输入；指定靶区后，引物需位于其两侧。\r\n候选需实验验证。未进行全基因组特异性检索。",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F),ForeColor=Color.FromArgb(96,110,119),Margin=new Padding(2,8,2,3)};root.Controls.Add(modeHint,0,5);

            var actions=new FlowLayoutPanel {Name="designActions",Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(4,9,0,0),Margin=Padding.Empty};
            design=ButtonFor("开始设计",StartDesign);design.Name="startDesign";design.BackColor=teal;design.ForeColor=Color.White;design.FlatAppearance.BorderColor=teal;design.MinimumSize=new Size(145,38);design.Font=new Font(Font,FontStyle.Bold);actions.Controls.Add(design);
            cancel=ButtonFor("取消",delegate{if(cancellation!=null)cancellation.Cancel();});cancel.Name="cancelDesign";cancel.Enabled=false;actions.Controls.Add(cancel);
            actions.Controls.Add(new Label {Text="本地设计 · 可在候选结果中另选在线 BLAST",AutoSize=true,Font=new Font(Font.FontFamily,9F),ForeColor=Color.DimGray,Margin=new Padding(8,10,0,0)});pageShell.Controls.Add(actions,0,1);
            designMode.SelectedIndexChanged+=delegate{UpdateDesignMode();};
            bool fittingInput=false;
            Action fitInput=delegate
            {
                if(fittingInput)return;fittingInput=true;
                try
                {
                    int width=Math.Max(root.MinimumSize.Width,inputScroll.ClientSize.Width-inputScroll.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
                    int height=Math.Max(root.MinimumSize.Height,root.GetPreferredSize(new Size(width,0)).Height);
                    inputScroll.AutoScrollMinSize=new Size(width+inputScroll.Padding.Horizontal,height+inputScroll.Padding.Vertical);
                    root.SetBounds(inputScroll.Padding.Left+inputScroll.AutoScrollPosition.X,inputScroll.Padding.Top+inputScroll.AutoScrollPosition.Y,width,height);
                }
                finally{fittingInput=false;}
            };
            inputScroll.Layout+=delegate{fitInput();};root.Layout+=delegate{fitInput();};fitInput();
        }

        private TableLayoutPanel RangeTable(string name,int ranges)
        {
            var table=new TableLayoutPanel {Name=name,Dock=DockStyle.Fill,ColumnCount=3,RowCount=ranges+1,Margin=new Padding(0,0,12,0)};
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute,24));for(int i=0;i<ranges;i++)table.RowStyles.Add(new RowStyle(SizeType.Percent,100F/ranges));
            string[] headers={"筛选项目","下限","上限"};for(int i=0;i<3;i++)table.Controls.Add(new Label {Text=headers[i],Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F,FontStyle.Bold),ForeColor=teal,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(2,0,0,0)},i,0);
            return table;
        }
        private void RangeRow(TableLayoutPanel table,int row,string title,NumericUpDown minimum,NumericUpDown maximum)
        {
            table.Controls.Add(new Label {Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(2,0,6,0)},0,row);
            table.Controls.Add(WithUnlimitedOption(minimum,title+"下限"),1,row);table.Controls.Add(WithUnlimitedOption(maximum,title+"上限"),2,row);
        }
        private TableLayoutPanel OptionsTable(string name,int rows)
        {
            var table=new TableLayoutPanel {Name=name,Dock=DockStyle.Fill,ColumnCount=2,RowCount=rows,BackColor=Color.FromArgb(244,248,249),Padding=new Padding(10,4,10,4),Margin=Padding.Empty};
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,56));
            return table;
        }
        private Label OptionLabel(string title)
        {return new Label {Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font.FontFamily,9F),Margin=new Padding(0,0,3,0)};}
        private void BuildRpaSettings(Control host)
        {
            rpaSettingsGroup=new GroupBox {Name="rpaSettings",Text="RPA 参数 · 上下限可分别设为无限制",Dock=DockStyle.Fill,Padding=new Padding(10,20,10,8)};host.Controls.Add(rpaSettingsGroup);
            var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,66));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,40));rpaSettingsGroup.Controls.Add(body);
            primerMin=UnlimitedNumber("primerMin",30,20,60);primerMax=UnlimitedNumber("primerMax",35,20,60);pairCount=Number(10,1,50);
            gcMin=UnlimitedNumber("gcMin",30,0,100);gcMax=UnlimitedNumber("gcMax",70,0,100);productIdeal=UnlimitedNumber("productIdeal",150,40,5000);
            productMin=UnlimitedNumber("productMin",100,40,5000);productMax=UnlimitedNumber("productMax",200,40,5000);
            var ranges=RangeTable("rpaRanges",3);RangeRow(ranges,1,"引物长度 (nt)",primerMin,primerMax);RangeRow(ranges,2,"GC 含量 (%)",gcMin,gcMax);RangeRow(ranges,3,"产物长度 (bp)",productMin,productMax);body.Controls.Add(ranges,0,0);
            var options=OptionsTable("rpaOptions",4);options.RowStyles.Add(new RowStyle(SizeType.Absolute,24));options.RowStyles.Add(new RowStyle(SizeType.Percent,50));options.RowStyles.Add(new RowStyle(SizeType.Percent,50));options.RowStyles.Add(new RowStyle(SizeType.Absolute,38));body.Controls.Add(options,1,0);
            var heading=OptionLabel("输出与偏好");heading.ForeColor=teal;heading.Font=new Font(heading.Font,FontStyle.Bold);options.Controls.Add(heading,0,0);options.SetColumnSpan(heading,2);
            options.Controls.Add(OptionLabel("候选对数"),0,1);options.Controls.Add(pairCount,1,1);
            preferredProductLabel=OptionLabel("偏好产物 (bp)");options.Controls.Add(preferredProductLabel,0,2);options.Controls.Add(WithUnlimitedOption(productIdeal,"偏好产物长度"),1,2);
            var restore=ButtonFor("恢复默认参数",delegate{ResetSettings();});restore.Name="restoreDefaults";restore.Dock=DockStyle.Fill;restore.MinimumSize=Size.Empty;restore.Margin=new Padding(0,3,0,0);options.Controls.Add(restore,0,3);options.SetColumnSpan(restore,2);
            var target=new FlowLayoutPanel {Name="targetOptions",Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,Padding=new Padding(2,4,0,0)};body.Controls.Add(target,0,1);body.SetColumnSpan(target,2);
            targetCheck=new CheckBox {Text="包围指定靶区",AutoSize=true,Margin=new Padding(0,4,12,0)};target.Controls.Add(targetCheck);
            targetStart=Number(1,1,20000);targetEnd=Number(1,1,20000);targetStart.Enabled=false;targetEnd.Enabled=false;
            foreach(var number in new[]{targetStart,targetEnd}){number.Dock=DockStyle.None;number.Width=88;number.Margin=new Padding(0,2,12,0);}
            target.Controls.Add(new Label {Text="起点",AutoSize=true,Margin=new Padding(0,5,6,0)});target.Controls.Add(targetStart);
            target.Controls.Add(new Label {Text="终点",AutoSize=true,Margin=new Padding(0,5,6,0)});target.Controls.Add(targetEnd);
            target.Controls.Add(new Label {Text="从 1 开始，含两端",AutoSize=true,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9F),Margin=new Padding(0,6,0,0)});
            targetCheck.CheckedChanged+=delegate{targetStart.Enabled=targetCheck.Checked&&!IsSnpMode;targetEnd.Enabled=targetCheck.Checked&&!IsSnpMode;InputChanged();};
        }
        private void BuildLampSettings(Control host)
        {
            lampSettingsGroup=new GroupBox {Name="lampSettings",Text="LAMP 参数 · 长度与 Tm 按单个结合片段计算",Dock=DockStyle.Fill,Padding=new Padding(10,20,10,8),Visible=false};host.Controls.Add(lampSettingsGroup);
            var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,66));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));lampSettingsGroup.Controls.Add(body);
            var defaults=new LampDesignSettings();
            lampRegionMin=UnlimitedNumber("lampRegionMin",defaults.RegionMin,15,35);lampRegionMax=UnlimitedNumber("lampRegionMax",defaults.RegionMax,15,35);lampCount=Number(defaults.MaxSets,1,50);
            lampGcMin=UnlimitedNumber("lampGcMin",(int)defaults.GcMin,0,100);lampGcMax=UnlimitedNumber("lampGcMax",(int)defaults.GcMax,0,100);
            lampSpanMin=UnlimitedNumber("lampSpanMin",defaults.SpanMin,100,600);lampSpanMax=UnlimitedNumber("lampSpanMax",defaults.SpanMax,100,600);
            lampCoreSpanMin=UnlimitedNumber("lampCoreSpanMin",defaults.CoreSpanMin,1,600);lampCoreSpanMax=UnlimitedNumber("lampCoreSpanMax",defaults.CoreSpanMax,1,600);
            var parameterTabs=new TabControl {Name="lampParameterTabs",Dock=DockStyle.Fill,Margin=new Padding(0,0,12,0)};body.Controls.Add(parameterTabs,0,0);
            var commonPage=new TabPage("长度与 GC") {Name="lampCommonPage",BackColor=Color.White,Padding=new Padding(8,4,3,5)};parameterTabs.TabPages.Add(commonPage);
            var commonLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};commonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));commonLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));commonLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));commonPage.Controls.Add(commonLayout);
            var ranges=RangeTable("lampRanges",4);ranges.Margin=Padding.Empty;RangeRow(ranges,1,"片段长度 (nt)",lampRegionMin,lampRegionMax);RangeRow(ranges,2,"GC 含量 (%)",lampGcMin,lampGcMax);RangeRow(ranges,3,"F3–B3 跨度 (bp)",lampSpanMin,lampSpanMax);RangeRow(ranges,4,"F2–B2 跨度 (bp)",lampCoreSpanMin,lampCoreSpanMax);commonLayout.Controls.Add(ranges,0,0);
            var definitions=new Label {Name="lampRangeDefinitions",Dock=DockStyle.Fill,Text="跨度均含两端；F3–B3 包围外引物，F2–B2 包围内引物的结合片段。\r\nF2c 是 F2 的互补序列；在同一模型与条件下，两者 Tm 相同。\r\nTm 参考条件：Na⁺ 50 mM、Mg²⁺ 4 mM、寡核苷酸 100 nM；不等于反应配方。",ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9F),Margin=new Padding(2,8,6,0)};commonLayout.Controls.Add(definitions,0,1);
            var tmPage=new TabPage("各区段 Tm") {Name="lampTmPage",BackColor=Color.White,Padding=new Padding(8,4,3,5)};parameterTabs.TabPages.Add(tmPage);
            var tmRanges=RangeTable("lampTmRanges",8);tmRanges.Margin=Padding.Empty;tmPage.Controls.Add(tmRanges);
            AddLampTmRow(tmRanges,1,"F3",defaults,out lampF3TmMin,out lampF3TmMax);AddLampTmRow(tmRanges,2,"B3",defaults,out lampB3TmMin,out lampB3TmMax);
            AddLampTmRow(tmRanges,3,"F2",defaults,out lampF2TmMin,out lampF2TmMax);AddLampTmRow(tmRanges,4,"B2",defaults,out lampB2TmMin,out lampB2TmMax);
            AddLampTmRow(tmRanges,5,"F1c",defaults,out lampF1cTmMin,out lampF1cTmMax);AddLampTmRow(tmRanges,6,"B1c",defaults,out lampB1cTmMin,out lampB1cTmMax);
            AddLampTmRow(tmRanges,7,"LF",defaults,out lampLFTmMin,out lampLFTmMax);AddLampTmRow(tmRanges,8,"LB",defaults,out lampLBTmMin,out lampLBTmMax);
            var table=OptionsTable("lampOptions",8);table.Dock=DockStyle.Top;table.Height=190;lampSettingsTable=table;body.Controls.Add(table,1,0);
            foreach(int height in new[]{24,34,22,32,32,0,0,38})table.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var heading=OptionLabel("输出与方法");heading.ForeColor=teal;heading.Font=new Font(heading.Font,FontStyle.Bold);table.Controls.Add(heading,0,0);table.SetColumnSpan(heading,2);
            table.Controls.Add(OptionLabel("候选组数"),0,1);table.Controls.Add(lampCount,1,1);
            var direction=OptionLabel("SNP 判别内引物");table.Controls.Add(direction,0,2);table.SetColumnSpan(direction,2);
            lampOrientation=new ComboBox {Name="lampOrientation",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,Enabled=false,Margin=new Padding(0,0,0,2)};lampOrientation.Items.AddRange(new object[]{"自动比较 FIP / BIP","指定 FIP（正向）","指定 BIP（反向）"});lampOrientation.SelectedIndex=0;table.Controls.Add(lampOrientation,0,3);table.SetColumnSpan(lampOrientation,2);lampOrientation.SelectedIndexChanged+=delegate{InputChanged();};
            lampLoops=new CheckBox {Text="尽可能设计环引物",Checked=true,AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F),Margin=new Padding(0,3,0,0)};lampLoops.CheckedChanged+=delegate{InputChanged();};table.Controls.Add(lampLoops,0,4);table.SetColumnSpan(lampLoops,2);
            lampPaTail=Number(5,4,7);lampPaTail.Name="lampPaTail";lampPaTailLabel=OptionLabel("RNA 后尾部 (nt)");table.Controls.Add(lampPaTailLabel,0,5);table.Controls.Add(lampPaTail,1,5);
            lampPaTailHint=OptionLabel("4–7 nt，含末位人为错配");lampPaTailHint.ForeColor=Color.DimGray;table.Controls.Add(lampPaTailHint,0,6);table.SetColumnSpan(lampPaTailHint,2);
            var restore=ButtonFor("恢复 LAMP 默认",delegate{ResetLampSettings();});restore.Name="restoreLampDefaults";restore.Dock=DockStyle.Fill;restore.MinimumSize=Size.Empty;restore.Margin=new Padding(0,3,0,0);table.Controls.Add(restore,0,7);table.SetColumnSpan(restore,2);
            lampPaTail.Visible=false;lampPaTailLabel.Visible=false;lampPaTailHint.Visible=false;
        }
        private void AddLampTmRow(TableLayoutPanel table,int row,string role,LampDesignSettings defaults,out NumericUpDown minimum,out NumericUpDown maximum)
        {
            LampTmRange range=defaults.GetTm(role);
            minimum=UnlimitedNumber("lamp"+role+"TmMin",(int)range.Min,35,90);maximum=UnlimitedNumber("lamp"+role+"TmMax",(int)range.Max,35,90);
            RangeRow(table,row,role+" Tm (°C)",minimum,maximum);
        }
    }
}
