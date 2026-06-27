using Alife.Framework;

namespace Alife.Test.Framework;

public class ModuleSystemSecurityTests
{
    [Test]
    public void DefaultSecurityModeOnlyAllowsConcreteSystemEventModules()
    {
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(SafeModule)), Is.True);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(PlainAttributedType)), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(AbstractAttributedModule)), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(IAttributedModule)), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(OpenGenericModule<>)), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(UnattributedModule)), Is.False);
    }

    [Test]
    public void CompatibilityModeAllowsLegacyAttributedConcreteTypes()
    {
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(PlainAttributedType), compatibilityMode: true), Is.True);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(AbstractAttributedModule), compatibilityMode: true), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(IAttributedModule), compatibilityMode: true), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(OpenGenericModule<>), compatibilityMode: true), Is.False);
        Assert.That(ModuleSystem.IsModuleTypeAllowed(typeof(UnattributedModule), compatibilityMode: true), Is.False);
    }

    [Module("安全测试模块", "实现 ISystemEvent 的有效模块")]
    sealed class SafeModule : ISystemEvent;

    [Module("兼容测试模块", "未实现 ISystemEvent 的旧式模块")]
    sealed class PlainAttributedType;

    [Module("抽象测试模块", "抽象模块不允许注册")]
    abstract class AbstractAttributedModule : ISystemEvent;

    [Module("接口测试模块", "接口不允许注册")]
    interface IAttributedModule : ISystemEvent;

    [Module("泛型测试模块", "开放泛型不允许注册")]
    sealed class OpenGenericModule<TValue> : ISystemEvent;

    sealed class UnattributedModule : ISystemEvent;
}
