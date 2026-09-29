# PA-LAMP 方法依据与软件设计边界

核对日期：2026-09-22。

本模式按 **PA-LAMP（primer-activatable loop-mediated isothermal amplification，引物可激活型环介导等温扩增）** 的原理生成候选。它使用带单个 RNA 碱基与 3′ C3 封闭的内引物，通过 RNase H2 切割激活；与依靠引物末端错配抑制延伸的 AS-LAMP 分开标识。以下分别说明原始文献证据、从序列比对得到的结论，以及软件中的通用化约定。

## 已核实的原始材料

Du 等的原始研究为 *Single-step, high-specificity detection of single nucleotide mutation by primer-activatable loop-mediated isothermal amplification (PA-LAMP)*，Analytica Chimica Acta 1050（2019）132–138，DOI：[10.1016/j.aca.2018.10.068](https://doi.org/10.1016/j.aca.2018.10.068)。[出版商公开正文片段](https://www.sciencedirect.com/science/article/pii/S0003267018313242)明确说明：改造 BIP，在与变异位点对应处设置 RNA 碱基，并以 3′ C3 spacer 阻止未经切割的引物延伸；RNase H2 在 RNA 的 **5′ 侧**切割，留下可延伸的 3′-OH。

本次另取得出版商提供的原始 [Electronic Supplementary Information（旧版 Word DOC）](https://ars.els-cdn.com/content/image/1-s2.0-S0003267018313242-mmc1.doc)，核对了 Table S1，并直接查看从该文件提取的 Scheme S1 和 Fig. S5 原图。附件 SHA-256 为 `C36C1E2A854DBBFC94773915356011D4074BD1C2BC04190E74F9B59669B25690`。

## RNA 后 DNA 尾长

原始附件 Table S1 给出以下构型；这里的尾长**不计 RNA 本身和 C3**。

| 原文名称 | RNA 后 DNA 总数 | 与同表模板配对的前段 DNA 数 | 尾部最后一位 |
| --- | ---: | ---: | --- |
| BIP-a | 4 | 3 | 不匹配 |
| BIP-b | 5 | 4 | 不匹配 |
| BIP-c | 6 | 5 | 不匹配 |
| BIP-d | 7 | 6 | 不匹配 |

尾长由 Table S1 直接计数；“前段配对、最后一位不匹配”是本次将引物与同表 template-Mutant/实际结合区段对齐后的结论，表下注释未单独表述这一设计规则。Fig. S5B 中 BIP-b 的匹配与不匹配靶标时间差最大；同表其余 RNA 碱基变体也使用 5 nt 尾。因此，**以 5 nt 尾作为软件起始设置有该研究实例依据，但不等于所有 SNP 的最优尾长**。[原始附件 Table S1、Fig. S5](https://ars.els-cdn.com/content/image/1-s2.0-S0003267018313242-mmc1.doc)

## 切割位置和坐标约定

按合成引物 5′→3′ 方向表示：

```text
封闭 BIP 前体：5′—B1c—B2有效区段—|—rN—DNA尾—C3—3′
                              ↑ RNase H2 在 RNA 的 5′ 侧切割

切后活性 BIP：5′—B1c—B2有效区段—3′-OH
被移除的片段：rN—DNA尾—C3
```

RNA 碱基覆盖 SNP；切后活性引物本身不保留该 RNA/SNP 位点。对于输入正链第 `p` 位 SNP，反向 BIP 的有效 B2 结合区段从正链 `p+1` 开始，向更高坐标延伸；前体的 RNA 对应 `p`，RNA 后 DNA 尾依次对应 `p−1、p−2……`。这是根据切割方向和 BIP 反向互补关系推导的软件坐标规则。末尾用于探索的错配也必须按实际引物方向判断。

由此，两种等位的封闭 BIP 前体可仅在 RNA 位点不同，而切后活性 BIP DNA 序列相同；其区分机制发生在引物的酶切激活阶段。不能把 AS-LAMP 的“有效引物 3′ 末端必须为 SNP”规则直接搬到 PA-LAMP。

## 支持切割机理的基础研究

Dobosy 等在 RNase H2 依赖 PCR 的原始研究中，通过产物分析确认切割位于 RNA 5′ 侧；其 RNA 后 4、5、6 个 DNA 的封闭底物可用于该研究体系，而 2、3 个 DNA 的构型未见相应 PCR 产物。该研究也指出，RNA 附近错配对切割的影响依赖序列，不能把“错配”解释为必然完全不切割。它支持底物和切割机理，**不是 PA-LAMP 的独立通用性能验证**。[Dobosy et al., BMC Biotechnology 2011, 11:80](https://link.springer.com/article/10.1186/1472-6750-11-80)

## 软件实现中需要明确标识的部分

- 默认 BIP 方向；有效 BIP 后接 SNP 对应 RNA、5 nt DNA 尾、3′ C3。其中前 4 nt 按模板配对，尾部末位枚举非配对 DNA 碱基。将这种构型推广到新序列，以及末位候选如何排序，属于软件的工程实现，不是原论文发布的通用自动设计算法。
- 若开放 4–7 nt 尾长，应说明该范围对应原文比较的构型长度，不表示每一种长度在任意新位点均有效。RNA 后尾长包含末位错配 DNA。
- 不在切后有效 B2 内自动叠加 AS-LAMP 的倒数第 2/3 位错配策略；这会改变研究机制，需要另立策略及验证依据。
- 输出应区分“需合成的封闭前体”“切后活性 DNA 引物”“原始靶区模板”。RNA 应写成 `rA/rC/rG/rU`，C3 应明确为化学封闭修饰；不能以普通 A/C/G/T FASTA 冒充完整订购信息。
- DNA 最近邻 Tm 可描述切后 DNA 结合区段的计算参考值；它没有描述含 RNA/C3 前体的真实热力学、RNase H2 切割效率、错配判别比或实验检测限。评分只用于候选排序。
- 原始研究的性能数据属于其特定实验。输入新 SNP 后得到的软件候选、自动枚举的末端错配和不同尾长均需要重新验证，不能标为已复现该论文的检测性能。

## 尚未核实的范围

本次已核实公开正文片段及完整补充附件，但未取得出版社限制访问的完整正文 PDF。除上述 Table S1、Fig. S5 可直接支持的构型比较外，不据此补写未读到的全部筛选准则，也不认定存在已经验证的通用 PA-LAMP 自动设计平台、统一最佳尾长或任意 SNP 的保证性结果。
