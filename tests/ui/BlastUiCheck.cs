using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RpaDesigner;

// Offline, hidden native-control checks. Never call StartBlast, Show/ShowDialog,
// export dialogs or the system clipboard. Fixtures below are artificial DNA.
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
        string output = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { dispatchError = e.Exception; };
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            using (var form = new MainForm())
            {
                Handles(form); Pump(form);
                notes.Add("Assembly=" + typeof(MainForm).Assembly.Location);
                using (Graphics g = form.CreateGraphics()) notes.Add("DPI=" + g.DpiX + "x" + g.DpiY);
                CheckInitialState(form);
                CheckDatabaseSettings(form);
                CheckElapsedProgress(form);
                CheckOriginalWorker(form);
                for (int mode = 0; mode < 6; mode++) CheckCandidatePreview(form, mode);
                CheckInvalidation(form);
                CheckUnsupportedQueries(form);
                CheckLayout(form, output);
                AssertNoNetworkState(form, "All offline checks");
                Check(!form.Visible, "Test never displays a desktop form.");
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); }
        notes.Add("Assertions=" + checks + "; failures=" + errors.Count);
        foreach (string error in errors) notes.Add("FAIL " + error);
        notes.Add("Offline only: no BLAST submission, external upload, export dialog, clipboard, or visible desktop window.");
        notes.Add(errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(Path.Combine(output, "blast-ui-test-report.txt"), notes.ToArray(), new UTF8Encoding(true));
        return errors.Count == 0 ? 0 : 1;
    }

    private static void CheckInitialState(MainForm form)
    {
        Check(!Field<CheckBox>(form, "blastEnabled").Checked, "Online BLAST is off by default.");
        Check(!Field<Control>(form, "blastOptions").Enabled, "BLAST settings are disabled while optional integration is off.");
        Check(!Field<Button>(form, "blastRun").Enabled && !Field<Button>(form, "blastCancel").Enabled && !Field<Button>(form, "blastExport").Enabled,
            "Submit, cancel and export begin disabled without a job or result.");
        Field<TabControl>(form, "tabs").SelectedTab = Field<TabPage>(form, "blastPage");
        Invoke(form, "LoadBlastCandidate");
        Check(Field<BlastQuerySet>(form, "blastQueries") == null && Field<TextBox>(form, "blastPreview").TextLength == 0,
            "An empty candidate grid yields no prepared upload.");
        Field<CheckBox>(form, "blastEnabled").Checked = true;
        Check(Field<Control>(form, "blastOptions").Enabled && !Field<Button>(form, "blastRun").Enabled,
            "Enabling BLAST alone does not enable submission without a selected candidate.");
        AssertNoNetworkState(form, "Enabling without candidates");
        Field<CheckBox>(form, "blastEnabled").Checked = false;
    }

    private static void CheckDatabaseSettings(MainForm form)
    {
        ComboBox database = Field<ComboBox>(form, "blastDatabase");
        string[] expected = { "refseq_representative_genomes", "refseq_genomes", "core_nt" };
        Check(database.Items.Count == expected.Length && database.SelectedIndex == 0, "Three database choices with reference genomes as default.");
        Field<TextBox>(form, "blastEmail").Text = " offline-ui@example.org ";
        Field<TextBox>(form, "blastExpected").Text = " NC_000010.11, NC_000001.11 ";
        for (int i = 0; i < expected.Length; i++)
        {
            database.SelectedIndex = i;
            BlastSettings settings = (BlastSettings)Invoke(form, "ReadBlastSettings");
            Check(settings.Database == expected[i], "Database UI maps to documented NCBI ID: " + expected[i]);
            Check(settings.Email == "offline-ui@example.org" && settings.ExpectedAccessions == "NC_000010.11, NC_000001.11",
                "Contact and optional accession settings are read without surrounding whitespace.");
        }
        database.SelectedIndex = 0;
        BlastSettings defaults = (BlastSettings)Invoke(form, "ReadBlastSettings");
        Check(defaults.MinCoverage == 90 && defaults.MinIdentity == 80 && defaults.MaxLocusSpan == 2000 && defaults.HitListSize == 100,
            "BLAST UI preserves default coverage, identity, locus span and hit count.");
        Check(Field<Label>(form, "blastScope").Text.Contains("全部物种") && Field<Label>(form, "blastScope").Text.Contains("core_nt"),
            "Search scope and core_nt chromosome limitation are visible.");
        Check(Field<Label>(form, "blastScope").Text.Contains("本程序要求填写联系邮箱")
            && !Field<Label>(form, "blastScope").Text.Contains("联系邮箱为 NCBI 要求"),
            "The UI attributes the contact-email policy to this app and NCBI's developer guidance.");
        AssertNoNetworkState(form, "Editing BLAST settings");
    }

    private static void CheckElapsedProgress(MainForm form)
    {
        var timer = Field<System.Windows.Forms.Timer>(form, "blastElapsedTimer");
        var label = Field<Label>(form, "blastState");
        string previous = label.Text;
        Check(timer != null && timer.Interval == 1000 && !timer.Enabled, "BLAST elapsed timer starts idle and ticks once per second.");
        var elapsed = Stopwatch.StartNew();
        try
        {
            Set(form, "blastBusy", true);
            Set(form, "blastElapsed", elapsed);
            Set(form, "blastProgressText", "NCBI 正在计算，RID OFFLINE_FIXTURE");
            Invoke(form, "UpdateBlastElapsed");
            string initial = label.Text;
            Check(initial.Contains("NCBI 正在计算") && initial.Contains("已用时 00:00"), "Running status shows stage and elapsed time.");
            timer.Start();
            Stopwatch deadline = Stopwatch.StartNew();
            while (label.Text == initial && deadline.ElapsedMilliseconds < 2500)
            {
                Thread.Sleep(40);
                Application.DoEvents();
            }
            Check(label.Text != initial && label.Text.Contains("已用时 00:"),
                "Elapsed time advances during a simulated server wait without a progress event or network request.");
        }
        finally
        {
            timer.Stop(); elapsed.Stop();
            Set(form, "blastBusy", false);
            Set(form, "blastElapsed", null);
            Set(form, "blastProgressText", null);
            label.Text = previous;
        }
        AssertNoNetworkState(form, "Offline elapsed display");
    }

    private static void CheckOriginalWorker(MainForm form)
    {
        Field<CheckBox>(form, "blastEnabled").Checked = false;
        Field<ComboBox>(form, "designMode").SelectedIndex = 0;
        Field<TextBox>(form, "sequenceBox").Text = WithPrivateTitle(ReportWriter.ExampleFasta());
        Field<NumericUpDown>(form, "pairCount").Value = 1;
        typeof(MainForm).GetMethod("StartDesign", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty });
        Stopwatch wait = Stopwatch.StartNew();
        while (Field<bool>(form, "busy") && wait.ElapsedMilliseconds < 60000)
        {
            Application.DoEvents();
            if (dispatchError != null) throw new Exception("UI dispatch failed.", dispatchError);
            Thread.Sleep(15);
        }
        Check(!Field<bool>(form, "busy"), "Existing offline design worker completes while BLAST is off.");
        if (Field<bool>(form, "busy")) throw new TimeoutException("Offline RPA worker exceeded 60 seconds.");
        Pump(form);
        DesignResult result = Field<DesignResult>(form, "result");
        Check(result != null && result.Pairs.Count > 0, "Existing RPA design still produces a candidate with BLAST disabled.");
        Invoke(form, "LoadBlastCandidate");
        Check(Field<BlastQuerySet>(form, "blastQueries") != null && !Field<Button>(form, "blastRun").Enabled,
            "A locally designed candidate can be previewed while online submission remains disabled.");
        AssertPreviewPrivate(form, "Offline RPA worker");
        AssertNoNetworkState(form, "Original design with online option off");
    }

    private static void CheckCandidatePreview(MainForm form, int mode)
    {
        PrepareFixture(form, mode);
        Field<CheckBox>(form, "blastEnabled").Checked = false;
        Invoke(form, "LoadBlastCandidate");
        BlastQuerySet queries = Field<BlastQuerySet>(form, "blastQueries");
        Check(queries != null && queries.Queries.Count > 0, "Mode " + mode + " loads selected candidate into query preview.");
        Check(!Field<Button>(form, "blastRun").Enabled, "Mode " + mode + " cannot run with online option off.");
        if (queries != null)
        {
            string preview = Field<TextBox>(form, "blastPreview").Text;
            var sequences = FastaSequences(preview);
            Check(sequences.Count == queries.Queries.Count, "Mode " + mode + " preview contains exactly the prepared query records.");
            foreach (BlastQuery query in queries.Queries)
            {
                Check(preview.Contains(">" + query.Id) && sequences.Contains(query.Sequence), "Mode " + mode + " preview contains query " + query.Id + ".");
                foreach (char c in query.Sequence) Check("ACGT".IndexOf(c) >= 0, "Mode " + mode + " submits only DNA-equivalent query bases.");
            }
            if (mode >= 2)
            {
                LampPrimerSet set = Field<LampDesignResult>(form, "lampResult").Sets[0];
                Check(!sequences.Contains(set.FIP.Sequence) && !sequences.Contains(set.BIP.Sequence), "Mode " + mode + " splits full inner primers into binding regions.");
                Check(sequences.Contains(set.FIP.Regions[0].Sequence) && sequences.Contains(set.FIP.Regions[1].Sequence), "Mode " + mode + " preview includes both FIP binding regions.");
                if (mode == 4) Check(sequences.Contains(set.BIP.ActivatedRegions[1].Sequence) && !preview.Contains("[C3]") && !preview.Contains("[r"),
                    "PA-LAMP checks the active B2 binding region without literal RNA/C3 order notation.");
            }
        }
        AssertPreviewPrivate(form, "Mode " + mode);
        Field<CheckBox>(form, "blastEnabled").Checked = true;
        Check(Field<Button>(form, "blastRun").Enabled && !Field<Button>(form, "blastCancel").Enabled,
            "Mode " + mode + " enables explicit submission without starting a job.");
        AssertNoNetworkState(form, "Toggle only, mode " + mode);
        Field<CheckBox>(form, "blastEnabled").Checked = false;
        Check(!Field<Button>(form, "blastRun").Enabled && Field<BlastQuerySet>(form, "blastQueries") == queries,
            "Mode " + mode + " disabling online preserves preview but disables submission.");
    }

    private static void CheckInvalidation(MainForm form)
    {
        PrepareFixture(form, 1); SeedCompletedReport(form);
        ComboBox mismatch = Field<ComboBox>(form, "mismatchMode");
        mismatch.SelectedIndex = mismatch.SelectedIndex == 1 ? 2 : 1;
        AssertCleared(form, "Changing SNP mismatch strategy");
        PrepareFixture(form, 1); SeedCompletedReport(form);
        TextBox input = Field<TextBox>(form, "sequenceBox");
        string text = input.Text; int marker = text.IndexOf('['); char alternate = 'A';
        foreach (char candidate in "ACGT")
            if (candidate != text[marker + 1] && candidate != text[marker + 3]) { alternate = candidate; break; }
        input.Text = text.Substring(0, marker + 3) + alternate + text.Substring(marker + 4);
        AssertCleared(form, "Changing the annotated SNP alternate allele");
        PrepareFixture(form, 5); SeedCompletedReport(form);
        Field<TextBox>(form, "sequenceBox").Clear();
        AssertCleared(form, "Clearing template input");
        PrepareFixture(form, 2); SeedCompletedReport(form);
        Field<ComboBox>(form, "designMode").SelectedIndex = 4;
        AssertCleared(form, "Switching amplification mode");
    }

    private static void SeedCompletedReport(MainForm form)
    {
        Invoke(form, "LoadBlastCandidate");
        Set(form, "blastCompletedQueries", Field<BlastQuerySet>(form, "blastQueries"));
        Set(form, "blastCompletedSettings", Invoke(form, "ReadBlastSettings"));
        Set(form, "blastResult", new BlastResult { Rid = "OFFLINE_FIXTURE", Database = "refseq_representative_genomes", RawXml = "<offline/>" });
        Field<TextBox>(form, "blastReport").Text = "Offline simulated completed report; no network request.";
        Invoke(form, "RefreshBlastControls");
        Check(Field<Button>(form, "blastExport").Enabled, "A simulated completed result enables export availability without opening a dialog.");
    }

    private static void CheckUnsupportedQueries(MainForm form)
    {
        foreach (string sequence in new[] { "ACGTAC", new String('A', 1001) })
        {
            PrepareFixture(form, 0);
            Field<DesignResult>(form, "result").Pairs[0].Forward.Sequence = sequence;
            Field<CheckBox>(form, "blastEnabled").Checked = true;
            Invoke(form, "LoadBlastCandidate"); Pump(form);
            Check(Field<BlastQuerySet>(form, "blastQueries") == null && Field<TextBox>(form, "blastPreview").TextLength == 0
                && !Field<Button>(form, "blastRun").Enabled,
                "Unsupported query length " + sequence.Length + " cannot leave a partial prepared upload or enabled submission.");
            string state = Field<Label>(form, "blastState").Text;
            Check(state.Contains(sequence.Length < 7 ? "7 nt" : "1,000 nt"),
                "Unsupported query length " + sequence.Length + " reports the explicit limit without a dialog: " + state);
            AssertNoNetworkState(form, "Unsupported query length " + sequence.Length);
        }
        Field<CheckBox>(form, "blastEnabled").Checked = false;
    }

    private static void AssertCleared(MainForm form, string reason)
    {
        Check(Field<BlastQuerySet>(form, "blastQueries") == null && Field<BlastQuerySet>(form, "blastCompletedQueries") == null
            && Field<BlastSettings>(form, "blastCompletedSettings") == null && Field<BlastResult>(form, "blastResult") == null,
            reason + " invalidates prepared and completed BLAST data.");
        Check(Field<TextBox>(form, "blastPreview").TextLength == 0 && Field<TextBox>(form, "blastReport").TextLength == 0
            && !Field<Button>(form, "blastRun").Enabled && !Field<Button>(form, "blastExport").Enabled,
            reason + " clears displayed queries/report and disables stale actions.");
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

    private static void CheckLayout(MainForm form, string output)
    {
        Set(form, "result", null); Set(form, "snpResult", null); Set(form, "lampResult", null);
        Field<DataGridView>(form, "grid").Rows.Clear(); Invoke(form, "ClearBlastResults");
        TabPage page = Field<TabPage>(form, "blastPage"); Field<TabControl>(form, "tabs").SelectedTab = page;
        Field<CheckBox>(form, "blastEnabled").Checked = true;
        Size normal = form.Size;
        foreach (bool minimum in new[] { false, true })
        {
            form.Size = minimum ? form.MinimumSize : normal; Pump(form);
            string state = minimum ? "minimum" : "default";
            notes.Add(state + " form=" + form.Size + "; page=" + page.ClientRectangle);
            CheckTree(page, "BLAST/" + state);
            Control options = Field<Control>(form, "blastOptions");
            int measuredRows = 0;
            foreach (Control row in options.Controls) measuredRows += row.GetPreferredSize(new Size(options.ClientSize.Width, 0)).Height;
            Check(options.Height <= measuredRows + 4, state + " parameter panel is compact: height=" + options.Height + "; measured rows=" + measuredRows);
            Check(Field<TextBox>(form, "blastReport").Height >= form.Font.Height * 3, state + " leaves at least three text lines for results.");
            if (minimum)
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "blast-page-minimum.png"), ImageFormat.Png); }
        }
        Field<CheckBox>(form, "blastEnabled").Checked = false;
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
