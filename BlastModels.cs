using System;
using System.Collections.Generic;

namespace RpaDesigner
{
    public sealed class BlastQuery
    {
        public string Id, Label, Sequence;
    }
    public sealed class BlastBinding
    {
        public string QueryId, Role;
        public bool Reverse;
    }
    public sealed class BlastReaction
    {
        public string Name;
        // Bindings are ordered along the original template's positive strand.
        public List<BlastBinding> Bindings = new List<BlastBinding>();
    }
    public sealed class BlastQuerySet
    {
        public string Mode, CandidateLabel;
        public List<BlastQuery> Queries = new List<BlastQuery>();
        public List<BlastReaction> Reactions = new List<BlastReaction>();
        public List<string> Notes = new List<string>();
    }
    public sealed class BlastSettings
    {
        public string Database = "refseq_representative_genomes";
        public string Email = "";
        // Optional local interpretation only: never filters the remote search.
        public string ExpectedAccessions = "";
        public int HitListSize = 100, MaxLocusSpan = 2000;
        public double MinCoverage = 90, MinIdentity = 80;
    }
    public sealed class BlastHit
    {
        public string QueryId, Accession, Title, QueryAligned, SubjectAligned;
        public int QueryLength, QueryFrom, QueryTo, SubjectFrom, SubjectTo;
        public int AlignLength, Identities, Gaps;
        public double EValue, BitScore;
        public double Coverage, Identity;
        public bool Reverse;
        // -1 when this HSP omits terminal bases or the aligned terminus is ambiguous.
        public int ThreePrimeMismatches = -1;
    }
    public sealed class BlastResult
    {
        public string Rid, Database, RawXml;
        public DateTime CompletedUtc;
        public List<BlastHit> Hits = new List<BlastHit>();
        public List<string> CompletedQueryIds = new List<string>();
        public List<string> Notes = new List<string>();
    }
}
