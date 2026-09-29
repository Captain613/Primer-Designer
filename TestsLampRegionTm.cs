using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    public static class LampRegionTmSelfTests
    {
        private static readonly string[] Roles = { "F3", "B3", "F2", "B2", "F1c", "B1c", "LF", "LB" };
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("LAMP independent region Tm, core span and magnesium regression tests (synthetic DNA).");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Action<string, Action> test = delegate(string name, Action body)
            {
                try { body(); passed++; lines.Add("PASS  " + name); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + name + ": " + ex); }
            };
            string sequence = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
            test("Practical defaults and legacy role fallbacks are explicit", delegate
            {
                var settings = new LampDesignSettings();
                Require(settings.RegionMin == 18 && settings.RegionMax == 27 && settings.GcMin == 35 && settings.GcMax == 70, "Default composition changed.");
                Require(settings.CoreSpanMin == 110 && settings.CoreSpanMax == 190 && settings.SpanMin == 120 && settings.SpanMax == 300, "Two independent spans lost.");
                Require(settings.MonovalentMilliMolar == 50 && settings.MagnesiumMilliMolar == 4 && settings.OligoNanoMolar == 100, "Reference buffer defaults wrong.");
                foreach (string role in Roles)
                {
                    LampTmRange tm = settings.GetTm(role); bool inner = Inner(role);
                    Require(tm.Min == (inner ? 60 : 55) && tm.Max == (inner ? 70 : 65), "Wrong default role: " + role);
                }
                settings.AnnealTmMin = 51; settings.AnnealTmMax = 72; settings.InnerTmMin = 56; settings.InnerTmMax = 78;
                foreach (string role in Roles) Require(settings.GetTm(role).Min == (Inner(role) ? 56 : 51), "Legacy fallback changed: " + role);
                settings.F3Tm = new LampTmRange(61, 62);
                Require(settings.GetTm("F3").Min == 61 && settings.GetTm("B3").Min == 51, "Explicit F3 override leaked to B3.");
                ExpectArgument(delegate { settings.GetTm("F2c"); });
            });
            test("Effective settings deeply copy every role, preserve flags and resolve independent bounds", delegate
            {
                LampDesignSettings settings = Broad();
                for (int i = 0; i < Roles.Length; i++) Set(settings, Roles[i], new LampTmRange(40 + i, 70 + i));
                settings.F3Tm.Min = Double.NaN; settings.F3Tm.MinUnlimited = true;
                settings.B2Tm.Max = -999; settings.B2Tm.MaxUnlimited = true;
                settings.CoreSpanMinUnlimited = true; settings.CoreSpanMin = -9;
                settings.CoreSpanMaxUnlimited = true; settings.CoreSpanMax = -8;
                LampDesignSettings effective = LampDesignEngine.EffectiveSettings(settings, sequence.Length);
                Require(Double.IsNegativeInfinity(effective.F3Tm.Min) && effective.F3Tm.Max == 70 && effective.F3Tm.MinUnlimited, "F3 one-sided infinity failed.");
                Require(effective.B2Tm.Min == 43 && Double.IsPositiveInfinity(effective.B2Tm.Max) && effective.B2Tm.MaxUnlimited, "B2 one-sided infinity failed.");
                Require(effective.CoreSpanMin == 1 && effective.CoreSpanMax == sequence.Length && settings.CoreSpanMin == -9 && settings.CoreSpanMax == -8, "Core span source mutation or clamp.");
                foreach (string role in Roles)
                {
                    Require(!Object.ReferenceEquals(effective.GetTm(role), settings.GetTm(role)), "Shared mutable range: " + role);
                    effective.GetTm(role).Max = 90;
                    Require(settings.GetTm(role).Max != 90, "Effective result changed caller: " + role);
                }
                Require(Double.IsNaN(settings.F3Tm.Min) && settings.B2Tm.Max == -999, "Dormant source values were discarded.");
            });
            test("Only active role ranges validate, while each role rejects invalid enabled values", delegate
            {
                LampDesignSettings settings = Broad();
                foreach (string role in Roles) Set(settings, role, new LampTmRange(40, 80));
                settings.AnnealTmMin = settings.InnerTmMin = Double.NaN;
                LampDesignEngine.EffectiveSettings(settings, sequence.Length); // Unused legacy fields cannot override explicit choices.
                foreach (string role in Roles)
                {
                    LampTmRange original = settings.GetTm(role); Set(settings, role, new LampTmRange(80, 40));
                    ExpectArgument(delegate { LampDesignEngine.EffectiveSettings(settings, sequence.Length); }); Set(settings, role, original);
                }
                settings.CoreSpanMin = 180; settings.CoreSpanMax = 120; ExpectArgument(delegate { LampDesignEngine.EffectiveSettings(settings, sequence.Length); });
                settings.CoreSpanMinUnlimited = true; LampDesignEngine.EffectiveSettings(settings, sequence.Length);
                settings.MagnesiumMilliMolar = -1; ExpectArgument(delegate { LampDesignEngine.EffectiveSettings(settings, sequence.Length); });
            });
            test("Eight distinct report ranges and core span/buffer settings are recorded", delegate
            {
                LampDesignSettings settings = Broad(); settings.CoreSpanMin = 130; settings.CoreSpanMax = 170;
                for (int i = 0; i < Roles.Length; i++) Set(settings, Roles[i], new LampTmRange(40 + i, 70 + i));
                settings.F3Tm.MinUnlimited = true; settings.LBTm.MaxUnlimited = true;
                string text = LampReportWriter.Params(settings);
                for (int i = 0; i < Roles.Length; i++)
                {
                    string expected = Roles[i] + " Tm：最低 " + (i == 0 ? "无限制" : (40 + i).ToString("0.0", CultureInfo.InvariantCulture))
                        + " / 最高 " + (i == 7 ? "无限制" : (70 + i).ToString("0.0", CultureInfo.InvariantCulture));
                    Require(text.Contains(expected), "Report role bound missing: " + expected);
                }
                Require(text.Contains("F2..B2 跨度（含两端）：最短 130 / 最长 170") && text.Contains("Mg²⁺ 4.0 mM") && text.Contains("100.0 nM"), "Span/buffer provenance missing.");
            });
            test("Catalog filters separately before retaining two windows for every region", delegate
            {
                LampDesignSettings settings = Broad(); settings.RegionMin = 18; settings.RegionMax = 25;
                for (int i = 0; i < Roles.Length; i++)
                {
                    double tm = Tm(sequence.Substring(0, 18 + i), settings);
                    Set(settings, Roles[i], new LampTmRange(tm - 0.00001, tm + 0.00001));
                }
                settings = LampDesignEngine.EffectiveSettings(settings, sequence.Length);
                object catalog = Invoke("BuildCatalog", sequence, settings, CancellationToken.None);
                MethodInfo get = catalog.GetType().GetMethod("For");
                for (int i = 0; i < Roles.Length; i++)
                {
                    object section = get.Invoke(catalog, new object[] { Roles[i] });
                    Array index = (Array)section.GetType().GetField("Start").GetValue(section);
                    IList windows = (IList)index.GetValue(1);
                    Require(windows != null && windows.Count > 0 && windows.Count <= 2, "Role candidate lost before filtering: " + Roles[i]);
                    bool found = false;
                    foreach (object window in windows)
                    {
                        int end = (int)window.GetType().GetField("End").GetValue(window);
                        double actual = (double)window.GetType().GetField("Tm").GetValue(window);
                        Require(Fits(actual, settings.GetTm(Roles[i])), "Catalog range leak: " + Roles[i]);
                        found |= end == 18 + i;
                    }
                    Require(found, "Expected role-specific length was pruned by a shared catalog: " + Roles[i]);
                }
            });
            test("Ordinary output honors all eight explicit role filters, independently", delegate
            {
                LampDesignSettings baseline = Broad(); baseline.IncludeLoops = true;
                LampDesignResult result = Design(sequence, baseline);
                Require(result.Sets.Count > 0, "Broad synthetic fixture has no candidates."); CheckResult(result);
                foreach (string role in Roles)
                {
                    LampDesignSettings blocked = Broad(); blocked.IncludeLoops = true; Set(blocked, role, new LampTmRange(89, 90));
                    LampDesignResult isolated = Design(sequence, blocked);
                    if (role == "LF" || role == "LB")
                    {
                        Require(isolated.Sets.Count > 0, "Optional loop filter removed valid core: " + role);
                        foreach (LampPrimerSet set in isolated.Sets) Require((role == "LF" ? set.LF : set.LB) == null, "Wrong loop range used: " + role);
                    }
                    else Require(isolated.Sets.Count == 0, "Core role ignored its independent Tm: " + role);
                }
            });
            test("SNP anchors and modified sequences use F2 versus B2 independently", delegate
            {
                const int position = 300;
                foreach (bool reverse in new bool[] { false, true })
                {
                    LampDesignSettings settings = Broad(); string role = reverse ? "B2" : "F2", other = reverse ? "F2" : "B2";
                    double expected = Tm(sequence.Substring(reverse ? position - 1 : position - 20, 20), settings);
                    Set(settings, role, new LampTmRange(expected - 0.00001, expected + 0.00001)); Set(settings, other, new LampTmRange(89, 90));
                    IList anchors = (IList)Invoke("Anchored", sequence, position, reverse, settings, CancellationToken.None);
                    Require(anchors.Count == 1, "Anchor used opposite role filter: " + role);
                    string dna = reverse ? DesignEngine.ReverseComplement(sequence.Substring(position - 1, 20)) : sequence.Substring(position - 20, 20);
                    var region = new LampRegion { Name = role, Sequence = dna, Start = 1, End = 20, Reverse = reverse };
                    Require(Invoke("ModifiedRegion", region, dna, settings) != null, "Unchanged region lost correct role filter.");
                    Set(settings, role, new LampTmRange(89, 90));
                    Require(Invoke("ModifiedRegion", region, dna, settings) == null, "Modified region ignored correct role filter.");
                }
            });
            test("Custom core span above old 180-nt cap is searched and both endpoints are enforced", delegate
            {
                LampDesignSettings settings = Broad(); settings.CoreSpanMin = 181; settings.CoreSpanMax = 200;
                LampDesignResult result = Design(sequence, settings);
                Require(result.Sets.Count > 0, "Old 180-nt search cap still active."); CheckResult(result);
                foreach (LampPrimerSet set in result.Sets)
                {
                    int span = set.BIP.Regions[1].End - set.FIP.Regions[1].Start + 1;
                    Require(span >= 181 && span <= 200, "Custom span out of range.");
                }
            });
            test("PA-LAMP applies B2 Tm to the activated DNA segment, not its longer precursor", delegate
            {
                LampDesignSettings settings = Broad(); settings.SnpMethod = "PA-LAMP"; settings.SnpOrientation = "BIP";
                SnpInput snp = Snp(sequence, 300);
                LampDesignResult baseline = LampDesignEngine.DesignSnp(snp, settings, null, CancellationToken.None);
                Require(baseline.Sets.Count > 0, "PA fixture has no candidates.");
                double activeTm = baseline.Sets[0].BIP.ActivatedRegions[1].Tm;
                settings.B2Tm = new LampTmRange(activeTm - 0.00001, activeTm + 0.00001);
                LampDesignResult constrained = LampDesignEngine.DesignSnp(snp, settings, null, CancellationToken.None);
                Require(constrained.Sets.Count > 0, "PA precursor was incorrectly filtered using active B2 range.");
                bool precursorOutside = false;
                foreach (LampPrimerSet set in constrained.Sets)
                {
                    Require(Fits(set.BIP.ActivatedRegions[1].Tm, settings.B2Tm), "Active B2 missed requested Tm.");
                    precursorOutside |= !Fits(set.BIP.Regions[1].Tm, settings.B2Tm);
                }
                Require(precursorOutside, "Fixture did not distinguish active and precursor Tm.");
            });
            test("Custom core spans move the SNP reverse-anchor search beyond the old fixed neighborhood", delegate
            {
                foreach (string side in new string[] { "FIP", "BIP" })
                {
                    LampDesignSettings settings = Broad(); settings.SnpOrientation = side; settings.CoreSpanMin = 210; settings.CoreSpanMax = 230;
                    LampDesignResult result = LampDesignEngine.DesignSnp(Snp(sequence, 300), settings, null, CancellationToken.None);
                    Require(result.Sets.Count > 0, "Custom SNP core span still uses old fixed neighborhood: " + side); CheckResult(result);
                    foreach (LampPrimerSet set in result.Sets)
                    {
                        Require(set.SpecificInner == side, "Wrong SNP orientation.");
                        if (side == "BIP") Require(set.FIP.Regions[1].Start < 120, "Fixture does not exercise the former SNP-minus-180 boundary.");
                    }
                }
            });
            test("New default demo yields candidates within configured ranges and uses magnesium Tm", delegate
            {
                LampDesignResult result = Design(sequence, new LampDesignSettings { MaxSets = 2 });
                Require(result.Sets.Count > 0, "Default synthetic demo produced no candidates."); CheckResult(result);
                foreach (LampPrimerSet set in result.Sets) foreach (LampOligo oligo in LampReportWriter.Oligos(set)) foreach (LampRegion region in oligo.Regions)
                    Require(Math.Abs(region.Tm - Tm(region.Sequence, result.Settings)) < 0.000001, "Engine omitted magnesium or used different Tm path.");
            });
            test("Practical defaults recover synthetic SNP candidates while explicit tight Tm remains hard", delegate
            {
                SnpInput difficult = Snp(sequence, 300);
                foreach (string method in new string[] { "mLAMP", "PA-LAMP" })
                {
                    LampDesignSettings practical = method == "mLAMP" ? LampDesignSettings.MLampDefaults()
                        : new LampDesignSettings { SnpMethod = "PA-LAMP", SnpOrientation = "BIP" };
                    practical.MaxSets = 2;
                    LampDesignResult available = LampDesignEngine.DesignSnp(difficult, practical, null, CancellationToken.None);
                    Require(available.Sets.Count > 0, method + " practical defaults yielded no candidate.");
                    foreach (LampPrimerSet set in available.Sets)
                    {
                        int coreSpan = set.BIP.Regions[1].End - set.FIP.Regions[1].Start + 1;
                        Require(coreSpan >= available.Settings.CoreSpanMin && coreSpan <= available.Settings.CoreSpanMax,
                            method + " output exceeded the practical core span.");
                        LampRegion activeB2 = method == "PA-LAMP" ? set.BIP.ActivatedRegions[1] : set.BIP.Regions[1];
                        Require(Fits(activeB2.Tm, available.Settings.GetTm("B2")), method + " active B2 Tm exceeded user settings.");
                    }
                    LampDesignSettings tight = method == "mLAMP" ? LampDesignSettings.MLampDefaults()
                        : new LampDesignSettings { SnpMethod = "PA-LAMP", SnpOrientation = "BIP" };
                    tight.MaxSets = 2;
                    foreach (string role in Roles) Set(tight, role, Inner(role) ? new LampTmRange(64, 66) : new LampTmRange(59, 61));
                    LampDesignResult constrained = LampDesignEngine.DesignSnp(difficult, tight, null, CancellationToken.None);
                    Require(constrained.Sets.Count == 0, method + " silently relaxed explicit narrow Tm settings.");
                }
            });
            test("Role scoring targets use range midpoints, with finite 60/65-degree fallback for unlimited bounds", delegate
            {
                LampDesignSettings settings = Broad(); settings.F3Tm = new LampTmRange(50, 60); settings.F1cTm = new LampTmRange(60, 70);
                Require((double)Invoke("TargetTm", settings, "F3") == 55 && (double)Invoke("TargetTm", settings, "F1c") == 65, "Explicit midpoint not used.");
                settings.F3Tm.MinUnlimited = true; settings.F1cTm.MaxUnlimited = true;
                Require((double)Invoke("TargetTm", settings, "F3") == 60 && (double)Invoke("TargetTm", settings, "F1c") == 65, "Unlimited score target became non-finite.");
            });
            lines.Add("RESULT: " + passed + " passed, " + failed + " failed.");
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true));
            return failed == 0 ? 0 : 1;
        }

        private static LampDesignSettings Broad()
        {
            return new LampDesignSettings { RegionMin = 20, RegionMax = 20, GcMin = 0, GcMax = 100, AnnealTmMin = 35, AnnealTmMax = 90,
                InnerTmMin = 35, InnerTmMax = 90, SpanMin = 100, SpanMax = 600, MaxSets = 2, IncludeLoops = false };
        }
        private static LampDesignResult Design(string sequence, LampDesignSettings settings)
        { return LampDesignEngine.Design(SequenceParser.Parse(sequence), settings, null, CancellationToken.None); }
        private static SnpInput Snp(string sequence, int position)
        {
            char reference = sequence[position - 1], alternate = reference == 'A' ? 'C' : 'A';
            return SnpParser.Parse(sequence.Substring(0, position - 1) + "[" + reference + ">" + alternate + "]" + sequence.Substring(position));
        }
        private static double Tm(string sequence, LampDesignSettings settings)
        { return LampThermodynamics.MeltingTemperature(sequence, settings.MonovalentMilliMolar, settings.OligoNanoMolar, settings.MagnesiumMilliMolar); }
        private static bool Inner(string role) { return role == "F1c" || role == "B1c" || role == "LF" || role == "LB"; }
        private static void Set(LampDesignSettings settings, string role, LampTmRange range)
        { typeof(LampDesignSettings).GetField(role + "Tm").SetValue(settings, range); }
        private static bool Fits(double value, LampTmRange range)
        { return (range.MinUnlimited || value >= range.Min - 0.0000001) && (range.MaxUnlimited || value <= range.Max + 0.0000001); }
        private static object Invoke(string name, params object[] values)
        {
            try { return typeof(LampDesignEngine).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, values); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        private static void CheckResult(LampDesignResult result)
        {
            foreach (LampPrimerSet set in result.Sets)
            {
                int coreSpan = set.BIP.Regions[1].End - set.FIP.Regions[1].Start + 1;
                Require(coreSpan >= result.Settings.CoreSpanMin && coreSpan <= result.Settings.CoreSpanMax, "Output core span does not honor settings.");
                foreach (LampOligo oligo in LampReportWriter.Oligos(set)) foreach (LampRegion region in oligo.Regions)
                    Require(Fits(region.Tm, result.Settings.GetTm(region.Name)), "Output Tm does not honor role " + region.Name);
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void ExpectArgument(Action action)
        {
            try { action(); } catch (ArgumentException) { return; }
            throw new InvalidOperationException("Expected invalid active settings to be rejected.");
        }
    }
}
