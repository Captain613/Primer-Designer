using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RpaDesigner
{
    public sealed partial class MainForm
    {
        private TabPage blastPage;
        private CheckBox blastEnabled;
        private ComboBox blastDatabase;
        private TextBox blastEmail, blastExpected, blastPreview, blastReport;
        private NumericUpDown blastCoverage, blastIdentity, blastSpan, blastLimit;
        private Button blastRun, blastCancel, blastExport;
        private Label blastState, blastScope;
        private Control blastOptions;
        private CancellationTokenSource blastCancellation;
        private System.Windows.Forms.Timer blastElapsedTimer;
        private Stopwatch blastElapsed;
        private string blastProgressText;
        private bool blastBusy;
        private BlastQuerySet blastQueries, blastCompletedQueries;
        private BlastSettings blastCompletedSettings;
        private BlastResult blastResult;

        private void BuildBlast()
        {
            blastPage=new TabPage("04  BLAST 特异性") {Name="blastPage",BackColor=Color.White,Padding=new Padding(12)};tabs.TabPages.Add(blastPage);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));blastPage.Controls.Add(root);
            for(int i=0;i<4;i++)root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,116));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            blastEnabled=new CheckBox {Name="blastEnabled",Text="启用 NCBI 在线 BLAST 特异性初筛（可选，默认关闭）",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font,FontStyle.Bold),ForeColor=teal,Margin=new Padding(0,0,0,7)};root.Controls.Add(blastEnabled,0,0);
            var options=new TableLayoutPanel {Name="blastOptions",Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};blastOptions=options;options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));options.RowStyles.Add(new RowStyle(SizeType.Absolute));options.RowStyles.Add(new RowStyle(SizeType.Absolute));root.Controls.Add(options,0,1);
            var row1=BlastFlow();options.Controls.Add(row1,0,0);
            row1.Controls.Add(BlastLabel("检索数据库"));blastDatabase=new ComboBox {Name="blastDatabase",DropDownStyle=ComboBoxStyle.DropDownList,Width=350,Margin=new Padding(0,3,14,3)};
            blastDatabase.Items.AddRange(new object[]{"RefSeq 参考基因组（默认）","RefSeq 基因组库（更广覆盖）","核心核酸库（core_nt，部分染色体不包含）"});blastDatabase.SelectedIndex=0;row1.Controls.Add(blastDatabase);
            row1.Controls.Add(BlastLabel("联系邮箱"));blastEmail=new TextBox {Name="blastEmail",Width=230,Margin=new Padding(0,3,5,3),AccessibleName="NCBI 联系邮箱"};row1.Controls.Add(blastEmail);
            var row2=BlastFlow();options.Controls.Add(row2,0,1);
            row2.Controls.Add(BlastLabel("预期登录号"));blastExpected=new TextBox {Name="blastExpected",Width=215,Margin=new Padding(0,3,12,3),AccessibleName="预期靶标登录号，可留空"};row2.Controls.Add(blastExpected);
            row2.Controls.Add(BlastLabel("覆盖≥%"));blastCoverage=BlastNumber("blastCoverage",90,1,100);row2.Controls.Add(blastCoverage);
            row2.Controls.Add(BlastLabel("一致≥%"));blastIdentity=BlastNumber("blastIdentity",80,1,100);row2.Controls.Add(blastIdentity);
            row2.Controls.Add(BlastLabel("组合跨度≤bp"));blastSpan=BlastNumber("blastSpan",2000,50,100000);blastSpan.Width=82;row2.Controls.Add(blastSpan);
            row2.Controls.Add(BlastLabel("每序列最多命中"));blastLimit=BlastNumber("blastLimit",100,10,500);row2.Controls.Add(blastLimit);
            blastScope=new Label {Name="blastScope",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F),ForeColor=Color.DimGray,Margin=new Padding(0,4,0,6),Text="检索所选库的全部物种。预期登录号可留空或以逗号分隔，仅用于本地标注，不缩小检索范围。本程序要求填写联系邮箱（依据 NCBI 开发者指南）。\r\nBLAST 使用短序列参数；未命中不等于特异性通过。core_nt 不包含部分真核染色体；初筛范围取决于数据库覆盖。"};root.Controls.Add(blastScope,0,2);
            var actions=BlastFlow();root.Controls.Add(actions,0,3);
            var load=ButtonFor("载入当前候选",delegate{LoadBlastCandidate();});load.Name="blastLoad";actions.Controls.Add(load);
            blastRun=ButtonFor("预览并提交 NCBI…",StartBlast);blastRun.Name="blastRun";blastRun.BackColor=teal;blastRun.ForeColor=Color.White;actions.Controls.Add(blastRun);
            blastCancel=ButtonFor("取消比对",delegate{if(blastCancellation!=null)blastCancellation.Cancel();});blastCancel.Name="blastCancel";actions.Controls.Add(blastCancel);
            blastExport=ButtonFor("导出 BLAST 结果…",ExportBlast);blastExport.Name="blastExport";actions.Controls.Add(blastExport);
            blastPreview=new TextBox {Name="blastPreview",AccessibleName="实际提交的匿名 FASTA 序列",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10F),BackColor=Color.FromArgb(247,250,251),Margin=new Padding(0,6,0,6)};root.Controls.Add(blastPreview,0,4);
            blastState=new Label {Name="blastState",Text="先完成引物设计，在候选列表选中一组，再载入此页。只有确认提交后才会联网。",AutoSize=true,Dock=DockStyle.Fill,ForeColor=teal,Margin=new Padding(0,2,0,6)};root.Controls.Add(blastState,0,5);
            blastReport=new TextBox {Name="blastReport",AccessibleName="BLAST 特异性初筛报告",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",10F),BackColor=Color.White};root.Controls.Add(blastReport,0,6);
            // A nested autosized table can measure wrapped flows at its minimum
            // width and retain that taller height even after docking full-width.
            // Measure each row at the actual available width so wrapped controls
            // fit while unused parameter space returns to the result panel.
            root.RowStyles[1].SizeType=SizeType.Absolute;
            bool fittingOptions=false;
            LayoutEventHandler fitOptions=delegate
            {
                if(fittingOptions)return;
                fittingOptions=true;
                try
                {
                    int width=Math.Max(1,root.ClientSize.Width-root.Padding.Horizontal-options.Margin.Horizontal);
                    int first=row1.GetPreferredSize(new Size(width,0)).Height;
                    int second=row2.GetPreferredSize(new Size(width,0)).Height;
                    options.RowStyles[0].Height=first;options.RowStyles[1].Height=second;
                    root.RowStyles[1].Height=first+second;
                }
                finally{fittingOptions=false;}
            };
            root.Layout+=fitOptions;
            blastElapsedTimer=new System.Windows.Forms.Timer {Interval=1000};
            blastElapsedTimer.Tick+=delegate{UpdateBlastElapsed();};
            Disposed+=delegate
            {
                if(blastElapsedTimer!=null){blastElapsedTimer.Stop();blastElapsedTimer.Dispose();blastElapsedTimer=null;}
            };
            blastEnabled.CheckedChanged+=delegate{RefreshBlastControls();};
            tabs.SelectedIndexChanged+=delegate{if(tabs.SelectedTab==blastPage&&!blastBusy)LoadBlastCandidate();};
            RefreshBlastControls();
        }
        private FlowLayoutPanel BlastFlow()
        {return new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=Padding.Empty,Padding=new Padding(0,2,0,2)};}
        private Label BlastLabel(string text)
        {return new Label {Text=text,AutoSize=true,Margin=new Padding(0,6,6,3)};}
        private NumericUpDown BlastNumber(string name,int value,int min,int max)
        {return new NumericUpDown {Name=name,Minimum=min,Maximum=max,Value=value,Width=60,Margin=new Padding(0,3,12,3)};}
        private BlastQuerySet CurrentBlastQueries()
        {
            if(lampResult!=null){var set=SelectedLampSet();return set==null?null:BlastQueryBuilder.ForLamp(set,LampReportWriter.ModeName(lampResult));}
            if(snpResult!=null){var set=SelectedSnpSet();return set==null?null:BlastQueryBuilder.ForSnp(set);}
            var pair=SelectedPair();return pair==null?null:BlastQueryBuilder.ForRpa(pair);
        }
        private void LoadBlastCandidate()
        {
            if(blastBusy)return;
            try
            {
                blastQueries=CurrentBlastQueries();
                blastPreview.Text=blastQueries==null?"":BlastOnline.BuildFasta(blastQueries);
            }
            catch(Exception ex)
            {
                blastQueries=null;blastPreview.Clear();blastState.Text="当前候选无法提交 BLAST："+ex.Message;RefreshBlastControls();return;
            }
            blastState.Text=blastQueries==null?"尚无可检查的候选。请先完成设计并选中一组。":
                "待提交："+blastQueries.Mode+" / "+blastQueries.CandidateLabel+" · "+blastQueries.Queries.Count+" 条去重后的序列。下方已有报告保留原候选标识。";
            RefreshBlastControls();
        }
        private BlastSettings ReadBlastSettings()
        {
            string[] databases={"refseq_representative_genomes","refseq_genomes","core_nt"};
            return new BlastSettings {Database=databases[blastDatabase.SelectedIndex],Email=blastEmail.Text.Trim(),ExpectedAccessions=blastExpected.Text.Trim(),
                MinCoverage=(double)blastCoverage.Value,MinIdentity=(double)blastIdentity.Value,MaxLocusSpan=(int)blastSpan.Value,HitListSize=(int)blastLimit.Value};
        }
        private void RefreshBlastControls()
        {
            if(blastEnabled==null)return;
            blastEnabled.Enabled=!blastBusy;blastOptions.Enabled=blastEnabled.Checked&&!blastBusy;
            blastRun.Enabled=blastEnabled.Checked&&!blastBusy&&!busy&&blastQueries!=null;
            blastCancel.Enabled=blastBusy;blastExport.Enabled=!blastBusy&&blastResult!=null;
            var load=blastPage.Controls.Find("blastLoad",true);if(load.Length==1)load[0].Enabled=!blastBusy&&!busy;
        }
        private void ClearBlastResults()
        {
            if(blastEnabled==null||blastBusy)return;
            blastQueries=null;blastResult=null;blastCompletedQueries=null;blastCompletedSettings=null;
            blastPreview.Clear();blastReport.Clear();blastState.Text="输入已更改；请完成设计后重新载入候选。";RefreshBlastControls();
        }
        private void UpdateBlastElapsed()
        {
            if(!blastBusy||blastElapsed==null||blastState==null||blastState.IsDisposed)return;
            int seconds=(int)blastElapsed.Elapsed.TotalSeconds;
            blastState.Text=blastProgressText+" · 已用时 "+(seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
        }
        private bool ConfirmBlastUpload(BlastQuerySet queries,BlastSettings settings)
        {
            using(var dialog=new Form {Text="确认发送至 NCBI BLAST",Font=Font,StartPosition=FormStartPosition.CenterParent,Size=new Size(760,620),MinimumSize=new Size(600,460),ShowInTaskbar=false,MinimizeBox=false,MaximizeBox=false})
            {
                var panel=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=3};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));dialog.Controls.Add(panel);
                panel.Controls.Add(new Label {AutoSize=true,Dock=DockStyle.Fill,Text="将下列序列及联系邮箱发送至 https://blast.ncbi.nlm.nih.gov/Blast.cgi\r\n"+queries.Mode+" / "+queries.CandidateLabel+"；数据库："+settings.Database+"（全部物种）\r\n邮箱："+settings.Email+"\r\n仅上传下方匿名 FASTA；不上传完整模板、原始文件名或标题。LAMP 内引物按结合区段检索。\r\nNCBI 会接收并处理这些数据。比对可能需要数分钟；取消本地等待不会撤回已发送的数据。",Margin=new Padding(0,0,0,10)},0,0);
                panel.Controls.Add(new TextBox {ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",10F),Text=BlastOnline.BuildFasta(queries)},0,1);
                var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,10,0,0)};
                var no=new Button {Text="不发送",DialogResult=DialogResult.Cancel,AutoSize=true,Height=34};var yes=new Button {Text="同意上传并开始",DialogResult=DialogResult.OK,AutoSize=true,Height=34};buttons.Controls.Add(no);buttons.Controls.Add(yes);panel.Controls.Add(buttons,0,2);dialog.AcceptButton=no;dialog.CancelButton=no;
                return dialog.ShowDialog(this)==DialogResult.OK;
            }
        }
        private void StartBlast(object sender,EventArgs e)
        {
            if(blastBusy||busy||!blastEnabled.Checked)return;
            LoadBlastCandidate();if(blastQueries==null)return;
            BlastSettings settings=ReadBlastSettings();BlastQuerySet queries=blastQueries;
            try{BlastOnline.Validate(queries,settings);}catch(Exception ex){MessageBox.Show(this,ex.Message,"请检查 BLAST 设置",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            if(!ConfirmBlastUpload(queries,settings))return;
            blastCancellation=new CancellationTokenSource();var token=blastCancellation.Token;blastBusy=true;blastResult=null;blastCompletedQueries=null;blastCompletedSettings=null;
            blastProgressText="正在提交 NCBI…";blastElapsed=Stopwatch.StartNew();blastElapsedTimer.Start();
            blastReport.Clear();UpdateBlastElapsed();SetInputEnabled(false);cancel.Enabled=false;grid.Enabled=false;RefreshBlastControls();
            var worker=new BackgroundWorker {WorkerReportsProgress=true};
            worker.DoWork+=delegate(object s,DoWorkEventArgs a){a.Result=BlastOnline.Run(queries,settings,delegate(string text){worker.ReportProgress(0,text);},token);};
            worker.ProgressChanged+=delegate(object s,ProgressChangedEventArgs a){if(!IsDisposed){blastProgressText=Convert.ToString(a.UserState);UpdateBlastElapsed();}};
            worker.RunWorkerCompleted+=delegate(object s,RunWorkerCompletedEventArgs a)
            {
                if(blastElapsedTimer!=null)blastElapsedTimer.Stop();
                if(blastElapsed!=null){blastElapsed.Stop();blastElapsed=null;}
                blastProgressText=null;
                worker.Dispose();blastBusy=false;if(blastCancellation!=null){blastCancellation.Dispose();blastCancellation=null;}if(IsDisposed)return;
                SetInputEnabled(true);grid.Enabled=true;
                if(a.Error!=null){blastState.Text=a.Error is OperationCanceledException?"已取消本地等待；已提交的 NCBI 任务可能继续运行。":"BLAST 未完成："+a.Error.Message;blastReport.Text="本次没有完成特异性分析，不能视为通过。";}
                else
                {
                    blastResult=(BlastResult)a.Result;blastCompletedQueries=queries;blastCompletedSettings=settings;
                    try{blastReport.Text=BlastAssessment.Text(queries,settings,blastResult);blastState.Text="BLAST 已完成 · RID "+blastResult.Rid+" · "+queries.CandidateLabel+"。请查看命中与组合风险，未命中不代表验证通过。";}
                    catch(Exception ex){blastState.Text="BLAST 已返回，但本地解释未完成："+ex.Message;blastReport.Text="未完成特异性解释。可导出原始 XML 复核。";}
                }
                RefreshBlastControls();
            };
            worker.RunWorkerAsync();
        }
        private void ExportBlast(object sender,EventArgs e)
        {
            if(blastResult==null)return;
            using(var dialog=new SaveFileDialog {Title="导出 BLAST 特异性初筛",Filter="完整说明 (*.txt)|*.txt|命中明细 (*.csv)|*.csv|NCBI 原始结果 (*.xml)|*.xml",FileName="BLAST_"+blastResult.Rid,DefaultExt="txt",AddExtension=true})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{string text=dialog.FilterIndex==2?BlastAssessment.Csv(blastCompletedQueries,blastCompletedSettings,blastResult):dialog.FilterIndex==3?blastResult.RawXml:BlastAssessment.Text(blastCompletedQueries,blastCompletedSettings,blastResult);File.WriteAllText(dialog.FileName,text,new UTF8Encoding(true));blastState.Text="已导出："+dialog.FileName;}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
    }
}
