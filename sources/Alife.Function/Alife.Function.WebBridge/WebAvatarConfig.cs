using System.Collections.Generic;

namespace Alife.Function.WebBridge;

public class WebAvatarConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> Modules { get; set; } = new();
}
