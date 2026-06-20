using Alife.Function.DeskPet;

namespace Alife.Test.DeskPet;

[TestFixture]
[Category("Integration")]
public class PetServerSmokeTests
{
    [Test]
    public async Task WaitReadyAndSendBubble_WorksWithoutManualVerification()
    {
        await using PetServer server = new("Mao");

        await server.WaitReadyAsync();
        server.ShowBubble("DeskPet smoke test");
        await Task.Delay(200);
        server.HideBubble();
    }
}
