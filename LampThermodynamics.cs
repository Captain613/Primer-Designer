using System;

namespace RpaDesigner
{
    public static class LampThermodynamics
    {
        // Direct unified ΔG°37 parameters (kcal/mol), SantaLucia 1998,
        // doi:10.1073/pnas.95.4.1460. Fixed 37°C / 1 M Na+ reference.
        // Integer hundredths preserve the published precision at the threshold.
        // Do not reconstruct these values from rounded H/S or apply Tm salts.
        public static double EndDeltaG37(string sixBases)
        {
            if (sixBases == null || sixBases.Length != 6)
                throw new ArgumentException("末端 ΔG°37 计算需要恰好 6 个 DNA 碱基。");
            string sequence = sixBases.ToUpperInvariant();
            foreach (char c in sequence)
                if ("ACGT".IndexOf(c) < 0) throw new ArgumentException("末端 ΔG°37 仅支持 A/C/G/T。");
            int energy = 0;
            for (int i = 0; i < 5; i++)
            {
                switch (sequence.Substring(i, 2))
                {
                    case "AA": case "TT": energy -= 100; break;
                    case "AT": energy -= 88; break;
                    case "TA": energy -= 58; break;
                    case "CA": case "TG": energy -= 145; break;
                    case "GT": case "AC": energy -= 144; break;
                    case "CT": case "AG": energy -= 128; break;
                    case "GA": case "TC": energy -= 130; break;
                    case "CG": energy -= 217; break;
                    case "GC": energy -= 224; break;
                    case "GG": case "CC": energy -= 184; break;
                }
            }
            energy += sequence[0] == 'A' || sequence[0] == 'T' ? 103 : 98;
            energy += sequence[5] == 'A' || sequence[5] == 'T' ? 103 : 98;
            if (sequence == DesignEngine.ReverseComplement(sequence)) energy += 43;
            return energy / 100.0;
        }

        // SantaLucia (1998), PNAS 95:1460-1465, Table 2. Enthalpy is
        // kcal/mol and entropy cal/(mol K). Terminal initiation is added
        // independently for each end. This is a perfect-match DNA model.
        // The legacy overload deliberately means Mg = 0; keep its numerical
        // contract for old callers and explicitly pass Mg in new designs.
        public static double MeltingTemperature(string sequence, double monovalentMilliMolar, double oligoNanoMolar)
        { return MeltingTemperature(sequence, monovalentMilliMolar, oligoNanoMolar, 0); }

        public static double MeltingTemperature(string sequence, double monovalentMilliMolar, double oligoNanoMolar, double magnesiumMilliMolar)
        {
            if (String.IsNullOrEmpty(sequence) || sequence.Length < 2)
                throw new ArgumentException("Tm 计算需要至少两个确定的 DNA 碱基。");
            // Validate concentrations even if the sequence is rejected below.
            ValidateConcentrations(monovalentMilliMolar, oligoNanoMolar, magnesiumMilliMolar);
            sequence = sequence.ToUpperInvariant();
            double h = 0, s = 0;
            for (int i = 0; i < sequence.Length; i++)
                if ("ACGT".IndexOf(sequence[i]) < 0) throw new ArgumentException("Tm 计算仅支持 A/C/G/T。");
            for (int i = 0; i < sequence.Length - 1; i++)
                AddNearestNeighbor(sequence[i], sequence[i + 1], ref h, ref s);
            bool symmetric = sequence == DesignEngine.ReverseComplement(sequence);
            return FromStackSums(sequence[0], sequence[sequence.Length - 1], sequence.Length,
                symmetric, h, s, monovalentMilliMolar, oligoNanoMolar, magnesiumMilliMolar);
        }

        // PrimerExplorer's published Mg-to-Na approximation, in mol/L:
        // [Na]effective = [Na]raw + 4 * sqrt([Mg]raw).
        // This is a fixed reference calculation, not a complete mixed-ion or
        // free-Mg/dNTP binding model. Applying this approximation to the 1998
        // entropy salt correction does not reproduce PrimerExplorer's model.
        public static double EffectiveMonovalentMilliMolar(double monovalentMilliMolar, double magnesiumMilliMolar)
        {
            if (!Finite(monovalentMilliMolar) || monovalentMilliMolar <= 0 || monovalentMilliMolar > 1000)
                throw new ArgumentException("Tm 参考单价盐浓度须大于 0 且不超过 1,000 mM。");
            if (!Finite(magnesiumMilliMolar) || magnesiumMilliMolar < 0 || magnesiumMilliMolar > 100)
                throw new ArgumentException("Tm 参考 Mg²⁺ 浓度须为 0–100 mM。");
            double effective = monovalentMilliMolar / 1000.0 + 4 * Math.Sqrt(magnesiumMilliMolar / 1000.0);
            // Deliberate software bound: do not extrapolate the effective salt
            // above the NN table's 1 M reference or silently clamp an input.
            if (effective > 1.0 + 1e-12)
                throw new ArgumentException("Mg²⁺ 换算后的等效单价盐超过 1,000 mM；请降低参考盐浓度。本模型不外推至该范围。");
            return Math.Min(1.0, effective) * 1000.0;
        }

        private static double ValidateConcentrations(double monovalentMilliMolar, double oligoNanoMolar, double magnesiumMilliMolar)
        {
            if (!Finite(oligoNanoMolar) || oligoNanoMolar <= 0 || oligoNanoMolar > 1000000)
                throw new ArgumentException("Tm 参考寡核苷酸浓度须大于 0 且不超过 1,000,000 nM。");
            return EffectiveMonovalentMilliMolar(monovalentMilliMolar, magnesiumMilliMolar);
        }

        internal static void AddNearestNeighbor(char first, char second, ref double h, ref double s)
        {
            switch (new String(new char[] { first, second }))
            {
                case "AA": case "TT": h -= 7.9; s -= 22.2; break;
                case "AT": h -= 7.2; s -= 20.4; break;
                case "TA": h -= 7.2; s -= 21.3; break;
                case "CA": case "TG": h -= 8.5; s -= 22.7; break;
                case "GT": case "AC": h -= 8.4; s -= 22.4; break;
                case "CT": case "AG": h -= 7.8; s -= 21.0; break;
                case "GA": case "TC": h -= 8.2; s -= 22.2; break;
                case "CG": h -= 10.6; s -= 27.2; break;
                case "GC": h -= 9.8; s -= 24.4; break;
                case "GG": case "CC": h -= 8.0; s -= 19.9; break;
            }
        }

        internal static double FromStackSums(char first, char last, int length, bool symmetric,
            double h, double s, double monovalentMilliMolar, double oligoNanoMolar)
        { return FromStackSums(first, last, length, symmetric, h, s, monovalentMilliMolar, oligoNanoMolar, 0); }

        internal static double FromStackSums(char first, char last, int length, bool symmetric,
            double h, double s, double monovalentMilliMolar, double oligoNanoMolar, double magnesiumMilliMolar)
        {
            if (length < 2 || "ACGT".IndexOf(first) < 0 || "ACGT".IndexOf(last) < 0 || !Finite(h) || !Finite(s))
                throw new ArgumentException("Tm 最近邻求和需要有效的 DNA 端点、长度及有限能量值。");
            double effectiveMilliMolar = ValidateConcentrations(monovalentMilliMolar, oligoNanoMolar, magnesiumMilliMolar);
            AddTerminal(first, ref h, ref s); AddTerminal(last, ref h, ref s);
            if (symmetric) s -= 1.4;
            // Exactly one salt correction. Do NOT additionally add 16.6 log[Na].
            s += 0.368 * (length - 1) * Math.Log(effectiveMilliMolar / 1000.0);
            double concentration = oligoNanoMolar * 1e-9 / (symmetric ? 1.0 : 4.0);
            return 1000 * h / (s + 1.987 * Math.Log(concentration)) - 273.15;
        }

        private static void AddTerminal(char c, ref double h, ref double s)
        {
            if (c == 'A' || c == 'T') { h += 2.3; s += 4.1; }
            else { h += 0.1; s -= 2.8; }
        }

        internal static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    }
}
