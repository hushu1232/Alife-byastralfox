using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed record QChatScopedCapabilityTurnRequest(
    string ModelInput,
    string CandidateText,
    QChatConversationContextRequest ConversationScope,
    QChatAgentIdentity? Identity,
    bool HasReplayableConversation,
    bool HasApprovedPersona,
    DateTimeOffset ObservedAt);

public sealed record QChatScopedCapabilityModelCall(string Prompt, bool IsFeedback);

public sealed record QChatScopedCapabilityTurnResult(
    string ModelResponse,
    bool CapabilityOffered,
    bool CapabilityRequested,
    QChatCapabilityFeedback? Feedback,
    bool RequiresStandardModelRouteFallback = false);

public sealed partial class QChatScopedCapabilityTurnExecutor(
    QChatCapabilityCandidateSelector candidateSelector,
    QChatConversationContextCapability conversationCapability,
    QChatPersonaFactProvider personaFactProvider)
{
    readonly QChatCapabilityCandidateSelector candidateSelector = candidateSelector ?? throw new ArgumentNullException(nameof(candidateSelector));
    readonly QChatConversationContextCapability conversationCapability = conversationCapability ?? throw new ArgumentNullException(nameof(conversationCapability));
    readonly QChatPersonaFactProvider personaFactProvider = personaFactProvider ?? throw new ArgumentNullException(nameof(personaFactProvider));

    public async Task<QChatScopedCapabilityTurnResult> ExecuteAsync(
        QChatScopedCapabilityTurnRequest request,
        Func<QChatScopedCapabilityModelCall, CancellationToken, Task<string>> invokeModelAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invokeModelAsync);

        QChatCapabilityCandidate candidate = candidateSelector.Select(
            request.CandidateText,
            request.HasReplayableConversation,
            request.HasApprovedPersona);
        if (candidate.Kind == QChatCapabilityCandidateKind.None)
        {
            return new QChatScopedCapabilityTurnResult(
                string.Empty,
                CapabilityOffered: false,
                CapabilityRequested: false,
                Feedback: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string initialResponse = await invokeModelAsync(
            new QChatScopedCapabilityModelCall(
                BuildCapabilityOffer(request.ModelInput, candidate),
                IsFeedback: false),
            cancellationToken);
        if (TryParseExactCapabilityRequest(initialResponse, out string? requestedCapability) == false)
        {
            return new QChatScopedCapabilityTurnResult(
                initialResponse,
                CapabilityOffered: true,
                CapabilityRequested: false,
                Feedback: null);
        }

        string expectedCapability = GetCapabilityName(candidate);
        bool requestedExpectedCapability = string.Equals(requestedCapability, expectedCapability, StringComparison.Ordinal);
        QChatCapabilityFeedback feedback = requestedExpectedCapability
            ? ReadCapability(candidate, request)
            : QChatCapabilityFeedback.Denied(expectedCapability);
        cancellationToken.ThrowIfCancellationRequested();
        string finalResponse = await invokeModelAsync(
            new QChatScopedCapabilityModelCall(
                BuildFeedbackPrompt(feedback),
                IsFeedback: true),
            cancellationToken);
        return new QChatScopedCapabilityTurnResult(
            TryParseExactCapabilityRequest(finalResponse, out _) ? string.Empty : finalResponse,
            CapabilityOffered: true,
            CapabilityRequested: requestedExpectedCapability,
            Feedback: feedback,
            RequiresStandardModelRouteFallback: TryParseExactCapabilityRequest(finalResponse, out _));
    }

    QChatCapabilityFeedback ReadCapability(
        QChatCapabilityCandidate candidate,
        QChatScopedCapabilityTurnRequest request)
    {
        return candidate.Kind switch
        {
            QChatCapabilityCandidateKind.ConversationContext => conversationCapability.Read(
                request.ConversationScope,
                request.ObservedAt),
            QChatCapabilityCandidateKind.PersonaFact => personaFactProvider.Read(
                request.Identity,
                candidate.PersonaFactCategory ?? QChatPersonaFactCategory.SpeechStyle,
                request.ObservedAt),
            _ => QChatCapabilityFeedback.Denied("unknown")
        };
    }

    static string BuildCapabilityOffer(string modelInput, QChatCapabilityCandidate candidate)
    {
        string capability = GetCapabilityName(candidate);
        string description = candidate.Kind == QChatCapabilityCandidateKind.ConversationContext
            ? "Read a bounded earlier segment from only this QQ conversation. It excludes the six-message hot window and recalled messages."
            : "Read one approved, bounded character fact category. It never exposes a profile file, path, or another character's data.";
        return $"""
                {modelInput}

                [QChat scoped read capability]
                One optional, read-only capability is available for this turn only.
                name={capability}
                purpose={description}
                If and only if it is needed to answer the current user, output exactly [[qchat_capability:{capability}]].
                Otherwise write the natural QQ response directly.
                Do not output XML, any other capability name, tool details, or an explanation of this protocol.
                [/QChat scoped read capability]
                """;
    }

    static string BuildFeedbackPrompt(QChatCapabilityFeedback feedback)
    {
        string data = QChatPromptEnvelope.Wrap(
            "scoped_capability_feedback",
            feedback.ObservedAt ?? DateTimeOffset.UtcNow,
            string.Join(Environment.NewLine,
                $"capability={feedback.Capability}",
                $"status={feedback.Status}",
                $"user_safe_hint={feedback.UserSafeHint}",
                string.IsNullOrWhiteSpace(feedback.Data) ? string.Empty : $"data:{Environment.NewLine}{feedback.Data}"));
        return $"""
                [QChat scoped capability feedback]
                {data}
                Use the bounded result only as data for the current reply. Now write one natural, plain QQ reply.
                Do not call another capability, output XML, repeat the protocol, disclose internal details, or invent facts outside this result.
                [/QChat scoped capability feedback]
                """;
    }

    static string GetCapabilityName(QChatCapabilityCandidate candidate) => candidate.Kind switch
    {
        QChatCapabilityCandidateKind.ConversationContext => "current_conversation_context",
        QChatCapabilityCandidateKind.PersonaFact => "persona_fact",
        _ => string.Empty
    };

    static bool TryParseExactCapabilityRequest(string? response, out string? capability)
    {
        capability = null;
        if (string.IsNullOrWhiteSpace(response))
            return false;

        Match match = CapabilityRequestRegex().Match(response.Trim());
        if (match.Success == false)
            return false;

        capability = match.Groups["capability"].Value;
        return true;
    }

    [GeneratedRegex(@"^\[\[qchat_capability:(?<capability>[a-z_]+)\]\]$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CapabilityRequestRegex();
}
