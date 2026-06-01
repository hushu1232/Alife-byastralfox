using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.SemanticKernel;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alife.Framework;

[Plugin(
    "OpenAI语言模型", "接入与OpenAI协议兼容的文本模型，实现最基本的文本对话功能。",
    url: "https://www.deepseek.com/",
    editorUI: typeof(OpenAILanguageModelUI),
    defaultCategory: "Alife 官方/模型接入/文本模型"
)]
public class OpenAILanguageModel(ILogger<OpenAILanguageModel> logger) :
    ILanguageModel,
    IConfigurable<OpenAILanguageModelConfig>
{
    public OpenAILanguageModelConfig? Configuration { get; set; }

    public void RegisterChatCompletion(IKernelBuilder kernelBuilder)
    {
        if (string.IsNullOrWhiteSpace(Configuration!.apiKey))
            throw new Exception("文本模型的key为空，请检查你的“OpenAI大语言模型”插件配置是否正确。");

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

        if (!string.IsNullOrWhiteSpace(Configuration!.extraHeaders))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(Configuration.extraHeaders);
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

        kernelBuilder.AddOpenAIChatCompletion(
            endpoint: new Uri(Configuration!.endpoint),
            modelId: Configuration!.modelId,
            apiKey: Configuration!.apiKey,
            httpClient: httpClient
        );
    }
    [Experimental("SKEXP0010")]
    public PromptExecutionSettings ProvidePromptExecutionSettings()
    {
        OpenAIPromptExecutionSettings settings = new();

        if (string.IsNullOrEmpty(Configuration!.reasoningEffort) == false)
            settings.ReasoningEffort = Configuration!.reasoningEffort;
        
        settings.ExtraBody = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(Configuration!.extraBody))
        {
            try
            {
                var bodyDict = JsonSerializer.Deserialize<Dictionary<string, object>>(Configuration.extraBody);
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

        return settings;
    }
}
