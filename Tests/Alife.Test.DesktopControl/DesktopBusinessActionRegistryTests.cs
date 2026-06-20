using Alife.Function.DesktopControl;

namespace Alife.Test.DesktopControl;

public sealed class DesktopBusinessActionRegistryTests
{
    [Test]
    public void CreateDefault_ResolvesOnlyOpenNotepad()
    {
        DesktopBusinessActionRegistry registry = DesktopBusinessActionRegistry.CreateDefault();

        bool supported = registry.TryResolve("  OPEN   notepad  ", out DesktopBusinessActionDescriptor? descriptor);
        bool unsupported = registry.TryResolve("open calculator", out DesktopBusinessActionDescriptor? unsupportedDescriptor);

        Assert.Multiple(() =>
        {
            Assert.That(supported, Is.True);
            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor!.ActionKey, Is.EqualTo("open_notepad"));
            Assert.That(descriptor.NormalizedRequest, Is.EqualTo("open notepad"));
            Assert.That(descriptor.ExecutableName, Is.EqualTo("notepad.exe"));
            Assert.That(descriptor.Arguments, Is.Empty);
            Assert.That(unsupported, Is.False);
            Assert.That(unsupportedDescriptor, Is.Null);
        });
    }
}
