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

// Hidden, in-process WinForms check; no desktop gestures, dialogs or clipboard.
internal static class MLampUiCheck
{
    private static readonly List<string> notes = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static readonly Color snpRed = Color.FromArgb(211, 47, 47);
    private static readonly Color mismatchBlue = Color.FromArgb(37, 99, 235);
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
                Handles(form); LayoutTree(form); Application.DoEvents();
                notes.Add("Assembly=" + typeof(MainForm).Assembly.Location);
                ComboBox mode = Field<ComboBox>(form, "designMode");
                Check(mode.Items.Count == 6 && mode.Items[5].ToString().Contains("mLAMP"), "Sixth mode exposes the paper-based mLAMP preset.");
                mode.SelectedIndex = 5; CheckPreset(form);
                ComboBox mismatch = Field<ComboBox>(form, "mismatchMode");
                Check(mismatch.Enabled, "mLAMP allows comparison of no/second/third-position artificial mismatch.");
                foreach (int i in new int[] { 0, 1, 2 })
                {
                    mismatch.SelectedIndex = i;
                    LampDesignSettings s = Settings(form);
                    Check(s.SnpMethod == "mLAMP" && s.SnpOrientation == "FIP" && s.ExtraMismatchFromThreePrime == (i == 0 ? 0 : i + 1), "UI mismatch option maps to mLAMP coordinate strategy " + i + ".");
                }
                Field<CheckBox>(form, "lampLoops").Checked = true;
                Invoke(form, "ResetLampSettings"); CheckPreset(form);
                foreach (int previous in new int[] { 4, 3, 2, 1, 0 })
                {
                    mode.SelectedIndex = previous;
                    Check(!Property<bool>(form, "IsMLampMode"), "Other mode " + previous + " is not mLAMP.");
                    if (previous == 4)
                    {
                        Check(!mismatch.Enabled && Settings(form).SnpMethod == "PA-LAMP", "PA switching retains RNA activation controls.");
                        Check(Field<ComboBox>(form, "lampOrientation").SelectedIndex == 2, "PA mode restores BIP orientation.");
                    }
                    if (previous == 3) Check(Field<ComboBox>(form, "lampOrientation").Enabled, "AS-LAMP still allows direction choice.");
                    mode.SelectedIndex = 5; CheckPreset(form);
                }
                // Literature modes must not overwrite freely chosen AS-LAMP inputs.
                foreach (int direction in new int[] { 1, 2 })
                {
                    mode.SelectedIndex = 3;
                    Field<ComboBox>(form, "lampOrientation").SelectedIndex = direction;
                    mismatch.SelectedIndex = direction - 1;
                    Field<CheckBox>(form, "lampLoops").Checked = direction == 2;
                    mode.SelectedIndex = 5; CheckPreset(form);
                    mode.SelectedIndex = 4; mode.SelectedIndex = 3;
                    Check(Field<ComboBox>(form, "lampOrientation").SelectedIndex == direction && mismatch.SelectedIndex == direction - 1
                        && Field<CheckBox>(form, "lampLoops").Checked == (direction == 2), "AS-LAMP restores direction/mismatch/loop preferences after mLAMP and PA-LAMP.");
                }
                mode.SelectedIndex = 5; CheckPreset(form);
                // Broad deterministic fixture isolates UI wiring from wet-lab performance.
                Field<NumericUpDown>(form, "lampRegionMin").Value = 20;
                Field<NumericUpDown>(form, "lampRegionMax").Value = 20;
                Field<NumericUpDown>(form, "lampCount").Value = 2;
                Field<NumericUpDown>(form, "lampGcMin").Value = 20;
                Field<NumericUpDown>(form, "lampGcMax").Value = 80;
                foreach(string role in new[]{"F3","B3","F2","B2","F1c","B1c","LF","LB"})
                {
                    Field<NumericUpDown>(form,"lamp"+role+"TmMin").Value=35;
                    Field<NumericUpDown>(form,"lamp"+role+"TmMax").Value=85;
                }
                Field<TextBox>(form, "sequenceBox").Text = LampReportWriter.ExampleSnpFasta();
                LayoutTree(form); Application.DoEvents(); Render(form, Path.Combine(output, "mlamp-input.png"));
                Run(form);
                LampDesignResult result = Field<LampDesignResult>(form, "lampResult");
                Check(result != null && result.Sets.Count > 0, "mLAMP worker produces candidates for artificial DNA.");
                if (result != null && result.Sets.Count > 0)
                {
                    Check(LampReportWriter.IsMLamp(result) && result.Settings.ExtraMismatchFromThreePrime == 3 && !result.Settings.IncludeLoops, "Worker retains the selected mLAMP preset.");
                    Check(Field<DesignResult>(form, "result") == null && Field<SnpDesignResult>(form, "snpResult") == null, "mLAMP does not leave stale RPA results.");
                    DataGridView grid = Field<DataGridView>(form, "grid");
                    Control view = Field<Control>(form, "detailsView");
                    Check(grid.Rows.Count == result.Sets.Count, "Candidate grid and mLAMP result agree.");
                    CheckResultCards(form, result);
                    TabControl detailTabs = Named<TabControl>(view, "detailTabs");
                    detailTabs.SelectedIndex = 0; LayoutTree(form); Application.DoEvents();
                    Render(form, Path.Combine(output, "mlamp-results.png"));
                    foreach (int option in new int[] { 0, 1 })
                    {
                        mismatch.SelectedIndex = option; Run(form);
                        LampDesignResult comparison = Field<LampDesignResult>(form, "lampResult");
                        Check(comparison != null && comparison.Sets.Count > 0, "UI produces a comparison result for mismatch option " + option + ".");
                        if (comparison != null && comparison.Sets.Count > 0)
                        {
                            Check(comparison.Settings.ExtraMismatchFromThreePrime == (option == 0 ? 0 : 2), "UI comparison retains its chosen artificial-mismatch offset.");
                            CheckResultCards(form, comparison);
                        }
                    }
                    mode.SelectedIndex = 4;
                    Check(Field<LampDesignResult>(form, "lampResult") == null, "Changing mode invalidates the previous mLAMP result.");
                    mode.SelectedIndex = 5; CheckPreset(form);
                }
                Check(!form.Visible, "Test never displays a desktop form.");
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); }
        notes.Add("Assertions checked=" + checks + "; failures=" + errors.Count);
        foreach (string error in errors) notes.Add("FAIL " + error);
        notes.Add("Limit: verifies hidden native controls, worker integration and copy formatting; no amplification kinetics or visible-window gesture test.");
        notes.Add(errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(Path.Combine(output, "mlamp-ui-test-report.txt"), notes.ToArray(), new UTF8Encoding(true));
        return errors.Count == 0 ? 0 : 1;
    }
    private static void CheckPreset(MainForm form)
    {
        LampDesignSettings s = Settings(form);
        Check(Property<bool>(form, "IsMLampMode") && s.SnpMethod == "mLAMP", "mLAMP mode and settings identity agree.");
        ComboBox direction = Field<ComboBox>(form, "lampOrientation");
        Check(direction.SelectedIndex == 1 && !direction.Enabled && s.SnpOrientation == "FIP", "mLAMP fixes the SNP-bearing inner primer to FIP.");
        Check(Field<ComboBox>(form, "mismatchMode").SelectedIndex == 2 && s.ExtraMismatchFromThreePrime == 3, "Entering/restoring mLAMP sets the third-from-end strategy.");
        Check(!Field<CheckBox>(form, "lampLoops").Checked && !s.IncludeLoops, "Entering/restoring mLAMP starts with four core primers.");
        Check(Field<Label>(form, "modeHint").Text.Contains("mLAMP"), "Mode hint identifies mLAMP.");
    }
    private static void CheckCopy(MainForm form, LampPrimerSet set, LampDesignResult result)
    {
        HighlightedReport report = LampReportWriter.HighlightedOrderingText(set, result);
        using (var source = new RichTextBox()) using (var pasted = new RichTextBox())
        {
            typeof(MainForm).GetMethod("ApplyHighlights", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source, report, "" });
            var data = new DataObject(); data.SetData(DataFormats.UnicodeText, source.Text); data.SetData(DataFormats.Rtf, source.Rtf);
            pasted.Rtf = (string)data.GetData(DataFormats.Rtf);
            Check(pasted.Text == report.Text && ((string)data.GetData(DataFormats.UnicodeText)).StartsWith("mLAMP_", StringComparison.Ordinal), "Copy payload preserves mLAMP names and complete DNA.");
            var expected = new HashSet<int>(); foreach (ReportHighlight mark in report.SnpHighlights) expected.Add(mark.Start);
            var expectedBlue = new HashSet<int>(); foreach (ReportHighlight mark in report.MismatchHighlights) expectedBlue.Add(mark.Start);
            Check(expected.Count == 2, "Copy highlights exactly the two true SNP termini.");
            Check(expectedBlue.Count == (result.Settings.ExtraMismatchFromThreePrime == 0 ? 0 : 2), "Copy highlights the artificial base of both FIP alleles only when enabled.");
            bool correctColors = true;
            for (int i = 0; i < pasted.TextLength; i++)
            {
                pasted.Select(i, 1);
                if ((pasted.SelectionColor.ToArgb() == snpRed.ToArgb()) != expected.Contains(i)
                    || (pasted.SelectionColor.ToArgb() == mismatchBlue.ToArgb()) != expectedBlue.Contains(i)) { correctColors = false; break; }
            }
            Check(correctColors, "Copied RTF preserves exact SNP red and artificial-mismatch blue positions after a rich-text round trip.");
        }
    }
    private static void CheckResultCards(MainForm form, LampDesignResult result)
    {
        DataGridView grid = Field<DataGridView>(form, "grid"); Control view = Field<Control>(form, "detailsView");
        Check(grid.Rows.Count == result.Sets.Count, "Candidate grid retains every comparison result.");
        for (int i = 0; i < result.Sets.Count; i++)
        {
            grid.CurrentCell = grid.Rows[i].Cells[0]; Application.DoEvents();
            LampPrimerSet set = result.Sets[i]; int offset = result.Settings.ExtraMismatchFromThreePrime;
            foreach (LampOligo p in LampReportWriter.Oligos(set))
            {
                Check(p.RnaIndex < 0 && String.IsNullOrEmpty(p.ThreePrimeBlock) && p.Sequence == p.OrderingSequence, "mLAMP card uses ordinary DNA without RNA/C3 modifications.");
                int blueIndex = p.SnpIndex >= 0 && offset > 0 ? p.Sequence.Length - offset : -1;
                CheckBox(view, "primer_" + LampReportWriter.OligoName(p, set, result), p.Sequence, p.SnpIndex, blueIndex);
            }
            CheckBox(view, "template_ref", set.ReferenceTemplate, result.Snp.Position - set.SpanStart, -1);
            CheckBox(view, "template_alt", set.AlternateTemplate, result.Snp.Position - set.SpanStart, -1);
            string reactions = LampReportWriter.Reactions(set, result);
            Check(reactions.Contains("FIP_ref") && reactions.Contains("FIP_alt"), "Separate reference and alternate reactions use their own FIPs.");
            CheckCopy(form, set, result);
        }
    }
    private static void CheckBox(Control view, string name, string sequence, int snpIndex, int mismatchIndex)
    {
        RichTextBox box = Named<RichTextBox>(view, name);
        Check(box.ReadOnly && box.Text == sequence, name + " contains the complete expected sequence.");
        bool correctColors = true;
        for (int i = 0; i < box.TextLength; i++)
        {
            box.Select(i, 1);
            if ((box.SelectionColor.ToArgb() == snpRed.ToArgb()) != (i == snpIndex)
                || (box.SelectionColor.ToArgb() == mismatchBlue.ToArgb()) != (i == mismatchIndex)) { correctColors = false; break; }
        }
        Check(correctColors, name + " colors only its exact SNP red and deliberate mismatch blue.");
        box.Select(0, 0);
    }
    private static void Run(MainForm form)
    {
        dispatchError = null; WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(new HandleContext(form));
        typeof(MainForm).GetMethod("StartDesign", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, EventArgs.Empty });
        var timer = Stopwatch.StartNew();
        while (Field<bool>(form, "busy"))
        {
            Application.DoEvents(); if (dispatchError != null) throw dispatchError;
            if (timer.ElapsedMilliseconds > 30000) throw new TimeoutException("mLAMP design timed out."); Thread.Sleep(5);
        }
        Application.DoEvents(); if (dispatchError != null) throw dispatchError;
    }
    private sealed class HandleContext : SynchronizationContext
    {
        private readonly Control control;
        public HandleContext(Control value) { control = value; }
        public override void Post(SendOrPostCallback callback, object state)
        {
            try { control.BeginInvoke(new Action(delegate { try { callback(state); } catch (Exception ex) { dispatchError = ex; } })); }
            catch (Exception ex) { dispatchError = ex; }
        }
    }
    private static LampDesignSettings Settings(MainForm form) { return (LampDesignSettings)Invoke(form, "LampSettings"); }
    private static object Invoke(object target, string method) { return target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null); }
    private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    private static T Property<T>(object target, string name) { return (T)target.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target, null); }
    private static T Named<T>(Control root, string name) where T : Control
    {
        Control[] matches = root.Controls.Find(name, true); if (matches.Length != 1) throw new InvalidOperationException("Missing/duplicate control " + name); return (T)matches[0];
    }
    private static void Check(bool condition, string description) { checks++; if (!condition) errors.Add(description); }
    private static void Handles(Control root) { IntPtr handle = root.Handle; foreach (Control child in root.Controls) Handles(child); }
    private static void LayoutTree(Control root) { root.PerformLayout(); foreach (Control child in root.Controls) LayoutTree(child); root.PerformLayout(); }
    private static void Render(MainForm form, string file)
    {
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(file, ImageFormat.Png); }
    }
}
