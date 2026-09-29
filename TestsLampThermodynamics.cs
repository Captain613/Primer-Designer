using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RpaDesigner
{
    public static class LampThermodynamicsSelfTests
    {
        public static int Run(string reportPath)
        {
            var report = new List<string>(); int passed = 0, failed = 0;
            report.Add("LAMP reference thermodynamics self-tests (offline)");
            report.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            report.Add("Checks numerical model contracts only; no experimental Tm, specificity or assay validation.");
            Action<string, Action> test = delegate(string name, Action action)
            {
                try { action(); passed++; report.Add("PASS  " + name); }
                catch (Exception ex) { failed++; report.Add("FAIL  " + name + ": " + ex.Message); report.Add(ex.StackTrace ?? ""); }
            };
            const string nonself = "CGTTGACGTTGACGTTGACG", self = "ACGTACGTACGTACGTACGT";

            test("Mg conversion uses molar units and default reference is 302.982 mM", delegate
            {
                Near(302.9822128134703, LampThermodynamics.EffectiveMonovalentMilliMolar(50, 4), 1e-9, "Default equivalent salt");
                Near(50, LampThermodynamics.EffectiveMonovalentMilliMolar(50, 0), 1e-12, "Mg-zero equivalent salt");
                Near(1000, LampThermodynamics.EffectiveMonovalentMilliMolar(1000, 0), 1e-10, "1 M boundary");
            });
            test("Legacy overload and Mg-zero reproduce fixed 1998 reference values", delegate
            {
                Near(57.0043324725905, LampThermodynamics.MeltingTemperature(nonself, 50, 250), 1e-9, "Legacy non-self Tm");
                Near(56.1305499377993, LampThermodynamics.MeltingTemperature(self, 50, 250), 1e-9, "Legacy self Tm");
                foreach (string sequence in new string[] { nonself, self, "AA", "GC", "TTTT", "ATAT", "GATTACA" })
                    Near(LampThermodynamics.MeltingTemperature(sequence, 50, 250), LampThermodynamics.MeltingTemperature(sequence, 50, 250, 0), 1e-12, "Mg zero regression");
            });
            test("Reference Mg Tm agrees with independent Table 2 energy sums and a single entropy salt correction", delegate
            {
                // Independent published-table sums including both terminal
                // initiation terms: H=-166.4, S=-450.1, 19 NN steps.
                double salt = 0.05 + 4 * Math.Sqrt(0.004);
                double expected = -166400 / (-450.1 + 0.368 * 19 * Math.Log(salt) + 1.987 * Math.Log(1e-7 / 4)) - 273.15;
                double actual = LampThermodynamics.MeltingTemperature(nonself, 50, 100, 4);
                Near(expected, actual, 1e-10, "Independent Mg Tm");
                Require(Math.Abs(actual - (expected + 16.6 * Math.Log10(salt))) > 5, "Unexpected second salt correction");
                Require(actual > LampThermodynamics.MeltingTemperature(nonself, 50, 100, 0), "Mg equivalent salt should raise predicted Tm");
            });
            test("Self complementarity uses C rather than C/4 and symmetry entropy", delegate
            {
                // H=-161.2, S=-438.4 already includes -1.4 symmetry entropy.
                double expected = -161200 / (-438.4 + 0.368 * 19 * Math.Log(0.05 + 4 * Math.Sqrt(0.004)) + 1.987 * Math.Log(1e-7)) - 273.15;
                Near(expected, LampThermodynamics.MeltingTemperature(self, 50, 100, 4), 1e-10, "Self complementary Mg Tm");
                Near(LampThermodynamics.MeltingTemperature(nonself, 50, 100, 4), LampThermodynamics.MeltingTemperature(ReverseComplement(nonself), 50, 100, 4), 1e-10, "Complement invariance");
            });
            test("All nearest-neighbor steps match independent 1998 enthalpy and entropy matrix", delegate
            {
                const string alphabet = "ACGT";
                double[,] hs = { { -7.9, -8.4, -7.8, -7.2 }, { -8.5, -8.0, -10.6, -7.8 }, { -8.2, -9.8, -8.0, -8.4 }, { -7.2, -8.2, -8.5, -7.9 } };
                double[,] ss = { { -22.2, -22.4, -21, -20.4 }, { -22.7, -19.9, -27.2, -21 }, { -22.2, -24.4, -19.9, -22.4 }, { -21.3, -22.2, -22.7, -22.2 } };
                for (int first = 0; first < 4; first++) for (int second = 0; second < 4; second++)
                {
                    double h = 0, s = 0; LampThermodynamics.AddNearestNeighbor(alphabet[first], alphabet[second], ref h, ref s);
                    Near(hs[first, second], h, 1e-12, "NN enthalpy"); Near(ss[first, second], s, 1e-12, "NN entropy");
                }
            });
            test("Prefix-sum path reproduces direct sequence Tm across different windows and Mg values", delegate
            {
                var dna = new StringBuilder(); var random = new Random(19980928);
                for (int i = 0; i < 140; i++) dna.Append("ACGT"[random.Next(4)]);
                string source = dna.ToString(); double[] h = new double[source.Length], s = new double[source.Length];
                for (int i = 1; i < source.Length; i++)
                { h[i] = h[i - 1]; s[i] = s[i - 1]; LampThermodynamics.AddNearestNeighbor(source[i - 1], source[i], ref h[i], ref s[i]); }
                foreach (double mg in new double[] { 0, 0.1, 4, 10 })
                    for (int start = 0; start < 80; start += 3) for (int length = 2; length <= 50; length += 3)
                    {
                        string part = source.Substring(start, length); int end = start + length - 1;
                        double prefix = LampThermodynamics.FromStackSums(source[start], source[end], length, part == ReverseComplement(part),
                            h[end] - h[start], s[end] - s[start], 50, 100, mg);
                        Near(LampThermodynamics.MeltingTemperature(part, 50, 100, mg), prefix, 1e-9, "Prefix path");
                    }
                double legacy = LampThermodynamics.FromStackSums(nonself[0], nonself[nonself.Length - 1], nonself.Length, false, -166.6, -444.5, 50, 250);
                Near(LampThermodynamics.MeltingTemperature(nonself, 50, 250), legacy, 1e-10, "Legacy stack overload");
            });
            test("Invalid DNA, concentrations, effective salt and stack metadata fail explicitly", delegate
            {
                foreach (double invalid in new double[] { Double.NaN, Double.NegativeInfinity, Double.PositiveInfinity, -1, 100.001 })
                { double value = invalid; ExpectArgument(delegate { LampThermodynamics.MeltingTemperature(nonself, 50, 100, value); }); }
                foreach (double invalid in new double[] { Double.NaN, Double.PositiveInfinity, -1, 0, 1000.01 })
                { double value = invalid; ExpectArgument(delegate { LampThermodynamics.MeltingTemperature(nonself, value, 100, 4); }); }
                foreach (double invalid in new double[] { Double.NaN, Double.PositiveInfinity, -1, 0, 1000001 })
                { double value = invalid; ExpectArgument(delegate { LampThermodynamics.MeltingTemperature(nonself, 50, value, 4); }); }
                ExpectArgument(delegate { LampThermodynamics.MeltingTemperature(nonself, 1000, 100, 0.01); });
                ExpectArgument(delegate { LampThermodynamics.MeltingTemperature(nonself, 50, 100, 100); });
                foreach (string invalid in new string[] { null, "", "A", "ACNT", "AC[rA]GT", "AC GT" })
                { string value = invalid; ExpectArgument(delegate { LampThermodynamics.MeltingTemperature(value, 50, 100, 4); }); }
                ExpectArgument(delegate { LampThermodynamics.FromStackSums('A', 'C', 1, false, -10, -30, 50, 100, 4); });
                ExpectArgument(delegate { LampThermodynamics.FromStackSums('N', 'C', 20, false, -10, -30, 50, 100, 4); });
                ExpectArgument(delegate { LampThermodynamics.FromStackSums('A', 'C', 20, false, Double.NaN, -30, 50, 100, 4); });
                Near(LampThermodynamics.MeltingTemperature(nonself, 50, 100, 4), LampThermodynamics.MeltingTemperature(nonself.ToLowerInvariant(), 50, 100, 4), 1e-12, "Case normalization");
            });
            report.Add(""); report.Add("Passed: " + passed); report.Add("Failed: " + failed); report.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllLines(reportPath, report.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }
        private static string ReverseComplement(string sequence)
        { char[] result = new char[sequence.Length]; for (int i = 0; i < sequence.Length; i++) result[sequence.Length - 1 - i] = "TGCA"["ACGT".IndexOf(sequence[i])]; return new String(result); }
        private static void ExpectArgument(Action action)
        { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected ArgumentException"); }
        private static void Near(double expected, double actual, double tolerance, string message)
        { Require(!Double.IsNaN(actual) && !Double.IsInfinity(actual) && Math.Abs(expected - actual) <= tolerance, message + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + ", actual " + actual.ToString("R", CultureInfo.InvariantCulture)); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
