using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace RpaDesigner
{
    // NCBI Common URL API. No query titles, template names or file paths are sent.
    public static class BlastOnline
    {
        private const string Endpoint = "https://blast.ncbi.nlm.nih.gov/Blast.cgi";
        private const int MaxResponseBytes = 20 * 1024 * 1024;
        private const int TotalTimeoutMs = 30 * 60 * 1000;
        internal const int RequestIntervalMs = 10000;
        internal const int PollIntervalMs = 60000;
        private static readonly SemaphoreSlim RequestGate = new SemaphoreSlim(1, 1);
        private static readonly Stopwatch ServiceClock = Stopwatch.StartNew();
        private static readonly Dictionary<string, long> RidNextRequest = new Dictionary<string, long>(StringComparer.Ordinal);
        private static long nextRequest;

        public static string BuildFasta(BlastQuerySet queries)
        {
            ValidateQueries(queries);
            var text = new StringBuilder();
            foreach (BlastQuery query in queries.Queries)
                text.Append('>').Append(query.Id).Append('\n').Append(query.Sequence.ToUpperInvariant()).Append('\n');
            return text.ToString();
        }

        public static void Validate(BlastQuerySet queries, BlastSettings settings)
        {
            ValidateQueries(queries);
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.Database != "refseq_representative_genomes" && settings.Database != "refseq_genomes" && settings.Database != "core_nt")
                throw new ArgumentException("请选择有效的 NCBI 核酸数据库。");
            if (String.IsNullOrWhiteSpace(settings.Email)) throw new ArgumentException("请填写联系邮箱；本程序按照 NCBI 开发者使用指南在请求中提供 email 与 tool。");
            if (settings.Email.Length > 254) throw new ArgumentException("联系邮箱过长。");
            try
            {
                MailAddress address = new MailAddress(settings.Email.Trim());
                if (!String.Equals(address.Address, settings.Email.Trim(), StringComparison.OrdinalIgnoreCase) || settings.Email.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                    throw new FormatException();
            }
            catch (FormatException) { throw new ArgumentException("联系邮箱格式无效。"); }
            if (settings.HitListSize < 1 || settings.HitListSize > 500) throw new ArgumentException("每段命中上限应为 1–500。");
            if (settings.MaxLocusSpan < 1 || settings.MaxLocusSpan > 1000000) throw new ArgumentException("位点组合的最大跨度应为 1–1,000,000 bp。");
            if (!Percentage(settings.MinCoverage) || !Percentage(settings.MinIdentity)) throw new ArgumentException("覆盖率和一致率阈值应为 0–100%。");
        }

        private static bool Percentage(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 100; }

        private static void ValidateQueries(BlastQuerySet queries)
        {
            if (queries == null || queries.Queries == null || queries.Queries.Count == 0) throw new ArgumentException("没有可提交的引物结合区段。");
            int total = 0; var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BlastQuery query in queries.Queries)
            {
                if (query == null || String.IsNullOrEmpty(query.Id) || !Regex.IsMatch(query.Id, @"\AQ[0-9]{3}\z") || !ids.Add(query.Id))
                    throw new ArgumentException("BLAST 查询必须使用互不重复的匿名编号 Q001 等。");
                if (String.IsNullOrEmpty(query.Sequence) || query.Sequence.Length < 7 || !Regex.IsMatch(query.Sequence, @"\A[ACGTacgt]+\z"))
                    throw new ArgumentException("BLAST 每段查询至少需要 7 nt，且只能包含 A、C、G、T；RNA、封闭修饰和连接序列应先按结合区段处理。");
                total += query.Sequence.Length;
                if (total > 1000) throw new ArgumentException("一次 BLAST 查询的总长度不能超过 1,000 nt；请减少待检查区段。");
            }
        }

        public static string BuildSubmission(BlastQuerySet queries, BlastSettings settings)
        {
            Validate(queries, settings);
            return Form("CMD", "Put", "PROGRAM", "blastn", "DATABASE", settings.Database,
                "QUERY", BuildFasta(queries), "WORD_SIZE", "7", "EXPECT", "1000", "NUCL_REWARD", "1",
                "NUCL_PENALTY", "-3", "GAPCOSTS", "5 2", "FILTER", "F", "SHORT_QUERY_ADJUST", "false",
                "HITLIST_SIZE", settings.HitListSize.ToString(CultureInfo.InvariantCulture), "FORMAT_TYPE", "XML2_S",
                "email", settings.Email.Trim(), "tool", "PrimerDesigner");
        }

        private static string Form(params string[] pairs)
        {
            var text = new StringBuilder();
            for (int i = 0; i < pairs.Length; i += 2)
            {
                if (i != 0) text.Append('&');
                text.Append(Uri.EscapeDataString(pairs[i])).Append('=').Append(Uri.EscapeDataString(pairs[i + 1]));
            }
            return text.ToString();
        }

        public static BlastResult Run(BlastQuerySet queries, BlastSettings settings, Action<string> progress, CancellationToken token)
        {
            string submission = BuildSubmission(queries, settings);
            var elapsed = Stopwatch.StartNew(); token.ThrowIfCancellationRequested();
            if (progress != null) progress("正在向 NCBI 提交匿名引物结合区段…");
            string response = Request(submission, null, elapsed, token);
            Match ridMatch = Regex.Match(response, @"\bRID\s*=\s*([A-Z0-9-]{5,64})\b", RegexOptions.IgnoreCase);
            if (!ridMatch.Success) throw new InvalidOperationException("NCBI 未返回有效 RID，无法确认提交状态；程序没有自动重复上传。请检查网络、数据库及邮箱设置。");
            string rid = ridMatch.Groups[1].Value;
            try
            {
            long firstCheckDelay = FirstCheckDelayMs(response);
            if (progress != null) progress("NCBI 已接收：RID " + rid + "。预计约 " + (firstCheckDelay / 1000).ToString(CultureInfo.InvariantCulture) + " 秒后首次检查；可随时取消。");
            Wait(firstCheckDelay, elapsed, token);
            while (true)
            {
                response = Request(Form("CMD", "Get", "RID", rid, "FORMAT_TYPE", "XML2_S", "email", settings.Email.Trim(), "tool", "PrimerDesigner"), rid, elapsed, token);
                string state = ResponseState(response);
                if (state == "WAITING" || state == "READY")
                {
                    if (progress != null) progress("NCBI " + (state == "WAITING" ? "仍在计算" : "正在生成报告") + "，RID " + rid + "；约 60 秒后再次检查。");
                    Wait(PollIntervalMs, elapsed, token); continue;
                }
                if (state == "FAILED") throw new InvalidOperationException("NCBI 搜索失败（FAILED），RID " + rid + "。本次不能形成特异性结论。");
                if (state == "UNKNOWN") throw new InvalidOperationException("NCBI 无法识别此任务（UNKNOWN），RID " + rid + "；任务可能已过期。本次不能形成特异性结论。");
                token.ThrowIfCancellationRequested();
                BlastResult result;
                try { result = ParseXmlCore(response, queries, settings.HitListSize); }
                catch (Exception ex)
                {
                    if (!(ex is InvalidDataException) && !(ex is XmlException)) throw;
                    throw new InvalidOperationException("NCBI 返回的报告无效或不完整，RID " + rid + "。本次不能形成特异性结论。" + ex.Message, ex);
                }
                result.Rid = rid;
                if (!String.Equals(result.Database, settings.Database, StringComparison.OrdinalIgnoreCase))
                    result.Notes.Add("请求数据库为 " + settings.Database + "；NCBI 报告中的实际数据库为 " + result.Database + "，应以后者解释检索范围。");
                if (progress != null) progress("BLAST 完成：" + result.CompletedQueryIds.Count + " 段查询，" + result.Hits.Count + " 条局部比对。");
                return result;
            }
            }
            catch (TimeoutException ex) { throw new TimeoutException(ex.Message + " RID " + rid + "。", ex); }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.IndexOf(rid, StringComparison.Ordinal) >= 0) throw;
                throw new InvalidOperationException(ex.Message + " RID " + rid + "。", ex);
            }
            catch (InvalidDataException ex) { throw new InvalidOperationException(ex.Message + " RID " + rid + "。本次未完成检查。", ex); }
            catch (IOException ex) { throw new InvalidOperationException("读取 NCBI 响应中断，RID " + rid + "。本次未完成检查。", ex); }
            catch (DecoderFallbackException ex) { throw new InvalidOperationException("NCBI 响应编码无效，RID " + rid + "。本次未完成检查。", ex); }
        }

        internal static string ResponseState(string response)
        {
            // Never interpret a status-looking string inside a valid XML report as a status page.
            string trimmed = (response ?? "").TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (trimmed.StartsWith("<?xml", StringComparison.Ordinal) || trimmed.StartsWith("<Blast", StringComparison.Ordinal)) return "";
            Match state = Regex.Match(response ?? "", @"\bStatus\s*=\s*(WAITING|FAILED|UNKNOWN|READY)\b", RegexOptions.IgnoreCase);
            return state.Success ? state.Groups[1].Value.ToUpperInvariant() : "";
        }

        // RTOE is NCBI's estimated completion time, not a polling interval.
        // The global request gate still enforces at least ten seconds after Put;
        // each subsequent Get for this RID remains at least one minute apart.
        internal static long FirstCheckDelayMs(string submissionResponse)
        {
            Match match = Regex.Match(submissionResponse ?? "", @"(?m)^[ \t]*RTOE[ \t]*=[ \t]*([0-9]+)[ \t]*\r?$", RegexOptions.IgnoreCase);
            long seconds;
            if (!match.Success || !Int64.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
                return PollIntervalMs;
            return Math.Max(RequestIntervalMs / 1000L, Math.Min(seconds, 1800L)) * 1000;
        }

        private static void Wait(long delay, Stopwatch elapsed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            long remaining = TotalTimeoutMs - elapsed.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException("NCBI BLAST 已等待 30 分钟，任务停止；尚未获得完整结果，不能判断特异性。");
            if (token.WaitHandle.WaitOne((int)Math.Min(delay, remaining))) token.ThrowIfCancellationRequested();
            if (delay >= remaining || elapsed.ElapsedMilliseconds >= TotalTimeoutMs)
                throw new TimeoutException("NCBI BLAST 已等待 30 分钟，任务停止；尚未获得完整结果，不能判断特异性。");
        }

        private static void ReserveRequest(string rid, Stopwatch elapsed, CancellationToken token)
        {
            int remaining = (int)Math.Max(0, TotalTimeoutMs - elapsed.ElapsedMilliseconds);
            if (!RequestGate.Wait(remaining, token)) throw new TimeoutException("等待 NCBI 请求队列超时。");
            try
            {
                long due = nextRequest, ridDue;
                if (rid != null && RidNextRequest.TryGetValue(rid, out ridDue)) due = Math.Max(due, ridDue);
                if (due > ServiceClock.ElapsedMilliseconds) Wait(due - ServiceClock.ElapsedMilliseconds, elapsed, token);
                token.ThrowIfCancellationRequested();
                if (elapsed.ElapsedMilliseconds >= TotalTimeoutMs) throw new TimeoutException("NCBI BLAST 总等待时间超过 30 分钟。");
                long now = ServiceClock.ElapsedMilliseconds;
                nextRequest = now + RequestIntervalMs;
                // Remove only expired keys. Concurrent runs with the same RID retain their shared deadline.
                var expired = new List<string>();
                foreach (KeyValuePair<string, long> item in RidNextRequest) if (item.Value < now) expired.Add(item.Key);
                foreach (string key in expired) RidNextRequest.Remove(key);
                if (rid != null) RidNextRequest[rid] = now + PollIntervalMs;
            }
            finally { RequestGate.Release(); }
        }

        private static string Request(string data, string rid, Stopwatch elapsed, CancellationToken token)
        {
            ReserveRequest(rid, elapsed, token);
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET Framework 4.x.
            var request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST"; request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
            request.UserAgent = "PrimerDesigner"; request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            int timeout = (int)Math.Max(1, Math.Min(120000, TotalTimeoutMs - elapsed.ElapsedMilliseconds));
            request.Timeout = timeout; request.ReadWriteTimeout = timeout;
            byte[] body = Encoding.UTF8.GetBytes(data); request.ContentLength = body.Length;
            using (token.Register(delegate { request.Abort(); }))
            {
                try
                {
                    using (Stream output = request.GetRequestStream()) output.Write(body, 0, body.Length);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300) throw new InvalidOperationException("NCBI 返回 HTTP " + (int)response.StatusCode + "，没有获得有效报告。");
                        if (response.ContentLength > MaxResponseBytes) throw new InvalidDataException("NCBI 响应超过 20 MB 上限，请减少命中上限。");
                        using (Stream input = response.GetResponseStream())
                        using (var memory = new MemoryStream())
                        {
                            byte[] buffer = new byte[8192]; int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                if (elapsed.ElapsedMilliseconds >= TotalTimeoutMs) throw new TimeoutException("NCBI BLAST 总等待时间超过 30 分钟。");
                                if (memory.Length + read > MaxResponseBytes) throw new InvalidDataException("NCBI 响应超过 20 MB 上限，请减少命中上限。");
                                memory.Write(buffer, 0, read);
                            }
                            token.ThrowIfCancellationRequested();
                            return new UTF8Encoding(false, true).GetString(memory.ToArray());
                        }
                    }
                }
                catch (WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (elapsed.ElapsedMilliseconds >= TotalTimeoutMs) throw new TimeoutException("NCBI BLAST 总等待时间超过 30 分钟。", ex);
                    throw new InvalidOperationException("无法获取 NCBI BLAST 响应（" + ex.Status + "）。请检查网络连接后重试；本次没有完成特异性检查。", ex);
                }
                catch (IOException)
                {
                    token.ThrowIfCancellationRequested();
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    token.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }

        public static BlastResult ParseXml(string xml, BlastQuerySet queries)
        {
            return ParseXmlCore(xml, queries, 0);
        }

        internal static BlastResult ParseXmlCore(string xml, BlastQuerySet queries, int hitLimit)
        {
            ValidateQueries(queries);
            if (String.IsNullOrWhiteSpace(xml) || xml.Length > MaxResponseBytes || Encoding.UTF8.GetByteCount(xml) > MaxResponseBytes) throw new InvalidDataException("报告为空或超过 20 MB 上限。");
            XDocument document;
            var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxResponseBytes };
            using (var text = new StringReader(xml.TrimStart('\uFEFF')))
            using (XmlReader reader = XmlReader.Create(text, readerSettings)) document = XDocument.Load(reader, LoadOptions.None);
            XElement root = document.Root;
            if (root == null || (!Named(root, "BlastXML2") && !Named(root, "BlastOutput2"))) throw new InvalidDataException("响应不是 BLAST XML2 报告。");
            var outputs = new List<XElement>();
            if (Named(root, "BlastOutput2")) outputs.Add(root);
            else foreach (XElement child in root.Elements())
            {
                if (!Named(child, "BlastOutput2")) throw new InvalidDataException("XML2 包含不支持的外部引用或结构；需要单文件 XML2 报告。");
                outputs.Add(child);
            }
            var wanted = new Dictionary<string, BlastQuery>(StringComparer.Ordinal);
            foreach (BlastQuery query in queries.Queries) wanted.Add(query.Id, query);
            var done = new HashSet<string>(StringComparer.Ordinal);
            var result = new BlastResult { RawXml = xml, CompletedUtc = DateTime.UtcNow };
            foreach (XElement output in outputs)
            {
                if (Child(output, "error", false) != null) throw new InvalidDataException("NCBI XML2 报告包含搜索错误。");
                XElement report = Wrapped(output, "report", "Report");
                if (!String.Equals(Value(report, "program"), "blastn", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("返回的报告不是 blastn 核酸比对。");
                XElement target = Wrapped(report, "search-target", "Target");
                string database = Value(target, "db");
                if (String.IsNullOrWhiteSpace(database)) throw new InvalidDataException("报告未说明实际检索数据库。");
                if (result.Database == null) result.Database = database;
                else if (result.Database != database) throw new InvalidDataException("同一批查询返回了不同数据库，不能合并解释。");
                XElement results = Wrapped(report, "results", "Results");
                XElement search = Wrapped(results, "search", "Search");
                string title = Value(search, "query-title", false);
                string id = String.IsNullOrWhiteSpace(title) ? Value(search, "query-id", false) : title.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
                if (id != null && id.StartsWith("lcl|", StringComparison.Ordinal)) id = id.Substring(4);
                BlastQuery query;
                if (id == null || !wanted.TryGetValue(id, out query)) throw new InvalidDataException("报告中出现未知或缺失的匿名查询编号。");
                if (!done.Add(id)) throw new InvalidDataException("报告重复返回查询 " + id + "。");
                if (Integer(search, "query-len") != query.Sequence.Length) throw new InvalidDataException(id + " 的返回长度与提交序列不一致。");
                string message = Value(search, "message", false);
                if (!String.IsNullOrWhiteSpace(message) && message.IndexOf("No hits found", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException(id + " 的报告带有无法确认完成状态的消息。");
                XElement hits = Child(search, "hits", false);
                int hitCount = 0;
                if (hits != null) foreach (XElement hit in hits.Elements())
                {
                    if (!Named(hit, "Hit")) throw new InvalidDataException("XML2 命中列表结构无效。");
                    ParseHit(hit, query, result); hitCount++;
                }
                if (hitCount == 0)
                {
                    // Empty or truncated XML is not an acceptable no-hit report.
                    XElement stat = Wrapped(search, "stat", "Statistics");
                    if (Number(stat, "eff-space") < 0 || Integer(stat, "hsp-len") < 0) throw new InvalidDataException("无命中报告缺少有效搜索统计。");
                    result.Notes.Add(id + "：NCBI 未返回命中；这不能证明特异性，也可能与短序列检索灵敏度或数据库覆盖范围有关。");
                }
                if (hitLimit > 0 && hitCount >= hitLimit)
                    result.Notes.Add(id + " 的命中记录数达到设置的 " + hitLimit + " 条上限，结果可能被截断；未列出的位点不能视为已排除。");
                result.CompletedQueryIds.Add(id);
            }
            foreach (BlastQuery query in queries.Queries) if (!done.Contains(query.Id)) throw new InvalidDataException("报告缺少查询 " + query.Id + "，不能使用部分结果形成结论。");
            return result;
        }

        private static void ParseHit(XElement hit, BlastQuery query, BlastResult result)
        {
            int subjectLength = Integer(hit, "len");
            if (subjectLength < 1) throw new InvalidDataException("命中记录长度无效。");
            XElement descriptions = Child(hit, "description", true), hsps = Child(hit, "hsps", true);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement description in descriptions.Elements())
            {
                if (!Named(description, "HitDescr")) throw new InvalidDataException("XML2 命中描述结构无效。");
                string accession = Value(description, "accession", false);
                if (String.IsNullOrWhiteSpace(accession)) accession = Value(description, "id");
                else accession = VersionedAccession(accession, Value(description, "id", false));
                if (String.IsNullOrWhiteSpace(accession)) throw new InvalidDataException("命中记录缺少 accession/ID。");
                if (!names.ContainsKey(accession)) names.Add(accession, Value(description, "title", false) ?? "");
            }
            if (names.Count == 0) throw new InvalidDataException("命中记录缺少序列标识。");
            int count = 0;
            foreach (XElement hsp in hsps.Elements())
            {
                if (!Named(hsp, "Hsp")) throw new InvalidDataException("XML2 局部比对结构无效。");
                int qFrom = Integer(hsp, "query-from"), qTo = Integer(hsp, "query-to"), sFrom = Integer(hsp, "hit-from"), sTo = Integer(hsp, "hit-to");
                string qSeq = Value(hsp, "qseq").ToUpperInvariant(), sSeq = Value(hsp, "hseq").ToUpperInvariant();
                int length = Integer(hsp, "align-len"), identities = Integer(hsp, "identity");
                int gaps = Child(hsp, "gaps", false) == null ? 0 : Integer(hsp, "gaps");
                double evalue = Number(hsp, "evalue"), bits = Number(hsp, "bit-score");
                if (qFrom < 1 || qTo < 1 || qFrom > query.Sequence.Length || qTo > query.Sequence.Length || sFrom < 1 || sTo < 1 || sFrom > subjectLength || sTo > subjectLength ||
                    length < 1 || qSeq.Length != length || sSeq.Length != length || identities < 0 || identities > length || gaps < 0 || gaps > length || evalue < 0 || bits < 0 ||
                    !Regex.IsMatch(qSeq, @"\A[ACGT-]+\z") || !Regex.IsMatch(sSeq, @"\A[ACGTRYSWKMBDHVN-]+\z")) throw new InvalidDataException("局部比对的坐标、序列或统计量无效。");
                int qBases = 0, sBases = 0, observedIdentities = 0, observedGaps = 0;
                for (int i = 0; i < length; i++)
                {
                    if (qSeq[i] != '-') qBases++; if (sSeq[i] != '-') sBases++;
                    if (qSeq[i] == '-' && sSeq[i] == '-') throw new InvalidDataException("局部比对不能在同一位置同时出现两个缺口。");
                    if (qSeq[i] == '-' || sSeq[i] == '-') observedGaps++;
                    else if (qSeq[i] == sSeq[i]) observedIdentities++;
                }
                if (qBases != Math.Abs(qTo - qFrom) + 1 || sBases != Math.Abs(sTo - sFrom) + 1 || observedIdentities != identities || observedGaps != gaps)
                    throw new InvalidDataException("局部比对序列与坐标/一致数/缺口数不一致。");
                if (qFrom > qTo)
                {
                    int swap = qFrom; qFrom = qTo; qTo = swap; swap = sFrom; sFrom = sTo; sTo = swap;
                    qSeq = ReverseComplement(qSeq); sSeq = ReverseComplement(sSeq);
                }
                if (!String.Equals(qSeq.Replace("-", ""), query.Sequence.Substring(qFrom - 1, qTo - qFrom + 1), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("返回的查询序列与提交区段不一致。");
                int terminal = TerminalMismatches(qSeq, sSeq, qFrom, qTo, query.Sequence.Length);
                foreach (KeyValuePair<string, string> name in names)
                    result.Hits.Add(new BlastHit { QueryId = query.Id, Accession = name.Key, Title = name.Value, QueryLength = query.Sequence.Length,
                        QueryFrom = qFrom, QueryTo = qTo, SubjectFrom = sFrom, SubjectTo = sTo, AlignLength = length, Identities = identities, Gaps = gaps,
                        EValue = evalue, BitScore = bits, Coverage = 100.0 * qBases / query.Sequence.Length, Identity = 100.0 * identities / length,
                        Reverse = sFrom > sTo, QueryAligned = qSeq, SubjectAligned = sSeq, ThreePrimeMismatches = terminal });
                count++;
            }
            if (count == 0) throw new InvalidDataException("命中记录缺少有效局部比对。");
        }

        private static string VersionedAccession(string accession, string id)
        {
            if (String.IsNullOrEmpty(id) || Regex.IsMatch(accession, @"\.[0-9]+\z")) return accession;
            string versioned = null;
            foreach (string token in id.Split('|'))
            {
                int dot = token.LastIndexOf('.');
                if (dot <= 0 || !String.Equals(token.Substring(0, dot), accession, StringComparison.Ordinal) || !Regex.IsMatch(token.Substring(dot + 1), @"\A[0-9]+\z")) continue;
                // Conflicting versions are ambiguous; keep the server's original accession.
                if (versioned != null && !String.Equals(versioned, token, StringComparison.Ordinal)) return accession;
                versioned = token;
            }
            return versioned ?? accession;
        }

        private static int TerminalMismatches(string qSeq, string sSeq, int from, int to, int queryLength)
        {
            int start = Math.Max(1, queryLength - 4);
            if (to != queryLength || from > start) return -1;
            int position = from - 1, mismatches = 0;
            for (int i = 0; i < qSeq.Length; i++)
            {
                if (qSeq[i] != '-') position++;
                // An insertion strictly inside the terminal five-base interval also counts.
                bool terminal = qSeq[i] == '-' ? position >= start && position < queryLength : position >= start;
                if (!terminal) continue;
                // IUPAC ambiguity does not establish either a match or a mismatch.
                // A deletion ('-') is known; an ambiguous subject base is not.
                if (sSeq[i] != '-' && "ACGT".IndexOf(sSeq[i]) < 0) return -1;
                if (qSeq[i] == '-' || qSeq[i] != sSeq[i]) mismatches++;
            }
            return mismatches;
        }

        private static string ReverseComplement(string sequence)
        {
            const string bases = "ACGTRYSWKMBDHVN-", complement = "TGCAYRSWMKVHDBN-";
            var result = new char[sequence.Length];
            for (int i = 0; i < result.Length; i++) result[result.Length - i - 1] = complement[bases.IndexOf(sequence[i])];
            return new string(result);
        }

        private static bool Named(XElement element, string name)
        {
            return element.Name.LocalName == name && (element.Name.NamespaceName == "" || element.Name.NamespaceName == "http://www.ncbi.nlm.nih.gov");
        }
        private static XElement Child(XElement parent, string name, bool required)
        {
            XElement found = null;
            foreach (XElement element in parent.Elements()) if (Named(element, name))
            {
                if (found != null) throw new InvalidDataException("报告包含重复字段 " + name + "。");
                found = element;
            }
            if (found == null && required) throw new InvalidDataException("报告缺少字段 " + name + "。");
            return found;
        }
        private static XElement Wrapped(XElement parent, string name, string type)
        {
            XElement container = Child(parent, name, true);
            return Child(container, type, false) ?? container; // NCBI schema and schema_alt use different type wrappers.
        }
        private static string Value(XElement element, string name) { return Value(element, name, true); }
        private static string Value(XElement element, string name, bool required)
        {
            XElement child = Child(element, name, required);
            if (child == null) return null;
            if (child.HasElements) throw new InvalidDataException("报告字段 " + name + " 不是标量。");
            return child.Value.Trim();
        }
        private static int Integer(XElement element, string name)
        {
            int value; if (!Int32.TryParse(Value(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new InvalidDataException("报告整数无效：" + name + "。");
            return value;
        }
        private static double Number(XElement element, string name)
        {
            double value; if (!Double.TryParse(Value(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || Double.IsInfinity(value) || Double.IsNaN(value)) throw new InvalidDataException("报告数值无效：" + name + "。");
            return value;
        }
    }
}
