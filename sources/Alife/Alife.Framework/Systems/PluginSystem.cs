using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Alife.Platform;
using CSScriptLib;
using Microsoft.Extensions.Logging;

namespace Alife.Framework;

public class PluginSystem
{
    public StringFolder GetPluginFolder()
    {
        return pluginFolder;
    }
    public Type? GetPlugin(string pluginID)
    {
        return pluginTypes.GetValueOrDefault(pluginID);
    }
    public string GetPluginID(Type pluginType)
    {
        return pluginType.FullName!;
    }
    public void ReloadPlugins()
    {
        //确保插件文件夹存在，防止报错
        string pluginRoot = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
        if (Directory.Exists(pluginRoot) == false)
            Directory.CreateDirectory(pluginRoot);

        AssemblyLoadContext compilingContext = new("CompilingContext", true);
        try
        {
            void LoadDll(string dllPath)
            {
                using var assemblyStream = new MemoryStream(File.ReadAllBytes(dllPath));
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
                compilingContext.LoadFromStream(assemblyStream, pdbStream);
                pdbStream?.Dispose();
            }

            //加载dll
            List<string> dllFiles = Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories).ToList();
            foreach (string dllFile in dllFiles)
            {
                try
                {
                    string assemblyName = AssemblyName.GetAssemblyName(dllFile).FullName;
                    if (defaultAssemblies.Contains(assemblyName))
                        continue;

                    LoadDll(dllFile);
                }
                catch (Exception)
                {
                    // 可能包含一些非C#的dll
                    // Console.WriteLine(e);
                }
            }

            //编译cs
            {
                var codeBuilder = new StringBuilder();

                //收集cs
                {
                    string[] csFiles = Directory.GetFiles(pluginRoot, "*.cs", SearchOption.AllDirectories);
                    foreach (var file in csFiles)
                        codeBuilder.AppendLine($"//css_include {file};");
                }

                // //收集razor
                // {
                //     var fileSystem = RazorProjectFileSystem.Create(pluginRoot);
                //     var engine = RazorProjectEngine.Create(
                //     RazorConfiguration.Default,
                //     fileSystem,
                //     _ => {});
                //     var razorFiles = Directory.GetFiles(pluginRoot, "*.razor", SearchOption.AllDirectories);
                //     foreach (var razorFile in razorFiles)
                //     {
                //         string relativePath = Path.GetRelativePath(pluginRoot, razorFile);
                //         var projectItem = fileSystem.GetItem(relativePath, fileKind: null);
                //         var codeDocument = engine.Process(projectItem);
                //         string output = Path.ChangeExtension(Path.Combine(AlifePath.TempFolderPath, relativePath), ".cs");
                //         Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                //         File.WriteAllText(output, codeDocument.GetCSharpDocument().GeneratedCode);
                //         codeBuilder.AppendLine($"//css_include {output};");
                //     }
                // }

                string dllPath = Path.Combine(AlifePath.TempFolderPath, "Plugins.dll");
                {
                    //添加环境
                    var compiler = CSScript.Evaluator.Clone();
                    compiler.ReferenceDomainAssemblies(DomainAssemblies.AllStatic);
                    foreach (var dll in dllFiles)//很遗憾，Roslyn似乎必须依赖于dll的原始文件，结果他又会把这些文件锁住
                        compiler.ReferenceAssembly(dll);

                    //编译代码
                    string masterScript = codeBuilder.ToString();
                    compiler.CompileAssemblyFromCode(masterScript, new CompileInfo { AssemblyFile = dllPath });
                }

                //加载到上下文
                LoadDll(dllPath);
            }

            //替换插件环境
            ReloadContext(compilingContext);

            void ReloadContext(AssemblyLoadContext context)
            {
                //替换上下文
                pluginTypes.Clear();
                if (pluginAssemblies != null)
                    pluginAssemblies.Unload();
                pluginAssemblies = context;

                //统计Plugin
                foreach (Assembly assembly in pluginAssemblies.Assemblies.Union(thisAssemblies))
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsAssignableTo(typeof(Plugin)) == false)
                            continue;
                        if (type.IsAbstract)
                            continue;
                        if (type.IsInterface)
                            continue;
                        if (type.GetCustomAttribute<PluginAttribute>() == null)
                            continue;

                        pluginTypes.Add(GetPluginID(type), type);
                    }
                }

                SyncFolder();
            }
        }
        catch
        {
            compilingContext.Unload();
            throw;
        }
    }
    public void SaveData()
    {
        storageSystem.SetObject("PluginSystem/PluginFolder", pluginFolder);
    }

    readonly StorageSystem storageSystem;
    readonly Dictionary<string, Type> pluginTypes;
    readonly StringFolder pluginFolder;
    readonly HashSet<string> defaultAssemblies;
    readonly Assembly[] thisAssemblies;
    AssemblyLoadContext? pluginAssemblies;

    public PluginSystem(StorageSystem storageSystem, ILogger<PluginSystem> logger)
    {
        this.storageSystem = storageSystem;

        pluginTypes = new Dictionary<string, Type>();
        pluginFolder = storageSystem.GetObject("PluginSystem/PluginFolder", new StringFolder("全部插件"))!;

        //预热程序集，因为插件可能依赖Alife自身的程序集，结果Alife本身目前未用到，导致未加载
        PreloadAllAssemblies();
        defaultAssemblies = AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.FullName).ToHashSet()!;
        thisAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("Alife") ?? false).ToArray();

        try
        {
            ReloadPlugins();
        }
        catch (Exception e)
        {
            logger.LogError(e, "加载插件失败");
        }
    }

    void SyncFolder()
    {
        HashSet<string> currentPlugins = pluginTypes.Keys.ToHashSet();

        //移除无效插件，同时如果有效则剔除
        pluginFolder.RemoveAll(name => currentPlugins.Remove(name) == false);
        //剩下的就是还没有的插件，添加到根目录
        foreach (var typeName in currentPlugins)
        {
            PluginAttribute? pluginAttribute = pluginTypes[typeName].GetCustomAttribute<PluginAttribute>();
            if (pluginAttribute == null)
                continue;

            StringFolder folder = pluginFolder;
            string[] path = pluginAttribute.DefaultCategory.Split("/", StringSplitOptions.RemoveEmptyEntries);
            foreach (string subFolderName in path)
            {
                string name = subFolderName;
                StringFolder? subFolder = folder.Folders.FirstOrDefault(subFolder => subFolder.Name == name);
                if (subFolder == null)
                {
                    subFolder = new StringFolder(subFolderName);
                    folder.Folders.Add(subFolder);
                }

                folder = subFolder;
            }

            folder.Strings.Add(typeName);
        }
    }

    void PreloadAllAssemblies()
    {
        var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(Assembly.GetEntryAssembly()!);
        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                // 如果这个程序集还没被加载过
                if (!loadedAssemblies.Contains(reference.FullName))
                {
                    try
                    {
                        // 强制加载它
                        var loaded = Assembly.Load(reference);
                        queue.Enqueue(loaded);
                        loadedAssemblies.Add(reference.FullName);
                    }
                    catch
                    {
                        // 忽略加载失败的程序集（有些可能是环境相关的）
                    }
                }
            }
        }
    }
}
