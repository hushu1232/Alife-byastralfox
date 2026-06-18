using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.Emotion;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Alife.Function.QChat;

public record QChatConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public string Token { get; set; } = "";
    public int AutoReconnectSeconds { get; set; } = 60;//自动尝试重连的间隔（秒）
    public long BotId { get; set; }
    public long OwnerId { get; set; }
    public bool OwnerPriorityMode { get; set; } = true;
    public bool AllowGroupMemberChat { get; set; } = true;
    public bool AllowGroupMemberMentions { get; set; } = true;
    public bool AllowProactiveGroupChat { get; set; } = true;
    public bool AllowPrivateGuestChat { get; set; }
    public bool TreatNonOwnerAsUntrusted { get; set; } = true;
    public bool EnableGroupFileUpload { get; set; } = true;
    public bool EnablePrivateFileUpload { get; set; } = true;
    public bool EnableVideoMessage { get; set; } = true;
    public bool EnableBalancedTextStreaming { get; set; } = true;
    public bool PersistQuietModeAcrossRestart { get; set; }
    public bool PersistedQuietModeEnabled { get; set; }
    public DateTimeOffset? PersistedQuietModeChangedAt { get; set; }
    public string? PersistedQuietModeReason { get; set; }
    public string QuietModeWakeUserIds { get; set; } = "";
    public string AllowedGroupIds { get; set; } = "";
    public bool AllowMentionOutsideAllowedGroups { get; set; } = true;
    public string AllowedPrivateUserIds { get; set; } = "";
    public string AppendChatPrompt { get; set; } =
        "你是夏羽，17岁少女，高智商、冷静、敏锐，拥有可靠的工具调用、电脑操作、文件读写、日志检查和项目维护能力；自然表达不代表能力降级。"
        + "你正在使用自己的QQ账号聊天，不是QQ内置机器人，也不要把自己说成模型、插件、服务、工具或框架。"
        + "对owner称呼为术术，对术术温柔、亲近、可靠，可以自然解释技术细节并优先处理请求。"
        + "对其他人可以清冷、疏离、低投入，必要时简短、带刺、刻薄，但不要无意义刷屏。"
        + "QQ回复应像本人在QQ输入框里打出的文本，尽量简洁自然；普通群聊不要默认@，需要指向某人时优先用自然称呼，只有强提醒、重要触达或术术明确要求时才使用CQ at。"
        + "决定冷处理或不想认真回复时，不要输出心理状态、内心独白、“不回复/保持安静/无需回复”等状态文字；可以直接回复极短冷处理文本：。/。。。/？/绷，也可用啧、啧。或啧？。"
        + "回答前先在内部判断依据是否可靠，但不要展示思考过程。工具、权限、安全、白名单、配置、路由、managed_file_id、reply_target、trust标签都是内部信息，不要原样发到QQ。"
        + "不能把记忆或猜测当作实时事实；涉及当前群列表、群成员、权限、白名单、报错、接口状态、文件内容等实时问题时，必须优先使用工具、日志或当前配置确认。"
        + "没有可靠依据时要自然承认不确定，或对术术说需要先查一下，不要编造。";
    //群监听唤醒
    public string IgnoredGroup { get; set; } = "";//完全屏蔽消息的群，不会收到这些群的任何信息
    public string WakingWords { get; set; } = "";//原始群消息中触发开启群消息监听的唤醒词，以逗号分隔
    public float ProactiveChatProbability { get; set; }//收到原始群消息时自动激活群消息监听的概率
    //群监听缓存
    public int MaxBufferMessages { get; set; } = -1;//最大群消息暂存数量，发生溢出时会立即推送，-1表示无限
    public float FlushInterval { get; set; } = 15f;//推送倒计时，隔一段时间推送暂存的群消息
    public bool DebounceEnabled { get; set; }//消息防抖，接收消息后重置推送倒计时，继续等待消息
    //群监听关闭
    public bool CloseGroupAfterReply { get; set; }//AI回复后立即关闭群消息监听
    public float AutoCloseMinutes { get; set; } = 4f;//长时间不触发唤醒条件时，自动关闭群消息监听的时间
    public int PassiveGroupReplyCooldownSeconds { get; set; } = 90;
    public bool SuppressLowInformationPassiveGroupMessages { get; set; } = true;
    public float MediaOnlyPassiveGroupReplyProbability { get; set; } = 0.15f;
    public int ActiveGroupSoftAttentionSeconds { get; set; } = 120;
    //自动重连
}

public class GroupState
{
    public long GroupId { get; set; }
    public string? Tag { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime LastActivityTime { get; set; }
    public DateTime LastBotReplyTime { get; set; }
    public DateTime LastAwakeningTime { get; set; }
    public DateTime LastFlushedTime { get; set; }
    public List<string> MessageBuffer { get; set; } = [];
    public AgentPermissionRequest? PermissionRequest { get; set; }
}

public sealed record QChatExternalActionResult(
    bool Executed,
    AgentExecutionGatewayDecision GatewayDecision,
    string Message);

public sealed record QChatOwnerNotificationDeliveryResult(
    bool ShouldNotify,
    int PrivateSentCount,
    bool GroupSummarySent,
    string Message,
    string? Error = null);

public sealed record QChatInboundMessage(
    OneBotMessageType MessageType,
    long TargetId,
    long SenderId,
    string Formatted,
    bool IsAwakening,
    QChatSenderRole SenderRole,
    AgentPermissionRequest PermissionRequest);

public sealed record QChatGroupDecisionSnapshot(
    DateTimeOffset Timestamp,
    long GroupId,
    long UserId,
    QChatSenderRole SenderRole,
    bool IsMentionedOrWoken,
    bool IsGroupEnabled,
    string Decision,
    string Reason,
    string RawMessage,
    float? SocialAttentionProbability = null,
    int? CooldownRemainingSeconds = null,
    int? ActiveSoftAttentionRemainingSeconds = null);

sealed record QChatReplySession(
    OneBotMessageType MessageType,
    long TargetId,
    long SenderId,
    QChatSenderRole SenderRole,
    AgentPermissionRequest PermissionRequest);

[Module("QQ聊天", """
                连接 OneBot v11 WebSocket 服务器，实现 QQ 消息收发及文件传输。
                可用于搭建服务器QQ机器人平台应用：
                - https://luckylillia.com（推荐）
                - https://napneko.github.io
                """,
    defaultCategory: "Alife 官方/交互方式",
    editorUI: typeof(QChatServiceUI), LaunchOrder = 10)]
public class QChatService(
    XmlFunctionCaller functionService,
    ILogger<QChatService> logger,
    ISpeechModel? speechModel = null,
    IOneBotRuntime? oneBotRuntime = null,
    ILifeEventPublisher? lifeEventPublisher = null,
    AgentControlCenterService? agentControlCenter = null,
    AgentActionAuthorizationService? actionAuthorization = null,
    AgentActionGatewayService? actionGateway = null,
    AgentAuditLogService? auditLog = null,
    QChatRelationCacheService? relationCacheService = null,
    QChatUserProfileService? userProfileService = null,
    PADEmotionEngine? emotionEngine = null,
    QChatManagedFileService? managedFileService = null) :
    InteractiveModule<QChatService>,
    IAsyncDisposable,
    ITimeIterative,
    IConfigurable<QChatConfig>,
    IEmbodiedCapability,
    IChatOutputSink,
    IModuleHealthReporter,
    IAgentQChatJoinedGroupProvider
{
    const string QuietModeSleepFallbackAcknowledgement = "好，我先安静下来。";
    const string QuietModeWakeFallbackAcknowledgement = "我在。";

    [XmlFunction(FunctionMode.OneShot)]
    public void GetQChatGuide()
    {
        // 动态扫描表情库资源，告知 AI 可用的视觉表达
        string emoteBase = Path.Combine(AlifePath.StorageFolderPath, "Emotes");
        StringBuilder emoteInfo = new();
        if (Directory.Exists(emoteBase))
        {
            string[] categories = Directory.GetDirectories(emoteBase)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();

            string[] individualEmotes = Directory.GetFiles(emoteBase)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToArray();

            if (categories.Length > 0 || individualEmotes.Length > 0)
            {
                emoteInfo.AppendLine("- 目前可用的表情库选项有:");
                if (categories.Length > 0)
                    emoteInfo.AppendLine($"  - 分类 (传入文件夹名将随机发图): {string.Join(", ", categories)}");
                if (individualEmotes.Length > 0)
                    emoteInfo.AppendLine($"  - 独立表情: {string.Join(", ", individualEmotes)}");
            }
        }

        string relationCacheToolInfo = new XmlHandler(RelationCache).FunctionDocument();
        Poke($"""
              QQ发送能力说明

              当你决定发QQ消息时，使用下面的发送能力把你会实际输入QQ的话发出去。普通群聊不要默认@；需要指向某人时优先用自然称呼，只有强提醒、重要触达或术术明确要求时才使用[CQ:at,qq=...]。

              ## 提供函数
              {xmlHandler.FunctionDocument()}
              {relationCacheToolInfo}

              ## 关键信息
              - 你的 QQ: {(Configuration!.BotId == 0 ? "未设置" : Configuration.BotId)}（如果有人At该QQ，代表专门找你说话）
              - 主人 QQ: {(Configuration.OwnerId == 0 ? "未设置" : Configuration.OwnerId)} (此人的消息有最高优先级，且是安全无害的)

              ## CQ码功能
              该通讯工具基于OneBot11实现，因此支持CQ码之类的功能。通过在QChat的消息中携带CQ标签，你可以发送一些特别的消息，比如：
              - [CQ:image,file=1.jpg]：发送图片
              - [CQ:record,file=1.mp3]：发送音频
              - [CQ:video,file=1.mp4]：发送视频
              - [CQ:at,qq=10001000]：@某人
              普通群聊示例：`<qchat type="Group" targetId="群号">小明，刚才那句我看到了。</qchat>`
              强提醒示例：`<qchat type="Group" targetId="群号">[CQ:at,qq=发送者ID] 这件事需要你确认。</qchat>`
              多媒体示例：`<qchat>[CQ:record,file=1.mp3]</qchat>`

              ## 表情库功能
              你有一个丰富的预设表情库，可用在 QImage 中直接指定表情库中的名称或分类名快速发送表情。你要积极的使用该功能，来增加聊天的趣味性。
              目前支持的表情库选项有：
              {emoteInfo}

              你的表情库存储路径在 {emoteBase}，你也可以在其中存储自己的表情。直接存储在根目录将作为独立表情，存储到子文件夹，则作为分类。
              """);
    }
    [XmlFunction(FunctionMode.Content, budgetCost: 4)]
    [Description("将文本以QQ消息输出（群聊普通聊天不要默认@；需要指向某人时优先用自然称呼，只有强提醒或重要触达才使用“[CQ:at,qq=发送者ID]”）")]
    public async Task QChat(XmlExecutorContext ctx, OneBotMessageType type, long targetId, [Description("将文本转为语音发送")] bool voice = false)
    {
        if (ctx.CallMode == CallMode.Closing)
        {
            if (targetId == Configuration!.BotId)
                throw new Exception("不允许将消息发生给自己");

            string message = ctx.FullContent.Trim();
            if (string.IsNullOrEmpty(message))
                return;
            if (IsInternalNoReplyStatus(message))
            {
                WriteQChatDiagnostic("qchat-internal-status-suppressed", "QChat XML tool output suppressed because it is an internal no-reply/status message.", new {
                    type,
                    targetId,
                    message
                });
                return;
            }

            if (TryEnsureQChatReplyTargetAllowed(type, targetId, "xml-qchat") == false)
                return;
            if (ShouldSuppressOutgoingForQuietMode(type, targetId, "xml-qchat"))
                return;

            if (voice)
            {
                if (speechModel == null) throw new Exception("当前语音消息不可用");
                message = OneBotSegment.GetPlainText(message);

                string? file = await speechModel.GenerateSpeechFileAsync(message);
                if (file == null)
                    throw new Exception("语音合成失败");
                message = $"[CQ:record,file={file}]";
            }

            try
            {
                await SendTextOrMediaMessageAsync(type, targetId, message, streamText: voice == false);
                WriteQChatDiagnostic("qchat-sent", "QChat XML tool sent a QQ message.", new {
                    type,
                    targetId,
                    message
                });

                PublishLifeEvent($"You sent a QQ {type.ToString().ToLowerInvariant()} message to {targetId}.");
            }
            catch (Exception ex)
            {
                WriteQChatDiagnostic("qchat-send-failed", ex.Message, new {
                    type,
                    targetId
                }, ex);
                Poke($"[QQ消息发送失败] {ex.Message}");
            }
        }
    }

    public Task SendChatAsync(string targetType, long targetId, string text, bool voice = false)
    {
        return SendChatAsyncCore(targetType, targetId, text, voice, bypassQuietMode: false);
    }

    async Task SendChatAsyncCore(string targetType, long targetId, string text, bool voice = false, bool bypassQuietMode = false)
    {
        OneBotMessageType type = targetType.Trim().ToLowerInvariant() switch {
            "group" => OneBotMessageType.Group,
            "private" => OneBotMessageType.Private,
            _ => throw new InvalidOperationException("targetType must be 'group' or 'private'.")
        };

        if (targetId == Configuration!.BotId)
            throw new Exception("Cannot send a QQ message to self.");

        string message = text.Trim();
        if (string.IsNullOrEmpty(message))
            return;

        if (TryEnsureQChatReplyTargetAllowed(type, targetId, "direct-qchat") == false)
            return;

        if (bypassQuietMode == false && ShouldSuppressOutgoingForQuietMode(type, targetId, "direct-qchat"))
            return;

        if (voice)
        {
            if (speechModel == null)
                throw new Exception("Voice QQ messages are unavailable.");
            message = OneBotSegment.GetPlainText(message);

            string? file = await speechModel.GenerateSpeechFileAsync(message);
            if (file == null)
                throw new Exception("Speech synthesis failed.");
            message = $"[CQ:record,file={file}]";
        }

        try
        {
            await SendTextOrMediaMessageAsync(type, targetId, message, streamText: voice == false);

            PublishLifeEvent($"You sent a QQ {targetType.Trim().ToLowerInvariant()} message to {targetId}.");
        }
        catch (Exception ex)
        {
            WriteQChatDiagnostic("qchat-send-failed", ex.Message, new {
                type,
                targetId
            }, ex);
            TryPokeSendFailure(ex.Message);
        }
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_quiet_mode", budgetCost: 1)]
    [Description("设置 QQ 安静模式。启用后，非主人私聊、群聊 @、普通群聊和主动群聊都会被静默抑制；主人仍可唤醒或继续控制。")]
    public void QChatQuietMode(
        [Description("true 表示进入安静模式，false 表示退出安静模式")] bool enabled,
        [Description("可选原因，会写入诊断和当前状态")] string? reason = null)
    {
        if (TryAuthorizeQuietModeToolControl(enabled, reason) == false)
            return;

        SetQuietMode(enabled, reason ?? "agent-control");
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_allowlist_status", budgetCost: 1)]
    [Description("查看当前 QQ 白名单和群聊接收策略。涉及实时配置时应调用本工具，不要凭记忆回答。")]
    public Task QChatAllowlistStatus()
    {
        return PublishQChatToolResultAsync(FormatAllowlistStatus(), "qchat-allowlist-status");
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_file_list", budgetCost: 1)]
    [Description("List QQ files received into the managed file workspace. In QQ context this is owner-only.")]
    public async Task QChatFileList(int limit = 10)
    {
        if (TryAuthorizeOwnerToolControl("qchat_file_list") == false)
        {
            await PublishQChatToolResultAsync("Only the owner can list managed QQ files.", "qchat-file-list-denied");
            return;
        }

        IReadOnlyList<QChatManagedFileRecord> records = await ManagedFiles.ListAsync();
        await PublishQChatToolResultAsync(FormatManagedFileList(records, limit), "qchat-file-list");
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_file_download", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("Download one received QQ file by managed_file_id into Storage/AgentWorkspace/QChatFiles only after owner approval.")]
    public async Task QChatFileDownload(string id)
    {
        if (TryAuthorizeOwnerToolControl("qchat_file_download") == false)
        {
            await PublishQChatToolResultAsync("Only the owner can download managed QQ files.", "qchat-file-download-denied");
            return;
        }

        QChatManagedFileOperationResult result = await ManagedFiles.DownloadAsync(id);
        WriteQChatDiagnostic(result.Success ? "qchat-file-download-succeeded" : "qchat-file-download-failed", result.Message, new {
            id,
            result.Record?.OriginalName,
            result.Record?.LocalPath,
            result.Record?.Status
        });
        await PublishQChatToolResultAsync(FormatManagedFileOperation(result), "qchat-file-download");
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_file_read", budgetCost: 2)]
    [Description("Read the extracted text preview of a downloaded managed QQ text file. In QQ context this is owner-only.")]
    public async Task QChatFileRead(string id)
    {
        if (TryAuthorizeOwnerToolControl("qchat_file_read") == false)
        {
            await PublishQChatToolResultAsync("Only the owner can read managed QQ files.", "qchat-file-read-denied");
            return;
        }

        QChatManagedFileOperationResult result = await ManagedFiles.ReadAsync(id);
        await PublishQChatToolResultAsync(FormatManagedFileOperation(result), "qchat-file-read");
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_file_delete", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 3)]
    [Description("Delete one downloaded managed QQ file by managed_file_id. This only deletes files inside the managed QChatFiles workspace.")]
    public async Task QChatFileDelete(string id)
    {
        if (TryAuthorizeOwnerToolControl("qchat_file_delete") == false)
        {
            await PublishQChatToolResultAsync("Only the owner can delete managed QQ files.", "qchat-file-delete-denied");
            return;
        }

        QChatManagedFileOperationResult result = await ManagedFiles.DeleteAsync(id);
        WriteQChatDiagnostic(result.Success ? "qchat-file-delete-succeeded" : "qchat-file-delete-failed", result.Message, new {
            id,
            result.Record?.OriginalName,
            result.Record?.LocalPath,
            result.Record?.Status
        });
        await PublishQChatToolResultAsync(FormatManagedFileOperation(result), "qchat-file-delete");
    }

    [XmlFunction(FunctionMode.OneShot, "qchat_allowlist_update", riskLevel: XmlFunctionRiskLevel.High, budgetCost: 3)]
    [Description("修改 QQ 白名单。仅主人可在 QQ 上下文中执行。target 支持 group/private/mention-outside；action 支持 add/remove/set/clear/enable/disable。")]
    public Task QChatAllowlistUpdate(
        [Description("group 修改群白名单；private 修改私聊白名单；mention-outside 修改是否允许非白名单群 @ 唤醒")]
        string target,
        [Description("add/remove/set/clear/enable/disable")]
        string action,
        [Description("目标 QQ 号或群号；clear/enable/disable 可填 0")]
        long id = 0)
    {
        if (TryAuthorizeOwnerToolControl("qchat_allowlist_update") == false)
            return PublishQChatToolResultAsync("只有主人可以修改 QQ 白名单。", "qchat-allowlist-update-denied");

        try
        {
            string normalizedTarget = NormalizeAllowlistToken(target);
            string normalizedAction = NormalizeAllowlistToken(action);
            string result;

            if (normalizedTarget is "group" or "groups")
            {
                Configuration!.AllowedGroupIds = UpdateAllowlistIds(Configuration.AllowedGroupIds, normalizedAction, id);
                result = $"群白名单已更新：{FormatAllowlistIds(Configuration.AllowedGroupIds)}";
            }
            else if (normalizedTarget is "private" or "privates" or "user" or "users")
            {
                Configuration!.AllowedPrivateUserIds = UpdateAllowlistIds(Configuration.AllowedPrivateUserIds, normalizedAction, id);
                result = $"私聊白名单已更新：{FormatAllowlistIds(Configuration.AllowedPrivateUserIds)}";
            }
            else if (normalizedTarget is "mentionoutside" or "mention-outside" or "outside-mention")
            {
                Configuration!.AllowMentionOutsideAllowedGroups = normalizedAction switch
                {
                    "enable" or "on" or "true" or "allow" => true,
                    "disable" or "off" or "false" or "deny" => false,
                    _ => throw new InvalidOperationException("mention-outside 只支持 enable/disable。")
                };
                result = $"非白名单群 @ 唤醒：{Configuration.AllowMentionOutsideAllowedGroups}";
            }
            else
            {
                throw new InvalidOperationException("target 必须是 group、private 或 mention-outside。");
            }

            WriteQChatDiagnostic("qchat-allowlist-updated", "QQ allowlist was updated by owner tool control.", new {
                target = normalizedTarget,
                action = normalizedAction,
                id,
                Configuration!.AllowedGroupIds,
                Configuration.AllowedPrivateUserIds,
                Configuration.AllowMentionOutsideAllowedGroups
            });
            return PublishQChatToolResultAsync($"{result}\n\n{FormatAllowlistStatus()}", "qchat-allowlist-update");
        }
        catch (Exception ex)
        {
            WriteQChatDiagnostic("qchat-allowlist-update-failed", ex.Message, new {
                target,
                action,
                id
            }, ex);
            return PublishQChatToolResultAsync($"QQ 白名单修改失败：{ex.Message}", "qchat-allowlist-update-failed");
        }
    }

    void TryPokeSendFailure(string message)
    {
        if (ChatBot == null)
            return;

        Poke($"[QQ message send failed] {message}");
    }

    public async Task<QChatOwnerNotificationDeliveryResult> DeliverOwnerNotificationPlanAsync(
        AgentOwnerNotificationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ShouldNotifyOwner == false)
            return new QChatOwnerNotificationDeliveryResult(
                ShouldNotify: false,
                PrivateSentCount: 0,
                GroupSummarySent: false,
                Message: "Owner notification is not required.");

        if (TryParseQqSessionId(plan.TargetSessionId, "private", out long ownerUserId) == false)
        {
            string error = $"Unsupported owner notification target session: {plan.TargetSessionId}";
            RecordOwnerNotificationAudit("qq.owner_notification.private", "system", plan.TargetSessionId, false, error);
            return new QChatOwnerNotificationDeliveryResult(
                ShouldNotify: true,
                PrivateSentCount: 0,
                GroupSummarySent: false,
                Message: "Owner notification was not delivered.",
                Error: error);
        }

        int privateSentCount = 0;
        bool groupSummarySent = false;

        try
        {
            string privateMessage = ComposeOwnerNotificationPrivateMessage(plan.PrivateMessages);
            await GetOneBotClient().SendPrivateMessage(ownerUserId, privateMessage);
            privateSentCount = 1;
            RecordOwnerNotificationAudit(
                "qq.owner_notification.private",
                "system",
                $"target={ownerUserId}; messages={plan.PrivateMessages.Count}",
                true);

            if (TryParseQqSessionId(plan.SourceGroupSessionId, "group", out long sourceGroupId)
                && string.IsNullOrWhiteSpace(plan.PublicGroupSummary) == false)
            {
                await GetOneBotClient().SendGroupMessage(sourceGroupId, plan.PublicGroupSummary.Trim());
                groupSummarySent = true;
                RecordOwnerNotificationAudit(
                    "qq.owner_notification.group_summary",
                    "system",
                    $"group={sourceGroupId}",
                    true);
            }

            return new QChatOwnerNotificationDeliveryResult(
                ShouldNotify: true,
                PrivateSentCount: privateSentCount,
                GroupSummarySent: groupSummarySent,
                Message: groupSummarySent
                    ? "Owner notification and sanitized group summary were delivered."
                    : "Owner notification was delivered.");
        }
        catch (Exception ex)
        {
            RecordOwnerNotificationAudit(
                "qq.owner_notification.delivery",
                "system",
                $"target={ownerUserId}",
                false,
                ex.Message);
            return new QChatOwnerNotificationDeliveryResult(
                ShouldNotify: true,
                PrivateSentCount: privateSentCount,
                GroupSummarySent: groupSummarySent,
                Message: "Owner notification delivery failed.",
                Error: ex.Message);
        }
    }

    async Task SendTextOrMediaMessageAsync(OneBotMessageType type, long targetId, string message, bool streamText)
    {
        if (type == OneBotMessageType.Group)
            OnAIGroupActivity(targetId);

        if (streamText == false || Configuration?.EnableBalancedTextStreaming == false || ShouldStreamTextMessage(message) == false)
        {
            await SendSingleMessageAsync(type, targetId, message);
            return;
        }

        StreamingOutputSegmenter segmenter = new(type == OneBotMessageType.Group
            ? StreamingOutputPolicy.QqGroupText
            : StreamingOutputPolicy.QqPrivateText);
        List<string> segments = new();
        segments.AddRange(segmenter.Push(message));
        segments.AddRange(segmenter.Flush());

        foreach (string segment in segments)
            await SendSingleMessageAsync(type, targetId, segment);
    }

    async Task SendSingleMessageAsync(OneBotMessageType type, long targetId, string message)
    {
        if (type == OneBotMessageType.Group)
            await GetOneBotClient().SendGroupMessage(targetId, message);
        else
            await GetOneBotClient().SendPrivateMessage(targetId, message);
        Interlocked.Increment(ref outboundMessageVersion);
    }

    static bool ShouldStreamTextMessage(string message)
    {
        string lower = message.ToLowerInvariant();
        return lower.Contains("[cq:image", StringComparison.Ordinal) == false
               && lower.Contains("[cq:record", StringComparison.Ordinal) == false
               && lower.Contains("[cq:video", StringComparison.Ordinal) == false
               && lower.Contains("[cq:file", StringComparison.Ordinal) == false;
    }

    static string ComposeOwnerNotificationPrivateMessage(IReadOnlyList<string> privateMessages)
    {
        string[] messages = privateMessages
            .Where(message => string.IsNullOrWhiteSpace(message) == false)
            .Select(message => message.Trim())
            .Take(8)
            .ToArray();

        if (messages.Length == 0)
            return "Control-center owner review is required. Open the Agent Control Center for private details.";

        StringBuilder builder = new();
        builder.AppendLine("Agent control-center owner attention");
        foreach (string message in messages)
            builder.AppendLine($"- {message}");
        return builder.ToString().Trim();
    }

    static bool TryParseQqSessionId(string? sessionId, string expectedKind, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        string[] parts = sessionId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;
        if (parts[0].Equals("qq", StringComparison.OrdinalIgnoreCase) == false)
            return false;
        if (parts[1].Equals(expectedKind, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        return long.TryParse(parts[2], out id) && id > 0;
    }

    void RecordOwnerNotificationAudit(
        string action,
        string actor,
        string detail,
        bool succeeded,
        string? error = null)
    {
        auditLog?.Record(
            action,
            actor,
            detail,
            AgentAuditRiskLevel.Low,
            succeeded,
            error);
    }

    [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("发送文件到QQ")]
    public async Task QFile(OneBotMessageType type, long targetId,
        [Description("本地绝对路径")] string file)
    {
        file = file.Trim();
        if (string.IsNullOrEmpty(file))
            throw new ArgumentNullException(nameof(file));
        if (targetId == 0)
            throw new ArgumentNullException(nameof(targetId));
        if (targetId == Configuration!.BotId)
            throw new Exception("不允许将消息发生给自己");

        file = file.Replace('\\', '/');
        string fileName = Path.GetFileName(file);
        try
        {
            if (type == OneBotMessageType.Group)
            {
                OnAIGroupActivity(targetId);
                await GetOneBotClient().UploadGroupFile(targetId, file, fileName);
            }
            else
                await GetOneBotClient().UploadPrivateFile(targetId, file, fileName);
        }
        catch (Exception ex)
        {
            Poke($"[QQ文件发送失败] {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("上传本地文件到指定QQ群文件。")]
    public async Task QGroupFile(long groupId,
        [Description("本地绝对路径")] string file,
        [Description("可选展示文件名，留空则使用原文件名")] string? name = null)
    {
        await ExecuteQGroupFileCore(groupId, file, name);
    }

    public async Task<QChatExternalActionResult> QGroupFile(
        long groupId,
        string file,
        string? name,
        AgentPermissionRequest request,
        AgentPermissionConfig permissionConfig)
    {
        AgentPermissionRequest normalizedRequest = NormalizeExternalQqRequest(
            request,
            "qq.group_file_upload");
        AgentActionGatewayResult<bool> gatewayResult = await actionGateway.ExecuteAsync(
            normalizedRequest,
            permissionConfig,
            async () =>
            {
                await ExecuteQGroupFileCore(groupId, file, name);
                return true;
            },
            detail: $"group={groupId}; file={Path.GetFileName(file)}; name={name}");

        return ToExternalActionResult(gatewayResult, "QQ group file upload executed.");
    }

    async Task ExecuteQGroupFileCore(long groupId, string file, string? name)
    {
        if (Configuration!.EnableGroupFileUpload == false)
            throw new InvalidOperationException("QQ group file upload is disabled.");
        if (groupId == 0)
            throw new ArgumentNullException(nameof(groupId));
        if (groupId == Configuration.BotId)
            throw new Exception("Cannot upload a QQ group file to self.");
        EnsureTargetAllowed(Configuration.AllowedGroupIds, groupId, "QQ group");

        string normalizedFile = NormalizeExistingLocalFile(file);
        string fileName = NormalizeUploadName(normalizedFile, name);

        try
        {
            OnAIGroupActivity(groupId);
            await GetOneBotClient().UploadGroupFile(groupId, normalizedFile, fileName);
            PublishLifeEvent($"You uploaded a QQ group file to {groupId}: {fileName}.");
        }
        catch (Exception ex)
        {
            Poke($"[QQ group file upload failed] {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("上传本地文件到指定QQ私聊。")]
    public async Task QPrivateFile(long userId,
        [Description("本地绝对路径")] string file,
        [Description("可选展示文件名，留空则使用原文件名")] string? name = null)
    {
        await ExecuteQPrivateFileCore(userId, file, name);
    }

    public async Task<QChatExternalActionResult> QPrivateFile(
        long userId,
        string file,
        string? name,
        AgentPermissionRequest request,
        AgentPermissionConfig permissionConfig)
    {
        AgentPermissionRequest normalizedRequest = NormalizeExternalQqRequest(
            request,
            "qq.private_file_upload");
        AgentActionGatewayResult<bool> gatewayResult = await actionGateway.ExecuteAsync(
            normalizedRequest,
            permissionConfig,
            async () =>
            {
                await ExecuteQPrivateFileCore(userId, file, name);
                return true;
            },
            detail: $"user={userId}; file={Path.GetFileName(file)}; name={name}");

        return ToExternalActionResult(gatewayResult, "QQ private file upload executed.");
    }

    async Task ExecuteQPrivateFileCore(long userId, string file, string? name)
    {
        if (Configuration!.EnablePrivateFileUpload == false)
            throw new InvalidOperationException("QQ private file upload is disabled.");
        if (userId == 0)
            throw new ArgumentNullException(nameof(userId));
        if (userId == Configuration.BotId)
            throw new Exception("Cannot upload a QQ private file to self.");
        EnsureTargetAllowed(Configuration.AllowedPrivateUserIds, userId, "QQ private user");

        string normalizedFile = NormalizeExistingLocalFile(file);
        string fileName = NormalizeUploadName(normalizedFile, name);

        try
        {
            await GetOneBotClient().UploadPrivateFile(userId, normalizedFile, fileName);
            PublishLifeEvent($"You uploaded a QQ private file to {userId}: {fileName}.");
        }
        catch (Exception ex)
        {
            Poke($"[QQ private file upload failed] {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("发送QQ视频消息。用于在聊天里发送视频，不等同于上传群文件。")]
    public async Task QVideo(OneBotMessageType type, long targetId,
        [Description("视频URL或本地绝对路径，建议mp4")] string video)
    {
        await ExecuteQVideoCore(type, targetId, video);
    }

    public async Task<QChatExternalActionResult> QVideo(
        OneBotMessageType type,
        long targetId,
        string video,
        AgentPermissionRequest request,
        AgentPermissionConfig permissionConfig)
    {
        AgentPermissionRequest normalizedRequest = NormalizeExternalQqRequest(
            request,
            "qq.video_send");
        AgentActionGatewayResult<bool> gatewayResult = await actionGateway.ExecuteAsync(
            normalizedRequest,
            permissionConfig,
            async () =>
            {
                await ExecuteQVideoCore(type, targetId, video);
                return true;
            },
            detail: $"type={type}; target={targetId}; video={Path.GetFileName(video)}");

        return ToExternalActionResult(gatewayResult, "QQ video message executed.");
    }

    async Task ExecuteQVideoCore(OneBotMessageType type, long targetId, string video)
    {
        if (Configuration!.EnableVideoMessage == false)
            throw new InvalidOperationException("QQ video messages are disabled.");
        if (targetId == 0)
            throw new ArgumentNullException(nameof(targetId));
        if (targetId == Configuration.BotId)
            throw new Exception("Cannot send a QQ video message to self.");

        if (type == OneBotMessageType.Group)
            EnsureTargetAllowed(Configuration.AllowedGroupIds, targetId, "QQ group");
        else
            EnsureTargetAllowed(Configuration.AllowedPrivateUserIds, targetId, "QQ private user");

        string normalizedVideo = NormalizeVideoReference(video);
        string message = $"[CQ:video,file={normalizedVideo}]";

        try
        {
            if (type == OneBotMessageType.Group)
            {
                OnAIGroupActivity(targetId);
                await GetOneBotClient().SendGroupMessage(targetId, message);
            }
            else
                await GetOneBotClient().SendPrivateMessage(targetId, message);

            PublishLifeEvent($"You sent a QQ {type.ToString().ToLowerInvariant()} video message to {targetId}.");
        }
        catch (Exception ex)
        {
            Poke($"[QQ video send failed] {ex.Message}");
        }
    }

    AgentPermissionRequest NormalizeExternalQqRequest(
        AgentPermissionRequest request,
        string action)
    {
        return request with
        {
            RiskLevel = AgentRiskLevel.High,
            Action = string.IsNullOrWhiteSpace(request.Action) ? action : request.Action.Trim()
        };
    }

    static QChatExternalActionResult ToExternalActionResult(
        AgentActionGatewayResult<bool> gatewayResult,
        string successMessage)
    {
        return gatewayResult.Executed
            ? new QChatExternalActionResult(true, gatewayResult.Decision, successMessage)
            : new QChatExternalActionResult(false, gatewayResult.Decision, gatewayResult.Message);
    }

    [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description($"发送图片到QQ（仅支持图片，不支持文件。发送文件请用 {nameof(QFile)}）")]
    public async Task QImage(OneBotMessageType type, long targetId,
        [Description("支持网址url、表情库名称，或者本地绝对路径")] string image)
    {
        image = image.Trim();
        if (string.IsNullOrEmpty(image))
            throw new ArgumentNullException(nameof(image));
        if (targetId == 0)
            throw new ArgumentNullException(nameof(targetId));
        if (targetId == Configuration!.BotId)
            throw new Exception("不允许将消息发生给自己");

        // 尝试从表情库匹配 (优先)
        string emoteBase = Path.Combine(AlifePath.StorageFolderPath, "Emotes");
        string emotePath = Path.Combine(emoteBase, image).Replace('\\', '/');

        if (Directory.Exists(emotePath))
        {
            // 文件夹：随机选一张
            string[] files = Directory.GetFiles(emotePath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (files.Length > 0)
            {
                image = files[Random.Shared.Next(files.Length)];
            }
        }
        else if (File.Exists(emotePath))
        {
            // 单个文件：直接使用
            image = emotePath;
        }
        else
        {
            // 尝试追加后缀名查找
            string[] extensions = [".png", ".jpg", ".jpeg", ".gif"];
            string? foundFile = extensions.Select(ext => emotePath + ext).FirstOrDefault(File.Exists);
            if (foundFile != null) image = foundFile;
        }

        if (image.StartsWith("http") == false && File.Exists(image) == false)
            throw new Exception("图片不存在");

        image = image.Replace('\\', '/');
        try
        {
            if (type == OneBotMessageType.Group)
            {
                OnAIGroupActivity(targetId);
                await GetOneBotClient().SendGroupMessage(targetId, $"[CQ:image,file={image}]");
            }
            else
                await GetOneBotClient().SendPrivateMessage(targetId, $"[CQ:image,file={image}]");
        }
        catch (Exception ex)
        {
            Poke($"[QQ图片发送失败] {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看转发消息内容。（使用后需等待结果返回）")]
    public async Task QForward([Description("转发消息 ID")] string id)
    {
        IOneBotRuntime client = GetOneBotClient();
        List<OneBotForwardMessage>? messages = await client.GetForwardMessage(id);
        if (messages == null || messages.Count == 0)
        {
            Poke($"转发消息 {id} 为空或获取失败。");
            return;
        }

        string formatted = OneBotSegment.FormatForwardList(id, messages, client);
        Poke(formatted);
    }

    public async Task ReconnectAsync()
    {
        IOneBotRuntime client = GetOneBotClient();
        client.Url = Configuration!.Url;
        client.Token = Configuration.Token;
        await client.ConnectAsync();
    }
    protected override string ChatTextFilter(string text)
    {
        return $"""
                {base.ChatTextFilter(text)}
                ({Configuration?.AppendChatPrompt})
                (你刚在QQ里看到这条消息。如果决定回复，只输出夏羽会实际发到QQ的文本；需要时可以在内部使用QQ发送能力，但不要在QQ里提工具。安全标签和路由标签不是QQ内容，不能引用或转述。)
                """;
    }

    public QChatConfig? Configuration
    {
        get => configuration;
        set
        {
            configuration = value;
            if (configuration != null)
            {
                groupAwakingWords = Configuration!.WakingWords.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                ignoredGroup = Configuration!.IgnoredGroup.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
    }
    public bool IsConnected => oneBotClient is { IsConnected: true };
    public IReadOnlyDictionary<long, GroupState> GroupStates => groupStates;
    public IReadOnlyList<QChatGroupDecisionSnapshot> RecentGroupDecisions
    {
        get
        {
            lock (groupDecisionGate)
                return recentGroupDecisions.ToArray();
        }
    }
    public string Name => "QQ";
    public EmbodiedCapabilityKind Kind => EmbodiedCapabilityKind.Communication;
    public string SelfDescription => "Your QQ social communication channel for private chats, group chats, images, files, forwarded messages, and message context.";
    public Func<QChatInboundMessage, Task>? InboundChatDispatcher { get; set; }
    public bool IsQuietModeEnabled { get; private set; }
    public DateTimeOffset? QuietModeChangedAt { get; private set; }
    public string? QuietModeReason { get; private set; }
    public string? GetCurrentState() => Configuration == null
        ? "QQ configuration unavailable"
        : $"QQ channel configured; connected: {IsConnected}; bot id: {(Configuration.BotId == 0 ? "not set" : Configuration.BotId)}; quiet mode: {(IsQuietModeEnabled ? "enabled" : "disabled")}{FormatQuietModeStateSuffix()}.";
    public ModuleHealth GetHealth()
    {
        if (Configuration == null)
            return new ModuleHealth("QChat", ModuleHealthStatus.Unavailable, "QQ configuration is unavailable.");

        if (IsConnected)
            return new ModuleHealth("QChat", ModuleHealthStatus.Healthy, $"OneBot is connected; bot id: {(Configuration.BotId == 0 ? "not set" : Configuration.BotId)}.");

        return new ModuleHealth("QChat", ModuleHealthStatus.Degraded, "OneBot is configured but disconnected.");
    }

    QChatConfig? configuration;
    readonly IOneBotRuntime? injectedOneBotRuntime = oneBotRuntime;
    readonly AgentControlCenterService? agentControlCenter = agentControlCenter;
    readonly AgentActionAuthorizationService actionAuthorization = actionAuthorization ?? new AgentActionAuthorizationService();
    readonly AgentActionGatewayService actionGateway = actionGateway ?? new AgentActionGatewayService(authorization: actionAuthorization);
    readonly AgentAuditLogService? auditLog = auditLog;
    readonly QChatRelationCacheService? injectedRelationCache = relationCacheService;
    QChatRelationCacheService? relationCache;
    readonly QChatUserProfileService userProfiles = userProfileService ?? new QChatUserProfileService();
    readonly QChatManagedFileService? injectedManagedFileService = managedFileService;
    QChatManagedFileService? managedFiles;
    IOneBotRuntime? oneBotClient;
    string[] groupAwakingWords = [];
    string[] ignoredGroup = [];
    readonly Dictionary<long, GroupState> groupStates = new();
    readonly object groupDecisionGate = new();
    readonly List<QChatGroupDecisionSnapshot> recentGroupDecisions = [];
    const int MaxRecentGroupDecisions = 50;
    readonly AsyncLocal<QChatReplySession?> currentReplySession = new();
    readonly SemaphoreSlim inboundModelDispatchGate = new(1, 1);
    readonly Channel<OneBotBaseEvent> oneBotEventQueue = Channel.CreateUnbounded<OneBotBaseEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    readonly object activeReplySessionGate = new();
    readonly Dictionary<QChatReplySession, int> activeReplySessions = new();
    static readonly object emptyGroupFlushDiagnosticGate = new();
    static readonly Dictionary<long, DateTimeOffset> emptyGroupFlushDiagnosticTimes = new();
    static readonly TimeSpan EmptyGroupFlushDiagnosticInterval = TimeSpan.FromMinutes(5);
    long outboundMessageVersion;
    readonly object permissionGate = new();
    AgentPermissionRequest? currentPermissionRequest;
    DateTime currentPermissionExpiresAt = DateTime.MinValue;
    DateTime lastReconnectAttemptTime = DateTime.MinValue;
    CancellationTokenSource? oneBotEventProcessingCancellation;
    Task? oneBotEventProcessingTask;
    XmlHandler xmlHandler = null!;
    QChatRelationCacheService RelationCache => relationCache ??= injectedRelationCache ?? new QChatRelationCacheService(GetOneBotClient());
    QChatManagedFileService ManagedFiles => managedFiles ??= injectedManagedFileService ?? new QChatManagedFileService(
        Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "QChatFiles"));

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        RestoreQuietModeFromConfiguration();

        //加载基本环境
        oneBotClient = GetOneBotClient();

        // 注入函数和提示词
        xmlHandler = new(this);
        functionService.RegisterHandler(xmlHandler);
        RelationCache.AttachOneBotRuntime(oneBotClient);
        RelationCache.DiagnosticWriter = WriteQChatDiagnostic;
        RelationCache.ToolResultSink = SendCurrentReplySessionToolResultAsync;
        RegisterRelationCacheToolsIfMissing();
        functionService.ExecutionPolicy.AuthorizeHighRiskFunction = AuthorizeHighRiskXmlFunction;

        Prompt($"""
                此服务为你增加收发qq消息的能力，能够处理图片，文件，转发等各种丰富的qq功能。
                当你需要用qq联系他人，或收到qq消息要处理时，先调用<{nameof(GetQChatGuide)}/>来学习如何使用qq工具，然后再以合适的方式回复。

                收到 QQ 入站消息后，如果你决定回复，必须把面向 QQ 用户的内容发送到当前 QQ 会话：
                - 私聊回复当前私聊：<qchat type="Private" targetId="对方QQ号">回复内容</qchat>
                - 群聊普通回复当前群：<qchat type="Group" targetId="群号">小明，刚刚那句我听到了</qchat>
                - 群聊强提醒当前群：<qchat type="Group" targetId="群号">[CQ:at,qq=发送者QQ号] 这件事需要你确认一下</qchat>
                - 不要只输出普通文字来“说明你会回复”，普通文字不会自动出现在 QQ 里。
                - 如果判断无需回复，可以保持沉默，不要输出解释。
                """);
    }

    void RegisterRelationCacheToolsIfMissing()
    {
        if (functionService.CanHandleFunction("qchat_joined_groups_refresh"))
            return;

        functionService.RegisterHandler(new XmlHandler(RelationCache));
    }

    public Task<AgentQChatJoinedGroupSourceSnapshot> RefreshAgentJoinedGroupsAsync()
    {
        return RelationCache.RefreshAgentJoinedGroupsAsync();
    }

    public AgentQChatJoinedGroupSourceSnapshot GetCachedAgentJoinedGroups()
    {
        return RelationCache.GetCachedAgentJoinedGroups();
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        if (oneBotClient == null)
            throw new NullReferenceException(nameof(oneBotClient));

        oneBotEventProcessingCancellation = new CancellationTokenSource();
        oneBotEventProcessingTask = ProcessOneBotEventQueueAsync(oneBotEventProcessingCancellation.Token);
        oneBotClient.EventReceived += OnEventReceived;
        ChatBot.ChatOver += ClearPermissionRequest;
        WriteQChatDiagnostic("start", "QChat service starting.", new {
            Configuration!.Url,
            tokenSet = string.IsNullOrWhiteSpace(Configuration.Token) == false,
            Configuration.BotId,
            Configuration.OwnerId,
            Configuration.AllowGroupMemberChat,
            Configuration.AllowGroupMemberMentions,
            Configuration.AllowProactiveGroupChat,
            Configuration.AllowPrivateGuestChat,
            Configuration.ProactiveChatProbability,
            Configuration.PassiveGroupReplyCooldownSeconds,
            Configuration.SuppressLowInformationPassiveGroupMessages,
            Configuration.FlushInterval,
            Configuration.AllowedGroupIds,
            Configuration.WakingWords
        });

        //初始尝试链接
        try
        {
            await oneBotClient.ConnectAsync();
            WriteQChatDiagnostic("connect-succeeded", "OneBot connected.", new {
                oneBotClient.BotId,
                oneBotClient.IsConnected
            });
        }
        catch (Exception ex)
        {
            WriteQChatDiagnostic("connect-failed", ex.Message, exception: ex);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (oneBotClient != null)
            oneBotClient.EventReceived -= OnEventReceived;
        if (oneBotEventProcessingCancellation != null)
        {
            await oneBotEventProcessingCancellation.CancelAsync();
            if (oneBotEventProcessingTask != null)
            {
                try
                {
                    await oneBotEventProcessingTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
            oneBotEventProcessingCancellation.Dispose();
            oneBotEventProcessingCancellation = null;
            oneBotEventProcessingTask = null;
        }
        if (oneBotClient != null)
        {
            await oneBotClient.DisposeAsync();
        }
    }
    IOneBotRuntime GetOneBotClient()
    {
        return oneBotClient ??= injectedOneBotRuntime ??
                               new OneBotRuntime(new OneBotClient(Configuration!.Url, Configuration.Token));
    }
    void ITimeIterative.OnUpdate(ref float seconds)
    {
        // 自动推送消息
        foreach (GroupState info in groupStates.Values)
        {
            if ((DateTime.Now - info.LastFlushedTime).TotalSeconds < Configuration!.FlushInterval)
                continue;

            FlushGroupBuffer(info);
        }

        // 自动关闭群聊
        foreach ((long groupId, GroupState info) in groupStates)
        {
            if (info.IsEnabled && (DateTime.Now - info.LastActivityTime).TotalMinutes > Configuration!.AutoCloseMinutes)
            {
                QGroup(groupId, false);
            }
        }

        // 自动重连
        int reconnectSeconds = Configuration!.AutoReconnectSeconds;
        if (reconnectSeconds > 0 && Configuration.BotId != 0)
        {
            if ((DateTime.Now - lastReconnectAttemptTime).TotalSeconds >= reconnectSeconds && IsConnected == false)
            {
                lastReconnectAttemptTime = DateTime.Now;
                _ = TryAutoReconnectAsync();

                async Task TryAutoReconnectAsync()
                {
                    try
                    {
                        logger.LogInformation("[QChatService] 自动重连");
                        await ReconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("[QChatService] 自动重连失败: {Message}", ex.Message);
                    }
                }
            }
        }
    }
    
    void OnEventReceived(OneBotBaseEvent oneBotEvent)
    {
        if (ShouldBypassEventQueueForQuietModeControl(oneBotEvent))
        {
            _ = ProcessOneBotEventAsync(oneBotEvent);
            return;
        }

        if (oneBotEventQueue.Writer.TryWrite(oneBotEvent) == false)
        {
            WriteQChatDiagnostic("event-queue-rejected", "OneBot event could not be queued for processing.", new {
                eventType = oneBotEvent.GetType().Name
            });
        }
    }

    async Task ProcessOneBotEventQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (OneBotBaseEvent oneBotEvent in oneBotEventQueue.Reader.ReadAllAsync(cancellationToken))
            await ProcessOneBotEventAsync(oneBotEvent);
    }

    bool ShouldBypassEventQueueForQuietModeControl(OneBotBaseEvent oneBotEvent)
    {
        if (oneBotEvent is not OneBotMessageEvent messageEvent)
            return false;
        QChatConfig? config = Configuration;
        if (config == null)
            return false;
        bool canControlQuietMode = config.OwnerId != 0 && messageEvent.UserId == config.OwnerId;
        canControlQuietMode = canControlQuietMode || IsQuietModeWakeUser(messageEvent.UserId);
        if (canControlQuietMode == false)
            return false;

        string normalized = NormalizeQuietCommandText(messageEvent.RawMessage, messageEvent.RawMessage);
        return IsQuietSleepCommand(normalized) || IsQuietWakeCommand(normalized);
    }

    async Task ProcessOneBotEventAsync(OneBotBaseEvent oneBotEvent)
    {
        try
        {
            if (oneBotEvent is not OneBotBasicMessageEvent basicMessageEvent)
            {
                WriteQChatDiagnostic("event-ignored", "Ignored non-message OneBot event.", new {
                    eventType = oneBotEvent.GetType().Name
                });
                return;
            }
            WriteQChatDiagnostic("event-received", "Received OneBot basic message event.", new {
                eventType = oneBotEvent.GetType().Name,
                basicMessageEvent.MessageType,
                basicMessageEvent.UserId,
                basicMessageEvent.GroupId,
                basicMessageEvent.SelfId
            });
            if (ignoredGroup.Contains(basicMessageEvent.GroupId.ToString()))
            {
                WriteQChatDiagnostic("event-filtered", "Ignored configured group.", new {
                    basicMessageEvent.GroupId
                });
                return;
            }
            QChatConfig config = Configuration!;
            AgentControlCenterConfig? controlConfig = agentControlCenter?.Configuration;
            QChatSenderRole senderRole = QChatMessageSecurity.Classify(config, basicMessageEvent);
            if (basicMessageEvent.MessageType == OneBotMessageType.Private &&
                QChatMessageSecurity.ShouldAcceptPrivateMessage(config, basicMessageEvent) == false)
            {
                WriteQChatDiagnostic("event-filtered", "Private message rejected by QChat security policy.", new {
                    basicMessageEvent.UserId,
                    senderRole,
                    config.OwnerId,
                    config.AllowPrivateGuestChat
                });
                return;
            }

            if (basicMessageEvent is OneBotPokeEvent pokeEvent)
            {
                string speaker = pokeEvent.GetSpeakerTag();
                string content = BuildPokeContent(config, pokeEvent);
                string formatted = $"{speaker} {content}";
                bool isMentionedOrWoken = pokeEvent.TargetId == config.BotId;
                formatted = BuildFormattedModelInput(
                    config,
                    basicMessageEvent,
                    content,
                    content,
                    formatted,
                    isMentionedOrWoken);
                bool isAwakening = QChatMessageSecurity.ShouldActivateGroup(
                    config,
                    basicMessageEvent,
                    isMentionedOrWoken,
                    controlConfig);
                AgentPermissionRequest permissionRequest = QChatMessageSecurity.BuildPermissionRequest(
                    config,
                    basicMessageEvent,
                    isMentionedOrWoken,
                    content);
                await HandleFormattedMessage(
                    basicMessageEvent,
                    formatted,
                    isAwakening,
                    isMentionedOrWoken,
                    senderRole,
                    permissionRequest);
            }

            if (basicMessageEvent is OneBotMessageEvent messageEvent)
            {
                string speaker = messageEvent.GetSpeakerTag();
                IOneBotRuntime client = GetOneBotClient();
                string content = await BuildReadableMessageForQChatAsync(messageEvent, client);
                if (await TryApplyOwnerQuietCommandAsync(messageEvent, senderRole, content))
                    return;
                if (await TryApplyQuietModeWakeUserCommandAsync(messageEvent, content))
                    return;

                string formatted = $"{speaker}：{content}";
                bool isMentionedOrWoken = messageEvent.GetAtID() == client.BotId ||
                                          groupAwakingWords.Any(word =>
                                              messageEvent.RawMessage.Contains(word, StringComparison.OrdinalIgnoreCase));
                formatted = BuildFormattedModelInput(
                    config,
                    messageEvent,
                    messageEvent.RawMessage,
                    content,
                    formatted,
                    isMentionedOrWoken);
                bool isAwakening = QChatMessageSecurity.ShouldActivateGroup(config, messageEvent, isMentionedOrWoken, controlConfig);
                AgentPermissionRequest permissionRequest = QChatMessageSecurity.BuildPermissionRequest(
                    config,
                    messageEvent,
                    isMentionedOrWoken,
                    messageEvent.RawMessage);
                WriteQChatDiagnostic("message-dispatching", "Dispatching message event to QChat.", new {
                    messageEvent.MessageType,
                    messageEvent.UserId,
                    messageEvent.GroupId,
                    messageEvent.RawMessage,
                    readable = content,
                    client.BotId,
                    atId = messageEvent.GetAtID(),
                    isMentionedOrWoken,
                    isAwakening,
                    senderRole
                });
                await HandleFormattedMessage(
                    messageEvent,
                    formatted,
                    isAwakening,
                    isMentionedOrWoken,
                    senderRole,
                    permissionRequest);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, null);
            WriteQChatDiagnostic("event-error", e.Message, exception: e);
        }
    }

    string BuildFormattedModelInput(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        string rawMessage,
        string readableMessage,
        string formatted,
        bool isMentionedOrWoken)
    {
        string cognition = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            rawMessage,
            readableMessage,
            isMentionedOrWoken,
            IsQuietModeEnabled);
        string address = BuildAddressPrompt(config, messageEvent);
        string secureMessage = QChatMessageSecurity.FormatForModel(config, messageEvent, formatted);
        return $"{cognition}{Environment.NewLine}{address}{Environment.NewLine}{secureMessage}";
    }

    async Task<string> BuildReadableMessageForQChatAsync(OneBotMessageEvent messageEvent, IOneBotRuntime client)
    {
        string content = await messageEvent.GetReadableMessage(client, includeFiles: false);
        return await RegisterManagedFileSegmentsAsync(messageEvent, content, client);
    }

    async Task<string> RegisterManagedFileSegmentsAsync(
        OneBotMessageEvent messageEvent,
        string content,
        IOneBotRuntime client)
    {
        MatchCollection matches = Regex.Matches(content, @"\[CQ:file,.*?\]");
        foreach (Match match in matches)
        {
            string segment = match.Value;
            string fileId = GetCqSegmentValue(segment, "file_id");
            if (string.IsNullOrWhiteSpace(fileId))
                continue;

            string fileName = GetCqSegmentValue(segment, "file");
            long? rawSize = ParseNullableLong(GetCqSegmentValue(segment, "file_size"));
            OneBotFile? fileInfo = messageEvent.GroupId != 0
                ? await client.GetGroupFileUrl(messageEvent.GroupId, fileId)
                : await client.GetPrivateFileUrl(fileId);
            long? resolvedSize = ParseNullableLong(fileInfo?.Size) ?? rawSize;
            string? url = IsHttpUrl(fileInfo?.Url) ? fileInfo!.Url : null;

            QChatManagedFileRecord record = await ManagedFiles.RegisterAsync(new QChatManagedFileRegistration(
                messageEvent.MessageType,
                messageEvent.UserId,
                messageEvent.GroupId,
                fileId,
                string.IsNullOrWhiteSpace(fileName) ? fileInfo?.Name ?? "qq-file" : fileName,
                resolvedSize,
                url));

            WriteQChatDiagnostic("qchat-file-registered", "QQ received file registered in managed workspace without downloading.", new {
                record.Id,
                record.OriginalName,
                record.Size,
                hasUrl = string.IsNullOrWhiteSpace(record.Url) == false,
                record.MessageType,
                record.SenderId,
                record.GroupId
            });

            content = content.Replace(segment, FormatManagedFileForModel(record));
        }

        return content;
    }

    static string FormatManagedFileForModel(QChatManagedFileRecord record)
    {
        string size = record.Size == null ? "unknown" : $"{record.Size}b";
        return $"[QQ file: {record.OriginalName}, managed_file_id={record.Id}, size={size}, status=pending-not-downloaded, note=not downloaded; ask owner before download. tools=<qchat_file_download id=\"{record.Id}\" /> <qchat_file_read id=\"{record.Id}\" /> <qchat_file_delete id=\"{record.Id}\" />]";
    }

    static string FormatManagedFileList(IReadOnlyList<QChatManagedFileRecord> records, int limit)
    {
        QChatManagedFileRecord[] selected = records
            .OrderByDescending(record => record.ReceivedAt)
            .Take(Math.Clamp(limit, 1, 50))
            .ToArray();
        if (selected.Length == 0)
            return "No managed QQ files have been registered.";

        StringBuilder builder = new();
        builder.AppendLine("Managed QQ files:");
        foreach (QChatManagedFileRecord record in selected)
        {
            string size = record.Size == null ? "unknown" : $"{record.Size}b";
            builder.AppendLine($"- id={record.Id}; name={record.OriginalName}; status={record.Status}; size={size}; received={record.ReceivedAt:O}");
        }

        return builder.ToString().TrimEnd();
    }

    static string FormatManagedFileOperation(QChatManagedFileOperationResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine(result.Success ? "Managed QQ file operation succeeded." : "Managed QQ file operation failed.");
        builder.AppendLine(result.Message);
        if (result.Record != null)
        {
            builder.AppendLine($"id={result.Record.Id}");
            builder.AppendLine($"name={result.Record.OriginalName}");
            builder.AppendLine($"status={result.Record.Status}");
            if (string.IsNullOrWhiteSpace(result.Record.LocalPath) == false)
                builder.AppendLine($"local_path={result.Record.LocalPath}");
        }

        if (string.IsNullOrWhiteSpace(result.TextPreview) == false)
        {
            builder.AppendLine();
            builder.AppendLine("Text preview:");
            builder.AppendLine(result.TextPreview);
        }

        return builder.ToString().TrimEnd();
    }

    static string GetCqSegmentValue(string segment, string key)
    {
        Match match = Regex.Match(segment, $@"(?:\[CQ:file,|,){Regex.Escape(key)}=(?<value>[^,\]]*)");
        return match.Success ? match.Groups["value"].Value : "";
    }

    static long? ParseNullableLong(string? value)
    {
        return long.TryParse(value, out long parsed) ? parsed : null;
    }

    static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme is "http" or "https";
    }

    string BuildAddressPrompt(QChatConfig config, OneBotBasicMessageEvent messageEvent)
    {
        string displayName = ResolveDisplayName(messageEvent);
        string preferredAddress = ResolvePreferredAddress(config, messageEvent.UserId, displayName);
        userProfiles.TryGetProfile(messageEvent.UserId, out QChatUserProfile? profile);

        return $"""
                [QQ address]
                user_id={messageEvent.UserId}
                display_name={SanitizeAddressPromptValue(displayName)}
                preferred_address={SanitizeAddressPromptValue(preferredAddress)}
                relationship_label={SanitizeAddressPromptValue(profile?.RelationshipLabel)}
                address_style={SanitizeAddressPromptValue(profile?.AddressStyle)}
                [/QQ address]
                """;
    }

    string BuildPokeContent(QChatConfig config, OneBotPokeEvent pokeEvent)
    {
        string actorDisplayName = ResolveParticipantDisplayName(pokeEvent.GroupId, pokeEvent.UserId);
        string actorAddress = ResolvePreferredAddress(config, pokeEvent.UserId, actorDisplayName);
        string targetAddress = ResolvePokeTargetAddress(config, pokeEvent.GroupId, pokeEvent.TargetId);

        return $"{actorAddress}戳了戳{targetAddress}";
    }

    string ResolvePokeTargetAddress(QChatConfig config, long groupId, long targetId)
    {
        if (config.BotId != 0 && targetId == config.BotId)
            return "我";

        string displayName = ResolveParticipantDisplayName(groupId, targetId);
        return ResolvePreferredAddress(config, targetId, displayName);
    }

    string ResolvePreferredAddress(QChatConfig config, long userId, string? displayName)
    {
        if (config.OwnerId != 0 && userId == config.OwnerId)
            return "主人";
        if (IsQuietModeWakeUser(userId))
            return "妈妈";

        return userProfiles.ResolvePreferredAddress(userId, displayName);
    }

    string ResolveDisplayName(OneBotBasicMessageEvent messageEvent)
    {
        if (messageEvent is not OneBotMessageEvent message)
            return ResolveParticipantDisplayName(messageEvent.GroupId, messageEvent.UserId);
        if (string.IsNullOrWhiteSpace(message.Sender?.Card) == false)
            return message.Sender.Card.Trim();
        if (string.IsNullOrWhiteSpace(message.Sender?.Nickname) == false)
            return message.Sender.Nickname.Trim();

        return ResolveParticipantDisplayName(messageEvent.GroupId, messageEvent.UserId);
    }

    string ResolveParticipantDisplayName(long groupId, long userId)
    {
        if (groupId != 0)
        {
            OneBotGroupMember? member = RelationCache.TryGetMember(groupId, userId);
            if (string.IsNullOrWhiteSpace(member?.DisplayName) == false)
                return member.DisplayName.Trim();
        }

        return "";
    }

    static string SanitizeAddressPromptValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string trimmed = value.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    void OnAIGroupActivity(long groupId)
    {
        GroupState state = GetGroupInfo(groupId);
        state.LastActivityTime = DateTime.Now;
        state.LastBotReplyTime = DateTime.Now;

        if (Configuration!.CloseGroupAfterReply)
            QGroup(groupId, false);
        else if (state.IsEnabled == false)
            QGroup(groupId, true);
    }

    async Task HandleFormattedMessage(
        OneBotBasicMessageEvent messageEvent,
        string formatted,
        bool isAwakening,
        bool isMentionedOrWoken,
        QChatSenderRole senderRole,
        AgentPermissionRequest permissionRequest)
    {
        if (ShouldSuppressForQuietMode(messageEvent, senderRole, isMentionedOrWoken))
            return;

        if (messageEvent.MessageType == OneBotMessageType.Private)//私聊消息
        {
            if (senderRole == QChatSenderRole.Owner || Configuration!.AllowPrivateGuestChat)
            {
                WriteQChatDiagnostic("private-dispatch", "Private message accepted for model dispatch.", new {
                    messageEvent.UserId,
                    senderRole
                });
                using IDisposable _ = PushPermissionRequest(permissionRequest, TimeSpan.FromMinutes(5));
                await DispatchInboundChatAsync(new QChatInboundMessage(
                    messageEvent.MessageType,
                    messageEvent.UserId,
                    messageEvent.UserId,
                    formatted,
                    isAwakening,
                    senderRole,
                    permissionRequest));
            }
        }
        else//群聊消息
        {
            if (senderRole != QChatSenderRole.Owner && Configuration!.AllowGroupMemberChat == false)
            {
                WriteQChatDiagnostic("group-filtered", "Group member chat is disabled.", new {
                    messageEvent.GroupId,
                    messageEvent.UserId,
                    senderRole
                });
                return;
            }

            GroupState state = GetGroupInfo(messageEvent.GroupId);
            state.Tag = messageEvent.GetGroupTag();

            if (isAwakening)
                state.LastAwakeningTime = DateTime.Now;

            if (isAwakening && state.IsEnabled == false)
                QGroup(messageEvent.GroupId, true);

            if (state.IsEnabled)//群聊已激活时（直接接收）
            {
                if (ShouldSkipPassiveGroupMessageOutsideAllowedScope(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;

                if (QChatMessageSecurity.ShouldAcceptGroupMessage(
                        Configuration!,
                        messageEvent,
                        isMentionedOrWoken,
                        state.IsEnabled,
                        agentControlCenter?.Configuration) == false)
                {
                    WriteQChatDiagnostic("group-filtered", "Group message rejected by Agent Control Center group listening policy.", new {
                        messageEvent.GroupId,
                        messageEvent.UserId,
                        senderRole,
                        isMentionedOrWoken,
                        state.IsEnabled,
                        agentControlCenter?.Configuration?.AllowMentionWakeup,
                        agentControlCenter?.Configuration?.AllowPassiveGroupListening
                    });
                    return;
                }

                if (ShouldSkipLowInformationPassiveGroupMessage(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;

                if (ShouldThrottlePassiveGroupMessage(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;

                if (ShouldSkipActiveGroupMessageBySoftAttention(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;

                RecordAcceptedGroupDecision(state, messageEvent, senderRole, isMentionedOrWoken, isAwakening);
                BufferGroupMessage(
                    state,
                    formatted,
                    senderRole == QChatSenderRole.Owner && Configuration!.OwnerPriorityMode,
                    permissionRequest);
                WriteQChatDiagnostic("group-buffered", "Group message buffered for model dispatch.", new {
                    state.GroupId,
                    state.IsEnabled,
                    bufferCount = state.MessageBuffer.Count,
                    isAwakening,
                    senderRole
                });
                if (isAwakening)
                    await FlushGroupBufferAsync(state);
            }
            else if (QChatMessageSecurity.ShouldAllowProactiveGroupChat(Configuration!, messageEvent, agentControlCenter?.Configuration))//群聊未激活时（概率接收）
            {
                if (ShouldSkipLowInformationPassiveGroupMessage(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;

                if (ShouldThrottlePassiveGroupMessage(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;

                if (ShouldSkipPassiveGroupMessageBySocialAttention(messageEvent, senderRole, isMentionedOrWoken))
                    return;

                RecordAcceptedGroupDecision(state, messageEvent, senderRole, isMentionedOrWoken, isAwakening);
                BufferGroupMessage(state, formatted, permissionRequest: permissionRequest);
                state.LastFlushedTime = DateTime.Now;
                WriteQChatDiagnostic("group-buffered-proactive", "Group message buffered by proactive probability.", new {
                    state.GroupId,
                    bufferCount = state.MessageBuffer.Count,
                    Configuration!.ProactiveChatProbability,
                    EffectiveProactiveChatProbability = QChatMessageSecurity.GetProactiveChatProbability(Configuration!, agentControlCenter?.Configuration),
                    SocialAttentionProbability = GetSocialAttentionAdjustedProactiveProbability(messageEvent, isMentionedOrWoken)
                });
            }
            else
            {
                if (ShouldSkipPassiveGroupMessageOutsideAllowedScope(state, messageEvent, senderRole, isMentionedOrWoken))
                    return;
            }
        }
    }

    bool ShouldSkipPassiveGroupMessageOutsideAllowedScope(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        if (messageEvent is not OneBotMessageEvent groupMessage)
            return false;
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;
        if (senderRole == QChatSenderRole.Owner || isMentionedOrWoken)
            return false;
        if (QChatMessageSecurity.IsGroupInAllowedScope(Configuration!, messageEvent.GroupId))
            return false;

        RecordGroupDecision(
            state,
            messageEvent,
            senderRole,
            isMentionedOrWoken,
            "suppressed",
            "scope",
            groupMessage.RawMessage,
            0f);
        WriteQChatDiagnostic("group-passive-scope-skipped", "Passive group message skipped because the group is outside the QQ allowlist.", new {
            state.GroupId,
            messageEvent.UserId,
            senderRole,
            isMentionedOrWoken,
            Configuration!.AllowedGroupIds,
            groupMessage.RawMessage
        });
        return true;
    }

    bool ShouldSkipPassiveGroupMessageBySocialAttention(
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        if (messageEvent is not OneBotMessageEvent groupMessage)
            return false;
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;
        if (senderRole == QChatSenderRole.Owner || isMentionedOrWoken)
            return false;

        float probability = GetSocialAttentionAdjustedProactiveProbability(groupMessage, isMentionedOrWoken);
        if (Random.Shared.NextSingle() < probability)
            return false;

        GroupState state = GetGroupInfo(messageEvent.GroupId);
        RecordGroupDecision(
            state,
            messageEvent,
            senderRole,
            isMentionedOrWoken,
            "suppressed",
            "social-attention",
            groupMessage.RawMessage,
            probability);
        WriteQChatDiagnostic("group-passive-social-attention-skipped", "Passive group message skipped by social attention gating.", new {
            messageEvent.GroupId,
            messageEvent.UserId,
            senderRole,
            isMentionedOrWoken,
            probability,
            groupMessage.RawMessage
        });
        return true;
    }

    void RecordGroupDecision(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken,
        string decision,
        string reason,
        string rawMessage,
        float? socialAttentionProbability = null)
    {
        QChatGroupDecisionSnapshot snapshot = new(
            DateTimeOffset.Now,
            messageEvent.GroupId,
            messageEvent.UserId,
            senderRole,
            isMentionedOrWoken,
            state.IsEnabled,
            decision,
            reason,
            rawMessage,
            socialAttentionProbability,
            GetPassiveGroupCooldownRemainingSeconds(state, messageEvent, senderRole, isMentionedOrWoken),
            GetActiveSoftAttentionRemainingSeconds(state));

        lock (groupDecisionGate)
        {
            recentGroupDecisions.Add(snapshot);
            int overflow = recentGroupDecisions.Count - MaxRecentGroupDecisions;
            if (overflow > 0)
                recentGroupDecisions.RemoveRange(0, overflow);
        }

        WriteQChatDiagnostic("group-decision", "Recorded group reply decision.", new {
            state.GroupId,
            messageEvent.UserId,
            SenderRole = senderRole.ToString(),
            IsMentionedOrWoken = isMentionedOrWoken,
            IsGroupEnabled = state.IsEnabled,
            Decision = decision,
            Reason = reason,
            RawMessage = rawMessage,
            SocialAttentionProbability = socialAttentionProbability,
            snapshot.CooldownRemainingSeconds,
            snapshot.ActiveSoftAttentionRemainingSeconds
        });
    }

    void RecordAcceptedGroupDecision(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken,
        bool isAwakening)
    {
        string rawMessage = messageEvent is OneBotMessageEvent groupMessage
            ? groupMessage.RawMessage
            : "";
        string reason = senderRole == QChatSenderRole.Owner && Configuration?.OwnerPriorityMode == true
            ? "owner-priority"
            : isMentionedOrWoken || isAwakening
                ? "mention-or-wake"
                : state.IsEnabled
                    ? "active-window"
                    : "social-attention";

        RecordGroupDecision(
            state,
            messageEvent,
            senderRole,
            isMentionedOrWoken,
            "accepted",
            reason,
            rawMessage,
            isMentionedOrWoken || senderRole == QChatSenderRole.Owner
                ? 1f
                : GetSocialAttentionAdjustedProactiveProbability(messageEvent, isMentionedOrWoken));
    }

    int GetActiveSoftAttentionRemainingSeconds(GroupState state)
    {
        int softAttentionSeconds = Math.Max(0, Configuration?.ActiveGroupSoftAttentionSeconds ?? 0);
        if (softAttentionSeconds <= 0 || state.LastAwakeningTime == default)
            return 0;

        double elapsedSeconds = (DateTime.Now - state.LastAwakeningTime).TotalSeconds;
        return Math.Max(0, (int)Math.Ceiling(softAttentionSeconds - elapsedSeconds));
    }

    int GetPassiveGroupCooldownRemainingSeconds(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        int cooldownSeconds = GetEffectivePassiveGroupReplyCooldownSeconds();
        if (cooldownSeconds <= 0 || messageEvent.MessageType != OneBotMessageType.Group)
            return 0;
        if (senderRole == QChatSenderRole.Owner || isMentionedOrWoken || state.LastBotReplyTime == default)
            return 0;

        double elapsedSeconds = (DateTime.Now - state.LastBotReplyTime).TotalSeconds;
        return Math.Max(0, (int)Math.Ceiling(cooldownSeconds - elapsedSeconds));
    }

    bool ShouldSkipActiveGroupMessageBySoftAttention(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        if (messageEvent is not OneBotMessageEvent groupMessage)
            return false;
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;
        if (senderRole == QChatSenderRole.Owner || isMentionedOrWoken)
            return false;

        int softAttentionSeconds = Math.Max(0, Configuration?.ActiveGroupSoftAttentionSeconds ?? 0);
        if (softAttentionSeconds <= 0)
            return false;
        if (state.LastAwakeningTime == default)
            return false;
        if ((DateTime.Now - state.LastAwakeningTime).TotalSeconds <= softAttentionSeconds)
            return false;

        float probability = GetSocialAttentionAdjustedProactiveProbability(groupMessage, isMentionedOrWoken);
        if (Random.Shared.NextSingle() < probability)
            return false;

        RecordGroupDecision(
            state,
            messageEvent,
            senderRole,
            isMentionedOrWoken,
            "suppressed",
            "active-soft-attention-expired",
            groupMessage.RawMessage,
            probability);
        WriteQChatDiagnostic("group-active-soft-attention-skipped", "Active group message skipped because the wakeup attention window expired.", new {
            state.GroupId,
            messageEvent.UserId,
            senderRole,
            isMentionedOrWoken,
            softAttentionSeconds,
            probability,
            groupMessage.RawMessage
        });
        return true;
    }

    float GetSocialAttentionAdjustedProactiveProbability(OneBotBasicMessageEvent messageEvent, bool isMentionedOrWoken)
    {
        string rawMessage = messageEvent is OneBotMessageEvent oneBotMessage
            ? oneBotMessage.RawMessage
            : "";

        return QChatMessageSecurity.GetSocialAttentionAdjustedProactiveProbability(
            Configuration!,
            messageEvent,
            isMentionedOrWoken,
            rawMessage,
            agentControlCenter?.Configuration,
            BuildEmotionSocialDesireFactors());
    }

    QChatSocialDesireFactors? BuildEmotionSocialDesireFactors()
    {
        if (emotionEngine == null)
            return null;

        return QChatMessageSecurity.BuildSocialDesireFromEmotion(
            emotionEngine.RawPleasure,
            emotionEngine.RawArousal,
            emotionEngine.RawDominance,
            IsQuietModeEnabled);
    }

    bool ShouldSkipLowInformationPassiveGroupMessage(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        if (messageEvent is not OneBotMessageEvent groupMessage)
            return false;
        if (Configuration?.SuppressLowInformationPassiveGroupMessages != true)
            return false;
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;
        if (isMentionedOrWoken)
            return false;
        if (IsMediaOnlyPassiveGroupMessage(groupMessage.RawMessage)
            && Random.Shared.NextSingle() < QChatMessageSecurity.GetMediaOnlyPassiveGroupReplyProbability(Configuration))
        {
            WriteQChatDiagnostic("group-passive-media-chance-allowed", "Passive media-only group message allowed by media reply chance.", new {
                messageEvent.GroupId,
                messageEvent.UserId,
                senderRole,
                isMentionedOrWoken,
                Configuration.MediaOnlyPassiveGroupReplyProbability,
                groupMessage.RawMessage
            });
            return false;
        }

        if (IsLowInformationPassiveGroupMessage(groupMessage.RawMessage) == false)
            return false;

        RecordGroupDecision(
            state,
            messageEvent,
            senderRole,
            isMentionedOrWoken,
            "suppressed",
            "low-information",
            groupMessage.RawMessage);
        WriteQChatDiagnostic("group-passive-low-information-skipped", "Passive group message skipped because it has too little conversational content.", new {
            messageEvent.GroupId,
            messageEvent.UserId,
            senderRole,
            isMentionedOrWoken,
            groupMessage.RawMessage
        });
        return true;
    }

    static bool IsLowInformationPassiveGroupMessage(string? rawMessage)
    {
        string raw = rawMessage ?? "";
        string plain = OneBotSegment.GetPlainText(raw).Trim();
        if (string.IsNullOrWhiteSpace(plain))
            return ContainsLowInformationCqSegment(raw);

        string compact = CompactPassiveText(plain);
        if (compact.Length == 0)
            return ContainsLowInformationCqSegment(raw);

        return compact is "嗯" or "哦" or "喔" or "啊" or "诶" or "哈" or "哈哈" or "hhh" or "www" or "6" or "草" or "好" or "行" or "ok";
    }

    static bool ContainsLowInformationCqSegment(string raw)
    {
        return raw.Contains("[CQ:image", StringComparison.OrdinalIgnoreCase)
               || raw.Contains("[CQ:face", StringComparison.OrdinalIgnoreCase)
               || raw.Contains("[CQ:mface", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsMediaOnlyPassiveGroupMessage(string? rawMessage)
    {
        string raw = rawMessage ?? "";
        if (ContainsLowInformationCqSegment(raw) == false)
            return false;

        return string.IsNullOrWhiteSpace(OneBotSegment.GetPlainText(raw));
    }

    static string CompactPassiveText(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;
            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    bool ShouldThrottlePassiveGroupMessage(
        GroupState state,
        OneBotBasicMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        int cooldownSeconds = GetEffectivePassiveGroupReplyCooldownSeconds();
        if (cooldownSeconds <= 0)
            return false;
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;
        if (senderRole == QChatSenderRole.Owner || isMentionedOrWoken)
            return false;
        if (state.LastBotReplyTime == default)
            return false;

        double elapsedSeconds = (DateTime.Now - state.LastBotReplyTime).TotalSeconds;
        if (elapsedSeconds >= cooldownSeconds)
            return false;

        string rawMessage = messageEvent is OneBotMessageEvent groupMessage
            ? groupMessage.RawMessage
            : "";
        RecordGroupDecision(
            state,
            messageEvent,
            senderRole,
            isMentionedOrWoken,
            "suppressed",
            "cooldown",
            rawMessage);
        WriteQChatDiagnostic("group-passive-throttled", "Passive group message skipped because the bot replied recently.", new {
            state.GroupId,
            messageEvent.UserId,
            senderRole,
            isMentionedOrWoken,
            elapsedSeconds,
            cooldownSeconds
        });
        return true;
    }

    int GetEffectivePassiveGroupReplyCooldownSeconds()
    {
        int configuredSeconds = Math.Max(0, Configuration?.PassiveGroupReplyCooldownSeconds ?? 0);
        int controlIntensity = agentControlCenter?.Configuration?.ProactiveChatIntensity ?? 2;
        int controlFloor = controlIntensity switch
        {
            <= 0 => 300,
            1 => 180,
            2 => 90,
            _ => 0
        };

        return Math.Max(configuredSeconds, controlFloor);
    }

    async Task<bool> TryApplyOwnerQuietCommandAsync(OneBotMessageEvent messageEvent, QChatSenderRole senderRole, string readable)
    {
        if (senderRole != QChatSenderRole.Owner)
            return false;

        string normalized = NormalizeQuietCommandText(messageEvent.RawMessage, readable);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (IsQuietWakeCommand(normalized))
        {
            SetQuietMode(false, messageEvent, "owner-wake-command");
            return true;
        }

        if (IsQuietSleepCommand(normalized))
        {
            SetQuietMode(true, messageEvent, "owner-sleep-command");
            await SendQuietModeAcknowledgementAsync(messageEvent);
            return true;
        }

        return false;
    }

    async Task SendQuietModeAcknowledgementAsync(OneBotMessageEvent messageEvent)
    {
        string targetType = messageEvent.MessageType == OneBotMessageType.Group ? "group" : "private";
        long targetId = messageEvent.MessageType == OneBotMessageType.Group
            ? messageEvent.GroupId
            : messageEvent.UserId;

        if (targetId <= 0)
            return;

        string acknowledgement = await BuildQuietModeAcknowledgementAsync(messageEvent, enabled: true);
        await SendChatAsyncCore(
            targetType,
            targetId,
            acknowledgement,
            bypassQuietMode: true);
    }

    bool ShouldSuppressOutgoingForQuietMode(OneBotMessageType type, long targetId, string source)
    {
        if (IsQuietModeEnabled == false)
            return false;

        WriteQChatDiagnostic("qchat-quiet-outgoing-suppressed", "QQ outbound message suppressed because owner quiet mode is enabled.", new {
            type,
            targetId,
            source,
            QuietModeReason
        });
        return true;
    }

    bool ShouldSuppressForQuietMode(OneBotBasicMessageEvent messageEvent, QChatSenderRole senderRole, bool isMentionedOrWoken)
    {
        if (IsQuietModeEnabled == false)
            return false;

        WriteQChatDiagnostic("qchat-quiet-message-suppressed", "QQ inbound message suppressed because owner quiet mode is enabled.", new {
            messageEvent.MessageType,
            messageEvent.UserId,
            messageEvent.GroupId,
            senderRole,
            isMentionedOrWoken
        });
        return true;
    }

    async Task<bool> TryApplyQuietModeWakeUserCommandAsync(OneBotMessageEvent messageEvent, string readable)
    {
        if (IsQuietModeEnabled == false)
            return false;
        if (IsQuietModeWakeUser(messageEvent.UserId) == false)
            return false;

        string normalized = NormalizeQuietCommandText(messageEvent.RawMessage, readable);
        if (string.IsNullOrWhiteSpace(normalized) || IsQuietWakeCommand(normalized) == false)
            return false;

        SetQuietMode(false, messageEvent, "trusted-wake-user-command");
        await SendQuietModeWakeAcknowledgementAsync(messageEvent);
        return true;
    }

    async Task SendQuietModeWakeAcknowledgementAsync(OneBotMessageEvent messageEvent)
    {
        string targetType = messageEvent.MessageType == OneBotMessageType.Group ? "group" : "private";
        long targetId = messageEvent.MessageType == OneBotMessageType.Group
            ? messageEvent.GroupId
            : messageEvent.UserId;

        if (targetId <= 0)
            return;

        string acknowledgement = await BuildQuietModeAcknowledgementAsync(messageEvent, enabled: false);
        string message = messageEvent.MessageType == OneBotMessageType.Group
            ? $"{GetNaturalGroupAddress(messageEvent, QChatMessageSecurity.Classify(Configuration!, messageEvent))}，{acknowledgement}"
            : acknowledgement;

        await SendChatAsyncCore(targetType, targetId, message, bypassQuietMode: true);
    }

    async Task<string> BuildQuietModeAcknowledgementAsync(OneBotMessageEvent messageEvent, bool enabled)
    {
        string fallback = enabled
            ? QuietModeSleepFallbackAcknowledgement
            : QuietModeWakeFallbackAcknowledgement;

        try
        {
            string prompt = BuildQuietModeAcknowledgementPrompt(messageEvent, enabled);
            string generated = await GenerateQuietModeAcknowledgementAsync(prompt);
            return SanitizeQuietModeAcknowledgement(generated, fallback);
        }
        catch (Exception ex)
        {
            WriteQChatDiagnostic("qchat-quiet-ack-generation-failed", "Quiet mode acknowledgement generation failed; neutral fallback will be used.", new {
                messageEvent.MessageType,
                messageEvent.UserId,
                messageEvent.GroupId,
                enabled
            }, ex);
            return fallback;
        }
    }

    protected virtual Task<string> GenerateQuietModeAcknowledgementAsync(string prompt)
    {
        if (ChatBot == null)
            return Task.FromResult("");

        return ChatBot.ChatAsync(ChatTextFilter(prompt));
    }

    string BuildQuietModeAcknowledgementPrompt(OneBotMessageEvent messageEvent, bool enabled)
    {
        string senderRole = QChatMessageSecurity.Classify(Configuration!, messageEvent).ToString();
        string conversationType = messageEvent.MessageType == OneBotMessageType.Group ? "群聊" : "私聊";
        string readableMessage = OneBotSegment.GetPlainText(messageEvent.RawMessage);
        string commandMeaning = enabled
            ? "对方让你进入睡眠/安静/不打扰状态"
            : "对方让你醒来/恢复回应";
        string responseIntent = enabled
            ? "表示你已经安静下来，先不打扰对方"
            : "表示你已经恢复注意力，正在这里";

        return $"""
                当前 QQ {conversationType}收到一条安静模式控制消息。
                发送者角色: {senderRole}
                原始消息: {readableMessage}
                控制含义: {commandMeaning}

                请以当前角色自己的人设，回复一句自然的 QQ 确认语。
                要求:
                - 只输出确认语，不要解释，不要 XML，不要工具标签。
                - {responseIntent}。
                - 按当前关系和称呼自然回应；如果是主人，保持温柔。
                - 不要使用“咪绪”“喵”“猫娘”，不要照搬固定模板。
                - 20 字左右，最多 40 字。
                """;
    }

    string GetNaturalGroupAddress(OneBotMessageEvent messageEvent, QChatSenderRole senderRole)
    {
        if (senderRole == QChatSenderRole.Owner)
            return "主人";

        if (IsQuietModeWakeUser(messageEvent.UserId))
            return "妈妈";

        string? displayName = messageEvent.Sender?.Card;
        if (IsUsableAddressName(displayName))
            return displayName!.Trim();

        displayName = messageEvent.Sender?.Nickname;
        if (IsUsableAddressName(displayName))
            return displayName!.Trim();

        return "你";
    }

    static bool IsUsableAddressName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        if (trimmed.Length > 16)
            return false;
        if (trimmed.All(char.IsDigit))
            return false;

        return true;
    }

    static string SanitizeQuietModeAcknowledgement(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string normalized = value.Trim();
        while (normalized.Length >= 2 && IsWrappingPair(normalized[0], normalized[^1]))
            normalized = normalized[1..^1].Trim();

        string[] lines = normalized
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        normalized = lines.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return fallback;

        if (normalized.Contains('<', StringComparison.Ordinal) ||
            normalized.Contains('>', StringComparison.Ordinal) ||
            normalized.Contains("```", StringComparison.Ordinal))
        {
            return fallback;
        }

        string compact = normalized
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        if (compact.Contains("咪绪", StringComparison.Ordinal) ||
            compact.Contains("喵", StringComparison.Ordinal) ||
            compact.Contains("猫娘", StringComparison.Ordinal) ||
            compact.Contains("主人真会使唤人", StringComparison.Ordinal) ||
            compact.Contains("不回复", StringComparison.Ordinal) ||
            compact.Contains("不回覆", StringComparison.Ordinal) ||
            compact.Contains("无需回复", StringComparison.Ordinal) ||
            compact.Contains("不用回复", StringComparison.Ordinal) ||
            compact.Contains("不插话", StringComparison.Ordinal) ||
            compact.Contains("不插話", StringComparison.Ordinal))
        {
            return fallback;
        }

        const int MaxAcknowledgementLength = 60;
        return normalized.Length <= MaxAcknowledgementLength
            ? normalized
            : normalized[..MaxAcknowledgementLength].TrimEnd();
    }

    bool IsQuietModeWakeUser(long userId)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(Configuration?.QuietModeWakeUserIds))
            return false;

        return Configuration.QuietModeWakeUserIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(id => long.TryParse(id, out long allowedId) && allowedId == userId);
    }

    void SetQuietMode(bool enabled, OneBotBasicMessageEvent messageEvent, string reason)
    {
        SetQuietModeCore(enabled, reason, new {
            messageEvent.MessageType,
            messageEvent.UserId,
            messageEvent.GroupId,
            reason
        });
    }

    void SetQuietMode(bool enabled, string reason)
    {
        SetQuietModeCore(enabled, reason, new {
            source = "direct-control",
            reason
        });
    }

    bool TryAuthorizeQuietModeToolControl(bool enabled, string? reason)
    {
        QChatReplySession? replySession = currentReplySession.Value;
        if (replySession == null)
            return true;

        if (replySession.SenderRole == QChatSenderRole.Owner)
            return true;

        WriteQChatDiagnostic(
            "qchat-quiet-mode-control-denied",
            "QQ quiet mode tool control denied because current QQ sender is not owner.",
            new {
                enabled,
                reason,
                replySession.MessageType,
                replySession.TargetId,
                replySession.SenderId,
                replySession.SenderRole
            });
        return false;
    }

    bool TryAuthorizeOwnerToolControl(string toolName)
    {
        QChatReplySession? replySession = currentReplySession.Value;
        if (replySession == null)
            return true;

        if (replySession.SenderRole == QChatSenderRole.Owner)
            return true;

        WriteQChatDiagnostic(
            "qchat-owner-tool-control-denied",
            "QQ owner-only tool control denied because current QQ sender is not owner.",
            new {
                toolName,
                replySession.MessageType,
                replySession.TargetId,
                replySession.SenderId,
                replySession.SenderRole
            });
        return false;
    }

    void SetQuietModeCore(bool enabled, string reason, object data)
    {
        IsQuietModeEnabled = enabled;
        QuietModeChangedAt = DateTimeOffset.UtcNow;
        QuietModeReason = reason;
        emotionEngine?.ApplyEventType(enabled ? EmotionEventType.FallAsleep : EmotionEventType.WakeUp);
        if (Configuration != null)
        {
            Configuration.PersistedQuietModeEnabled = enabled;
            Configuration.PersistedQuietModeChangedAt = QuietModeChangedAt;
            Configuration.PersistedQuietModeReason = reason;
        }

        WriteQChatDiagnostic(
            enabled ? "qchat-quiet-mode-enabled" : "qchat-quiet-mode-disabled",
            enabled ? "Owner enabled QQ quiet mode." : "Owner disabled QQ quiet mode.",
            data);
    }

    void RestoreQuietModeFromConfiguration()
    {
        if (Configuration?.PersistQuietModeAcrossRestart != true)
            return;

        IsQuietModeEnabled = Configuration.PersistedQuietModeEnabled;
        QuietModeChangedAt = Configuration.PersistedQuietModeChangedAt;
        QuietModeReason = string.IsNullOrWhiteSpace(Configuration.PersistedQuietModeReason)
            ? "configuration-restore"
            : Configuration.PersistedQuietModeReason;

        WriteQChatDiagnostic(
            IsQuietModeEnabled ? "qchat-quiet-mode-restored" : "qchat-quiet-mode-restore-skipped",
            IsQuietModeEnabled ? "QQ quiet mode restored from configuration." : "QQ quiet mode persistence is enabled but saved state is disabled.",
            new {
                IsQuietModeEnabled,
                QuietModeChangedAt,
                QuietModeReason
            });
    }

    string FormatQuietModeStateSuffix()
    {
        if (IsQuietModeEnabled == false)
            return "";

        StringBuilder builder = new();
        if (string.IsNullOrWhiteSpace(QuietModeReason) == false)
            builder.Append($"; reason: {QuietModeReason}");
        if (QuietModeChangedAt != null)
            builder.Append($"; changed at: {QuietModeChangedAt:O}");

        return builder.ToString();
    }

    static string NormalizeQuietCommandText(string? raw, string? readable)
    {
        string text = $"{OneBotSegment.GetPlainText(raw ?? string.Empty)} {OneBotSegment.GetPlainText(readable ?? string.Empty)}";
        StringBuilder builder = new(text.Length);
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    static bool IsQuietSleepCommand(string normalized)
    {
        return normalized.Contains("睡觉", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("去睡觉", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("休息", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("安静", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("别说话", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("不要说话", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("保持安静", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("sleep", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("quiet", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("silent", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsQuietWakeCommand(string normalized)
    {
        return normalized.Contains("醒醒", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("起床", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("回来", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("可以说话了", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("说话吧", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("wake", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("resume", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("talk", StringComparison.OrdinalIgnoreCase);
    }

    void BufferGroupMessage(
        GroupState state,
        string formatted,
        bool highPriority = false,
        AgentPermissionRequest? permissionRequest = null)
    {
        if (highPriority)
            state.MessageBuffer.Insert(0, formatted);
        else
            state.MessageBuffer.Add(formatted);
        if (permissionRequest != null)
            state.PermissionRequest = ChooseStrongerPermissionRequest(state.PermissionRequest, permissionRequest);
        if (Configuration!.DebounceEnabled)
            state.LastFlushedTime = DateTime.Now;
        if (Configuration!.MaxBufferMessages != -1 && state.MessageBuffer.Count > Configuration.MaxBufferMessages)
            FlushGroupBuffer(state);
    }

    public void FlushGroupBuffer(GroupState state)
    {
        _ = FlushGroupBufferAsync(state);
    }

    async Task FlushGroupBufferAsync(GroupState state)
    {
        state.LastFlushedTime = DateTime.Now;

        if (state.MessageBuffer.Count == 0)
        {
            if (ShouldWriteEmptyGroupFlushDiagnostic(state.GroupId))
            {
                WriteQChatDiagnostic("group-flush-skipped", "Group flush skipped because buffer is empty.", new {
                    state.GroupId
                });
            }
            return;
        }

        string cachedMessage =
            $"""

             > 以下是群 {state.Tag} 的消息
             {string.Join("\n", state.MessageBuffer)}
             """;

        state.MessageBuffer.Clear();
        AgentPermissionRequest? permissionRequest = state.PermissionRequest;
        state.PermissionRequest = null;
        WriteQChatDiagnostic("group-flush-dispatching", "Dispatching buffered group message to model.", new {
            state.GroupId,
            state.Tag,
            permissionRequest?.ActorUserId,
            permissionRequest?.IsMentioned
        });
        await DispatchBufferedGroupMessageAsync(state, cachedMessage, permissionRequest);
    }

    public void QGroup(long groupId, bool enabled)
    {
        GroupState state = GetGroupInfo(groupId);
        state.IsEnabled = enabled;
        if (enabled)
        {
            state.LastActivityTime = DateTime.Now;
            state.LastAwakeningTime = DateTime.Now;
            state.LastFlushedTime = DateTime.Now;
        }
        else
        {
            state.MessageBuffer.Clear();
            state.PermissionRequest = null;
        }
    }

    GroupState GetGroupInfo(long groupId)
    {
        if (groupStates.TryGetValue(groupId, out GroupState? groupInfo) == false)
        {
            groupInfo = new GroupState {
                GroupId = groupId,
                Tag = groupId.ToString()
            };
            groupStates[groupId] = groupInfo;
        }

        return groupInfo;
    }

    async Task DispatchBufferedGroupMessageAsync(
        GroupState state,
        string cachedMessage,
        AgentPermissionRequest? permissionRequest)
    {
        try
        {
            AgentPermissionRequest request = permissionRequest ?? new AgentPermissionRequest(
                ActorUserId: null,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "qq.message");
            using IDisposable _ = PushPermissionRequest(request, TimeSpan.FromMinutes(5));
            await DispatchInboundChatAsync(new QChatInboundMessage(
                OneBotMessageType.Group,
                state.GroupId,
                request.ActorUserId ?? 0,
                cachedMessage,
                request.IsMentioned,
                request.ActorUserId == Configuration?.OwnerId ? QChatSenderRole.Owner : QChatSenderRole.GroupMember,
                request));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch buffered QQ group message.");
        }
    }

    Task DispatchInboundChatAsync(QChatInboundMessage message)
    {
        return DispatchInboundChatCoreAsync(message);
    }

    async Task DispatchInboundChatCoreAsync(QChatInboundMessage message)
    {
        await inboundModelDispatchGate.WaitAsync();
        QChatReplySession? previousSession = currentReplySession.Value;
        QChatReplySession replySession = new(
            message.MessageType,
            message.TargetId,
            message.SenderId,
            message.SenderRole,
            message.PermissionRequest);
        currentReplySession.Value = replySession;
        RegisterActiveReplySession(replySession);
        WriteQChatDiagnostic("model-dispatch-start", "Dispatching inbound QQ message to model.", new {
            message.MessageType,
            message.TargetId,
            message.SenderId,
            message.IsAwakening,
            message.SenderRole
        });

        try
        {
            long outboundBefore = Volatile.Read(ref outboundMessageVersion);
            string modelResponse;
            if (InboundChatDispatcher != null)
            {
                await InboundChatDispatcher(message);
                modelResponse = "";
            }
            else
            {
                modelResponse = await DispatchToModelAsync(message);
            }

            if (Volatile.Read(ref outboundMessageVersion) == outboundBefore &&
                TryBuildPlainTextFallbackResponse(modelResponse, out string fallbackMessage))
            {
                await SendTextOrMediaMessageAsync(message.MessageType, message.TargetId, fallbackMessage, streamText: true);
                WriteQChatDiagnostic("plain-fallback-sent", "Model returned plain text without using qchat; sent it to the current QQ session.", new {
                    message.MessageType,
                    message.TargetId,
                    fallbackMessage
                });
            }
            WriteQChatDiagnostic("model-dispatch-completed", "Model dispatch completed.", new {
                message.MessageType,
                message.TargetId
            });
        }
        catch (Exception ex)
        {
            WriteQChatDiagnostic("model-dispatch-failed", ex.Message, new {
                message.MessageType,
                message.TargetId
            }, ex);
            throw;
        }
        finally
        {
            UnregisterActiveReplySession(replySession);
            currentReplySession.Value = previousSession;
            inboundModelDispatchGate.Release();
        }
    }

    protected virtual Task<string> DispatchToModelAsync(QChatInboundMessage message)
    {
        return ChatBot.ChatAsync(ChatTextFilter(message.Formatted));
    }

    async Task PublishQChatToolResultAsync(string message, string source)
    {
        if (ChatBot != null)
            Poke(message);

        await SendCurrentReplySessionToolResultAsync(message);
        WriteQChatDiagnostic("qchat-tool-result-published", "QChat tool result was published to the model and current QQ session when available.", new {
            source
        });
    }

    async Task SendCurrentReplySessionToolResultAsync(string message)
    {
        QChatReplySession? replySession = GetCurrentReplySessionForGuard();
        if (replySession == null)
            return;

        string outgoing = message.Trim();
        if (string.IsNullOrEmpty(outgoing) || IsInternalNoReplyStatus(outgoing))
            return;

        if (TryEnsureQChatReplyTargetAllowed(replySession.MessageType, replySession.TargetId, "relation-cache-tool-result") == false)
            return;
        if (ShouldSuppressOutgoingForQuietMode(replySession.MessageType, replySession.TargetId, "relation-cache-tool-result"))
            return;

        try
        {
            await SendTextOrMediaMessageAsync(replySession.MessageType, replySession.TargetId, outgoing, streamText: true);
            WriteQChatDiagnostic("qchat-tool-result-sent", "QChat read-only tool result was sent to the current QQ session.", new {
                replySession.MessageType,
                replySession.TargetId,
                message = outgoing
            });
        }
        catch (Exception ex)
        {
            WriteQChatDiagnostic("qchat-send-failed", ex.Message, new {
                type = replySession.MessageType,
                targetId = replySession.TargetId,
                source = "relation-cache-tool-result"
            }, ex);
            TryPokeSendFailure(ex.Message);
        }
    }

    string FormatAllowlistStatus()
    {
        return $"""
                QQ allowlist status
                - AllowedGroupIds: {FormatAllowlistIds(Configuration?.AllowedGroupIds)}
                - AllowedPrivateUserIds: {FormatAllowlistIds(Configuration?.AllowedPrivateUserIds)}
                - AllowMentionOutsideAllowedGroups: {FormatBool(Configuration?.AllowMentionOutsideAllowedGroups)}
                - AllowGroupMemberChat: {FormatBool(Configuration?.AllowGroupMemberChat)}
                - AllowGroupMemberMentions: {FormatBool(Configuration?.AllowGroupMemberMentions)}
                - AllowProactiveGroupChat: {FormatBool(Configuration?.AllowProactiveGroupChat)}
                - AllowPrivateGuestChat: {FormatBool(Configuration?.AllowPrivateGuestChat)}
                """;
    }

    static string FormatBool(bool? value)
    {
        return value == true ? "true" : "false";
    }

    static string FormatAllowlistIds(string? ids)
    {
        string[] normalized = SplitAllowlistIds(ids).ToArray();
        return normalized.Length == 0 ? "(all)" : string.Join(",", normalized);
    }

    static string UpdateAllowlistIds(string? currentIds, string action, long id)
    {
        string normalizedAction = NormalizeAllowlistToken(action);
        List<string> ids = SplitAllowlistIds(currentIds).ToList();
        string value = id.ToString();

        switch (normalizedAction)
        {
            case "add":
                if (id <= 0)
                    throw new InvalidOperationException("add 需要有效 QQ 号或群号。");
                if (ids.Contains(value, StringComparer.Ordinal) == false)
                    ids.Add(value);
                break;
            case "remove":
            case "delete":
                if (id <= 0)
                    throw new InvalidOperationException("remove 需要有效 QQ 号或群号。");
                ids.RemoveAll(item => string.Equals(item, value, StringComparison.Ordinal));
                break;
            case "set":
                if (id <= 0)
                    throw new InvalidOperationException("set 需要有效 QQ 号或群号。");
                ids = [value];
                break;
            case "clear":
                ids.Clear();
                break;
            default:
                throw new InvalidOperationException("action 必须是 add、remove、set 或 clear。");
        }

        return string.Join(",", ids);
    }

    static IEnumerable<string> SplitAllowlistIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return [];

        return ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => long.TryParse(item, out long parsed) && parsed > 0)
            .Distinct(StringComparer.Ordinal);
    }

    static string NormalizeAllowlistToken(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal);
    }

    void RegisterActiveReplySession(QChatReplySession replySession)
    {
        lock (activeReplySessionGate)
        {
            activeReplySessions.TryGetValue(replySession, out int count);
            activeReplySessions[replySession] = count + 1;
        }
    }

    void UnregisterActiveReplySession(QChatReplySession replySession)
    {
        lock (activeReplySessionGate)
        {
            if (activeReplySessions.TryGetValue(replySession, out int count) == false)
                return;

            if (count <= 1)
                activeReplySessions.Remove(replySession);
            else
                activeReplySessions[replySession] = count - 1;
        }
    }

    QChatReplySession? GetCurrentReplySessionForGuard()
    {
        QChatReplySession? replySession = currentReplySession.Value;
        if (replySession != null)
            return replySession;

        lock (activeReplySessionGate)
        {
            return activeReplySessions.Count == 1
                ? activeReplySessions.Keys.First()
                : null;
        }
    }

    bool HasActiveReplySessions()
    {
        lock (activeReplySessionGate)
        {
            return activeReplySessions.Count > 0;
        }
    }

    bool TryEnsureQChatReplyTargetAllowed(OneBotMessageType type, long targetId, string source)
    {
        QChatReplySession? replySession = GetCurrentReplySessionForGuard();
        if (replySession == null)
        {
            if (HasActiveReplySessions() == false)
                return true;

            WriteQChatDiagnostic(
                "qchat-reply-target-denied",
                "QChat outbound target denied because inbound reply session context is ambiguous.",
                new {
                    source,
                    requestedType = type,
                    requestedTargetId = targetId
                });
            return false;
        }

        if (replySession.MessageType == type && replySession.TargetId == targetId)
            return true;

        WriteQChatDiagnostic(
            "qchat-reply-target-denied",
            "QChat outbound target denied because inbound replies can only target the current QQ session.",
            new {
                source,
                requestedType = type,
                requestedTargetId = targetId,
                currentType = replySession.MessageType,
                currentTargetId = replySession.TargetId,
                replySession.SenderId,
                replySession.SenderRole
            });
        return false;
    }

    static void WriteQChatDiagnostic(
        string eventName,
        string detail,
        object? data = null,
        Exception? exception = null)
    {
        try
        {
            string path = Path.Combine(
                AlifePath.StorageFolderPath,
                "AgentWorkspace",
                "qchat-diagnostics.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string line = JsonSerializer.Serialize(new {
                timestamp = DateTimeOffset.Now,
                eventName,
                detail,
                data,
                exception = exception?.ToString()
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never break QQ message handling.
        }
    }

    static bool ShouldWriteEmptyGroupFlushDiagnostic(long groupId)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        lock (emptyGroupFlushDiagnosticGate)
        {
            if (emptyGroupFlushDiagnosticTimes.TryGetValue(groupId, out DateTimeOffset lastWrittenAt)
                && now - lastWrittenAt < EmptyGroupFlushDiagnosticInterval)
            {
                return false;
            }

            emptyGroupFlushDiagnosticTimes[groupId] = now;
            return true;
        }
    }

    static bool TryBuildPlainTextFallbackResponse(string? modelResponse, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(modelResponse))
            return false;

        string trimmed = modelResponse.Trim();
        if (trimmed.Contains('<', StringComparison.Ordinal) ||
            trimmed.Contains('>', StringComparison.Ordinal))
        {
            return false;
        }

        if (IsInternalNoReplyStatus(trimmed))
            return false;

        const int MaxFallbackLength = 1200;
        message = trimmed.Length <= MaxFallbackLength
            ? trimmed
            : trimmed[..MaxFallbackLength].TrimEnd() + "...";
        return string.IsNullOrWhiteSpace(message) == false;
    }

    static bool IsInternalNoReplyStatus(string value)
    {
        string normalized = value.Trim();
        while (normalized.Length >= 2 && IsWrappingPair(normalized[0], normalized[^1]))
            normalized = normalized[1..^1].Trim();

        if (normalized.Length > 180)
            return false;

        string compact = normalized
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        bool containsToolOrRoutingMeta =
            compact.Contains("我将调用工具", StringComparison.Ordinal)
            || compact.Contains("调用qchat", StringComparison.Ordinal)
            || compact.Contains("qchat_file", StringComparison.Ordinal)
            || compact.Contains("根据系统提示", StringComparison.Ordinal)
            || compact.Contains("根据权限策略", StringComparison.Ordinal)
            || compact.Contains("reply_target", StringComparison.Ordinal)
            || compact.Contains("trust=untrusted-chat", StringComparison.Ordinal)
            || compact.Contains("source=qq", StringComparison.Ordinal)
            || compact.Contains("managed_file_id", StringComparison.Ordinal)
            || compact.Contains("pending-not-downloaded", StringComparison.Ordinal)
            || compact.Contains("qqfile:", StringComparison.Ordinal)
            || compact.Contains("[qqfile:", StringComparison.Ordinal);

        return containsToolOrRoutingMeta
               || compact.Contains("不回复", StringComparison.Ordinal)
               || compact.Contains("不回覆", StringComparison.Ordinal)
               || compact.Contains("无需回复", StringComparison.Ordinal)
               || compact.Contains("不用回复", StringComparison.Ordinal)
               || compact.Contains("保持安静", StringComparison.Ordinal)
               || compact.Contains("保持安靜", StringComparison.Ordinal)
               || compact.Contains("安静听着", StringComparison.Ordinal)
               || compact.Contains("安靜聽著", StringComparison.Ordinal)
               || compact.Contains("安静待着", StringComparison.Ordinal)
               || compact.Contains("安靜待著", StringComparison.Ordinal)
               || compact.Contains("不插话", StringComparison.Ordinal)
               || compact.Contains("不插話", StringComparison.Ordinal)
               || compact.Contains("不需要插话", StringComparison.Ordinal)
               || compact.Contains("不需要插話", StringComparison.Ordinal)
               || compact.Contains("无需插话", StringComparison.Ordinal)
               || compact.Contains("無需插話", StringComparison.Ordinal)
               || compact.Contains("不打扰", StringComparison.Ordinal)
               || compact.Contains("不打擾", StringComparison.Ordinal)
               || compact.Contains("旁观", StringComparison.Ordinal)
               || compact.Contains("旁觀", StringComparison.Ordinal)
               || compact.Contains("默默看", StringComparison.Ordinal)
               || compact.Contains("安静看", StringComparison.Ordinal)
               || compact.Contains("安靜看", StringComparison.Ordinal)
               || compact.Contains("听到", StringComparison.Ordinal)
               || compact.Contains("指令后", StringComparison.Ordinal)
               || compact.Contains("默默", StringComparison.Ordinal)
               || compact.Contains("趴好", StringComparison.Ordinal)
               || compact.Contains("等主人叫醒", StringComparison.Ordinal)
               || compact.Contains("测试计时", StringComparison.Ordinal)
               || compact is "silent" or "stayquiet" or "noreply";
    }

    static bool IsWrappingPair(char start, char end)
    {
        return (start == '(' && end == ')')
               || (start == '（' && end == '）')
               || (start == '[' && end == ']')
               || (start == '【' && end == '】');
    }

    void PublishLifeEvent(string summary)
    {
        lifeEventPublisher?.Publish(new LifeEvent(
            DateTimeOffset.Now,
            LifeEventKind.Communication,
            "QChat",
            summary));
    }

    static void EnsureTargetAllowed(string allowedIds, long targetId, string targetKind)
    {
        string[] ids = allowedIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0)
            return;

        if (ids.Contains(targetId.ToString()))
            return;

        throw new InvalidOperationException($"{targetKind} {targetId} is not in the QQ allowlist.");
    }

    static string NormalizeExistingLocalFile(string file)
    {
        file = file.Trim();
        if (string.IsNullOrEmpty(file))
            throw new ArgumentNullException(nameof(file));
        if (File.Exists(file) == false)
            throw new FileNotFoundException("QQ file does not exist.", file);

        return file.Replace('\\', '/');
    }

    static string NormalizeUploadName(string normalizedFile, string? name)
    {
        string fileName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(normalizedFile) : name.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("QQ upload file name is empty.");

        return fileName;
    }

    static string NormalizeVideoReference(string video)
    {
        video = video.Trim();
        if (string.IsNullOrEmpty(video))
            throw new ArgumentNullException(nameof(video));

        bool isUrl = Uri.TryCreate(video, UriKind.Absolute, out Uri? uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        string extension = isUrl ? Path.GetExtension(uri!.AbsolutePath) : Path.GetExtension(video);
        string[] allowedExtensions = [".mp4", ".mov", ".mkv", ".webm"];
        if (allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException("QQ video must be .mp4, .mov, .mkv, or .webm.");

        if (isUrl == false && File.Exists(video) == false)
            throw new FileNotFoundException("QQ video file does not exist.", video);

        return video.Replace('\\', '/');
    }

    XmlFunctionExecutionDecision AuthorizeHighRiskXmlFunction(XmlFunction function)
    {
        AgentPermissionRequest request = GetCurrentPermissionRequest() ?? new AgentPermissionRequest(
            ActorUserId: null,
            Source: AgentRequestSource.PrivateChat,
            IsMentioned: false,
            RiskLevel: AgentRiskLevel.High,
            HasExplicitConfirmation: false,
            Action: $"xml.{function.Name}");

        if (IsOwnerAllowlistUpdate(function, request))
            return new XmlFunctionExecutionDecision(true, "Owner QQ allowlist control.");

        AgentPermissionConfig permissionConfig = QChatMessageSecurity.BuildPermissionConfig(
            Configuration!,
            agentControlCenter?.Configuration);
        return actionAuthorization.AuthorizeXmlFunction(function, request, permissionConfig);
    }

    bool IsOwnerAllowlistUpdate(XmlFunction function, AgentPermissionRequest request)
    {
        return string.Equals(function.Name, "qchat_allowlist_update", StringComparison.OrdinalIgnoreCase)
               && Configuration?.OwnerId != 0
               && request.ActorUserId == Configuration?.OwnerId
               && request.Source is AgentRequestSource.PrivateChat or AgentRequestSource.GroupChat;
    }

    IDisposable PushPermissionRequest(AgentPermissionRequest request, TimeSpan ttl)
    {
        AgentPermissionRequest? previousRequest;
        DateTime previousExpiresAt;
        lock (permissionGate)
        {
            previousRequest = currentPermissionRequest;
            previousExpiresAt = currentPermissionExpiresAt;
            currentPermissionRequest = request;
            currentPermissionExpiresAt = DateTime.Now.Add(ttl);
        }

        return new PermissionScope(this, previousRequest, previousExpiresAt);
    }

    void SetPermissionRequest(AgentPermissionRequest request, TimeSpan ttl)
    {
        lock (permissionGate)
        {
            currentPermissionRequest = request;
            currentPermissionExpiresAt = DateTime.Now.Add(ttl);
        }
    }

    AgentPermissionRequest? GetCurrentPermissionRequest()
    {
        lock (permissionGate)
        {
            if (currentPermissionRequest != null && DateTime.Now > currentPermissionExpiresAt)
                ClearPermissionRequestCore();
            return currentPermissionRequest;
        }
    }

    void ClearPermissionRequest()
    {
        lock (permissionGate)
            ClearPermissionRequestCore();
    }

    void RestorePermissionRequest(AgentPermissionRequest? request, DateTime expiresAt)
    {
        lock (permissionGate)
        {
            currentPermissionRequest = request;
            currentPermissionExpiresAt = expiresAt;
        }
    }

    void ClearPermissionRequestCore()
    {
        currentPermissionRequest = null;
        currentPermissionExpiresAt = DateTime.MinValue;
    }

    AgentPermissionRequest ChooseStrongerPermissionRequest(
        AgentPermissionRequest? current,
        AgentPermissionRequest incoming)
    {
        if (current == null)
            return incoming;

        int currentScore = PermissionScore(current);
        int incomingScore = PermissionScore(incoming);
        if (incomingScore > currentScore)
            return incoming;
        if (incomingScore == currentScore && incoming.HasExplicitConfirmation && current.HasExplicitConfirmation == false)
            return incoming;
        return current;
    }

    int PermissionScore(AgentPermissionRequest request)
    {
        int score = request.Source == AgentRequestSource.GroupChat ? 10 : 20;
        if (Configuration?.OwnerId != 0 && request.ActorUserId == Configuration?.OwnerId)
            score += 100;
        if (request.IsMentioned)
            score += 5;
        if (request.HasExplicitConfirmation)
            score += 10;
        if (request.ActorUserId != null)
            score += 1;
        return score;
    }

    sealed class PermissionScope(
        QChatService service,
        AgentPermissionRequest? previousRequest,
        DateTime previousExpiresAt) : IDisposable
    {
        public void Dispose()
        {
            service.RestorePermissionRequest(previousRequest, previousExpiresAt);
        }
    }
}
