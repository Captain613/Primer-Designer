using System;
using System.Collections.Generic;
using System.Globalization;

namespace RpaDesigner
{
    public sealed class LampEndStabilityResult
    {
        public string Region, End, Sequence, Scope;
        public double? DeltaG37;
        public bool HasArtificialMismatch;
        public string Status
        {
            get
            {
                if (!DeltaG37.HasValue) return "未评估：需要至少 6 个确定的 DNA 碱基";
                if (HasArtificialMismatch) return "仅完全互补参考；实际错配结合未评估";
                return DeltaG37.Value <= LampEndStability.ReferenceThreshold
                    ? "达到参考标准（非实验验证）" : "末端偏弱（高于参考阈值）";
            }
        }
        public override string ToString()
        {
            return Region + " " + End + " 端 6 nt：" + Sequence
                + "；ΔG°37 = " + (DeltaG37.HasValue ? DeltaG37.Value.ToString("F2", CultureInfo.InvariantCulture) + " kcal/mol" : "未计算");
        }
    }

    // Output-only assessment. Never participates in search, filtering or ranking.
    public static class LampEndStability
    {
        public const double ReferenceThreshold = -5.0;
        public const string Model = "SantaLucia 1998 ΔG°37；37°C、1 M Na⁺、完全匹配 DNA；末端 6 nt；无额外盐/Mg²⁺校正";
        public const string Explanation = "以下 ΔG°37 表示引物末端 6 个碱基与完全互补 DNA 配对形成局部双链的参考稳定性。数值越负，参考双链越稳定；≤ −5.00 kcal/mol 为较稳定的参考标准，> −5.00 为末端偏弱。仅供比较，不筛除候选、不改变排序。\n"
            + "计算固定在 37°C、1 M Na⁺ 条件下，采用 SantaLucia 1998 模型，不随本次 Tm 的盐/浓度设置改变，也不代表实际反应温度下的结合或实验效果。F2/B2、F3/B3、LF/LB 取 3′ 端，F1c/B1c 取 5′ 端。\n"
            + "含人为错配的末端仍按完全互补假设计算，实际错配结合未评估，不能用上述阈值判断其真实结合是否达标；非目标等位模板结合未评估。PA-LAMP 修饰 BIP 的数值只针对切后有效 DNA，不评价 RNA/C3 前体或酶切效率。区段不足 6 个确定 DNA 碱基时显示“未计算”。发卡和二聚体需另行检查。";

        public static List<LampEndStabilityResult> Evaluate(LampOligo oligo, LampPrimerSet set)
        {
            if (oligo == null) throw new ArgumentNullException("oligo");
            if (set == null) throw new ArgumentNullException("set");
            var results = new List<LampEndStabilityResult>();
            bool activated = oligo.RnaIndex >= 0;
            foreach (LampRegion region in activated ? oligo.ActivatedRegions : oligo.Regions)
            {
                bool five = region.Name == "F1c" || region.Name == "B1c";
                if (!five && region.Name != "F2" && region.Name != "B2" && region.Name != "F3"
                    && region.Name != "B3" && region.Name != "LF" && region.Name != "LB") continue;
                string sequence = (region.Sequence ?? "").ToUpperInvariant();
                int offset = five ? 0 : Math.Max(0, sequence.Length - 6);
                string end = sequence.Length < 6 ? sequence : sequence.Substring(offset, 6);
                var value = new LampEndStabilityResult { Region = region.Name, End = five ? "5′" : "3′",
                    Sequence = end, Scope = activated ? "切后有效 DNA" : "完全互补参考" };
                bool valid = sequence.Length >= 6;
                foreach (char c in sequence) if ("ACGT".IndexOf(c) < 0) valid = false;
                if (valid) value.DeltaG37 = LampThermodynamics.EndDeltaG37(end);
                // Positions are in the input plus strand; segment sequences are
                // already in synthesis direction, including reverse primers.
                int mismatchOffset = region.Reverse ? region.End - set.ExtraMismatchPosition : set.ExtraMismatchPosition - region.Start;
                value.HasArtificialMismatch = !activated && oligo.SnpIndex >= 0 && set.ExtraMismatchPosition > 0
                    && mismatchOffset >= offset && mismatchOffset < offset + 6
                    && set.ExtraMismatchPosition >= region.Start && set.ExtraMismatchPosition <= region.End;
                results.Add(value);
            }
            return results;
        }
    }
}
