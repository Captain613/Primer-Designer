using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    public static class UnlimitedLampSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("Unlimited LAMP boundary software checks; artificial DNA only.");
            Action<string, Action> test = delegate(string title, Action action)
            {
                var watch = Stopwatch.StartNew();
                try { action(); passed++; lines.Add("PASS  " + title + " (" + watch.ElapsedMilliseconds + " ms)"); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + title + ": " + ex); }
            };
            string source = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;

            test("Finite defaults match explicit reference ranges with all unlimited switches off", delegate
            {
                var result = RunDesign(source, new LampDesignSettings());
                Require(result.Sets.Count == 10, "Default count changed.");
                var explicitSettings = new LampDesignSettings { RegionMin = 17, RegionMax = 30, GcMin = 30, GcMax = 75,
                    CoreSpanMin = 100, CoreSpanMax = 220, SpanMin = 110, SpanMax = 350,
                    MonovalentMilliMolar = 50, MagnesiumMilliMolar = 4, OligoNanoMolar = 100 };
                foreach (string role in new string[] { "F3", "B3", "F2", "B2", "F1c", "B1c", "LF", "LB" })
                {
                    bool high = role == "F1c" || role == "B1c" || role == "LF" || role == "LB";
                    typeof(LampDesignSettings).GetField(role + "Tm").SetValue(explicitSettings, new LampTmRange(high ? 58 : 52, high ? 72 : 68));
                }
                var explicitResult = RunDesign(source, explicitSettings);
                Require(explicitResult.Sets.Count == result.Sets.Count, "Implicit and explicit finite candidate counts differ.");
                for (int i = 0; i < result.Sets.Count; i++)
                {
                    var a = result.Sets[i]; var b = explicitResult.Sets[i];
                    Require(a.SpanStart == b.SpanStart && a.SpanEnd == b.SpanEnd && Math.Abs(a.Score - b.Score) < 0.001
                        && a.FIP.Sequence == b.FIP.Sequence && a.BIP.Sequence == b.BIP.Sequence, "Finite defaults differ from the same explicit region ranges.");
                }
                foreach (string name in FlagNames()) Require(!(bool)typeof(LampDesignSettings).GetField(name).GetValue(result.Settings), "Default unexpectedly removes boundary: " + name);
            });
            test("All disabled boundaries resolve independently without changing source or candidate count", delegate
            {
                var settings = Unlimited(); settings.MaxSets = 2;
                settings.RegionMin = 999; settings.RegionMax = -1; settings.SpanMin = 9999; settings.SpanMax = -2;
                settings.GcMin = Double.NaN; settings.GcMax = Double.NaN;
                settings.AnnealTmMin = settings.AnnealTmMax = settings.InnerTmMin = settings.InnerTmMax = Double.NaN;
                var result = RunDesign(source, settings); var actual = result.Settings;
                Require(actual.RegionMin == 2 && actual.RegionMax == source.Length && actual.SpanMin == 1 && actual.SpanMax == source.Length, "Physical/template limits were not resolved.");
                Require(actual.GcMin == 0 && actual.GcMax == 100 && Double.IsNegativeInfinity(actual.AnnealTmMin)
                    && Double.IsPositiveInfinity(actual.AnnealTmMax) && Double.IsNegativeInfinity(actual.InnerTmMin) && Double.IsPositiveInfinity(actual.InnerTmMax), "Disabled composition bounds remained active.");
                foreach (string name in FlagNames()) Require((bool)typeof(LampDesignSettings).GetField(name).GetValue(actual), "Flag lost during copy: " + name);
                Require(settings.RegionMin == 999 && settings.RegionMax == -1 && settings.SpanMin == 9999 && Double.IsNaN(settings.GcMin), "Input settings were mutated.");
                Require(result.Sets.Count > 0 && result.Sets.Count <= 2 && result.SearchTruncated, "Unlimited boundaries must retain the finite candidate count and disclosed bounded search.");
                string summary = LampReportWriter.Params(actual);
                Require(!summary.Contains("Infinity") && !summary.Contains("NaN") && summary.Contains("请求 2 组"), "Resolved infinities leaked into user parameters.");
            });
            test("Asymmetric boundaries keep their enabled partners and reject invalid enabled limits", delegate
            {
                var settings = new LampDesignSettings { RegionMinUnlimited = true, RegionMin = 999, RegionMax = 20,
                    GcMinUnlimited = true, GcMin = 99, GcMax = 60, AnnealTmMinUnlimited = true, AnnealTmMin = 999, AnnealTmMax = 60,
                    InnerTmMaxUnlimited = true, InnerTmMin = 60, InnerTmMax = -999, SpanMinUnlimited = true, SpanMin = 9999, SpanMax = 300, MaxSets = 1 };
                var result = RunDesign(source, settings); var s = result.Settings;
                Require(s.RegionMin == 2 && s.RegionMax == 20 && s.GcMin == 0 && s.GcMax == 60 && s.AnnealTmMax == 60
                    && s.InnerTmMin == 60 && s.SpanMax == 300 && s.SpanMin == 1, "An enabled partner was changed.");
                Require(Double.IsNegativeInfinity(s.AnnealTmMin) && Double.IsPositiveInfinity(s.InnerTmMax), "One-sided Tm disabling failed.");
                string summary = LampReportWriter.Params(s);
                Require(summary.Contains("最低 无限制 / 最高 60.0") && summary.Contains("最低 60.0 / 最高 无限制") && !summary.Contains("Infinity"), "Asymmetric Tm summary is incorrect.");
                settings.RegionMax = 36; ExpectArgument(delegate { RunDesign(source, settings); });
                settings.RegionMax = 20; settings.InnerTmMin = 91; ExpectArgument(delegate { RunDesign(source, settings); });
                settings.InnerTmMin = 60; settings.MaxSets = 0; ExpectArgument(delegate { RunDesign(source, settings); });
            });
            test("Unlimited region maximum can produce regions longer than the old 35 nt ceiling", delegate
            {
                var settings = Unlimited(); settings.RegionMinUnlimited = false; settings.RegionMin = 35;
                settings.GcMinUnlimited = settings.GcMaxUnlimited = false; settings.GcMin = settings.GcMax = 50;
                var result = RunDesign(source, settings);
                Require(result.Sets.Count > 0, "Beyond-35 fixture produced no candidate.");
                foreach (var oligo in LampReportWriter.Oligos(result.Sets[0])) foreach (var region in oligo.Regions)
                    Require(region.Sequence.Length > 35, "An odd 35 nt region cannot have exactly 50% GC.");
            });
            test("Unlimited minimum explores below 15 nt while retaining the two-base Tm model minimum", delegate
            {
                var settings = Unlimited(); settings.RegionMaxUnlimited = false; settings.RegionMax = 15;
                settings.GcMinUnlimited = settings.GcMaxUnlimited = false; settings.GcMin = settings.GcMax = 100;
                var result = RunDesign(Repeat("ACGT", 100), settings);
                Require(result.Sets.Count > 0, "Below-15 fixture produced no candidate.");
                foreach (var oligo in LampReportWriter.Oligos(result.Sets[0])) foreach (var region in oligo.Regions)
                    Require(region.Sequence.Length == 2, "Expected the only contiguous 100%-GC segment, CG.");
            });
            test("Very short SNP flank with third-from-end mismatch has no negative index or fabricated candidate", delegate
            {
                var settings = Unlimited(); settings.SnpMethod = "mLAMP"; settings.SnpOrientation = "FIP"; settings.ExtraMismatchFromThreePrime = 3;
                char original = source[1], alternate = original == 'A' ? 'C' : 'A';
                var input = SnpParser.Parse(">short_flank\n" + source.Substring(0, 1) + "[" + original + ">" + alternate + "]" + source.Substring(2));
                var result = LampDesignEngine.DesignSnp(input, settings, null, CancellationToken.None);
                Require(result.Sets.Count == 0, "An impossible two-base flank produced a candidate.");
            });
            test("Prefix GC/nearest-neighbor/symmetry agrees with direct calculations", delegate
            {
                Type scannerType = typeof(LampDesignEngine).GetNestedType("WindowScanner", BindingFlags.NonPublic);
                var settings = Unlimited(); settings.GcMin = 0; settings.GcMax = 100;
                foreach (string text in new string[] { source.Substring(0, 120), Repeat("ACGT", 40), "AAAAAAGCGCATATATNNACGTTGCATGCAACGT" })
                {
                    object scanner = Activator.CreateInstance(scannerType, new object[] { text, CancellationToken.None });
                    MethodInfo make = scannerType.GetMethod("Make");
                    for (int start = 1; start < text.Length; start++) for (int length = 2; length <= text.Length - start + 1; length++)
                    {
                        string part = text.Substring(start - 1, length);
                        object window = make.Invoke(scanner, new object[] { start, length, settings });
                        bool valid = part.IndexOf('N') < 0 && !part.Contains("AAAAAA") && !part.Contains("CCCCCC") && !part.Contains("GGGGGG") && !part.Contains("TTTTTT");
                        Require((window != null) == valid, "Prefix invalid/homopolymer filter differs.");
                        if (window == null) continue;
                        double actual = (double)window.GetType().GetField("Tm").GetValue(window);
                        double expected = LampThermodynamics.MeltingTemperature(part, settings.MonovalentMilliMolar, settings.OligoNanoMolar, settings.MagnesiumMilliMolar);
                        Require(Math.Abs(actual - expected) < 0.0000001, "Prefix nearest-neighbor drift: " + part + " / " + actual.ToString("R") + " / " + expected.ToString("R"));
                    }
                }
            });
            test("Broad longest-template search remains cancellable during catalog construction", delegate
            {
                var settings = Unlimited();
                using (var cancellation = new CancellationTokenSource())
                {
                    bool caught = false; var watch = Stopwatch.StartNew(); cancellation.CancelAfter(10);
                    try { LampDesignEngine.Design(SequenceParser.Parse(Repeat("ACGT", 5000)), settings, null, cancellation.Token); }
                    catch (OperationCanceledException) { caught = true; }
                    Require(caught && watch.ElapsedMilliseconds < 5000, "Broad search ignored timely cancellation.");
                }
            });
            lines.Add(""); lines.Add("Passed: " + passed); lines.Add("Failed: " + failed); lines.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }
        private static LampDesignSettings Unlimited()
        {
            return new LampDesignSettings { RegionMinUnlimited = true, RegionMaxUnlimited = true, SpanMinUnlimited = true, SpanMaxUnlimited = true,
                CoreSpanMinUnlimited = true, CoreSpanMaxUnlimited = true,
                GcMinUnlimited = true, GcMaxUnlimited = true, AnnealTmMinUnlimited = true, AnnealTmMaxUnlimited = true,
                InnerTmMinUnlimited = true, InnerTmMaxUnlimited = true, MaxSets = 1, IncludeLoops = false };
        }
        private static IEnumerable<string> FlagNames()
        { return new string[] { "RegionMinUnlimited", "RegionMaxUnlimited", "SpanMinUnlimited", "SpanMaxUnlimited", "CoreSpanMinUnlimited", "CoreSpanMaxUnlimited", "GcMinUnlimited", "GcMaxUnlimited", "AnnealTmMinUnlimited", "AnnealTmMaxUnlimited", "InnerTmMinUnlimited", "InnerTmMaxUnlimited" }; }
        private static LampDesignResult RunDesign(string text, LampDesignSettings settings)
        { return LampDesignEngine.Design(SequenceParser.Parse(text), settings, null, CancellationToken.None); }
        private static string Repeat(string text, int count) { var value = new StringBuilder(); for (int i = 0; i < count; i++) value.Append(text); return value.ToString(); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void ExpectArgument(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid enabled boundary was accepted."); }
    }
}
