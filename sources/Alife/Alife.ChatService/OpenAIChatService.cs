using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.SemanticKernel;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alife.ChatService;

[Plugin(
"OpenAI大语言模型", "接入基于OpenAI协议的对话模型，实现最基本的文本对话功能。",
url: "https://www.deepseek.com/",
editorUI: typeof(OpenAIChatServiceUI),
defaultCategory: "Alife 官方/对话能力"
)]
public class OpenAIChatService(ILogger<OpenAIChatService> logger) : Plugin, IConfigurable<ChatServiceConfig>, IProvideExecutionSettings
{
    public ChatServiceConfig? Configuration { get; set; }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
        SocketsHttpHandler handler = new() {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        // 使用通用处理器拦截并破解所有 OpenAI 兼容协议的思考过程字段
        OpenAICompatibleHandler reasoningHandler = new(handler);

        HttpClient httpClient = new(reasoningHandler) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        if (!string.IsNullOrWhiteSpace(Configuration!.customHeaders))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(Configuration.customHeaders);
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "解析自定义请求头失败");
            }
        }

        context.KernelBuilder.AddOpenAIChatCompletion(
        endpoint: new Uri(Configuration!.endpoint),
        modelId: Configuration!.modelId,
        apiKey: Configuration!.apiKey,
        httpClient: httpClient
        );
    }

    [Experimental("SKEXP0010")]
    public void ProvideSettings(OpenAIPromptExecutionSettings settings)
    {
        if (Configuration!.thinkingEnabled)
            settings.ReasoningEffort = Configuration!.reasoningEffort;

        // 思考模式支持
        settings.ExtraBody = new Dictionary<string, object?>();
        settings.ExtraBody["thinking"] = new {
            type = Configuration!.thinkingEnabled ? "enabled" : "disabled"
        };

        if (!string.IsNullOrWhiteSpace(Configuration!.customBody))
        {
            try
            {
                var bodyDict = JsonSerializer.Deserialize<Dictionary<string, object>>(Configuration.customBody);
                if (bodyDict != null)
                {
                    foreach (var kvp in bodyDict)
                    {
                        settings.ExtraBody[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "解析自定义请求体失败");
            }
        }
    }
}
