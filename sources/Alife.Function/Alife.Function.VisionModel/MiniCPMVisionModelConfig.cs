namespace Alife.Function.Vision;

public class MiniCPMVisionModelConfig
{
    /// <summary>
    /// 视觉token降采样模式：16x（更快，默认）或 4x（保留更多细节）。
    /// </summary>
    public string DownsampleMode { get; set; } = "16x";

    /// <summary>
    /// 模型加载精度：int4（NF4量化，省显存，默认）或 bf16（半精度，更快更准）。
    /// </summary>
    public string Precision { get; set; } = "int4";
}
