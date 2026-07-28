using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.QChat;

public interface IQChatSemanticWebResearchModel
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public interface IQChatSemanticWebResearchRouter
{
    Task<QChatSemanticWebResearchDecision> RouteAsync(
        QChatSemanticWebResearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class QChatSemanticKernelWebResearchModel(IChatCompletionService chatCompletionService)
    : IQChatSemanticWebResearchModel
{
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ChatHistory history = [];
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);
        ChatMessageContent response = await chatCompletionService.GetChatMessageContentAsync(
            history,
            cancellationToken: cancellationToken);
        return response.Content ?? string.Empty;
    }
}

public sealed class QChatLlmSemanticWebResearchRouter(IQChatSemanticWebResearchModel model)
    : IQChatSemanticWebResearchRouter
{
    const int MaxQueryChars = 160;

    public async Task<QChatSemanticWebResearchDecision> RouteAsync(
        QChatSemanticWebResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(100, request.Config.RouterTimeoutMilliseconds)));

        try
        {
            string response = await model.CompleteAsync(
                BuildSystemPrompt(),
                BuildUserPrompt(request),
                timeout.Token);
            return TryParse(response, request, out QChatSemanticWebResearchDecision decision)
                ? decision
                : Fallback(request, "router_invalid_response");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fallback(request, "router_timeout");
        }
        catch (Exception)
        {
            return Fallback(request, "router_invalid_response");
        }
    }

    static string BuildSystemPrompt() =>
        """
        Make two independent decisions. Decide turnComplete first.
        turnComplete means it is appropriate to answer now without interrupting the sender; it does not mean the fragment's likely meaning can merely be guessed.
        Set turnComplete=false when question is cut off mid-clause, depends grammatically on surrounding text, reads like setup for a request or conclusion that is probably still coming, or recentContext shows the sender is building one thought across messages. Lack of terminal punctuation alone is not enough. When genuinely uncertain, choose false and wait.
        Examples: "我想把它做得" is false; a following comparative fragment such as "更自然一点" can remain false when it reads as setup; "你觉得重点是什么" is true. "I want to make it feel" is false; "more natural" can remain false as setup; "What should we prioritize?" is true.
        Decide whether a QQ question needs live web research to answer accurately. Use semantics, not keyword lists.
        Research is appropriate for time-sensitive facts, verification, changing status, or niche information that needs external evidence.
        Research is not appropriate for companionship, creative writing, subjective conversation, or stable knowledge already answerable without live evidence.
        Decide research independently from turnComplete. The reason must briefly explain both decisions in this form: "research: ...; turn: ...".
        When an official source is explicitly requested and its official domain is confidently known, constrain query with site:<official-domain> and choose standard or deep; never guess a domain.
        For product-update questions, target the exact official support/docs subdomain and release-notes or changelog page when confidently known, for example: site:help.openai.com ChatGPT release notes.
        Return only one JSON object with exactly these fields: turnComplete (boolean), shouldResearch (boolean), uncertain (boolean), query (string), depth (quick|standard|deep), maxSources (integer 1..5), reasonCategory (temporal|verification|niche|explicit|stable|creative|companion|unknown), reason (string).
        """;

    static string BuildUserPrompt(QChatSemanticWebResearchRequest request) =>
        $"agent={request.AgentId}\nsenderRole={request.SenderRole}\nmessageType={request.MessageEvent.MessageType}\nquestion={request.Question.Trim()}\nrecentContext={request.RecentContext.Trim()}";

    static bool TryParse(
        string raw,
        QChatSemanticWebResearchRequest request,
        out QChatSemanticWebResearchDecision decision)
    {
        decision = default!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("shouldResearch", out JsonElement shouldResearchElement) == false ||
                shouldResearchElement.ValueKind != JsonValueKind.True && shouldResearchElement.ValueKind != JsonValueKind.False ||
                root.TryGetProperty("uncertain", out JsonElement uncertainElement) == false ||
                uncertainElement.ValueKind != JsonValueKind.True && uncertainElement.ValueKind != JsonValueKind.False ||
                TryGetString(root, "query", out string query) == false ||
                TryGetString(root, "depth", out string depthValue) == false ||
                root.TryGetProperty("maxSources", out JsonElement maxSourcesElement) == false ||
                maxSourcesElement.TryGetInt32(out int maxSources) == false ||
                TryGetString(root, "reasonCategory", out string reasonCategoryValue) == false ||
                TryGetString(root, "reason", out string reason) == false)
            {
                return false;
            }

            if (Enum.TryParse(depthValue, ignoreCase: true, out QChatSemanticWebResearchDepth depth) == false ||
                Enum.TryParse(reasonCategoryValue, ignoreCase: true, out QChatSemanticWebResearchReasonCategory reasonCategory) == false ||
                maxSources is < 1 or > 5)
            {
                return false;
            }

            string normalizedQuery = query.Trim();
            bool shouldResearch = shouldResearchElement.GetBoolean();
            if (shouldResearch && (normalizedQuery.Length == 0 || normalizedQuery.Length > MaxQueryChars))
                return false;

            bool? turnComplete = null;
            if (root.TryGetProperty("turnComplete", out JsonElement turnCompleteElement))
            {
                if (turnCompleteElement.ValueKind != JsonValueKind.True &&
                    turnCompleteElement.ValueKind != JsonValueKind.False)
                {
                    return false;
                }

                turnComplete = turnCompleteElement.GetBoolean();
            }

            decision = new QChatSemanticWebResearchDecision(
                shouldResearch,
                uncertainElement.GetBoolean(),
                normalizedQuery,
                depth,
                maxSources,
                reasonCategory,
                reason.Trim(),
                turnComplete);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (root.TryGetProperty(propertyName, out JsonElement element) == false ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    static QChatSemanticWebResearchDecision Fallback(
        QChatSemanticWebResearchRequest request,
        string reason) => new(
        request.Config.ResearchOnUncertainty,
        true,
        request.Question.Trim(),
        QChatSemanticWebResearchDepth.Quick,
        Math.Clamp(request.Config.QuickMaxSources, 1, 5),
        QChatSemanticWebResearchReasonCategory.Unknown,
        reason);
}
