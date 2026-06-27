using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Skill;

[Module("Skill工具", "Skill 是一种渐进式（按需加载省token）的工具包，通过预编写的手册引导和规范AI完成各种各样的复杂任务。\n你可以使用“modelscope skills add”来手动添加新的技能。",
    defaultCategory: "astralfox-alife/功能底座")]
public class SkillService(XmlFunctionCaller functionService) : InteractiveModule<SkillService>
{
    [XmlFunction(FunctionMode.OneShot)]
    public void StudySkill(string name)
    {
        string skillDocPath = Path.Combine(skillsPath, name, "SKILL.md");
        if (File.Exists(skillDocPath) == false)
        {
            ChatBot.Poke($"[{nameof(StudySkill)}] skill文件不存在");
            return;
        }

        string skillDoc = File.ReadAllText(skillDocPath);
        string[] appendFiles = Directory.GetFiles(Path.Combine(skillsPath, name), "*", SearchOption.AllDirectories);

        Poke(
            $"""
             [{nameof(StudySkill)}] 已读取 {name} skill

             > 包含文件：
             - {string.Join("\n- ", appendFiles)}

             > 手册内容：
             ```
             {skillDoc}
             ```
             """);
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void GetSkillCraftingGuide()
    {
        Poke($$$"""
                每个skill都是一个文件夹，它由一个SKILL.md（使用手册），和可能附带的脚本、资源文件构成。
                所有的skill都存放在{{{skillsPath}}}目录中。可以参考其中已有的skill，来学习正确的skill格式。
                如果要使用 skill，只需要先阅读一下 skillFolder/SKILL.md ，然后按其指导的方式去使用即可（注意手册中提到的文件一般是相对 skillFolder 的路径，你可以用python执行）。
                """);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        //注册函数
        XmlHandler xmlHandler = new(this);
        functionService.RegisterHandlerWithoutDocument(xmlHandler);

        //附加提示词
        string[] skills = Directory.Exists(skillsPath)
            ? Directory.GetDirectories(skillsPath).Select(Path.GetFileName).Cast<string>().ToArray()
            : [];
        Prompt($$$"""
                  此服务让你拥有使用和管理 skill 的功能，skill 是一种可按需装载的文档功能，可以在需要的时候扩展你的能力

                  目前存在的 skill 有：
                  - {{{string.Join("\n- ", skills)}}}

                  你可以通过<{{{nameof(StudySkill)}}} name=""/>装载他们。此外可以调用<{{{nameof(GetSkillCraftingGuide)}}}/>来学习自己创建skill
                  """);
    }

    readonly string skillsPath = Path.Combine(AlifePath.StorageFolderPath, "Skills");
}
