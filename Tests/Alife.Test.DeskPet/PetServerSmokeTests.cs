using Alife.Function.DeskPet;

namespace Alife.Test.DeskPet;

[TestFixture]
[Category("Integration")]
public class PetServerSmokeTests
{
    [Test]
    public async Task WaitReadyAndRoundTrip_WorksWithoutManualVerification()
    {
        string? clientExecutablePath = Environment.GetEnvironmentVariable("ALIFE_DESKPET_CLIENT_EXECUTABLE");
        await using PetServer server = new("Mao", clientExecutablePath);

        await server.WaitReadyAsync();
        (double x, double y) = await server.GetPositionAsync();
        Assert.That(double.IsFinite(x) && double.IsFinite(y), Is.True);

        server.ShowBubble("DeskPet smoke test");
        await Task.Delay(200);
        server.HideBubble();
    }
}
