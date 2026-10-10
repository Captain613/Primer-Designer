using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RpaDesigner;
using Batch = LampBatchReview;

// Hidden offline native-control checks: no website, clipboard, dialog or network.
internal static class BlastUiCheck
{
    private const string PrivateTitle = "PRIVATE_LOCAL_FASTA_TITLE_NOT_FOR_UPLOAD";
    private static readonly List<string> notes = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static readonly MethodInfo visibleState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);
    private static Exception dispatchError;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { dispatchError = e.Exception; };
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Control.CheckForIllegalCrossThreadCalls = true;
            using (var form = new MainForm())
            {
                Handles(form); Pump(form); notes.Add("Assembly=" + typeof(MainForm).Assembly.Location);
                Check(!Field<Button>(form, "blastRun").Enabled && !Field<Button>(form, "blastOpenReport").Enabled, "Empty file selection cannot start or display an old result.");
                Check(Field<TextBox>(form, "blastExpected").TextLength == 0 && Field<NumericUpDown>(form, "blastSite").Value == 0, "No hard-coded CYP2C9 target for other users.");
                Check(form.Controls.Find("blastEnabled", true).Length == 0 && form.Controls.Find("blastEmail", true).Length == 0, "Online submit and contact-email workflow removed.");
                Check(Field<TextBox>(form, "blastWebOrganism").Text == BlastWebPreset.DefaultOrganism, "New webpage defaults to human and remains editable.");
                var help = (TextBox)form.Controls.Find("blastWebHelp", true).Single();
                Check(help.ReadOnly && help.Multiline && help.ScrollBars == ScrollBars.Vertical && help.Text.Contains("Max matches in a query range = 0") && help.Text.Contains("布局跨度上限 = 1000 bp") && help.Text.Contains("不会自动提交序列"), "Scrollable instructions explain webpage values and local review boundaries.");
                for (int mode = 0; mode < 6; mode++) CheckTemplate(form, mode, output);
                CheckManualImport(form, output); CheckLayout(form, output);
                Check(!form.Visible, "Test never displays a desktop window."); AssertNoNetworkState(form, "Final state");
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); }
        notes.Add("Assertions=" + checks + "; failures=" + errors.Count); foreach (string error in errors) notes.Add("FAIL " + error);
        notes.Add("Offline only: no BLAST submission, network request, dialog, clipboard or visible desktop window.");
        notes.Add(errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL"); File.WriteAllLines(Path.Combine(output, "blast-ui-test-report.txt"), notes.ToArray(), new UTF8Encoding(true)); return errors.Count == 0 ? 0 : 1;
    }
    private static void CheckTemplate(MainForm form, int mode, string output)
    {
        PrepareFixture(form, mode); Invoke(form, "LoadBlastCandidate"); Pump(form);
        string preview = Field<TextBox>(form, "blastPreview").Text;
        Check(preview.Length > 0 && Field<Button>(form, "blastCopy").Enabled && Field<Button>(form, "blastExportFasta").Enabled, "Mode " + mode + " can copy/export actual queries without enabling online upload.");
        string path = Path.Combine(output, "mode-" + mode + ".fasta"); File.WriteAllText(path, preview);
        var primers = Batch.Engine.ReadFasta(path); var profiles = Batch.Engine.BuildProfiles(primers);
        Check(profiles.Count == (mode == 0 || mode == 2 ? 1 : mode == 1 ? 3 : 2), "Mode " + mode + " retains actual independent reactions.");
        Check(primers.All(p => preview.Contains(">" + p.Id) && p.Dna.All(c => "ACGT".Contains(c))), "Mode " + mode + " has importable named DNA records.");
        if (mode >= 2)
        {
            LampPrimerSet set = Field<LampDesignResult>(form, "lampResult").Sets[0];
            Check(!primers.Any(p => p.Dna == set.FIP.Sequence || p.Dna == set.BIP.Sequence), "Mode " + mode + " splits full inner primers.");
            if (mode == 4) Check(profiles.All(p => p.Primers[4].Dna == set.BIP.ActivatedRegions[1].Sequence), "PA-LAMP uses active B2.");
        }
        AssertPreviewPrivate(form, "Mode " + mode); AssertNoNetworkState(form, "Template only");
    }
    private static void CheckManualImport(MainForm form, string output)
    {
        Set(form, "result", null); Set(form, "snpResult", null); Set(form, "lampResult", null); Field<DataGridView>(form, "grid").Rows.Clear(); Invoke(form, "ClearBlastResults");
        var settings = BlastManualSelfTests.SyntheticFixture(Path.Combine(output, "manual-input"), "gui", true, false);
        Field<TextBox>(form, "blastXmlFile").Text = settings.Xml; Field<TextBox>(form, "blastFastaFile").Text = settings.Fasta;
        Field<TextBox>(form, "blastExpected").Text = settings.ExpectedAccession; Field<NumericUpDown>(form, "blastSite").Value = settings.ExpectedSite;
        Field<CheckBox>(form, "blastNetwork").Checked = false;
        var uiSettings = (Batch.Settings)Invoke(form, "ReadBlastSettings"); var window = Batch.Engine.Read(settings).Windows.Single();
        Directory.CreateDirectory(uiSettings.CacheDirectory); File.Copy(Batch.Engine.CachePath(settings, window), Batch.Engine.CachePath(uiSettings, window), true);
        Check(Field<Button>(form, "blastRun").Enabled && Field<BlastQuerySet>(form, "blastQueries") == null, "Existing files can be reviewed without a design candidate.");
        StartAndWait(form);
        var result = Field<Batch.Analysis>(form, "blastBatchResult"); string report = Field<string>(form, "blastReportPath");
        Check(result != null && result.Combinations.Count(c => c.Expected) == 2 && result.FailedWindows == 0, "GUI restores both expected alleles from truncated XML.");
        Check(File.Exists(report) && Field<Button>(form, "blastOpenReport").Enabled, "HTML saved and report button enabled.");
        Check(Field<TextBox>(form, "blastReport").Text.Contains("预期位点组合 2 个") && Field<TextBox>(form, "blastReport").Text.Contains("不等于特异性通过"), "Summary states count and interpretation scope.");
        Field<NumericUpDown>(form, "blastMismatches").Value = 3;
        Check(Field<Batch.Analysis>(form, "blastBatchResult") == null && Field<TextBox>(form, "blastReport").TextLength == 0 && !Field<Button>(form, "blastOpenReport").Enabled, "Changing thresholds invalidates a stale result.");
        Field<NumericUpDown>(form, "blastMismatches").Value = 4;
        string badFasta = Path.Combine(output, "mismatched.fasta"); string original = File.ReadAllText(settings.Fasta); var first = Batch.Engine.ReadFasta(settings.Fasta)[0];
        File.WriteAllText(badFasta, original.Replace(first.Dna, (first.Dna[0] == 'A' ? "C" : "A") + first.Dna.Substring(1)));
        Field<TextBox>(form, "blastFastaFile").Text = badFasta; StartAndWait(form);
        Check(Field<Batch.Analysis>(form, "blastBatchResult") == null && !Field<Button>(form, "blastOpenReport").Enabled && Field<Label>(form, "blastState").Text.Contains("碱基不一致"), "Mismatched XML/FASTA fails visibly and cannot retain previous report.");
        Field<TextBox>(form, "blastFastaFile").Text = settings.Fasta;
        typeof(MainForm).GetMethod("StartBlast", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty });
        Field<CancellationTokenSource>(form, "blastCancellation").Cancel(); WaitIdle(form);
        Check(!Field<Button>(form, "blastOpenReport").Enabled && Field<Label>(form, "blastState").Text.Contains("已取消"), "Cancellation produces no completed assessment.");
        StartAndWait(form);
        string before = Field<string>(form, "blastReportPath"); Field<TabControl>(form, "tabs").SelectedTab = Field<TabPage>(form, "blastPage"); Pump(form);
        Check(before == Field<string>(form, "blastReportPath"), "Switching to the review tab preserves file-based result provenance.");
        Field<TabControl>(form, "blastGuideTabs").SelectedIndex = 1; Pump(form);
        Check(Field<TabControl>(form, "blastGuideTabs").SelectedIndex == 1, "Parameter explanation opens inside the manual review page.");
        Field<TextBox>(form, "blastWebOrganism").Text = "Mus musculus (taxid:10090)";
        string webUrl = (string)Invoke(form, "ReadBlastWebUrl");
        Check(Uri.UnescapeDataString(webUrl).Contains("Mus musculus (taxid:10090)") && !webUrl.Contains(PrivateTitle) && !webUrl.Contains("NC_SYNTH") && !webUrl.Contains("QUERY=") && !webUrl.Contains(settings.Xml), "Website uses selected organism without including local data.");
        Check(before == Field<string>(form, "blastReportPath") && Field<Button>(form, "blastOpenReport").Enabled, "Webpage preferences cannot relabel or invalidate existing XML results.");
        Field<TextBox>(form, "blastWebOrganism").Text = BlastWebPreset.DefaultOrganism;
        Field<TabControl>(form, "blastGuideTabs").SelectedIndex = 0;
    }
    private static void StartAndWait(MainForm form)
    { typeof(MainForm).GetMethod("StartBlast", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty }); WaitIdle(form); }
    private static void WaitIdle(MainForm form)
    {
        Stopwatch wait = Stopwatch.StartNew(); while (Field<bool>(form, "blastBusy") && wait.ElapsedMilliseconds < 30000) { Application.DoEvents(); Thread.Sleep(15); }
        Pump(form); if (Field<bool>(form, "blastBusy")) throw new TimeoutException("Manual review exceeded 30 seconds.");
        if (dispatchError != null) throw new Exception("UI dispatch failed.", dispatchError);
    }
    private static void CheckLayout(MainForm form, string output)
    {
        Field<TabControl>(form, "tabs").SelectedTab = Field<TabPage>(form, "blastPage"); Size normal = form.Size;
        foreach (bool minimum in new[] { false, true })
        {
            form.Size = minimum ? form.MinimumSize : normal; Pump(form); var page = Field<TabPage>(form, "blastPage"); string state = minimum ? "minimum" : "default";
            notes.Add(state + " form=" + form.Size + "; page=" + page.ClientRectangle);
            for (int section = 0; section < 2; section++)
            {
                Field<TabControl>(form, "blastGuideTabs").SelectedIndex = section; Pump(form); CheckTree(page, "BLAST/" + state + "/" + section);
                if (section == 0) Check(Field<TextBox>(form, "blastReport").Height >= form.Font.Height * 3, state + " leaves at least three text lines for the result.");
                else Check(form.Controls.Find("blastWebHelp", true).Single().Height >= form.Font.Height * 10, state + " provides a readable scrolling parameter guide.");
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "blast-" + (section == 0 ? "manual-" : "settings-") + state + ".png"), ImageFormat.Png); }
            }
        }
    }

    private static void PrepareFixture(MainForm form, int mode)
    {
        Set(form, "result", null); Set(form, "snpResult", null); Set(form, "lampResult", null);
        Invoke(form, "ClearBlastResults");
        Field<ComboBox>(form, "designMode").SelectedIndex = mode;
        Field<TextBox>(form, "sequenceBox").Text = WithPrivateTitle(mode == 1 || mode >= 3 ? LampReportWriter.ExampleSnpFasta() : ReportWriter.ExampleFasta());
        DataGridView grid = Field<DataGridView>(form, "grid");
        grid.Rows.Clear(); grid.Rows.Add("#1", "80.0", "200"); grid.CurrentCell = grid.Rows[0].Cells[0];
        // Install after selecting the grid row so ShowPair is not asked to render
        // these minimal query-builder fixtures as complete scientific reports.
        if (mode == 0)
        {
            var result = new DesignResult { Input = SequenceParser.Parse(WithPrivateTitle(ReportWriter.ExampleFasta())), Settings = new DesignSettings() };
            result.Pairs.Add(new PrimerPair { Rank = 1, Forward = Primer("ACGTAGTCGATCGTACGTCA", 1), Reverse = Primer("TGCATCGATGCTAGCTACGA", 180) });
            Set(form, "result", result);
        }
        else if (mode == 1)
        {
            var result = new SnpDesignResult { Input = SnpParser.Parse(WithPrivateTitle(LampReportWriter.ExampleSnpFasta())), Settings = new SnpDesignSettings() };
            result.Sets.Add(new SnpPrimerSet { Rank = 1, ReferenceForward = Primer("ACGTAGTCGATCGTACGTCA", 1), AlternateForward = Primer("ACGTAGTCGATCGTACGTCC", 1),
                CommonReverse = Primer("TGCATCGATGCTAGCTACGA", 180), ControlForward = Primer("GTCACTAGCTAGTCGATGCA", 25) });
            Set(form, "snpResult", result);
        }
        else
        {
            var settings = new LampDesignSettings { SnpMethod = mode == 4 ? "PA-LAMP" : mode == 5 ? "mLAMP" : "AS-LAMP" };
            var result = new LampDesignResult { Input = SequenceParser.Parse(WithPrivateTitle(ReportWriter.ExampleFasta())), Settings = settings };
            if (mode >= 3) result.Snp = SnpParser.Parse(WithPrivateTitle(LampReportWriter.ExampleSnpFasta()));
            result.Sets.Add(LampFixture(mode)); Set(form, "lampResult", result);
        }
    }

    private static Primer Primer(string sequence, int start)
    { return new Primer { Sequence = sequence, Start = start, End = start + sequence.Length - 1, Tm = 60, Gc = 50 }; }

    private static LampRegion Region(string name, string sequence, int start, bool reverse)
    { return new LampRegion { Name = name, Sequence = sequence, Start = start, End = start + sequence.Length - 1, Reverse = reverse, Tm = 60, Gc = 50 }; }

    private static LampOligo Oligo(string name, params LampRegion[] regions)
    {
        string sequence = ""; foreach (LampRegion r in regions) sequence += r.Sequence;
        var p = new LampOligo { Name = name, Sequence = sequence, Metrics = Primer(sequence, 1) }; p.Regions.AddRange(regions); return p;
    }

    private static LampPrimerSet LampFixture(int mode)
    {
        var f1 = Region("F1c", "GTCAGCTAGCTACGATCGTA", 70, true);
        var f2 = Region("F2", "ACGTAGTCGATCGTACGTCA", 30, false);
        var b1 = Region("B1c", "AGCATGCTAGTCGACTAGCA", 120, false);
        var b2 = Region("B2", "TGCATCGATGCTAGCTACGA", 160, true);
        var set = new LampPrimerSet { Rank = 1, SpanStart = 1, SpanEnd = 219, SpanLength = 219,
            F3 = Oligo("F3", Region("F3", "GTCACTAGCTAGTCGATGCA", 1, false)),
            B3 = Oligo("B3", Region("B3", "CGATGCATGACTGCATAGCT", 200, true)),
            FIP = Oligo("FIP", f1, f2), BIP = Oligo("BIP", b1, b2) };
        if (mode == 3 || mode == 5)
        {
            set.SpecificInner = "FIP"; set.FIP.SnpIndex = set.FIP.Sequence.Length - 1;
            set.AlternateInner = Oligo("FIP_alt", f1, Region("F2", "ACGTAGTCGATCGTACGTCC", 30, false));
            set.AlternateInner.SnpIndex = set.AlternateInner.Sequence.Length - 1;
        }
        if (mode == 4)
        {
            LampOligo active = set.BIP; set.SpecificInner = "BIP";
            set.BIP = Oligo("BIP", b1, Region("B2", b2.Sequence + "AGCTAG", 154, true));
            set.AlternateInner = Oligo("BIP_alt", b1, Region("B2", b2.Sequence + "CGCTAG", 154, true));
            foreach (LampOligo precursor in new[] { set.BIP, set.AlternateInner })
            {
                precursor.RnaIndex = active.Sequence.Length; precursor.SnpIndex = precursor.RnaIndex;
                precursor.ThreePrimeBlock = "C3"; precursor.ActivationTailLength = 5;
                precursor.ActivatedSequence = active.Sequence; precursor.ActivatedMetrics = active.Metrics;
                precursor.ActivatedRegions.AddRange(active.Regions);
            }
        }
        return set;
    }

    private static void CheckTree(Control parent, string path)
    {
        foreach (Control child in parent.Controls)
        {
            if (visibleState != null && !(bool)visibleState.Invoke(child, new object[] { 2 })) continue;
            string name = path + "/" + (String.IsNullOrEmpty(child.Name) ? child.GetType().Name : child.Name);
            Rectangle b = child.Bounds, allowed = parent.ClientRectangle;
            Check(b.Width > 0 && b.Height > 0 && b.Left >= -2 && b.Top >= -2 && b.Right <= allowed.Right + 2 && b.Bottom <= allowed.Bottom + 2,
                name + " bounds=" + b + "; parent=" + allowed);
            if (child is Label || child is Button || child is CheckBox)
            {
                int width = Math.Max(1, child.ClientSize.Width - child.Padding.Horizontal - (child is CheckBox ? SystemInformation.MenuCheckSize.Width + 5 : 0));
                int height = child.ClientSize.Height - child.Padding.Vertical;
                using (Graphics g = child.CreateGraphics())
                {
                    Size text = TextRenderer.MeasureText(g, child.Text, child.Font, new Size(width, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    Check(text.Width <= width + 2 && text.Height <= height + 2, name + " text=" + text + "; available=" + width + "x" + height + "; bounds=" + b + "; value=" + child.Text.Replace("\r\n", " / "));
                }
            }
            ComboBox combo = child as ComboBox;
            if (combo != null)
                using (Graphics g = child.CreateGraphics()) foreach (object option in combo.Items)
                {
                    Size text = TextRenderer.MeasureText(g, option.ToString(), combo.Font, Size.Empty, TextFormatFlags.SingleLine);
                    Check(text.Width <= combo.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4 && text.Height <= combo.ClientSize.Height,
                        name + " option='" + option + "' text=" + text + "; client=" + combo.ClientSize);
                }
            if (!(child is TextBoxBase) && !(child is NumericUpDown) && !(child is ComboBox)) CheckTree(child, name);
        }
    }

    private static void AssertPreviewPrivate(MainForm form, string context)
    {
        string preview = Field<TextBox>(form, "blastPreview").Text;
        Check(!preview.Contains(PrivateTitle) && !preview.Contains("offline-ui@example.org") && !preview.Contains("NC_000010.11"),
            context + " FASTA excludes original title, local expected accessions and contact metadata.");
    }
    private static void AssertNoNetworkState(MainForm form, string context)
    {
        Check(!Field<bool>(form, "blastBusy") && Field<CancellationTokenSource>(form, "blastCancellation") == null,
            context + " does not create a network job or cancellation token.");
    }
    private static List<string> FastaSequences(string text)
    {
        var result = new List<string>(); var sequence = new StringBuilder();
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith(">", StringComparison.Ordinal)) { if (sequence.Length > 0) { result.Add(sequence.ToString()); sequence.Length = 0; } }
            else sequence.Append(line.Trim());
        }
        if (sequence.Length > 0) result.Add(sequence.ToString()); return result;
    }
    private static string WithPrivateTitle(string fasta)
    { int line = fasta.IndexOf('\n'); return ">" + PrivateTitle + "\n" + (line >= 0 ? fasta.Substring(line + 1) : fasta); }
    private static T Field<T>(object instance, string name)
    { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static void Set(object instance, string name, object value)
    { instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value); }
    private static object Invoke(object instance, string name)
    { return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null); }
    private static void Handles(Control c) { IntPtr unused = c.Handle; foreach (Control child in c.Controls) Handles(child); }
    private static void Layout(Control c) { c.PerformLayout(); foreach (Control child in c.Controls) Layout(child); c.PerformLayout(); }
    private static void Pump(Control c) { Layout(c); Application.DoEvents(); if (dispatchError != null) throw new Exception("UI dispatch failed.", dispatchError); }
    private static void Check(bool passed, string message) { checks++; if (!passed) errors.Add(message); }
}
