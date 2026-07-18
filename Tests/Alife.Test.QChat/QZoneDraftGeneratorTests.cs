using Alife.Function.QChat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneDraftGeneratorTests
{
    [Test]
    public async Task SemanticKernelGenerator_TrimsDraftAndUsesOnlyMinimalEnvelopeHistory()
    {
        RecordingChatCompletionService completion = new("  夜风很轻，适合把今天收好。  ");
        QZoneSemanticKernelDraftGenerator generator = new(completion);
        QZoneDraftRequest request = new(
            "mixu",
            QZoneAutonomyContentEnvelope.MixuWarmBright,
            ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]);

        string draft = await generator.GenerateAsync(request);
        string prompt = string.Join("\n", completion.History.Select(message => message.Content));

        Assert.Multiple(() =>
        {
            Assert.That(draft, Is.EqualTo("夜风很轻，适合把今天收好。"));
            Assert.That(completion.History, Has.Count.EqualTo(2));
            Assert.That(completion.History[0].Role, Is.EqualTo(AuthorRole.System));
            Assert.That(completion.History[1].Role, Is.EqualTo(AuthorRole.User));
            Assert.That(prompt, Does.Contain("ordinary safe social moment"));
            Assert.That(prompt, Does.Contain("warm and bright"));
            Assert.That(prompt, Does.Contain("160"));
            Assert.That(prompt, Does.Not.Contain("Cookie").IgnoreCase);
            Assert.That(prompt, Does.Not.Contain("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        });
    }

    [Test]
    public void SemanticKernelGenerator_RejectsBlankCompletionWithSafeCode()
    {
        QZoneSemanticKernelDraftGenerator generator = new(new RecordingChatCompletionService(" \r\n "));
        QZoneDraftRequest request = new(
            "xiayu",
            QZoneAutonomyContentEnvelope.XiaYuRestrained,
            []);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await generator.GenerateAsync(request))!;

        Assert.That(exception.Message, Is.EqualTo("qzone_draft_empty"));
    }

    sealed class RecordingChatCompletionService(string response) : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();
        public IReadOnlyList<ChatMessageContent> History { get; private set; } = [];

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            History = chatHistory.ToArray();
            return Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                [new ChatMessageContent(AuthorRole.Assistant, response)]);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
