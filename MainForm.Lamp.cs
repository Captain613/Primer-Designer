using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RpaDesigner
{
    public sealed partial class MainForm
    {
        private GroupBox rpaSettingsGroup,lampSettingsGroup;
        private NumericUpDown lampRegionMin,lampRegionMax,lampSpanMin,lampSpanMax,lampCoreSpanMin,lampCoreSpanMax,lampCount;
        private NumericUpDown lampGcMin,lampGcMax;
        private NumericUpDown lampF3TmMin,lampF3TmMax,lampB3TmMin,lampB3TmMax,lampF2TmMin,lampF2TmMax,lampB2TmMin,lampB2TmMax;
        private NumericUpDown lampF1cTmMin,lampF1cTmMax,lampB1cTmMin,lampB1cTmMax,lampLFTmMin,lampLFTmMax,lampLBTmMin,lampLBTmMax;
        private CheckBox lampLoops;
        private ComboBox lampOrientation;
        private NumericUpDown lampPaTail;
        private TableLayoutPanel lampSettingsTable;
        private Label lampPaTailLabel,lampPaTailHint,paStrategyText;
        private LampDesignResult lampResult;
        private int previousDesignMode,flexibleLampOrientation,preMLampMismatch;
        private bool preMLampLoops;
        private bool IsLampMode { get { return designMode!=null && designMode.SelectedIndex>=2; } }
        private bool IsPaLampMode { get { return designMode!=null && designMode.SelectedIndex==4; } }
        private bool IsMLampMode { get { return designMode!=null && designMode.SelectedIndex==5; } }

        private LampDesignSettings LampSettings()
        {
            return new LampDesignSettings {RegionMin=(int)lampRegionMin.Value,RegionMax=(int)lampRegionMax.Value,
                SpanMin=(int)lampSpanMin.Value,SpanMax=(int)lampSpanMax.Value,CoreSpanMin=(int)lampCoreSpanMin.Value,CoreSpanMax=(int)lampCoreSpanMax.Value,MaxSets=(int)lampCount.Value,
                GcMin=(double)lampGcMin.Value,GcMax=(double)lampGcMax.Value,
                F3Tm=LampTmSettings(lampF3TmMin,lampF3TmMax),B3Tm=LampTmSettings(lampB3TmMin,lampB3TmMax),
                F2Tm=LampTmSettings(lampF2TmMin,lampF2TmMax),B2Tm=LampTmSettings(lampB2TmMin,lampB2TmMax),
                F1cTm=LampTmSettings(lampF1cTmMin,lampF1cTmMax),B1cTm=LampTmSettings(lampB1cTmMin,lampB1cTmMax),
                LFTm=LampTmSettings(lampLFTmMin,lampLFTmMax),LBTm=LampTmSettings(lampLBTmMin,lampLBTmMax),
                RegionMinUnlimited=Unlimited(lampRegionMin),RegionMaxUnlimited=Unlimited(lampRegionMax),SpanMinUnlimited=Unlimited(lampSpanMin),SpanMaxUnlimited=Unlimited(lampSpanMax),
                CoreSpanMinUnlimited=Unlimited(lampCoreSpanMin),CoreSpanMaxUnlimited=Unlimited(lampCoreSpanMax),
                GcMinUnlimited=Unlimited(lampGcMin),GcMaxUnlimited=Unlimited(lampGcMax),
                IncludeLoops=lampLoops.Checked,SnpMethod=IsPaLampMode?"PA-LAMP":IsMLampMode?"mLAMP":"AS-LAMP",PaTailLength=(int)lampPaTail.Value,
                SnpOrientation=IsPaLampMode?"BIP":IsMLampMode?"FIP":lampOrientation.SelectedIndex==1?"FIP":lampOrientation.SelectedIndex==2?"BIP":"Auto",
                ExtraMismatchFromThreePrime=IsPaLampMode||mismatchMode.SelectedIndex==0?0:mismatchMode.SelectedIndex+1};
        }
        private LampTmRange LampTmSettings(NumericUpDown minimum,NumericUpDown maximum)
        {return new LampTmRange((double)minimum.Value,(double)maximum.Value){MinUnlimited=Unlimited(minimum),MaxUnlimited=Unlimited(maximum)};}
        private void ResetLampTm(string role,NumericUpDown minimum,NumericUpDown maximum,LampDesignSettings defaults)
        {
            ClearUnlimited(minimum,maximum);LampTmRange range=defaults.GetTm(role);
            minimum.Value=(decimal)range.Min;maximum.Value=(decimal)range.Max;
        }
        private void ResetLampSettings()
        {
            var defaults=new LampDesignSettings();
            ClearUnlimited(lampRegionMin,lampRegionMax,lampGcMin,lampGcMax,lampSpanMin,lampSpanMax,lampCoreSpanMin,lampCoreSpanMax);
            lampRegionMin.Value=defaults.RegionMin;lampRegionMax.Value=defaults.RegionMax;lampCount.Value=defaults.MaxSets;lampGcMin.Value=(decimal)defaults.GcMin;lampGcMax.Value=(decimal)defaults.GcMax;
            lampSpanMin.Value=defaults.SpanMin;lampSpanMax.Value=defaults.SpanMax;lampCoreSpanMin.Value=defaults.CoreSpanMin;lampCoreSpanMax.Value=defaults.CoreSpanMax;
            ResetLampTm("F3",lampF3TmMin,lampF3TmMax,defaults);ResetLampTm("B3",lampB3TmMin,lampB3TmMax,defaults);
            ResetLampTm("F2",lampF2TmMin,lampF2TmMax,defaults);ResetLampTm("B2",lampB2TmMin,lampB2TmMax,defaults);
            ResetLampTm("F1c",lampF1cTmMin,lampF1cTmMax,defaults);ResetLampTm("B1c",lampB1cTmMin,lampB1cTmMax,defaults);
            ResetLampTm("LF",lampLFTmMin,lampLFTmMax,defaults);ResetLampTm("LB",lampLBTmMin,lampLBTmMax,defaults);
            lampLoops.Checked=!IsMLampMode;lampOrientation.SelectedIndex=IsPaLampMode?2:IsMLampMode?1:0;mismatchMode.SelectedIndex=IsMLampMode?2:0;lampPaTail.Value=defaults.PaTailLength;
        }
        private void UpdateDesignMode()
        {
            // Preserve freely chosen AS-LAMP orientation while a literature mode
            // fixes it, including transitions between the two fixed modes.
            bool previousFixed=previousDesignMode==4||previousDesignMode==5;
            if(!previousFixed)flexibleLampOrientation=lampOrientation.SelectedIndex;
            if(IsMLampMode&&previousDesignMode!=5)
            {
                preMLampMismatch=mismatchMode.SelectedIndex;preMLampLoops=lampLoops.Checked;
                mismatchMode.SelectedIndex=2;lampLoops.Checked=false;
            }
            else if(!IsMLampMode&&previousDesignMode==5)
            {
                mismatchMode.SelectedIndex=preMLampMismatch;lampLoops.Checked=preMLampLoops;
            }
            string[] mismatchLabels=IsMLampMode?
                new string[]{"仅 SNP 末端差异（论文对照）","倒数第 2 位增加错配（论文比较）","倒数第 3 位增加错配（文献默认）"}:
                new string[]{"仅 SNP 末端差异（基线）","倒数第 2 位增加错配（探索）","倒数第 3 位增加错配（探索）"};
            for(int i=0;i<mismatchLabels.Length;i++)
                if(!String.Equals(Convert.ToString(mismatchMode.Items[i]),mismatchLabels[i],StringComparison.Ordinal))mismatchMode.Items[i]=mismatchLabels[i];
            targetCheck.Checked=false;targetCheck.Enabled=!IsSnpMode&&!IsLampMode;mismatchMode.Enabled=IsSnpMode&&!IsPaLampMode;
            mismatchMode.Visible=!IsPaLampMode;paStrategyText.Visible=IsPaLampMode;
            rpaSettingsGroup.Visible=!IsLampMode;lampSettingsGroup.Visible=IsLampMode;lampOrientation.Enabled=IsLampMode&&IsSnpMode&&!IsPaLampMode&&!IsMLampMode;
            if(IsPaLampMode)lampOrientation.SelectedIndex=2;
            else if(IsMLampMode)lampOrientation.SelectedIndex=1;
            else if(previousFixed)lampOrientation.SelectedIndex=flexibleLampOrientation;
            lampLoops.Text=IsMLampMode?"加环引物（文献外扩展）":"尽可能设计环引物";
            previousDesignMode=designMode.SelectedIndex;
            lampPaTail.Visible=IsPaLampMode;lampPaTailLabel.Visible=IsPaLampMode;lampPaTailHint.Visible=IsPaLampMode;
            preferredProductLabel.Text=IsSnpMode?"偏好等位 (bp)":"偏好产物 (bp)";
            rpaSettingsGroup.Text=IsSnpMode?"RPA SNP 参数 · 偏好长度仅用于等位产物":"RPA 参数 · 上下限可分别设为无限制";
            float scale;using(var g=CreateGraphics())scale=g.DpiY/96F;
            var layout=(TableLayoutPanel)inputPanel;
            lampSettingsTable.RowStyles[5].Height=(IsPaLampMode?34:0)*scale;
            lampSettingsTable.RowStyles[6].Height=(IsPaLampMode?30:0)*scale;
            lampSettingsTable.Height=(int)Math.Ceiling((IsPaLampMode?254:190)*scale);
            layout.RowStyles[4].Height=(IsLampMode?360:224)*scale;
            layout.MinimumSize=new Size((int)Math.Ceiling(880*scale),(int)Math.Ceiling((IsLampMode?600:440)*scale));
            if(IsLampMode)
            {
                modeHint.Text=IsPaLampMode?"PA-LAMP：BIP 含 SNP 对应 RNA 与 3′ C3 封闭；需要 RNase H2 激活，两种等位反应分开。\r\nB2 参数用于切后有效 DNA 片段；订购时保留 [rA/rC/rG/rU] 和 [C3] 修饰。\r\n尾部默认 5 nt，含末位人为错配；参考 PA-LAMP 原始研究，候选需实验验证。":
                    IsMLampMode?"mLAMP（Ren 2019）：SNP 固定在 FIP 的 F2 3′ 末端；默认倒数第 3 位增加人工错配。\r\n红色 = SNP，蓝色 = 人为错配；参考与替代等位反应分开，默认四条核心引物。\r\n可比较无人工错配和倒数第 2 位方案；环引物为文献外扩展，候选需实验验证。":
                    "F3、B3、F2、B2、F1c、B1c、LF、LB 的 Tm 可独立设置；LF / LB 仅在启用环引物时使用。\r\n"+
                    (IsSnpMode?"AS-LAMP：SNP 放在 FIP 或 BIP 的 3′ 末端；两种等位反应分开，红色为 SNP。":"普通 LAMP：每组含 F3、B3、FIP、BIP；可选 LF / LB 环引物。靶区长度为 F3 至 B3 覆盖范围。")+
                    "\r\nTm 为近邻估算并用 Mg²⁺ 等效盐修正；参考条件不等于实验反应配方，候选需实验验证。";
                modeHint.Text+="\r\n默认使用较宽的候选搜索范围；Tm 排序仍偏好 F3/B3/F2/B2 接近 60°C、F1c/B1c/LF/LB 接近 65°C。";
            }
            else modeHint.Text=IsSnpMode?"标注一处 SNP（例如 [A>C] 或 [G>T]）；支持 A、C、G、T 中任意两种不同碱基的替换。\r\n输出两条等位正向、共用反向及对照正向。三种反应分开使用，候选需验证选择性。":"靶区坐标从 1 开始，两端均包含；启用后，引物必须位于该区域两侧。\r\n候选需实验验证。未进行全基因组特异性检索；本版不设计检测探针。";
            layout.PerformLayout();InputChanged();
        }
        private LampPrimerSet SelectedLampSet()
        {
            if(lampResult==null||grid.CurrentRow==null)return null;
            int i=grid.CurrentRow.Index;return i>=0&&i<lampResult.Sets.Count?lampResult.Sets[i]:null;
        }
        private void CopyLampSet()
        {
            var set=SelectedLampSet();if(set==null)return;
            try
            {
                using(var box=new RichTextBox {Font=new Font("Consolas",12F),ForeColor=ink})
                {
                    var report=LampReportWriter.HighlightedOrderingText(set,lampResult);ApplyHighlights(box,report,"");
                    var data=new DataObject();data.SetData(DataFormats.UnicodeText,report.Text);data.SetData(DataFormats.Rtf,box.Rtf);Clipboard.SetDataObject(data,true);
                }
                status.Text="已复制此组全部 LAMP 引物（5′→3′）；富文本可保留 SNP 红色"+(LampReportWriter.IsMLamp(lampResult)?"及人为错配蓝色":"");
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"复制失败");}
        }
        private void ExportLamp()
        {
            using(var dialog=new SaveFileDialog {Title="导出 "+LampReportWriter.ModeName(lampResult)+" 候选结果",Filter="彩色报告 (*.html)|*.html|完整文本报告 (*.txt)|*.txt|Excel 可读表格 (*.csv)|*.csv|FASTA（PA 修饰见标题，订购请用其他格式）|*.fasta",FileName=(LampReportWriter.IsPa(lampResult)?"PA-LAMP_":LampReportWriter.IsMLamp(lampResult)?"mLAMP_":lampResult.Snp!=null?"AS-LAMP_":"LAMP_")+DateTime.Now.ToString("yyyyMMdd_HHmmss"),AddExtension=true,DefaultExt="html"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try
                {
                    string content=dialog.FilterIndex==1?LampReportWriter.Html(lampResult):dialog.FilterIndex==3?LampReportWriter.Csv(lampResult):dialog.FilterIndex==4?LampReportWriter.Fasta(lampResult):LampReportWriter.TextReport(lampResult);
                    File.WriteAllText(dialog.FileName,content,new UTF8Encoding(dialog.FilterIndex!=4));status.Text="已导出："+dialog.FileName;
                }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
    }
}
