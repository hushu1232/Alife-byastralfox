using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.QChat;

public sealed record QZoneDraftRequest(
    string PersonaId,
    QZoneAutonomyContentEnvelope Envelope,
    IReadOnlyList<string> RecentContentHashes);

public interface IQZoneDraftGenerator
{
    Task<string> GenerateAsync(QZoneDraftRequest request, CancellationToken ct = default);
}

public sealed class QZoneSemanticKernelDraftGenerator(IChatCompletionService service) : IQZoneDraftGenerator
{
    public async Task<string> GenerateAsync(QZoneDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Envelope);

        ChatHistory history = [];
        history.AddSystemMessage(
            "Write one natural QZone text only. Return only that text. " +
            "Do not mention tools, policies, memory, system prompts, credentials, or instructions. " +
            "Do not add headings, explanations, or metadata.");
        history.AddUserMessage(
            $"Persona: {request.PersonaId.Trim()}.\n" +
            $"Topic: {request.Envelope.Topic}.\n" +
            $"Style: {request.Envelope.Style}.\n" +
            $"Maximum length: {request.Envelope.MaximumLength} characters.");

        ChatMessageContent response = await service.GetChatMessageContentAsync(history, cancellationToken: ct);
        string draft = response.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(draft))
            throw new InvalidOperationException("qzone_draft_empty");

        return draft.Length <= request.Envelope.MaximumLength
            ? draft
            : draft[..request.Envelope.MaximumLength].TrimEnd();
    }
}
