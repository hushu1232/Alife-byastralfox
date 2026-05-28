namespace Alife.ChatService;

public class ChatServiceConfig
{
    public string endpoint = "https://api.deepseek.com/v1";
    public string modelId = "deepseek-v4-flash";
    public string apiKey = "";
    public bool thinkingEnabled = false;
    public string reasoningEffort = "low";
    public string customHeaders = "";
    public string customBody = "";
}
