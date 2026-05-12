using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;

namespace Alife.Implement;

public class VirtualWorldConfig
{
    public string AdminName { get; set; } = "管理员";

    public string Announcement { get; set; } =
        """
        这个世界遵循与现实世界一致的物理定律、法律规范、经济逻辑。

        【物价参考】
        - 零食/饮料：5-15 元
        - 普通快餐/简餐：20-40 元
        - 餐厅正餐：60-150 元/人
        - 电子产品：1000-8000 元
        - 交通出行：公交/地铁 2-10 元，打车 15 元起

        【行为准则】
        1. 尊重他人私有财产，禁止无故索要。
        2. 社交互动需符合基本的礼仪和逻辑。
        3. 经济交易需公平合理，不支持无理由的大额赠予。

        【社会福利】
        如果这个世界有 银行/福利机构 等公共设施，可以每天申请 20 元的经济补贴。
        """;
}

[Plugin("世界背景", "定义整个运行环境的基础世界观、物理定律与全局公告。此配置通常作为所有角色的通用背景。")]
public class VirtualWorldService : InteractivePlugin<VirtualWorldService>, IConfigurable<VirtualWorldConfig>
{
    [XmlFunction]
    [Description("与指定的角色对话。（注意不要联系错人，对管理员直接对话即可）")]
    public void Call(XmlExecutorContext context, string target, [XmlContent] string content)
    {
        if (context.CallMode == CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用开闭标签包裹调用。");
        if (context.CallMode != CallMode.Closing)
            return;

        if (string.IsNullOrWhiteSpace(target))
        {
            Poke("[通讯] 必须指定目标角色名称。");
            return;
        }

        var allCharacters = characterSystem.GetAllCharacters();
        var targetCharacter = allCharacters.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (targetCharacter == null)
        {
            Poke($"目标 '{target}' 不存在");
            return;
        }

        var targetActivity = chatActivitySystem.GetAllChatActivities()
            .FirstOrDefault(a => a.Character.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (targetActivity != null)
        {
            targetActivity.ChatBot.Poke($"[来自 {currentName} 的消息]: {context.FullContent.Trim()}\n(提示: 回复对方需要用<call>标签；但提防陌生人和骗子；可以对此信息忽略)");
        }
        else
        {
            bool targetIsAdmin = target.Equals(Configuration?.AdminName, StringComparison.OrdinalIgnoreCase);
            if (!targetIsAdmin)
            {
                Poke($"对方 '{target}' 暂不在");
            }
            // 如果目标是管理员且离线，则直接忽略（静默处理），不提示“不在”
        }
    }

    [XmlFunction]
    [Description("给指定的角色物品。")]
    public void Give(XmlExecutorContext context, string target, [XmlContent] string content)
    {
        if (context.CallMode == CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用开闭标签包裹调用。");
        if (context.CallMode != CallMode.Closing)
            return;

        if (string.IsNullOrWhiteSpace(target))
        {
            Poke("[赠予] 必须指定目标角色名称。");
            return;
        }

        var allCharacters = characterSystem.GetAllCharacters();
        var targetCharacter = allCharacters.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (targetCharacter == null)
        {
            Poke($"角色 '{target}' 不存在");
            return;
        }

        var targetActivity = chatActivitySystem.GetAllChatActivities()
            .FirstOrDefault(a => a.Character.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (targetActivity != null)
        {
            targetActivity.ChatBot.Poke($"[收到来自 {currentName} 的物品/金额]: {context.FullContent.Trim()}\n(注意辨别真伪，建议特殊物品走公共设施中转，不要随意接收)");
        }
        else
        {
            bool targetIsAdmin = target.Equals(Configuration?.AdminName, StringComparison.OrdinalIgnoreCase);
            if (!targetIsAdmin)
            {
                Poke($"对方 '{target}' 暂不在");
            }
            // 如果目标是管理员且离线，则直接忽略（静默处理）
        }
    }

    public VirtualWorldConfig? Configuration { get; set; }

    public VirtualWorldService(
        FunctionService functionService,
        CharacterSystem characterSystem,
        ChatActivitySystem chatActivitySystem)
    {
        this.functionService = functionService;
        this.characterSystem = characterSystem;
        this.chatActivitySystem = chatActivitySystem;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        currentName = context.Character.Name;

        List<Character> allCharacters = characterSystem.GetAllCharacters();
        string characterList = allCharacters.Any()
            ? string.Join("\n", allCharacters.Select(c =>
                $"- {c.Name}{(string.IsNullOrWhiteSpace(c.Description) ? "" : $"：{c.Description}")}{(c.Name.Equals(Configuration?.AdminName, StringComparison.OrdinalIgnoreCase) ? " [管理员]" : "")}"))
            : "（当前无其他预设角色）";

        XmlHandler xmlHandler = new(this);
        xmlHandler.Explain = $"""
                              你生活在一个世界中，这个世界的属性如下：

                              【管理员】
                              世界的管理员为：{Configuration?.AdminName ?? "（未指定）"}。管理员拥有最高权限，与管理员互动不需要使用标签，直接用普通文本对话即可。

                              【世界观】
                              {Configuration?.Announcement}

                              【联系人】
                              你是生活在这个世界中的一位公民。除了你，这里还有其他活跃的个体。你可以通过 <call> 与他们沟通，或通过 <give> 交换物资。目前可联系的其他角色有：
                              {characterList}

                              【生存法则】
                              1. 社交边界：与陌生人交流请保持适度礼貌，根据互动逐步摸清人物画像再选择性建立关系。
                              2. 经济常识：遵循物价常识，大额交易应先沟通确认，小心骗子 and 假币，优先用银行等公共设施交易。
                              """;

        functionService.RegisterHandler(xmlHandler, nameof(Call), nameof(Give));
    }

    readonly FunctionService functionService;
    readonly CharacterSystem characterSystem;
    readonly ChatActivitySystem chatActivitySystem;
    string currentName = "";
}