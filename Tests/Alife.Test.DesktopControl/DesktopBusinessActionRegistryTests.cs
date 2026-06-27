using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopBusinessActionRegistryTests
{
    [Test]
    public void CreateDefault_ResolvesOnlyLowRiskOpenActions()
    {
        DesktopBusinessActionRegistry registry = DesktopBusinessActionRegistry.CreateDefault();

        bool notepadSupported = registry.TryResolve("  OPEN   notepad  ", out DesktopBusinessActionDescriptor? notepad);
        bool calculatorSupported = registry.TryResolve("open calculator", out DesktopBusinessActionDescriptor? calculator);
        bool unsupported = registry.TryResolve("open powershell", out DesktopBusinessActionDescriptor? unsupportedDescriptor);

        Assert.Multiple(() =>
        {
            Assert.That(notepadSupported, Is.True);
            Assert.That(notepad, Is.Not.Null);
            Assert.That(notepad!.ActionKey, Is.EqualTo("open_notepad"));
            Assert.That(notepad.NormalizedRequest, Is.EqualTo("open notepad"));
            Assert.That(notepad.ExecutableName, Is.EqualTo("notepad.exe"));
            Assert.That(notepad.Arguments, Is.Empty);
            Assert.That(calculatorSupported, Is.True);
            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator!.ActionKey, Is.EqualTo("open_calculator"));
            Assert.That(calculator.NormalizedRequest, Is.EqualTo("open calculator"));
            Assert.That(calculator.ExecutableName, Is.EqualTo("calc.exe"));
            Assert.That(calculator.Arguments, Is.Empty);
            Assert.That(unsupported, Is.False);
            Assert.That(unsupportedDescriptor, Is.Null);
        });
    }
}
