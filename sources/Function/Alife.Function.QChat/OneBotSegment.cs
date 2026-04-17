using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

/// <summary>
/// OneBot CQ 码与富文本处理工具
/// </summary>
public static class OneBotSegment
{
    /// <summary>
    /// 构造 At 消息片段
    /// </summary>
    public static string At(long userId) => $"[CQ:at,qq={userId}]";

    /// <summary>
    /// 构造表情片段
    /// </summary>
    public static string Face(int id) => $"[CQ:face,id={id}]";

    /// <summary>
    /// 构造图片片段
    /// </summary>
    public static string Image(string file) => $"[CQ:image,file={file}]";

    /// <summary>
    /// 尝试从消息中提取 CQ 码中的 file_id
    /// </summary>
    public static bool TryGetFileId(string message, out string fileId)
    {
        fileId = string.Empty;
        if (string.IsNullOrEmpty(message)) return false;

        Match match = Regex.Match(message, @"\[CQ:file,.*?file_id=(?<id>[^,\]]+)");
        if (match.Success == false) return false;

        fileId = match.Groups["id"].Value;
        return true;
    }

    /// <summary>
    /// 检查消息是否提到特定的 QQ 号
    /// </summary>
    public static bool IsAt(string content, long selfId)
    {
        if (string.IsNullOrEmpty(content)) return false;
        return content.Contains($"[CQ:at,qq={selfId}]") || content.Contains($"[CQ:at,qq={selfId},");
    }
}
