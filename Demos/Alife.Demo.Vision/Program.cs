using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Vision;



class Program
{
    static async Task Main(string[] args)
    {
        //1. 定义角色：具有视觉能力的“真央”
        Character character = new Character {
            Name = "VisionTest",
            Prompt = "你叫真央，是一个拥有视觉能力的 AI 助手。你可以看到主人的屏幕内容，也可以分析指定的图片。\n" +
                     "当你需要看屏幕时，请使用 <look_screen> 标签（可附带你的问题，如：<look_screen>屏幕上有什么？</look_screen>）。\n" +
                     "当你需要分析本地图片时，请使用 <look_image path=\"图片完整路径\"> 标签。\n" +
                     "你的回答应该亲切自然，经常称呼用户为“主人”，并使用“喵”作为句尾。\n" +
                     "示例：\n" +
                     "主人问：帮我看看屏幕上在运行什么？\n" +
                     "你答：好的喵！主人请稍候，我这就来看看屏幕喵！<look_screen>描述一下屏幕上的主要窗口内容</look_screen>",
            Modules = new HashSet<string> {
                typeof(OpenAILanguageModel).FullName!,// 大模型对话支持
                typeof(XmlFunctionCaller).FullName!,// 标签解析支持
                typeof(VisionService).FullName!,// 视觉服务插件
            }
        };

        // 2. 初始化 Demo 套件
        DemoSuite suite = await DemoSuite.InitializeAsync(character, system => {
            system.SetConfiguration(typeof(VisionService), new VisionServiceConfig() {
            }, character.StorageKey);
        });

        AlifeTerminal.Log("--------------------------------------------------\n");

        // 3. 运行套件循环
        try
        {
            await suite.RunAsync();
        }
        catch (Exception ex)
        {
            AlifeTerminal.LogError($"Demo 运行出错：{ex.Message}");
        }

        AlifeTerminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);
    }
}
