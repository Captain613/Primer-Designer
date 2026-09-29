using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RpaDesigner
{
    // The snapshot contains only the chosen primers/binding segments. Original
    // FASTA headers, input templates, file paths and ordering modifications are
    // deliberately absent from the remote query model.
    public static class BlastQueryBuilder
    {
        public static BlastQuerySet ForRpa(PrimerPair pair)
        {
            if (pair == null) throw new ArgumentNullException("pair");
            BlastQuerySet set = NewSet("RPA", pair.Rank);
            AddPair(set, "RPA 反应", pair.Forward, pair.Reverse, "F", "R");
            return set;
        }

        public static BlastQuerySet ForSnp(SnpPrimerSet primers)
        {
            if (primers == null) throw new ArgumentNullException("primers");
            BlastQuerySet set = NewSet("RPA SNP", primers.Rank);
            AddPair(set, "参考等位反应", primers.ReferenceForward, primers.CommonReverse, "F_ref", "R_common");
            AddPair(set, "替代等位反应", primers.AlternateForward, primers.CommonReverse, "F_alt", "R_common");
            AddPair(set, "对照反应", primers.ControlForward, primers.CommonReverse, "F_control", "R_common");
            set.Notes.Add("三个反应分别解释；两条等位正向按实际合成序列（含人为错配）查询。BLAST 不预测 SNP 等位区分能力。");
            return set;
        }

        public static BlastQuerySet ForLamp(LampPrimerSet primers, string mode)
        {
            if (primers == null) throw new ArgumentNullException("primers");
            BlastQuerySet set = NewSet(String.IsNullOrWhiteSpace(mode) ? "LAMP" : mode, primers.Rank);
            bool pa = primers.BIP != null && primers.BIP.RnaIndex >= 0;
            AddLampReaction(set, primers, primers.FIP, primers.BIP,
                primers.AlternateInner == null ? "LAMP 反应" : "参考等位反应", "ref", pa);
            if (primers.AlternateInner != null)
            {
                if (primers.SpecificInner != "FIP" && primers.SpecificInner != "BIP")
                    throw new ArgumentException("LAMP 等位候选缺少 FIP/BIP 方向信息。");
                AddLampReaction(set, primers,
                    primers.SpecificInner == "FIP" ? primers.AlternateInner : primers.FIP,
                    primers.SpecificInner == "BIP" ? primers.AlternateInner : primers.BIP,
                    "替代等位反应", "alt", pa);
            }
            AddAuxiliary(set, primers.LF, "LF（辅助环引物）");
            AddAuxiliary(set, primers.LB, "LB（辅助环引物）");
            set.Notes.Add("FIP/BIP 按独立结合片段查询，不将拼接引物整体与连续模板比对。组合检查仅要求 F3、F2、F1c、B1c、B2、B3 六个核心区；LF/LB 仅作辅助查询。");
            set.Notes.Add("F1c/B1c 查询片段的 3′ 末端并非完整 FIP/BIP 的可延伸 3′ 端；末端错配统计不得直接解释为扩增能力。");
            if (pa)
                set.Notes.Add("PA-LAMP 仅查询切后有效 DNA 结合区（B2 使用 ActivatedRegions）。未上传 RNA、C3 或会被移除的错配尾；未评估 RNase H2 激活机制、前体结合、阻断完整性或等位选择性。两种等位切后序列相同时共用查询，但仍分别列出反应。");
            else if (primers.AlternateInner != null)
                set.Notes.Add("参考与替代等位分别解释；片段保留实际合成序列中的 SNP 与人为错配。BLAST 不预测等位选择性。");
            return set;
        }

        private static BlastQuerySet NewSet(string mode, int rank)
        {
            return new BlastQuerySet { Mode = mode, CandidateLabel = mode + " 候选 #" + rank.ToString(CultureInfo.InvariantCulture) };
        }

        private static void AddPair(BlastQuerySet set, string name, Primer forward, Primer reverse, string fRole, string rRole)
        {
            if (forward == null || reverse == null) throw new ArgumentException("候选引物不完整。");
            BlastReaction reaction = new BlastReaction { Name = name };
            Bind(set, reaction, fRole, forward.Sequence, false);
            Bind(set, reaction, rRole, reverse.Sequence, true);
            set.Reactions.Add(reaction);
        }

        private static void AddLampReaction(BlastQuerySet set, LampPrimerSet primers, LampOligo fip, LampOligo bip,
            string name, string allele, bool pa)
        {
            BlastReaction reaction = new BlastReaction { Name = name };
            BindRegion(set, reaction, primers.F3, "F3", false, "F3", false);
            BindRegion(set, reaction, fip, "F2", false, "F2_" + allele, false);
            BindRegion(set, reaction, fip, "F1c", true, "F1c_" + allele, false);
            BindRegion(set, reaction, bip, "B1c", false, "B1c_" + allele, pa);
            BindRegion(set, reaction, bip, "B2", true, "B2_" + allele + (pa ? "（切后有效 DNA）" : ""), pa);
            BindRegion(set, reaction, primers.B3, "B3", true, "B3", false);
            set.Reactions.Add(reaction);
        }

        private static void BindRegion(BlastQuerySet set, BlastReaction reaction, LampOligo oligo,
            string name, bool reverse, string label, bool activated)
        {
            if (oligo == null) throw new ArgumentException("LAMP 候选缺少 " + name + "。");
            List<LampRegion> regions = activated ? oligo.ActivatedRegions : oligo.Regions;
            LampRegion selected = null;
            foreach (LampRegion region in regions)
                if (region.Name == name) { selected = region; break; }
            if (selected == null || selected.Reverse != reverse)
                throw new ArgumentException("LAMP 候选 " + name + " 结合区缺失或方向不符。");
            string queryId = AddQuery(set, label, selected.Sequence);
            reaction.Bindings.Add(new BlastBinding { QueryId = queryId, Role = name, Reverse = reverse });
        }

        private static void AddAuxiliary(BlastQuerySet set, LampOligo oligo, string label)
        { if (oligo != null) AddQuery(set, label, oligo.Sequence); }

        private static void Bind(BlastQuerySet set, BlastReaction reaction, string role, string sequence, bool reverse)
        { reaction.Bindings.Add(new BlastBinding { QueryId = AddQuery(set, role, sequence), Role = role, Reverse = reverse }); }

        private static string AddQuery(BlastQuerySet set, string label, string sequence)
        {
            if (String.IsNullOrWhiteSpace(sequence)) throw new ArgumentException("BLAST 查询序列为空。");
            sequence = sequence.ToUpperInvariant();
            foreach (char value in sequence)
                if ("ACGT".IndexOf(value) < 0) throw new ArgumentException("BLAST 查询只能包含明确的 A/C/G/T DNA 碱基。");
            foreach (BlastQuery query in set.Queries)
                if (query.Sequence == sequence)
                {
                    if (!(" / " + query.Label + " / ").Contains(" / " + label + " / ")) query.Label += " / " + label;
                    return query.Id;
                }
            string id = "Q" + (set.Queries.Count + 1).ToString("000", CultureInfo.InvariantCulture);
            set.Queries.Add(new BlastQuery { Id = id, Label = label, Sequence = sequence });
            return id;
        }
    }

    public static class BlastAssessment
    {
        private const int CombinationLimit = 200, SearchStepLimit = 50000;
        internal sealed class Locus
        {
            public string Reaction, Accession;
            public bool Reverse;
            public long Start, End;
            public List<BlastHit> Hits = new List<BlastHit>();
        }
        internal sealed class Evaluation
        {
            public List<Locus> Loci = new List<Locus>();
            public bool Truncated;
            public int SearchSteps;
        }

        public static string Text(BlastQuerySet queries, BlastSettings settings, BlastResult result)
        {
            CheckArguments(queries, settings, result);
            Evaluation evaluation = Evaluate(queries, settings, result);
            StringBuilder text = new StringBuilder();
            text.AppendLine("Primer Designer · NCBI BLAST 潜在脱靶检查");
            text.AppendLine("候选快照：" + queries.CandidateLabel);
            text.AppendLine("RID：" + result.Rid + "    完成时间 UTC：" + result.CompletedUtc.ToString("o", CultureInfo.InvariantCulture));
            text.AppendLine("请求数据库：" + settings.Database + "    NCBI 返回数据库：" + (String.IsNullOrEmpty(result.Database) ? "未报告" : result.Database));
            text.AppendLine("候选 HSP 阈值：查询覆盖率 ≥ " + F(settings.MinCoverage) + "%；一致率 ≥ " + F(settings.MinIdentity)
                + "%；返回上限 " + settings.HitListSize + " 条数据库记录/查询；组合跨度 ≤ " + settings.MaxLocusSpan + " nt。");
            text.AppendLine("覆盖率与一致率逐个 HSP 计算，不合并不连续 HSP；末端 5 nt 错配仅供审阅，不作硬筛选。");
            text.AppendLine("预期登录号（仅本地标注）：" + (String.IsNullOrWhiteSpace(settings.ExpectedAccessions) ? "未指定；不推断目标。" : settings.ExpectedAccessions));
            text.AppendLine("预期登录号吻合不表示坐标正确；同一染色体可含多个非目标位点。标题/基因名不参与目标认定。");
            text.AppendLine("本报告不是特异性通过证明：单条命中不等于扩增，几何组合也不预测实际反应、SNP 区分或临床表现。");
            foreach (string note in queries.Notes) text.AppendLine("说明：" + note);
            foreach (string note in result.Notes) text.AppendLine("NCBI/解析提示：" + note);
            text.AppendLine();
            text.AppendLine("查询快照（5′→3′；与上传 DNA 完全一致；相同序列仅提交一次）");
            foreach (BlastQuery query in queries.Queries)
            {
                text.AppendLine(query.Id + "  " + query.Label + "  " + query.Sequence.Length + " nt");
                text.AppendLine(query.Sequence);
            }
            text.AppendLine();
            text.AppendLine("逐查询结果");
            foreach (BlastQuery query in queries.Queries)
            {
                List<BlastHit> hits = HitsFor(result, query.Id);
                int eligible = 0, partial = 0, unknownTerminal = 0;
                HashSet<string> accessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BlastHit hit in hits)
                {
                    if (Qualified(hit, settings)) eligible++;
                    if (hit.Coverage < 99.999 || hit.QueryFrom != 1 || hit.QueryTo != query.Sequence.Length) partial++;
                    if (hit.ThreePrimeMismatches < 0) unknownTerminal++;
                    if (!String.IsNullOrEmpty(hit.Accession)) accessions.Add(hit.Accession);
                }
                bool complete = result.CompletedQueryIds.Contains(query.Id);
                text.AppendLine(query.Id + " " + query.Label + "：" + accessions.Count + " 条记录 / " + hits.Count
                    + " 个 HSP，达到阈值 " + eligible + " 个。");
                if (!complete) text.AppendLine("  警告：返回结果缺少该查询的完成记录；不能按零命中解释。");
                else if (hits.Count == 0) text.AppendLine("  本次未发现返回命中；短序列、数据库覆盖和检索截断均可能造成漏检，不能证明特异性。");
                if (partial > 0) text.AppendLine("  警告：" + partial + " 个 HSP 仅覆盖部分查询，不能视为整条完全配对。");
                if (unknownTerminal > 0) text.AppendLine("  警告：" + unknownTerminal + " 个 HSP 的查询 3′ 末端 5 nt 未完整覆盖或含歧义碱基（未知）。");
                if (accessions.Count >= settings.HitListSize)
                    text.AppendLine("  警告：达到返回上限，可能存在未返回命中；组合检查不完整。");
                foreach (BlastHit hit in hits)
                {
                    text.AppendLine("  " + hit.Accession + " " + hit.SubjectFrom + "→" + hit.SubjectTo + " (" + Strand(hit.Reverse)
                        + ")；query " + hit.QueryFrom + "–" + hit.QueryTo + "；一致率 " + F(hit.Identity) + "% / 覆盖率 "
                        + F(hit.Coverage) + "%；3′末端5nt错配 " + Terminal(hit) + "；E=" + E(hit.EValue)
                        + "；" + (Qualified(hit, settings) ? "达到阈值" : "未达到阈值") + "；" + ExpectedLabel(hit.Accession, settings));
                    text.AppendLine("    " + OneLine(hit.Title));
                }
            }
            HashSet<string> ids = new HashSet<string>();
            foreach (BlastQuery query in queries.Queries) ids.Add(query.Id);
            foreach (BlastHit hit in result.Hits)
                if (!ids.Contains(hit.QueryId ?? "")) { text.AppendLine("警告：存在无法对应到查询快照的 HSP，已排除组合分析。"); break; }
            text.AppendLine();
            text.AppendLine("反应组合检查（同一登录号、正确方向与顺序、非重叠；支持整体反向链）");
            foreach (BlastReaction reaction in queries.Reactions)
            {
                int count = 0;
                foreach (Locus locus in evaluation.Loci) if (locus.Reaction == reaction.Name) count++;
                text.AppendLine(reaction.Name + "：" + (count > 0 ? "发现 " + count + " 组需核查的潜在结合组合。" : "本次未发现满足条件的组合，不能证明特异性。"));
                foreach (Locus locus in evaluation.Loci)
                {
                    if (locus.Reaction != reaction.Name) continue;
                    text.AppendLine("  " + locus.Accession + " " + locus.Start + "–" + locus.End + " / 整体" + Strand(locus.Reverse)
                        + " / 跨度 " + (locus.End - locus.Start + 1) + " nt / " + ExpectedLabel(locus.Accession, settings));
                    for (int i = 0; i < locus.Hits.Count; i++)
                    {
                        BlastHit hit = locus.Hits[i];
                        text.AppendLine("    " + reaction.Bindings[i].Role + " (" + hit.QueryId + ")：" + hit.SubjectFrom + "→" + hit.SubjectTo
                            + "；一致率 " + F(hit.Identity) + "% / 覆盖率 " + F(hit.Coverage) + "%；3′末端5nt错配 " + Terminal(hit));
                    }
                }
            }
            if (evaluation.Truncated) text.AppendLine("警告：组合搜索达到计算/输出上限（" + SearchStepLimit + " 步或 " + CombinationLimit + " 组），已截断；未列出不代表不存在。");
            text.AppendLine("结论范围仅限本次数据库、返回的单个 HSP 与上述阈值。即使未发现额外命中，也不能证明无脱靶；需结合目标登录号/坐标、实验和独立分析核查。");
            return text.ToString();
        }

        // Each row carries the RID and snapshot; no row relies on mutable UI state.
        public static string Csv(BlastQuerySet queries, BlastSettings settings, BlastResult result)
        {
            CheckArguments(queries, settings, result);
            Evaluation evaluation = Evaluate(queries, settings, result);
            StringBuilder csv = new StringBuilder();
            Row(csv, "type", "candidate", "RID", "completed_UTC", "requested_database", "returned_database", "query_id", "label_or_reaction",
                "query_sequence_5to3", "accession", "subject_from", "subject_to", "subject_strand", "query_from", "query_to", "identity_percent", "coverage_percent",
                "terminal_5nt_mismatches", "E_value", "meets_threshold", "expected_accession_label", "title_or_note");
            string[] prefix = { "", queries.CandidateLabel, result.Rid, result.CompletedUtc.ToString("o", CultureInfo.InvariantCulture), settings.Database, result.Database };
            Action<string, string> note = delegate(string kind, string value) { DataRow(csv, prefix, kind, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", value); };
            note("settings", "coverage >= " + F(settings.MinCoverage) + "; identity >= " + F(settings.MinIdentity) + "; hitlist=" + settings.HitListSize
                + "; maximum locus span=" + settings.MaxLocusSpan + "; expected accessions=" + settings.ExpectedAccessions);
            note("limitation", "潜在脱靶筛查；不能证明特异性。单条 HSP/几何组合不等于扩增。预期登录号不能替代坐标核对。末端未完整覆盖或含歧义碱基=未知。");
            foreach (string value in queries.Notes) note("query_note", value);
            foreach (string value in result.Notes) note("result_note", value);
            foreach (BlastQuery query in queries.Queries)
            {
                List<BlastHit> hits = HitsFor(result, query.Id);
                HashSet<string> records = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BlastHit hit in hits) if (!String.IsNullOrEmpty(hit.Accession)) records.Add(hit.Accession);
                string summary = result.CompletedQueryIds.Contains(query.Id) ? (hits.Count == 0 ? "本次未发现返回命中，不能证明特异性" : "返回 HSP=" + hits.Count) : "缺少查询完成记录，结果不完整";
                if (records.Count >= settings.HitListSize) summary += "；达到返回上限，可能漏检";
                DataRow(csv, prefix, "query", query.Id, query.Label, query.Sequence, "", "", "", "", "", "", "", "", "", "", "", "", summary);
                foreach (BlastHit hit in hits)
                    DataRow(csv, prefix, "HSP", query.Id, query.Label, query.Sequence, hit.Accession, N(hit.SubjectFrom), N(hit.SubjectTo), Strand(hit.Reverse),
                        N(hit.QueryFrom), N(hit.QueryTo), F(hit.Identity), F(hit.Coverage), Terminal(hit), E(hit.EValue), Qualified(hit, settings) ? "yes" : "no", ExpectedLabel(hit.Accession, settings), hit.Title);
            }
            foreach (Locus locus in evaluation.Loci)
            {
                StringBuilder segments = new StringBuilder();
                foreach (BlastHit hit in locus.Hits) { if (segments.Length > 0) segments.Append("; "); segments.Append(hit.QueryId + " " + hit.SubjectFrom + "->" + hit.SubjectTo); }
                DataRow(csv, prefix, "potential_combination", "", locus.Reaction, "", locus.Accession, N(locus.Start), N(locus.End), Strand(locus.Reverse), "", "", "", "", "", "", "", ExpectedLabel(locus.Accession, settings), segments.ToString());
            }
            if (evaluation.Loci.Count == 0) note("combination_note", "本次未发现满足条件的组合，不能证明特异性。");
            if (evaluation.Truncated) note("warning", "组合搜索达到计算/输出上限，已截断；未列出不代表不存在。");
            return csv.ToString();
        }

        internal static Evaluation Evaluate(BlastQuerySet queries, BlastSettings settings, BlastResult result)
        {
            Evaluation evaluation = new Evaluation();
            foreach (BlastReaction reaction in queries.Reactions)
            {
                if (reaction.Bindings.Count != 2 && reaction.Bindings.Count != 6) continue;
                Dictionary<string, List<BlastHit>> records = new Dictionary<string, List<BlastHit>>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> relevant = new HashSet<string>();
                foreach (BlastBinding binding in reaction.Bindings) relevant.Add(binding.QueryId);
                foreach (BlastHit hit in result.Hits)
                {
                    if (!relevant.Contains(hit.QueryId ?? "") || !Qualified(hit, settings) || String.IsNullOrEmpty(hit.Accession)
                        || hit.SubjectFrom <= 0 || hit.SubjectTo <= 0) continue;
                    List<BlastHit> group;
                    if (!records.TryGetValue(hit.Accession, out group)) { group = new List<BlastHit>(); records.Add(hit.Accession, group); }
                    group.Add(hit);
                }
                foreach (KeyValuePair<string, List<BlastHit>> record in records)
                {
                    for (int reverse = 0; reverse < 2; reverse++)
                    {
                        bool wholeReverse = reverse == 1;
                        List<List<BlastHit>> choices = new List<List<BlastHit>>(); bool possible = true;
                        foreach (BlastBinding binding in reaction.Bindings)
                        {
                            List<BlastHit> matches = new List<BlastHit>(); HashSet<string> distinct = new HashSet<string>();
                            foreach (BlastHit hit in record.Value)
                            {
                                if (hit.QueryId != binding.QueryId || hit.Reverse != (binding.Reverse ^ wholeReverse)) continue;
                                string identity = hit.SubjectFrom + ":" + hit.SubjectTo + ":" + hit.QueryFrom + ":" + hit.QueryTo;
                                if (distinct.Add(identity)) matches.Add(hit);
                            }
                            matches.Sort(delegate(BlastHit a, BlastHit b) { return OrientedStart(a, wholeReverse).CompareTo(OrientedStart(b, wholeReverse)); });
                            if (matches.Count == 0) { possible = false; break; }
                            choices.Add(matches);
                        }
                        if (possible) Search(choices, 0, wholeReverse, 0, 0, new List<BlastHit>(), reaction.Name, record.Key, settings.MaxLocusSpan, evaluation);
                        if (evaluation.Truncated) return evaluation;
                    }
                }
            }
            return evaluation;
        }

        private static void Search(List<List<BlastHit>> choices, int index, bool reverse, long first, long previous,
            List<BlastHit> selected, string reaction, string accession, int maxSpan, Evaluation evaluation)
        {
            foreach (BlastHit hit in choices[index])
            {
                if (++evaluation.SearchSteps > SearchStepLimit) { evaluation.Truncated = true; return; }
                long start = OrientedStart(hit, reverse), end = OrientedEnd(hit, reverse), begin = index == 0 ? start : first;
                if (index > 0 && start <= previous) continue;
                if (end - begin + 1 > maxSpan) continue;
                selected.Add(hit);
                if (index == choices.Count - 1)
                {
                    if (evaluation.Loci.Count >= CombinationLimit) { evaluation.Truncated = true; selected.RemoveAt(selected.Count - 1); return; }
                    evaluation.Loci.Add(new Locus { Reaction = reaction, Accession = accession, Reverse = reverse,
                        Start = reverse ? -end : begin, End = reverse ? -begin : end, Hits = new List<BlastHit>(selected) });
                }
                else Search(choices, index + 1, reverse, begin, end, selected, reaction, accession, maxSpan, evaluation);
                selected.RemoveAt(selected.Count - 1);
                if (evaluation.Truncated) return;
            }
        }

        private static long OrientedStart(BlastHit hit, bool reverse)
        { return reverse ? -(long)Math.Max(hit.SubjectFrom, hit.SubjectTo) : Math.Min(hit.SubjectFrom, hit.SubjectTo); }
        private static long OrientedEnd(BlastHit hit, bool reverse)
        { return reverse ? -(long)Math.Min(hit.SubjectFrom, hit.SubjectTo) : Math.Max(hit.SubjectFrom, hit.SubjectTo); }
        private static bool Qualified(BlastHit hit, BlastSettings settings)
        { return hit.Coverage >= settings.MinCoverage && hit.Identity >= settings.MinIdentity; }
        private static List<BlastHit> HitsFor(BlastResult result, string id)
        { List<BlastHit> hits = new List<BlastHit>(); foreach (BlastHit hit in result.Hits) if (hit.QueryId == id) hits.Add(hit); return hits; }
        internal static bool Expected(string accession, string list)
        {
            if (String.IsNullOrEmpty(accession) || String.IsNullOrWhiteSpace(list)) return false;
            foreach (string token in list.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n', '，', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = token.Trim();
                if (value.IndexOf('.') >= 0)
                { if (String.Equals(value, accession, StringComparison.OrdinalIgnoreCase)) return true; }
                else
                {
                    int dot = accession.LastIndexOf('.');
                    string baseAccession = dot < 0 ? accession : accession.Substring(0, dot);
                    if (String.Equals(value, baseAccession, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }
        private static string ExpectedLabel(string accession, BlastSettings settings)
        { return String.IsNullOrWhiteSpace(settings.ExpectedAccessions) ? "未指定预期目标" : Expected(accession, settings.ExpectedAccessions) ? "预期登录号吻合，仍需核对坐标" : "预期列表外，需核查"; }
        private static string Terminal(BlastHit hit) { return hit.ThreePrimeMismatches < 0 ? "未知（未完整覆盖或含歧义碱基）" : N(hit.ThreePrimeMismatches); }
        private static string Strand(bool reverse) { return reverse ? "负链" : "正链"; }
        private static string F(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string E(double value) { return value.ToString("0.###E+0", CultureInfo.InvariantCulture); }
        private static string N(long value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string OneLine(string value) { return (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '); }
        private static void CheckArguments(BlastQuerySet queries, BlastSettings settings, BlastResult result)
        {
            if (queries == null || settings == null || result == null) throw new ArgumentNullException("BLAST 查询、设置及结果不能为空。");
            if (settings.MaxLocusSpan <= 0 || settings.HitListSize <= 0 || Double.IsNaN(settings.MinCoverage) || Double.IsNaN(settings.MinIdentity)
                || settings.MinCoverage < 0 || settings.MinCoverage > 100 || settings.MinIdentity < 0 || settings.MinIdentity > 100)
                throw new ArgumentException("BLAST 分析阈值无效。");
        }
        private static void DataRow(StringBuilder csv, string[] prefix, string type, params string[] values)
        {
            string[] row = new string[prefix.Length + values.Length]; Array.Copy(prefix, row, prefix.Length); row[0] = type;
            Array.Copy(values, 0, row, prefix.Length, values.Length); Row(csv, row);
        }
        private static void Row(StringBuilder csv, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) csv.Append(',');
                string value = values[i] ?? "";
                string trimmed = value.TrimStart();
                if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) value = "'" + value;
                csv.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            }
            csv.AppendLine();
        }
    }
}
