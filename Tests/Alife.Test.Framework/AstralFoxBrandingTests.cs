using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public class AstralFoxBrandingTests
{
    [Test]
    public void PublicDocumentationUsesAstralFoxAlifeBranding()
    {
        string root = FindRepositoryRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.That(readme, Does.Contain("# astralfox-alife"));
        Assert.That(readme, Does.Not.Contain("github.com/bdffzi/Alife/releases"));
        Assert.That(readme, Does.Not.Contain("# Alife -"));
        Assert.That(readme, Does.Not.Contain("![Alife Logo]"));
    }

    [Test]
    public void PrimaryFrontendSurfacesUseAstralFoxAlifeBranding()
    {
        string root = FindRepositoryRoot();
        string mainWindow = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "MainWindow.xaml"));
        string indexHtml = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "wwwroot", "index.html"));
        string welcome = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Pages", "WelcomePage", "WelcomeUI.razor"));

        Assert.That(mainWindow, Does.Contain("Title=\"astralfox-alife\""));
        Assert.That(mainWindow, Does.Contain("Text=\"astralfox-alife\""));
        Assert.That(mainWindow, Does.Not.Contain("Title=\"Alife\""));
        Assert.That(indexHtml, Does.Contain("<title>astralfox-alife</title>"));
        Assert.That(welcome, Does.Contain("astralfox-alife"));
        Assert.That(welcome, Does.Not.Contain("Alife.Client Project"));
    }

    [Test]
    public void ModuleDisplayCategoriesUseAstralFoxAlifeBranding()
    {
        string root = FindRepositoryRoot();
        string sourcesRoot = Path.Combine(root, "sources");

        foreach (string file in Directory.EnumerateFiles(sourcesRoot, "*.cs", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(file);
            Assert.That(content, Does.Not.Contain("defaultCategory: \"Alife 官方/"), file);
            Assert.That(content, Does.Not.Contain("defaultCategory: \"Alife Official/"), file);
        }
    }

    [Test]
    public void FrontendFilesDoNotExposeUpstreamAlifeReleaseLinks()
    {
        string root = FindRepositoryRoot();
        string sourcesRoot = Path.Combine(root, "sources");
        string[] extensions = [".razor", ".html", ".xaml"];

        foreach (string file in Directory.EnumerateFiles(sourcesRoot, "*.*", SearchOption.AllDirectories)
                     .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            string content = File.ReadAllText(file);
            Assert.That(content, Does.Not.Contain("github.com/BDFFZI/Alife/releases"), file);
            Assert.That(content, Does.Not.Contain("github.com/bdffzi/Alife/releases"), file);
        }
    }

    [Test]
    public void DevMarkComponentExistsAndIsUsedInPrimaryConfigurationSurfaces()
    {
        string root = FindRepositoryRoot();
        string devMarkPath = Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Utility", "AstralFoxDevMark.razor");
        string characterHomePath = Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Character", "CharacterHomeUI.razor");
        string moduleConfigPath = Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Module", "ModuleConfigUI.razor");
        string chatWindowPath = Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Pages", "CharacterWorkspacePage", "ChatWindowUI.razor");

        Assert.That(File.Exists(devMarkPath), Is.True, "AstralFox development mark component should exist.");
        Assert.That(File.ReadAllText(devMarkPath), Does.Contain("AstralFox重铸版本"));
        Assert.That(File.ReadAllText(characterHomePath), Does.Contain("<AstralFoxDevMark"));
        Assert.That(File.ReadAllText(moduleConfigPath), Does.Contain("<AstralFoxDevMark"));
        Assert.That(File.ReadAllText(chatWindowPath), Does.Contain("<AstralFoxDevMark"));
    }

    [Test]
    public void PrimaryUiUsesClearHumanReadableLabels()
    {
        string root = FindRepositoryRoot();
        string characterHome = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Character", "CharacterHomeUI.razor"));
        string chatWindow = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Pages", "CharacterWorkspacePage", "ChatWindowUI.razor"));
        string configSave = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Utility", "ConfigSaveUI.razor"));
        string moduleConfig = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Module", "ModuleConfigUI.razor"));
        string moduleEditor = File.ReadAllText(Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Module", "ModuleEditorUI.razor"));

        Assert.That(characterHome, Does.Contain("角色工作台"));
        Assert.That(characterHome, Does.Contain("激活角色"));
        Assert.That(characterHome, Does.Contain("保存变更"));
        Assert.That(characterHome, Does.Contain("模块能力"));
        Assert.That(characterHome, Does.Contain("状态与启动"));
        Assert.That(characterHome, Does.Contain("人格简介"));
        Assert.That(characterHome, Does.Contain("系统设定"));
        Assert.That(characterHome, Does.Contain("请选择角色"));
        Assert.That(chatWindow, Does.Contain("清理对话日志"));
        Assert.That(chatWindow, Does.Contain("消息标签"));
        Assert.That(chatWindow, Does.Contain("保留条数"));
        Assert.That(chatWindow, Does.Contain("输入消息"));
        Assert.That(chatWindow, Does.Contain("发送"));
        Assert.That(configSave, Does.Contain("保存到全局"));
        Assert.That(configSave, Does.Contain("保存到当前角色"));
        Assert.That(configSave, Does.Contain("删除角色配置"));
        Assert.That(configSave, Does.Contain("全局配置影响默认行为"));
        Assert.That(moduleConfig, Does.Contain("此模块没有专用配置界面"));
        Assert.That(moduleEditor, Does.Contain("配置模块"));
        Assert.That(moduleEditor, Does.Contain("保存配置"));
    }

    [Test]
    public void PrimaryUiAvoidsPlayfulPlaceholderTone()
    {
        string root = FindRepositoryRoot();
        string[] uiFiles =
        [
            Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Character", "CharacterHomeUI.razor"),
            Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Elements", "Utility", "ConfigSaveUI.razor"),
            Path.Combine(root, "sources", "Alife", "Alife.Client", "Components", "Pages", "CharacterWorkspacePage", "ChatWindowUI.razor"),
        ];

        foreach (string file in uiFiles)
        {
            string content = File.ReadAllText(file);
            Assert.That(content, Does.Not.Contain("喵"), $"Primary UI copy should stay clear and neutral in {Path.GetFileName(file)}.");
            Assert.That(content, Does.Not.Contain("猫娘"), $"Primary UI placeholders should not push a specific persona in {Path.GetFileName(file)}.");
        }
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Alife repository root.");
    }
}
