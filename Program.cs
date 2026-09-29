using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("RPA / LAMP 引物设计助手")]
[assembly: AssemblyDescription("RPA and LAMP primer designer with optional NCBI BLAST screening")]
[assembly: AssemblyVersion("0.19.0.0")]
[assembly: AssemblyFileVersion("0.19.0.0")]

namespace RpaDesigner
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--snp-self-test") return SnpSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--highlight-self-test") return HighlightSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--lamp-self-test") return LampSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--lamp-region-tm-self-test") return LampRegionTmSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--lamp-thermodynamics-self-test") return LampThermodynamicsSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--pa-lamp-self-test") return PaLampSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--mlamp-self-test") return MLampSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--unlimited-rpa-self-test") return UnlimitedRpaSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--unlimited-lamp-self-test") return UnlimitedLampSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--blast-online-self-test") return BlastOnlineSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--blast-analysis-self-test") return BlastAnalysisSelfTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--mlamp-demo-report")
                {
                    var mlamp = LampDesignEngine.DesignSnp(SnpParser.Parse(LampReportWriter.ExampleSnpFasta()),
                        LampDesignSettings.MLampDefaults(),null,CancellationToken.None);
                    File.WriteAllText(args[1],LampReportWriter.TextReport(mlamp),new UTF8Encoding(true));
                    return mlamp.Sets.Count>0?0:1;
                }
                if (args.Length == 2 && (args[0] == "--lamp-demo-report" || args[0] == "--lamp-snp-demo-report"))
                {
                    var lamp = args[0] == "--lamp-snp-demo-report"
                        ? LampDesignEngine.DesignSnp(SnpParser.Parse(LampReportWriter.ExampleSnpFasta()),new LampDesignSettings(),null,CancellationToken.None)
                        : LampDesignEngine.Design(SequenceParser.Parse(LampReportWriter.ExampleFasta()),new LampDesignSettings(),null,CancellationToken.None);
                    File.WriteAllText(args[1],LampReportWriter.TextReport(lamp),new UTF8Encoding(true));
                    return lamp.Sets.Count>0?0:1;
                }
                if (args.Length == 2 && args[0] == "--snp-demo-report")
                {
                    var snp = SnpDesignEngine.Design(SnpParser.Parse(SnpReportWriter.ExampleFasta()),new SnpDesignSettings(),null,CancellationToken.None);
                    File.WriteAllText(args[1],SnpReportWriter.TextReport(snp),new UTF8Encoding(true));
                    return snp.Sets.Count>0?0:1;
                }
                if (args.Length == 2 && args[0] == "--demo-report")
                {
                    var result = DesignEngine.Design(SequenceParser.Parse(ReportWriter.ExampleFasta()), new DesignSettings(), null, CancellationToken.None);
                    File.WriteAllText(args[1], ReportWriter.TextReport(result), new UTF8Encoding(true));
                    return result.Pairs.Count > 0 ? 0 : 1;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), ex.ToString()); return 1; }
                MessageBox.Show(ex.Message, "程序错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
