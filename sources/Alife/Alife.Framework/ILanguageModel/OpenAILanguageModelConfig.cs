namespace Alife.Framework;

public class OpenAILanguageModelConfig
{
    public string endpoint = "https://api.krill-ai.com/v1";
    public string modelId = "grok-4.5";
    public string plannerModelId = "";
    public string executorModelId = "";
    public bool enablePlanner = false;
    public string apiKey = "";
    public string reasoningEffort = "";
    public string extraHeaders = "";
    public string extraBody = "{}";
    public string fallbackEndpoint = "";
    public string fallbackModelId = "";
    public string fallbackApiKey = "";
    public string fallbackExtraHeaders = "";
    public string fallbackExtraBody = "{}";
}
