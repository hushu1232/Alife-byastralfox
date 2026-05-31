using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.PluginEditor;

[Plugin("自我升级", "让 AI 拥有直接编辑和热重载插件的能力。（非常危险，没搞好就会导致无法正常加载插件！）",
defaultCategory: "Alife 官方/生活环境"
)]
public class PluginEditorService(
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    PluginSystem pluginSystem,
    XmlFunctionCaller functionCaller) :
    InteractivePlugin<PluginEditorService>
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("尝试编译重载插件")]
    public void ReloadPlugin()
    {
        //编译副本插件文件夹
        PluginLoadContext context = pluginSystem.CompilePlugin(pluginCopyRoot);
        context.Unload();

        //将正式的插件文件夹重置为副本的状态（忽略BaseDirectory目录，因为里面基本都是标准的dll和原生dll，无法修改）
        SyncPluginsFromCopy();

        pluginSystem.ReloadPlugins();
        Poke("插件重载成功！接下来请确认角色配置文件是否确添加插件，然后重启角色活动，以使插件生效。");
    }
    [XmlFunction(FunctionMode.OneShot)]
    [Description("重启角色活动")]
    public void RestartActivity([Description("为空时表示重启自己")] string? charactorName = null)
    {
        Character? character;
        if (charactorName == null)
            character = Character;
        else
        {
            character = characterSystem.GetAllCharacters().Find(ch => ch.Name == charactorName);
            if (character == null)
                throw new Exception("角色不存在，请检查名称是否正确！");
        }

        chatActivitySystem.Deactivate(character).ContinueWith(async _ => {
            chatActivitySystem.ActivationFailed += OnActivationFailed;
            await chatActivitySystem.Activate(character);
            chatActivitySystem.ActivationFailed -= OnActivationFailed;

            Exception? ex = null;

            void OnActivationFailed(Character arg1, Exception arg2)
            {
                ex = arg2;
            }

            if (character != Character)
                ChatBot.Poke($"{charactorName} 激活 {(ex == null ? "成功" : "失败\n" + ex)}");
        });
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取插件开发文档")]
    public void GetGuide()
    {
        Poke($$$"""
                # 插件开发指南

                ## 示例代码
                ```csharp
                using System.ComponentModel;
                using Alife.Framework;
                using Alife.Function.FunctionCaller;

                [Plugin("插件名", "插件描述")]
                public class MyPlugin(XmlFunctionCaller functionService) : InteractivePlugin<MyPlugin>
                {
                    [XmlFunction(FunctionMode.OneShot)]
                    [Description("函数描述")]
                    public Task DoSomething([Description("参数描述")] string input)
                    {
                        // 你的逻辑
                        Poke("结果：" + input);
                        return Task.CompletedTask;
                    }

                    public override async Task AwakeAsync(AwakeContext context)
                    {
                        await base.AwakeAsync(context);
                        functionService.RegisterHandler(this);
                        Prompt("此插件的功能说明...");
                    }
                }
                ```

                - `[Plugin]` 标记插件类
                - 继承 `InteractivePlugin<T>` 获得 `Poke()`、`Prompt()` 等方法
                - 构造函数参数自动依赖注入（其他插件、Logger、系统服务等）
                - `[XmlFunction(FunctionMode.OneShot)]` 标记可调用函数
                - `Poke()` 向 AI 返回结果
                - `AwakeAsync` 中 `RegisterHandler(this)` 注册函数

                ## 开发环境：
                1. 本框架支持热编译热重载C#代码，来编写插件的功能。因此只需在插件文件夹直接编写cs代码，即可创建插件，没有任何其他文件名等要求。
                2. 插件文件夹是`{{{pluginCopyRoot}}}`，所有已有插件以及你新增的插件，都要放到该目录下。
                3. 角色配置文件是`{{{characterSystem.GetCharacterConfigFile(Character)}}}`，插件的开关需要在中设置。

                ## 插件开发步骤
                1. 翻阅插件文件夹，确定已有插件，以及参考其中插件的实现。
                2. 在插件文件夹新增或修改插件的.cs后，通过 <{{{nameof(ReloadPlugin)}}}> 重载插件。
                3. 重载成功后，检查并修改角色配置文件，确保其中正确包含了要启用的插件。
                4. 通过 <{{{nameof(RestartActivity)}}}> 重启对话活动，插件将在重启后生效。

                ## 使用提示
                1. 如果重载插件成功，那说明代码肯定是没问题的，只要插件在插件文件夹中，就一定是能加载到程序中。
                2. 如果重载成功但依然没法使用，通常都是角色配置的问题，你需要确保配置填写无误，确定启用了插件。

                ## 更多信息
                项目源码在：https://github.com/BDFFZI/Alife
                你可以参考此仓库，以及阅读他的ReadMe来了解更多
                """);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("确认当前启用的插件")]
    public void GetPlugins()
    {
        string[] pluginName = Character.Plugins.Select(pluginSystem.GetPlugin)
            .Where(type => type != null)
            .Cast<Type>()
            .Select(type => type.FullName!)
            .ToArray();
        Poke($"当前启用的插件有：{string.Join("\n", pluginName)}");
    }

    string pluginRoot = null!;
    string pluginCopyRoot = null!;


    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        //创建插件文件夹副本
        pluginRoot = pluginSystem.GetPluginFolderRoot();
        pluginCopyRoot = Path.Combine(AlifePath.TempFolderPath, "PluginsRuntime");
        CopyPluginFolder(pluginRoot, pluginCopyRoot);

        functionCaller.RegisterHandler(this);
        Prompt("此服务让你拥有自定义插件的能力，这个功能非常强大，能让你实现自我进化！但同时也非常危险，因为如果失败，你可能无法恢复。因此请在自用户的配合下使用。");
    }

    void CopyPluginFolder(string source, string destination)
    {
        if (Directory.Exists(destination))
            Directory.Delete(destination, true);
        Directory.CreateDirectory(destination);

        foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relativePath = dirPath[(source.Length + 1)..];
            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        foreach (string filePath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            string relativePath = filePath[(source.Length + 1)..];
            File.Copy(filePath, Path.Combine(destination, relativePath), true);
        }
    }

    void SyncPluginsFromCopy()
    {
        //删除原插件文件夹中的所有内容（忽略 BaseDirectory 目录，因为里面基本都是标准的dll和原生dll，无法修改）
        foreach (string dir in Directory.GetDirectories(pluginRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(dir) == "BaseDirectory") continue;
            Directory.Delete(dir, true);
        }

        foreach (string file in Directory.GetFiles(pluginRoot, "*.*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }

        //从副本同步（忽略 BaseDirectory 目录）
        foreach (string dir in Directory.GetDirectories(pluginCopyRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(dir) == "BaseDirectory") continue;
            CopyPluginFolder(dir, Path.Combine(pluginRoot, Path.GetFileName(dir)));
        }

        foreach (string file in Directory.GetFiles(pluginCopyRoot, "*.*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(pluginRoot, Path.GetFileName(file)), true);
        }
    }
}
