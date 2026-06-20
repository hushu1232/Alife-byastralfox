using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Alife.Platform;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace Alife.Framework;

public class ModuleLoadContext(string moduleDirectory) : AssemblyLoadContext("ModuleContext", isCollectible: true)
{
    public Dictionary<Assembly, string> AssemblyPaths => assemblyPaths;

    public void LoadDll(string dllPath)
    {
        using var assemblyStream = new MemoryStream(File.ReadAllBytes(dllPath));
        string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
        MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
        Assembly assembly = LoadFromStream(assemblyStream, pdbStream);
        pdbStream?.Dispose();
        assemblyPaths.Add(assembly, dllPath);
    }

    readonly string baseDirectory = Path.Combine(moduleDirectory, "BaseDirectory");
    readonly string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? $"win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? $"linux-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
            : $"osx-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
    readonly string[] searchPaths = new string[4];

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string path = Path.Combine(baseDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(path))
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            string pdbPath = Path.ChangeExtension(path, ".pdb");
            MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
            Assembly assembly = LoadFromStream(stream, pdbStream);
            pdbStream?.Dispose();
            return assembly;
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (Path.HasExtension(unmanagedDllName) == false)
            unmanagedDllName += ".dll";
        searchPaths[0] = Path.Combine(baseDirectory, "runtimes", rid, "native", unmanagedDllName);
        searchPaths[1] = Path.Combine(baseDirectory, unmanagedDllName);
        searchPaths[2] = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", unmanagedDllName);
        searchPaths[3] = Path.Combine(AppContext.BaseDirectory, unmanagedDllName);

        foreach (string path in searchPaths)
        {
            if (File.Exists(path))
                return LoadUnmanagedDllFromPath(path);
        }

        if (NativeLibrary.TryLoad(unmanagedDllName, out IntPtr handle))
            return handle;

        return IntPtr.Zero;
    }

    readonly Dictionary<Assembly, string> assemblyPaths = new();
}

public class ModuleSecuritySettings
{
    public bool CompatibilityMode { get; set; }
}

public class ModuleSystem
{
    public string GetModuleFolderRoot()
    {
        return moduleRoot;
    }
    public StringFolder GetModuleFolder()
    {
        return moduleFolder;
    }
    public IEnumerable<Type> GetAllModules()
    {
        return moduleTypes.Values;
    }
    public Type? GetModule(string moduleID)
    {
        return moduleTypes.GetValueOrDefault(moduleID);
    }
    public bool CompatibilityMode
    {
        get => securitySettings.CompatibilityMode;
        set
        {
            if (securitySettings.CompatibilityMode == value)
                return;

            securitySettings.CompatibilityMode = value;
            SaveSecuritySettings();
        }
    }
    public string GetModuleID(Type moduleType)
    {
        return moduleType.FullName!;
    }
    public static bool IsModuleTypeAllowed(Type type, bool compatibilityMode = false)
    {
        if (type.GetCustomAttribute<ModuleAttribute>() == null)
            return false;
        if (type.IsAbstract)
            return false;
        if (type.IsInterface)
            return false;
        if (type.ContainsGenericParameters)
            return false;
        if (compatibilityMode)
            return true;

        return typeof(ISystemEvent).IsAssignableFrom(type)
               || typeof(ILanguageModel).IsAssignableFrom(type)
               || type.GetInterfaces().Any(IsModuleDependencyProviderInterface);
    }
    public void ReloadModules()
    {
        //确保模块文件夹存在，防止报错]
        if (Directory.Exists(moduleRoot) == false)
            Directory.CreateDirectory(moduleRoot);

        ReloadContext(CompileModule(moduleRoot));
    }
    public ModuleLoadContext CompileModule(string source)
    {
        ModuleLoadContext compilingContext = new(source);
        try
        {
            //加载dll
            foreach (string file in Directory.GetFiles(source, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    string assemblyName = AssemblyName.GetAssemblyName(file).FullName;
                    if (defaultAssemblies.Contains(assemblyName) == false)
                        compilingContext.LoadDll(file);
                }
                catch
                {
                    // ignored
                }
            }

            //编译cs
            {
                string dllPath = Path.Combine(AlifePath.TempFolderPath, "Modules.dll");
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");

                //解析语法树
                var syntaxTrees = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
                    .Select(file => CSharpSyntaxTree.ParseText(
                        File.ReadAllText(file),
                        new CSharpParseOptions(LanguageVersion.Latest)))
                    .ToList();

                //收集元数据引用（去重）
                var references = new List<MetadataReference>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                        continue;
                    references.Add(MetadataReference.CreateFromFile(asm.Location));
                }
                foreach (var path in compilingContext.AssemblyPaths.Values)
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }

                //编译
                var compilation = CSharpCompilation.Create(
                    "Modules",
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithAllowUnsafe(true)
                        .WithOptimizationLevel(OptimizationLevel.Release));

                var emitResult = compilation.Emit(dllPath, pdbPath);

                if (!emitResult.Success)
                {
                    var errors = string.Join("\n", emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                    throw new Exception($"模块编译失败:\n{errors}");
                }

                compilingContext.LoadDll(dllPath);
            }

            return compilingContext;
        }
        catch
        {
            compilingContext.Unload();
            throw;
        }
    }
    public void SaveData()
    {
        storageSystem.SetObject(moduleSystemConfig, moduleFolder);
    }
    public void SaveSecuritySettings()
    {
        storageSystem.SetObject(moduleSecurityConfig, securitySettings);
    }

#if DEBUG
    readonly string moduleRoot = Path.Combine(AlifePath.StorageFolderPath, "PluginsDebug");
#else
    readonly string moduleRoot = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
#endif

    readonly string moduleSystemConfig = "ModuleCategory";
    readonly string moduleSecurityConfig = "ModuleSecurity";
    readonly StorageSystem storageSystem;
    readonly Dictionary<string, Type> moduleTypes;
    readonly StringFolder moduleFolder;
    readonly ModuleSecuritySettings securitySettings;
    readonly HashSet<string> defaultAssemblies;
    readonly Assembly[] thisAssemblies;
    AssemblyLoadContext? moduleAssemblies;

    public ModuleSystem(StorageSystem storageSystem, ILogger<ModuleSystem> logger)
    {
        this.storageSystem = storageSystem;

        moduleTypes = new Dictionary<string, Type>();
        moduleFolder = storageSystem.GetObject(moduleSystemConfig, new StringFolder("全部模块"))!;
        securitySettings = storageSystem.GetObject(moduleSecurityConfig, new ModuleSecuritySettings())!;

        //预热程序集，因为模块可能依赖Alife自身的程序集，结果Alife本身目前未用到，导致未加载
        PreloadAllAssemblies();
        defaultAssemblies = AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.FullName).ToHashSet()!;
        thisAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("Alife") ?? false).ToArray();

        try
        {
            ReloadModules();
        }
        catch (Exception e)
        {
            logger.LogError(e, "加载模块失败");
        }
    }

    void ReloadContext(AssemblyLoadContext context)
    {
        //替换上下文
        moduleTypes.Clear();
        if (moduleAssemblies != null)
            moduleAssemblies.Unload();
        moduleAssemblies = context;

        //统计Module
        foreach (Assembly assembly in moduleAssemblies.Assemblies.Union(thisAssemblies))
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (IsModuleTypeAllowed(type, securitySettings.CompatibilityMode) == false)
                    continue;

                moduleTypes.Add(GetModuleID(type), type);
            }
        }

        SyncFolder();
    }
    void SyncFolder()
    {
        HashSet<string> currentModules = moduleTypes.Keys.ToHashSet();

        //移除无效模块，同时如果有效则剔除
        moduleFolder.RemoveAll(name => currentModules.Remove(name) == false);
        //剩下的就是还没有的模块，添加到根目录
        foreach (var typeName in currentModules)
        {
            ModuleAttribute? moduleAttribute = moduleTypes[typeName].GetCustomAttribute<ModuleAttribute>();
            if (moduleAttribute == null)
                continue;

            StringFolder folder = moduleFolder;
            string[] path = moduleAttribute.DefaultCategory.Split("/", StringSplitOptions.RemoveEmptyEntries);
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

    static bool IsModuleDependencyProviderInterface(Type type)
    {
        return type.FullName is
            "Alife.Function.Speech.ISpeechModel" or
            "Alife.Function.Vision.IVisionModel" or
            "Alife.Function.AuditoryModel.IAuditoryModel";
    }
}
