using System.ComponentModel;
using System.Text;
using Alife.Basic;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Memory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Implement;

[Plugin("记忆服务", "自动管理和分层压缩对话记忆，提供长期记忆检索能力。", LaunchOrder = -100)]
public class MemoryService : Plugin
{
    [XmlFunction]
    [Description("读取记忆档案的完整记录。")]
    public async Task Recall(XmlExecutorContext ctx, [Description("记录索引（如：0-20240101120000-20240101130000）")] string index)
    {
        if (ctx.CallMode != CallMode.OneShot)
            return;

        string? memory = await memoryManager.ReadMemory(index);
        chatBot.Poke(memory != null
            ? $"[{nameof(MemoryService)}] 读取完整记忆如下：\n{memory}"
            : $"[{nameof(MemoryService)}] 未找到记忆记录");
    }
    [XmlFunction]
    [Description("在归档的记忆记录中搜索内容。")]
    public async Task Search(XmlExecutorContext ctx, [XmlContent] string query)
    {
        List<SearchResult> results = await memoryManager.SearchMemory(query);
        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine($"[{nameof(MemoryService)}] 按匹配度搜索到如下内容：");
        for (int index = 0; index < results.Count; index++)
        {
            SearchResult searchResult = results[index];
            stringBuilder.AppendLine($"{index}. 索引：{searchResult.Name},发生时间：{searchResult.StartTime}到{searchResult.EndTime},相似度：{searchResult.Score}");
        }
        chatBot.Poke(stringBuilder.ToString());
    }

    MemoryManager memoryManager = null!;
    ChatBot chatBot = null!;
    ChatHistory chatHistory = null!;

    public MemoryService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatHistory = chatBot.ChatHistory;

        //每次对话后检测压缩
        chatBot.ChatHistoryAdd += OnChatHistoryAdd;

        //初始化向量化器和感知人设的压缩器
        TextVectorizer vectorizer = new(AlifePath.ModelsFolderPath);
        AlifeTextCompressor compressor = new(kernel.GetRequiredService<IChatCompletionService>(), chatHistory);
        string storagePath = Path.Combine(AlifePath.StorageFolderPath, "Memory");
        memoryManager = new MemoryManager(compressor, vectorizer, storagePath);

        //加载历史记忆
        memoryManager.LoadHistory(chatHistory);

        return Task.CompletedTask;
    }

    async void OnChatHistoryAdd(ChatMessageContent content)
    {
        try
        {
            await chatBot.ChatSemaphore.WaitAsync();
            await memoryManager.Filter(chatHistory);
            memoryManager.SaveHistory(chatHistory);
            chatBot.ChatSemaphore.Release();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    /// <summary>
    /// 感知上下文的人设化压缩器
    /// </summary>
    class AlifeTextCompressor(IChatCompletionService chatCompletionService, ChatHistory history) : TextCompressor
    {
        public override async Task<string> Compress(string text)
        {
            ChatHistory chatHistory = new(history);
            chatHistory.AddMessage(AuthorRole.User,
                $"""
                 [{nameof(MemoryService)}] 触发上下文压缩了！
                 如下是之前的一段聊天记录或记忆档案，待会他们将会被归档并移出上下文，所以请你用第一人称视角简述一下内容，方便日后回忆。（注意写明时间段，并按重要程度进行取舍）：

                 {text}
                 """);
            ChatMessageContent content = await chatCompletionService.GetChatMessageContentAsync(chatHistory);
            if (content.Content == null)
                throw new Exception("记忆压缩失败！");

            return content.Content;
        }
    }
}
