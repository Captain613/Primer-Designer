using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using RpaDesigner;

// Deterministic rendering fixtures only. No visible windows or clipboard writes.
internal static class SnpHighlightUiCheck
{
    private static readonly Color snpRed = Color.FromArgb(211, 47, 47);
    private static readonly Color mismatchBlue = Color.FromArgb(37, 99, 235);
    private static readonly List<string> errors = new List<string>();
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var host = new Form { Size = new Size(1200, 900) })
            using (var view = new ResultDetailsView { Dock = DockStyle.Fill })
            {
                host.Controls.Add(view); Handles(host);
                // Disabled highlighting is checked after an enabled candidate to catch stale colors.
                foreach (int offset in new int[] { 3, 0, 2 })
                {
                    SnpDesignResult result = Fixture(offset);
                    foreach (SnpPrimerSet set in result.Sets)
                    {
                        view.ShowSnp(set, result); Handles(view); host.PerformLayout(); Application.DoEvents();
                        CheckCard(view, "primer_F_ref", set.ReferenceForward.Sequence,
                            set.ReferenceForward.Sequence.Length - 1, offset == 0 ? -1 : set.ReferenceForward.Sequence.Length - offset);
                        CheckCard(view, "primer_F_alt", set.AlternateForward.Sequence,
                            set.AlternateForward.Sequence.Length - 1, offset == 0 ? -1 : set.AlternateForward.Sequence.Length - offset);
                        CheckCard(view, "primer_R_common", set.CommonReverse.Sequence, -1, -1);
                        CheckCard(view, "primer_F_control", set.ControlForward.Sequence, -1, -1);
                        CheckCard(view, "product_ref", set.ReferencePair.AmpliconSequence,
                            result.Input.Position - set.ReferencePair.AmpliconStart,
                            offset == 0 ? -1 : set.ExtraMismatchPosition - set.ReferencePair.AmpliconStart);
                        CheckCard(view, "product_alt", set.AlternatePair.AmpliconSequence,
                            result.Input.Position - set.AlternatePair.AmpliconStart,
                            offset == 0 ? -1 : set.ExtraMismatchPosition - set.AlternatePair.AmpliconStart);
                        CheckCard(view, "product_control_ref", set.ControlPair.AmpliconSequence,
                            result.Input.Position - set.ControlPair.AmpliconStart, -1);
                        CheckCard(view, "product_control_alt", set.AlternateControlAmpliconSequence,
                            result.Input.Position - set.ControlPair.AmpliconStart, -1);
                        CheckOrderingRtf(set, result.Input, offset);
                    }
                    CheckFullReportDisplay(result, offset);
                }
                // Ordinary RPA remains uncolored after leaving the SNP result view.
                SnpDesignResult prior = Fixture(3); SnpPrimerSet candidate = prior.Sets[0];
                view.ShowPair(candidate.ControlPair, new DesignResult { Input = prior.Input.Reference, Settings = prior.Settings.Base });
                CheckCard(view, "primer_F", candidate.ControlForward.Sequence, -1, -1);
                CheckCard(view, "primer_R", candidate.CommonReverse.Sequence, -1, -1);
                CheckCard(view, "product_main", candidate.ControlPair.AmpliconSequence, -1, -1);
                Check(!host.Visible, "Test must never display a desktop window.");
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); }
        var report = new List<string>();
        report.Add("RPA SNP artificial-mismatch highlighting UI checks");
        report.Add("Assembly=" + typeof(MainForm).Assembly.Location);
        report.Add("Assertions checked=" + checks + "; failures=" + errors.Count);
        foreach (string error in errors) report.Add("FAIL " + error);
        report.Add("Verifies hidden sequence cards and Unicode/RTF copy payloads; does not alter the desktop clipboard.");
        report.Add(errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(Path.Combine(output, "snp-highlight-ui-test-report.txt"), report.ToArray(), new UTF8Encoding(true));
        return errors.Count == 0 ? 0 : 1;
    }

    private static SnpDesignResult Fixture(int offset)
    {
        // Reuse the core rendering fixture, which contains controls spanning the
        // extra-mismatch coordinate but without the synthetic substitution.
        MethodInfo factory = typeof(HighlightSelfTests).GetMethod("Fixture", BindingFlags.NonPublic | BindingFlags.Static);
        return (SnpDesignResult)factory.Invoke(null, new object[] { offset });
    }

    private static void CheckCard(Control view, string name, string sequence, int snpIndex, int mismatchIndex)
    {
        Control[] controls = view.Controls.Find(name, true);
        if (controls.Length != 1) throw new InvalidOperationException("Missing or duplicate sequence card " + name);
        var box = (RichTextBox)controls[0];
        Check(box.ReadOnly && box.Text == sequence, name + " retains its complete literal DNA.");
        var red = Indices(snpIndex); var blue = Indices(mismatchIndex);
        CheckColors(box, red, blue, name);
        CheckRtfCopy(box, red, blue, name + " single-sequence copy");
    }

    private static void CheckOrderingRtf(SnpPrimerSet set, SnpInput input, int offset)
    {
        HighlightedReport report = SnpReportWriter.HighlightedOrderingText(set, input);
        var red = new HashSet<int>(); var blue = new HashSet<int>(); int lineStart = 0, row = 0;
        foreach (string line in report.Text.Split('\n'))
        {
            if (row < 2)
            {
                red.Add(lineStart + line.Length - 1);
                if (offset > 0) blue.Add(lineStart + line.Length - offset);
            }
            lineStart += line.Length + 1; row++;
        }
        Check(row == 4, "Ordering copy includes exactly four oligo rows.");
        using (var box = new RichTextBox())
        {
            Apply(box, report); Check(box.Text == report.Text, "Ordering copy preserves literal text.");
            CheckColors(box, red, blue, "Ordering copy"); CheckRtfCopy(box, red, blue, "Ordering copy RTF");
        }
    }

    private static void CheckFullReportDisplay(SnpDesignResult result, int offset)
    {
        HighlightedReport report = SnpReportWriter.HighlightedTextReport(result);
        var red = new HashSet<int>(); var blue = new HashSet<int>();
        foreach (ReportHighlight mark in report.SnpHighlights) red.Add(mark.Start);
        foreach (ReportHighlight mark in report.MismatchHighlights) blue.Add(mark.Start);
        Check(red.Count == 20 && blue.Count == (offset == 0 ? 0 : 12), "Full report retains all SNPs and only synthetic mismatch bases.");
        using (var box = new RichTextBox())
        {
            // The product exports full reports as HTML/TXT. Its RTF copy actions
            // copy sequence cards and ordering groups, checked separately above.
            Apply(box, report); Check(box.Text == report.Text, "Full report display preserves exact Unicode text.");
            CheckColors(box, red, blue, "Full report");
        }
    }

    private static void Apply(RichTextBox box, HighlightedReport report)
    {
        typeof(MainForm).GetMethod("ApplyHighlights", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { box, report, "" });
    }

    private static void CheckRtfCopy(RichTextBox source, HashSet<int> red, HashSet<int> blue, string description)
    {
        var payload = new DataObject();
        payload.SetData(DataFormats.UnicodeText, source.Text); payload.SetData(DataFormats.Rtf, source.Rtf);
        using (var pasted = new RichTextBox())
        {
            pasted.Rtf = (string)payload.GetData(DataFormats.Rtf);
            Check(pasted.Text == source.Text && (string)payload.GetData(DataFormats.UnicodeText) == source.Text,
                description + " preserves Unicode DNA and RTF text.");
            CheckColors(pasted, red, blue, description);
        }
    }


    private static void CheckColors(RichTextBox box, HashSet<int> red, HashSet<int> blue, string description)
    {
        bool correct = true;
        for (int i = 0; i < box.TextLength; i++)
        {
            box.Select(i, 1); int color = box.SelectionColor.ToArgb();
            if ((color == snpRed.ToArgb()) != red.Contains(i) || (color == mismatchBlue.ToArgb()) != blue.Contains(i))
            { correct = false; break; }
        }
        Check(correct, description + " uses exact SNP red and synthetic-mismatch blue coordinates.");
        box.Select(0, 0);
    }
    private static HashSet<int> Indices(int index) { var set = new HashSet<int>(); if (index >= 0) set.Add(index); return set; }
    private static void Handles(Control root) { IntPtr handle = root.Handle; foreach (Control child in root.Controls) Handles(child); }
    private static void Check(bool condition, string description) { checks++; if (!condition) errors.Add(description); }
}
