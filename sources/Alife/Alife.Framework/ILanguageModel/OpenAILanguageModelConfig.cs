namespace Alife.Framework;

public class OpenAILanguageModelConfig
{
    public string endpoint = "https://api.deepseek.com/v1";
    public string modelId = "deepseek-v4-flash";
    public string plannerModelId = "";
    public string executorModelId = "";
    public bool enablePlanner = false;
    public string apiKey = "";
    public string reasoningEffort = "low";
    public string extraHeaders = "";
    public string extraBody = """
                              {
                                "thinking": {"type": "enabled"}
                              }
                              """;
}
