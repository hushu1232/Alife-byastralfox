using Alife.Function.Memory;

namespace Alife.Test.Framework;

public class MemoryTextSanitizerTests
{
    [Test]
    public void SanitizeText_RemovesQChatToolAndSystemNoiseButKeepsUsefulMemory()
    {
        string text = """
                      主人希望咪绪保持自然。
                      <qchat type="Group" targetId="123">bad xml</qchat>
                      [XmlFunctionCaller] qchat tag error: invalid child closing tag
                      [系统报点] timer fired; do not tell the owner this was automatic
                      妈妈只有睡眠模式唤醒权限。
                      """;

        MemoryTextSanitizationResult result = MemoryTextSanitizer.Default.SanitizeText(text);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.RemovedSegments, Is.EqualTo(3));
        Assert.That(result.Text, Does.Contain("主人希望咪绪保持自然。"));
        Assert.That(result.Text, Does.Contain("妈妈只有睡眠模式唤醒权限。"));
        Assert.That(result.Text, Does.Not.Contain("<qchat"));
        Assert.That(result.Text, Does.Not.Contain("XmlFunctionCaller"));
        Assert.That(result.Text, Does.Not.Contain("系统报点"));
        Assert.That(result.Text, Does.Not.Contain("do not tell the owner"));
    }

    [Test]
    public void SanitizeHistoryJson_RemovesNoisyHistoryRecordsAndSanitizesMemoryRecords()
    {
        string historyJson = """
                             [
                               {
                                 "Role": { "Label": "assistant" },
                                 "Content": "[记忆存档(1-good)]\n主人希望咪绪保持自然。\n<qchat type=\"Group\">bad xml</qchat>\n妈妈只有睡眠模式唤醒权限。",
                                 "MemoryMeta": {
                                   "Level": 1,
                                   "StartTime": "2026-06-16T21:00:00+08:00",
                                   "EndTime": "2026-06-16T21:10:00+08:00",
                                   "Name": "1-good"
                                 }
                               },
                               {
                                 "Role": { "Label": "user" },
                                 "Content": "[系统报点] timer fired; do not tell the owner this was automatic",
                                 "MemoryMeta": {
                                   "Level": 0,
                                   "StartTime": "2026-06-16T22:00:00+08:00",
                                   "EndTime": "2026-06-16T22:00:00+08:00",
                                   "Name": "0-noise"
                                 }
                               }
                             ]
                             """;

        MemoryHistorySanitizationResult result = MemoryTextSanitizer.Default.SanitizeHistoryJson(historyJson);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.RemovedRecords, Is.EqualTo(1));
        Assert.That(result.SanitizedRecords, Is.EqualTo(1));
        Assert.That(result.Json, Does.Contain("主人希望咪绪保持自然。"));
        Assert.That(result.Json, Does.Contain("妈妈只有睡眠模式唤醒权限。"));
        Assert.That(result.Json, Does.Not.Contain("<qchat"));
        Assert.That(result.Json, Does.Not.Contain("系统报点"));
        Assert.That(result.Json, Does.Not.Contain("do not tell the owner"));
    }

    [Test]
    public void ShouldDropHistoryRecord_DropsSystemReportAtLevelZeroOnly()
    {
        MemoryTextSanitizer sanitizer = MemoryTextSanitizer.Default;

        Assert.That(sanitizer.ShouldDropHistoryRecord(
            "[系统报点]程序已重启。(回复消息时保持简洁，禁用旁白、emoji)",
            level: 0), Is.True);
        Assert.That(sanitizer.ShouldDropHistoryRecord(
            "[记忆存档(1-good)]\n主人希望咪绪保持自然。\n[系统报点]旧噪声",
            level: 1), Is.False);
    }

    [Test]
    public void SanitizeText_RemovesLeakedSilentStatusButKeepsOwnerQuietInstruction()
    {
        string text = """
                      主人要求咪绪在群聊中保持安静，减少刷屏。
                      （不回复，保持安静）
                      [2103917668]：[对“3340947887：（保持安静，不回复）”的回复]你一直在回复
                      妈妈只有睡眠模式唤醒权限。
                      """;

        MemoryTextSanitizationResult result = MemoryTextSanitizer.Default.SanitizeText(text);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.RemovedSegments, Is.EqualTo(2));
        Assert.That(result.Text, Does.Contain("主人要求咪绪在群聊中保持安静，减少刷屏。"));
        Assert.That(result.Text, Does.Contain("妈妈只有睡眠模式唤醒权限。"));
        Assert.That(result.Text, Does.Not.Contain("（不回复，保持安静）"));
        Assert.That(result.Text, Does.Not.Contain("（保持安静，不回复）"));
    }

    [Test]
    public void SanitizeText_RemovesStageDirectionNoReplyVariants()
    {
        string text = """
                      主人要求夏羽在群聊中少刷屏。
                      （安静等待）
                      （安静待机）
                      [不插话]
                      *沉默看着*
                      术术允许正常问题继续回复。
                      """;

        MemoryTextSanitizationResult result = MemoryTextSanitizer.Default.SanitizeText(text);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.RemovedSegments, Is.EqualTo(4));
        Assert.That(result.Text, Does.Contain("主人要求夏羽在群聊中少刷屏。"));
        Assert.That(result.Text, Does.Contain("术术允许正常问题继续回复。"));
        Assert.That(result.Text, Does.Not.Contain("安静等待"));
        Assert.That(result.Text, Does.Not.Contain("安静待机"));
        Assert.That(result.Text, Does.Not.Contain("不插话"));
        Assert.That(result.Text, Does.Not.Contain("沉默看着"));
    }

    [Test]
    public void SanitizeText_RemovesRoleplayStageDirectionVariants()
    {
        string text = """
                      术术要求夏羽用自然聊天回复。
                      （揉揉鼻子，尾巴尖微微动了一下）
                      （耳朵压低，安静地趴在一边）
                      （内心：我应该安静）
                      夏羽需要直接不发送 QQ 状态说明。
                      """;

        MemoryTextSanitizationResult result = MemoryTextSanitizer.Default.SanitizeText(text);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.RemovedSegments, Is.EqualTo(3));
        Assert.That(result.Text, Does.Contain("术术要求夏羽用自然聊天回复。"));
        Assert.That(result.Text, Does.Contain("夏羽需要直接不发送 QQ 状态说明。"));
        Assert.That(result.Text, Does.Not.Contain("揉揉鼻子"));
        Assert.That(result.Text, Does.Not.Contain("耳朵压低"));
        Assert.That(result.Text, Does.Not.Contain("内心"));
    }

    [Test]
    public void ShouldDropHistoryRecord_DropsLeakedSilentStatusAtLevelZeroOnly()
    {
        MemoryTextSanitizer sanitizer = MemoryTextSanitizer.Default;

        Assert.That(sanitizer.ShouldDropHistoryRecord("（没理，保持安静）", level: 0), Is.True);
        Assert.That(sanitizer.ShouldDropHistoryRecord("（安静待机）", level: 0), Is.True);
        Assert.That(sanitizer.ShouldDropHistoryRecord("主人要求咪绪在群聊中保持安静，减少刷屏。", level: 0), Is.False);
    }
}
