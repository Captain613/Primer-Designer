using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RpaDesigner
{
    public static class BlastManualTemplate
    {
        sealed class Record
        {
            public string Role, Sequence;
            public List<string> Reactions = new List<string>();
        }
        public static string GroupName(BlastQuerySet set)
        {
            string mode = Regex.Replace(set.Mode ?? "Set", @"[^A-Za-z0-9]+", "_").Trim('_');
            if (mode.Length == 0) mode = "Set";
            var rank = Regex.Match(set.CandidateLabel ?? "", @"#(\d+)");
            return mode + "_" + (rank.Success ? Int32.Parse(rank.Groups[1].Value).ToString("000", CultureInfo.InvariantCulture) : "001");
        }
        public static string Create(BlastQuerySet set)
        {
            if (set == null || set.Reactions.Count == 0) throw new ArgumentException("没有可导出的候选反应。");
            var records = new List<Record>();
            var reactionNames = new List<string>();
            for (int i = 0; i < set.Reactions.Count; i++)
            {
                string name = set.Reactions.Count == 1 ? "common" : i == 0 ? "ref" : i == 1 ? "alt" : i == 2 ? "control" : "reaction" + (i + 1);
                reactionNames.Add(name);
                foreach (BlastBinding binding in set.Reactions[i].Bindings)
                {
                    string role = binding.Role.StartsWith("F_", StringComparison.Ordinal) ? "F" : binding.Role.StartsWith("R_", StringComparison.Ordinal) ? "R" : binding.Role;
                    BlastQuery query = set.Queries.Single(q => q.Id == binding.QueryId);
                    Add(records, role, query.Sequence, name);
                }
            }
            foreach (BlastQuery query in set.Queries)
            {
                foreach (string role in new[] { "LF", "LB" })
                    if (query.Label.Contains(role + "（辅助环引物）")) foreach (string name in reactionNames) Add(records, role, query.Sequence, name);
            }
            var text = new StringBuilder(); string group = GroupName(set);
            foreach (Record record in records)
            {
                string suffix = record.Reactions.Count == reactionNames.Count ? "common" : String.Join("_", record.Reactions);
                text.Append('>').Append(group).Append('_').Append(record.Role).Append('_').Append(suffix)
                    .Append(" reactions=").AppendLine(String.Join(",", record.Reactions));
                text.AppendLine(record.Sequence);
            }
            return text.ToString();
        }
        static void Add(List<Record> records, string role, string sequence, string reaction)
        {
            if (String.IsNullOrWhiteSpace(sequence) || sequence.Length < 7 || sequence.Length > 1000 || sequence.Any(c => "ACGT".IndexOf(c) < 0))
                throw new ArgumentException("网页 BLAST 查询需为 7–1000 nt 的明确 A/C/G/T 序列：" + role);
            Record record = records.FirstOrDefault(r => r.Role == role && r.Sequence == sequence);
            if (record == null) { record = new Record { Role = role, Sequence = sequence }; records.Add(record); }
            if (!record.Reactions.Contains(reaction)) record.Reactions.Add(reaction);
        }
    }
}
