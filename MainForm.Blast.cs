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
        private TextBox blastPreview, blastReport, blastXmlFile, blastFastaFile, blastExpected, blastWebOrganism;
        private TabControl blastGuideTabs;
        private NumericUpDown blastSite, blastMismatches, blastSpan;
        private ComboBox blastExpectedRole;
        private CheckBox blastNetwork;
        private Button blastLoad, blastCopy, blastExportFasta, blastRun, blastCancel, blastOpenReport;
        private Control blastFiles, blastOptions;
        private Label blastState;
        private CancellationTokenSource blastCancellation;
        private System.Windows.Forms.Timer blastElapsedTimer;
        private Stopwatch blastElapsed;
        private string blastProgressText, blastReportPath;
        private bool blastBusy;
        private BlastQuerySet blastQueries;
        private LampBatchReview.Analysis blastBatchResult;

        private void BuildBlast()
        {
            blastPage = new TabPage("04  BLAST 手动复核") { Name = "blastPage", BackColor = Color.White, Padding = new Padding(12) };
            tabs.TabPages.Add(blastPage);
            blastGuideTabs = new TabControl { Name = "blastGuideTabs", Dock = DockStyle.Fill };
            blastPage.Controls.Add(blastGuideTabs);
            var workflow = new TabPage("模板与文件复核") { BackColor = Color.White, Padding = new Padding(0, 5, 0, 0) };
            var guide = new TabPage("NCBI 网页参数与说明") { BackColor = Color.White, Padding = new Padding(9) };
            blastGuideTabs.TabPages.Add(workflow); blastGuideTabs.TabPages.Add(guide);
            var guideRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            guideRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            guideRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); guideRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); guideRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); guide.Controls.Add(guideRoot);
            var webActions = BlastFlow(); guideRoot.Controls.Add(webActions, 0, 0);
            webActions.Controls.Add(BlastLabel("网页目标物种"));
            blastWebOrganism = new TextBox { Name = "blastWebOrganism", AccessibleName = "新打开的 NCBI 网页目标物种", Text = BlastWebPreset.DefaultOrganism, Width = 310, MaxLength = 200, Margin = new Padding(0, 3, 12, 3) }; webActions.Controls.Add(blastWebOrganism);
            var guideWebsite = ButtonFor("打开 NCBI（推荐设置）", OpenBlastWebsite); guideWebsite.Name = "blastGuideWebsite"; webActions.Controls.Add(guideWebsite);
            guideRoot.Controls.Add(new Label { Name = "blastWebScope", Text = "物种设置只影响下一次打开的网页；已下载 XML 的搜索范围以该文件为准。", AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray, Margin = new Padding(0, 3, 0, 8) }, 0, 1);
            guideRoot.Controls.Add(new TextBox { Name = "blastWebHelp", AccessibleName = "NCBI 推荐参数及本地复核选项说明", Text = BlastWebPreset.Explanation, ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10F), BackColor = Color.White }, 0, 2);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); workflow.Controls.Add(root);
            for (int i = 0; i < 10; i++) root.RowStyles.Add(new RowStyle(i == 2 ? SizeType.Absolute : i == 9 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 85 : i == 9 ? 100 : 0));
            root.Controls.Add(new Label { Name = "blastInstructions", Text = "1 导出区段 FASTA → 2 在 NCBI 网页 BLAST 并下载完整 XML → 3 选择 XML 和对应 FASTA，批量复核。", AutoSize = true, Dock = DockStyle.Fill, ForeColor = teal, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) }, 0, 0);
            var exportActions = BlastFlow(); root.Controls.Add(exportActions, 0, 1);
            blastLoad = ButtonFor("载入当前候选", delegate { LoadBlastCandidate(); }); blastLoad.Name = "blastLoad"; exportActions.Controls.Add(blastLoad);
            blastCopy = ButtonFor("复制区段 FASTA", delegate { if (blastPreview.TextLength > 0) Clipboard.SetText(blastPreview.Text); }); blastCopy.Name = "blastCopy"; exportActions.Controls.Add(blastCopy);
            blastExportFasta = ButtonFor("导出区段 FASTA…", ExportBlastFasta); blastExportFasta.Name = "blastExportFasta"; exportActions.Controls.Add(blastExportFasta);
            var website = ButtonFor("打开 NCBI（推荐设置）", OpenBlastWebsite); website.Name = "blastWebsite"; exportActions.Controls.Add(website);
            blastPreview = new TextBox { Name = "blastPreview", AccessibleName = "用于网页 BLAST 的区段 FASTA 模板", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10F), BackColor = Color.FromArgb(247, 250, 251), Margin = new Padding(0, 7, 0, 8) }; root.Controls.Add(blastPreview, 0, 2);
            var files = new TableLayoutPanel { Name = "blastFiles", ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
            files.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            files.RowStyles.Add(new RowStyle(SizeType.AutoSize)); files.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            blastFiles = files; root.Controls.Add(files, 0, 3);
            blastXmlFile = BlastFileRow(files, 0, "完整 BLAST XML", "blastXmlFile", "选择 XML…", "XML 文件|*.xml|所有文件|*.*");
            blastFastaFile = BlastFileRow(files, 1, "区段 FASTA", "blastFastaFile", "选择 FASTA…", "FASTA 文件|*.fasta;*.fa;*.fna;*.txt|所有文件|*.*");
            var options = new TableLayoutPanel { Name = "blastOptions", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty }; blastOptions = options; root.Controls.Add(options, 0, 4);
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); options.RowStyles.Add(new RowStyle(SizeType.Absolute)); options.RowStyles.Add(new RowStyle(SizeType.Absolute));
            var expected = BlastFlow(); options.Controls.Add(expected, 0, 0);
            expected.Controls.Add(BlastLabel("预期登录号（可空）")); blastExpected = new TextBox { Name = "blastExpected", Width = 165, Margin = new Padding(0, 3, 12, 3) }; expected.Controls.Add(blastExpected);
            expected.Controls.Add(BlastLabel("区段")); blastExpectedRole = new ComboBox { Name = "blastExpectedRole", Width = 72, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 3) }; blastExpectedRole.Items.AddRange(new object[] { "F2", "B2", "F", "R" }); blastExpectedRole.SelectedIndex = 0; expected.Controls.Add(blastExpectedRole);
            expected.Controls.Add(BlastLabel("该区段3′末位坐标（0=未指定）")); blastSite = BlastNumber("blastSite", 0, 0, 2000000000); blastSite.Width = 145; expected.Controls.Add(blastSite);
            var thresholds = BlastFlow(); options.Controls.Add(thresholds, 0, 1);
            thresholds.Controls.Add(BlastLabel("每区段最多错配")); blastMismatches = BlastNumber("blastMismatches", 4, 0, 10); thresholds.Controls.Add(blastMismatches);
            thresholds.Controls.Add(BlastLabel("布局跨度上限(bp)")); blastSpan = BlastNumber("blastSpan", 1000, 100, 10000); blastSpan.Width = 90; thresholds.Controls.Add(blastSpan);
            blastNetwork = new CheckBox { Name = "blastNetwork", Text = "联网补齐公开参考区段（已有缓存可离线）", Checked = true, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 5) }; root.Controls.Add(blastNetwork, 0, 5);
            var reviewActions = BlastFlow(); root.Controls.Add(reviewActions, 0, 6);
            blastRun = ButtonFor("开始批量复核", StartBlast); blastRun.Name = "blastRun"; blastRun.BackColor = teal; blastRun.ForeColor = Color.White; reviewActions.Controls.Add(blastRun);
            blastCancel = ButtonFor("取消复核", delegate { if (blastCancellation != null) blastCancellation.Cancel(); }); blastCancel.Name = "blastCancel"; reviewActions.Controls.Add(blastCancel);
            blastOpenReport = ButtonFor("打开复核报告", delegate { if (File.Exists(blastReportPath)) { try { Process.Start(blastReportPath); } catch (Exception ex) { blastState.Text = "报告未打开：" + ex.Message; } } }); blastOpenReport.Name = "blastOpenReport"; reviewActions.Controls.Add(blastOpenReport);
            blastState = new Label { Name = "blastState", Text = "可载入当前候选导出模板，也可直接选择已有 XML / FASTA 文件。", AutoSize = true, Dock = DockStyle.Fill, ForeColor = teal, Margin = new Padding(0, 3, 0, 5) }; root.Controls.Add(blastState, 0, 7);
            root.Controls.Add(new Label { Name = "blastScope", Text = "预期坐标使用所选参考序列的基因组坐标，不能直接填输入模板的相对坐标。复核不重新上传引物。\r\nLAMP 按六核心区段、RPA 按 F/R 组合；实际错配保留。结果是计算初筛，未检出其他组合不能判定特异性通过。", AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray, Font = new Font(Font.FontFamily, 9F), Margin = new Padding(0, 2, 0, 6) }, 0, 8);
            blastReport = new TextBox { Name = "blastReport", AccessibleName = "BLAST 批量复核进度和结果", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10F), BackColor = Color.White }; root.Controls.Add(blastReport, 0, 9);
            // Measure wrapped flows at the available width; nested AutoSize
            // tables otherwise retain a height measured at their minimum width.
            root.RowStyles[4].SizeType = SizeType.Absolute;
            bool fittingOptions = false;
            root.Layout += delegate
            {
                if (fittingOptions) return;
                fittingOptions = true;
                try
                {
                    int width = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal - options.Margin.Horizontal);
                    int first = expected.GetPreferredSize(new Size(width, 0)).Height;
                    int second = thresholds.GetPreferredSize(new Size(width, 0)).Height;
                    options.RowStyles[0].Height = first; options.RowStyles[1].Height = second;
                    root.RowStyles[4].Height = first + second;
                }
                finally { fittingOptions = false; }
            };
            blastElapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 }; blastElapsedTimer.Tick += delegate { UpdateBlastElapsed(); };
            Disposed += delegate { if (blastElapsedTimer != null) { blastElapsedTimer.Stop(); blastElapsedTimer.Dispose(); blastElapsedTimer = null; } };
            blastXmlFile.TextChanged += delegate { InvalidateBlastReview(); RefreshBlastControls(); };
            blastFastaFile.TextChanged += delegate { InvalidateBlastReview(); RefreshBlastControls(); };
            blastExpected.TextChanged += delegate { InvalidateBlastReview(); };
            blastExpectedRole.SelectedIndexChanged += delegate { InvalidateBlastReview(); };
            blastSite.ValueChanged += delegate { InvalidateBlastReview(); };
            blastMismatches.ValueChanged += delegate { InvalidateBlastReview(); };
            blastSpan.ValueChanged += delegate { InvalidateBlastReview(); };
            blastNetwork.CheckedChanged += delegate { InvalidateBlastReview(); };
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedTab == blastPage && !blastBusy) LoadBlastCandidate(); };
            RefreshBlastControls();
        }
        private FlowLayoutPanel BlastFlow()
        { return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 2) }; }
        private string ReadBlastWebUrl() { return BlastWebPreset.Create(blastWebOrganism.Text); }
        private void OpenBlastWebsite(object sender, EventArgs e)
        {
            try { Process.Start(ReadBlastWebUrl()); }
            catch (Exception ex) { blastGuideTabs.SelectedIndex = 0; blastState.Text = "网页未打开：" + ex.Message; }
        }
        private Label BlastLabel(string text)
        { return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 6, 3) }; }
        private NumericUpDown BlastNumber(string name, int value, int min, int max)
        { return new NumericUpDown { Name = name, Minimum = min, Maximum = max, Value = value, Width = 60, Margin = new Padding(0, 3, 12, 3) }; }
        private TextBox BlastFileRow(TableLayoutPanel row, int index, string label, string name, string buttonText, string filter)
        {
            row.Controls.Add(BlastLabel(label), 0, index);
            var box = new TextBox { Name = name, AccessibleName = label + " 文件路径", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 10, 8) }; row.Controls.Add(box, 1, index);
            var button = ButtonFor(buttonText, delegate { using (var dialog = new OpenFileDialog { Filter = filter, Title = "选择" + label }) if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; }); button.Name = name + "Choose"; row.Controls.Add(button, 2, index); return box;
        }
        private BlastQuerySet CurrentBlastQueries()
        {
            if (lampResult != null) { var set = SelectedLampSet(); return set == null ? null : BlastQueryBuilder.ForLamp(set, LampReportWriter.ModeName(lampResult)); }
            if (snpResult != null) { var set = SelectedSnpSet(); return set == null ? null : BlastQueryBuilder.ForSnp(set); }
            var pair = SelectedPair(); return pair == null ? null : BlastQueryBuilder.ForRpa(pair);
        }
        private void LoadBlastCandidate()
        {
            if (blastBusy) return;
            try { blastQueries = CurrentBlastQueries(); blastPreview.Text = blastQueries == null ? "" : BlastManualTemplate.Create(blastQueries); }
            catch (Exception ex) { blastQueries = null; blastPreview.Clear(); blastState.Text = "当前候选无法导出 BLAST 模板：" + ex.Message; RefreshBlastControls(); return; }
            if (String.IsNullOrEmpty(blastReportPath)) blastState.Text = blastQueries == null ? "尚无设计候选；仍可选择已有 XML 和对应区段 FASTA 复核。" : "已载入：" + blastQueries.CandidateLabel + "。导出文件用于网页 BLAST；复核以选取的文件为准。";
            RefreshBlastControls();
        }
        private void ExportBlastFasta(object sender, EventArgs e)
        {
            if (blastPreview.TextLength == 0) return;
            using (var dialog = new SaveFileDialog { Title = "导出网页 BLAST 区段模板", Filter = "区段 FASTA (*.fasta)|*.fasta", FileName = BlastManualTemplate.GroupName(blastQueries) + "_BLAST区段.fasta", DefaultExt = "fasta", AddExtension = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, blastPreview.Text, new UTF8Encoding(false)); blastFastaFile.Text = dialog.FileName; blastState.Text = "已导出区段 FASTA。用此文件在网页 BLAST 查询，下载全部查询的完整 XML 后选择复核。"; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "FASTA 导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
        private void RefreshBlastControls()
        {
            if (blastRun == null) return;
            bool idle = !blastBusy && !busy;
            blastLoad.Enabled = idle; blastCopy.Enabled = idle && blastPreview.TextLength > 0; blastExportFasta.Enabled = blastCopy.Enabled;
            blastFiles.Enabled = idle; blastOptions.Enabled = idle; blastNetwork.Enabled = idle;
            blastRun.Enabled = idle && File.Exists(blastXmlFile.Text.Trim()) && File.Exists(blastFastaFile.Text.Trim());
            blastCancel.Enabled = blastBusy; blastOpenReport.Enabled = idle && File.Exists(blastReportPath);
        }
        private void InvalidateBlastReview()
        {
            if (blastBusy || blastReport == null) return;
            blastBatchResult = null; blastReportPath = null; blastReport.Clear(); blastState.Text = "文件或复核设置已更改，请重新复核。"; RefreshBlastControls();
        }
        private void ClearBlastResults()
        {
            if (blastPreview == null || blastBusy) return;
            blastQueries = null; blastPreview.Clear(); InvalidateBlastReview(); RefreshBlastControls();
        }
        private LampBatchReview.Settings ReadBlastSettings()
        {
            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BLAST复核");
            return new LampBatchReview.Settings { Xml = blastXmlFile.Text.Trim(), Fasta = blastFastaFile.Text.Trim(), ExpectedAccession = blastExpected.Text.Trim(), ExpectedRole = blastExpectedRole.Text, ExpectedSite = (int)blastSite.Value,
                MaxMismatches = (int)blastMismatches.Value, MaxSpan = (int)blastSpan.Value, Network = blastNetwork.Checked, CacheDirectory = Path.Combine(basePath, "参考缓存"),
                Output = Path.Combine(basePath, "结果", "复核_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")) };
        }
        private void UpdateBlastElapsed()
        {
            if (!blastBusy || blastElapsed == null || blastState.IsDisposed) return;
            int seconds = (int)blastElapsed.Elapsed.TotalSeconds;
            blastState.Text = blastProgressText + " · 已用时 " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
        private void OnBlastUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (!InvokeRequired) { action(); return; }
            try { BeginInvoke(action); } catch (InvalidOperationException) { if (!IsDisposed && IsHandleCreated) throw; }
        }
        private void StartBlast(object sender, EventArgs e)
        {
            if (blastBusy || busy) return;
            var settings = ReadBlastSettings();
            if (!File.Exists(settings.Xml) || !File.Exists(settings.Fasta)) { blastState.Text = "请选择存在的完整 XML 和对应区段 FASTA 文件。"; return; }
            InvalidateBlastReview(); blastCancellation = new CancellationTokenSource(); var token = blastCancellation.Token;
            blastBusy = true; blastProgressText = "正在读取文件…"; blastElapsed = Stopwatch.StartNew(); blastElapsedTimer.Start();
            SetInputEnabled(false); cancel.Enabled = false; grid.Enabled = false; RefreshBlastControls(); UpdateBlastElapsed();
            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate(object s, DoWorkEventArgs a)
            {
                var analysis = LampBatchReview.Engine.Run(settings, delegate(string message) { worker.ReportProgress(0, message); }, token);
                token.ThrowIfCancellationRequested(); string path = LampBatchReview.Engine.WriteReport(settings, analysis);
                File.WriteAllText(Path.Combine(settings.Output, "BLAST批量复核报告.txt"), LampBatchReview.Engine.TextReport(settings, analysis), new UTF8Encoding(true));
                a.Result = new object[] { analysis, path };
            };
            worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs a) { OnBlastUi(delegate { blastProgressText = Convert.ToString(a.UserState); blastReport.AppendText(blastProgressText + Environment.NewLine); UpdateBlastElapsed(); }); };
            worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs a)
            {
                worker.Dispose();
                OnBlastUi(delegate
                {
                if (blastElapsedTimer != null) blastElapsedTimer.Stop(); if (blastElapsed != null) blastElapsed.Stop();
                blastElapsed = null; blastBusy = false; if (blastCancellation != null) { blastCancellation.Dispose(); blastCancellation = null; } if (IsDisposed) return;
                SetInputEnabled(true); grid.Enabled = true;
                if (a.Error != null) { blastState.Text = a.Error is OperationCanceledException ? "复核已取消，未完成特异性初筛。" : "复核未完成：" + a.Error.Message; blastReport.AppendText("未完成复核，不能按无脱靶或通过解释。" + Environment.NewLine); }
                else
                {
                    var output = (object[])a.Result; blastBatchResult = (LampBatchReview.Analysis)output[0]; blastReportPath = (string)output[1];
                    blastReport.Text = LampBatchReview.Engine.TextReport(settings, blastBatchResult) + Environment.NewLine + "报告：" + blastReportPath;
                    bool incomplete = blastBatchResult.FailedWindows > 0 || blastBatchResult.Truncated || blastBatchResult.MissingQueries;
                    blastState.Text = incomplete ? "复核存在未完成项，请查看报告。" : "批量复核完成；结果是计算初筛，请查看组合与错配。";
                }
                RefreshBlastControls();
                });
            };
            worker.RunWorkerAsync();
        }
    }
}
