using System;
using System.Linq;

namespace RpaDesigner
{
    public static class BlastWebPreset
    {
        public const string DefaultOrganism = "Homo sapiens (taxid:9606)";

        // NCBI's webpage bookmark flags preserve the database and unchecked
        // filters when its client-side defaults initialize. This opens a form;
        // it does not submit a URL API job or include a primer query. With
        // USER_FORMAT_DEFAULTS enabled, omitted formatter values become empty
        // hidden inputs, so the complete display defaults must be explicit.
        // The current search form carries the tabbed results choice in ADV_VIEW;
        // NEW_VIEW alone does not preserve that choice in a bookmark.
        public static string Create(string organism)
        {
            organism = (organism ?? "").Trim();
            if (organism.Length > 200 || organism.Any(Char.IsControl))
                throw new ArgumentException("物种名称请使用不超过 200 字符的单行名称或 taxid。");
            return "https://blast.ncbi.nlm.nih.gov/Blast.cgi?PAGE_TYPE=BlastSearch&PROGRAM=blastn&BLAST_PROGRAMS=blastn&PAGE=Nucleotides"
                + "&USER_FORMAT_DEFAULTS=on&SET_SAVED_SEARCH=true&PROG_DEFAULTS=on&DATABASE=refseq_genomes"
                + (organism.Length == 0 ? "" : "&EQ_MENU=" + Uri.EscapeDataString(organism))
                + "&MAX_NUM_SEQ=1000&SHORT_QUERY_ADJUST=on&EXPECT=1000&WORD_SIZE=7&HSP_RANGE_MAX=0&MATCH_SCORES=1%2C-3&GAPCOSTS=5%202&FILTER=F"
                + "&FORMAT_TYPE=HTML&FORMAT_OBJECT=Alignment&ADV_VIEW=on&NEW_VIEW=true&ALIGNMENT_VIEW=Pairwise"
                + "&SHOW_OVERVIEW=true&SHOW_LINKOUT=true&GET_SEQUENCE=true&NCBI_GI=false&SHOW_CDS_FEATURE=false"
                + "&DESCRIPTIONS=1000&ALIGNMENTS=1000&NUM_OVERVIEW=1000&MASK_CHAR=2&MASK_COLOR=1&LINE_LENGTH=60";
        }

        public static string Explanation
        {
            get
            {
                return String.Join("\r\n", new[] {
                    "点击“打开 NCBI（推荐设置）”会打开已预填参数的核酸 BLAST 页面。物种默认人类，可在上方改为目标物种；留空时需在网页选择物种。",
                    "打开后粘贴 / 上传本程序导出的区段 FASTA，再手动点击 BLAST；完成后下载全部查询的完整 XML/XML2。链接只含搜索设置，打开网页不会自动提交序列。",
                    "NCBI 页面可能改版。提交前展开 Algorithm parameters，核对下列值；网页上仍可自行调整。",
                    "",
                    "网页搜索参数：推荐值与含义",
                    "Database = RefSeq Genome Database（refseq_genomes）：按目标物种基因组查找潜在结合位置。人类基因组 DNA 扩增使用此库；RefSeq RNA 不能替代基因组复核。",
                    "Organism = Homo sapiens (taxid:9606)：仅搜索人类记录。更换物种后应使用相应基因组；限制人类的结果不评估其他物种的交叉反应。",
                    "Program = Somewhat similar sequences (blastn)：使用 BLASTN，适合本流程的短区段搜索。",
                    "Automatically adjust parameters for short input sequences = 勾选：短序列自动调整；提交后可在 Search Summary 核对实际参数。",
                    "Word size = 7：启动比对的连续匹配种子长度。数值小可增加短引物命中机会；不是允许错配 7 个。",
                    "Expect threshold / E-value = 1000：较宽的统计报告阈值，保留更多短序列局部命中；不是匹配率或扩增成功率。",
                    "Match/Mismatch scores = 1 / −3：每个匹配加 1 分、错配扣 3 分，用于 BLAST 比对评分；不是允许 3 处错配。",
                    "Gap costs = Existence 5 / Extension 2：插入或缺失的开口 / 延伸罚分。此预设使用 5/2，避免与当前算法不兼容的 0/0。",
                    "Max target sequences = 1000：每条查询最多返回 1000 条数据库目标记录；一条染色体记录可以包含多个局部命中，不是总结合位点数。",
                    "Max matches in a query range = 0：关闭按查询范围的命中裁剪，尽量保留同一引物区段的多个位置；0 不代表不返回命中。",
                    "Low complexity / Species-specific repeats / Mask for lookup table only / Lower case masking = 不勾选：保留短引物查询，不屏蔽相关碱基。",
                    "Query subrange 与额外 Entrez 限制 = 留空；不启用 Align two or more sequences：搜索完整区段，并查询所选物种的基因组数据库。",
                    "结果显示 = HTML，新版视图、图形概览及 Pairwise 比对：保留常规结果界面。显示格式不会改变搜索命中；下载时仍选择全部查询的 XML/XML2。",
                    "",
                    "本地批量复核选项（与上述网页参数分别设置）",
                    "完整 BLAST XML + 区段 FASTA：使用同一次网页查询下载的完整结果与原始区段文件；不是完整订购 FIP/BIP 的 FASTA。",
                    "预期登录号：目标参考序列，例如 NC_000010.11。可留空，但所有组合将待确认。",
                    "区段：选择用于标注预期位点的锚点。mLAMP SNP 在 F2 末端时选 F2；软件仍复核六个核心区段，不是仅复核所选区段。",
                    "该区段 3′末位坐标：所选参考序列的 1-based 基因组坐标，不是输入模板的相对坐标。填 0 表示未指定。",
                    "每区段最多错配 = 4：完整参考比较的候选初筛上限，包含人为错配与等位错配；不是 BLAST 的 Mismatch score。",
                    "布局跨度上限 = 1000 bp：完整组合的最大外侧跨度，不是网页的 E-value 1000。",
                    "联网补齐公开参考区段 = 默认勾选：补齐局部比对遗漏的末端；已有参考缓存时可离线。缺少缓存且禁用联网会产生未完成区域。",
                    "",
                    "结果用于计算初筛。未检出其他组合不能判定特异性通过；二聚体、发卡、自扩增和等位区分需要另外评估。"
                });
            }
        }
    }
}
