using System.IO;
using System.Text.Json;
using Alife.Function.DeskPet;

namespace Alife.Test.DeskPet;

public class PetProcessLive2DProtocolTests
{
    [Test]
    public void Live2DParameterCommandsSerializeToPetJsProtocol()
    {
        AssertCommandJson(
            new ParamCommand("ParamAngleX", 12.5f),
            """{"$type":"param","Id":"ParamAngleX","Value":12.5}""");
        AssertCommandJson(
            new ParamsCommand(new Dictionary<string, float>
            {
                ["ParamAngleX"] = 10f,
                ["ParamEyeLOpen"] = 0.5f
            }),
            """{"$type":"params","Params":{"ParamAngleX":10,"ParamEyeLOpen":0.5}}""");
        AssertCommandJson(
            new LipSyncCommand(0.75f),
            """{"$type":"lip-sync","Value":0.75}""");
        AssertCommandJson(
            new IdleCycleCommand(false, new Dictionary<string, float>
            {
                ["blinkInterval"] = 3000f
            }),
            """{"$type":"idle-cycle","Enabled":false,"Params":{"blinkInterval":3000}}""");
        AssertCommandJson(
            new GetParamsCommand(),
            """{"$type":"get-params"}""");
    }

    [Test]
    public void ParamsListEventDeserializesFromPetJsProtocol()
    {
        const string json = """
            {
              "$type": "params-list",
              "params": {
                "ParamAngleX": { "value": 12, "min": -30, "max": 30 },
                "ParamEyeLOpen": { "value": 0.5, "min": 0, "max": 1 }
              }
            }
            """;

        IpcEvent? ipcEvent = JsonSerializer.Deserialize<IpcEvent>(json, PetProcess.JsonOptions);

        Assert.That(ipcEvent, Is.TypeOf<ParamsListEvent>());
        ParamsListEvent paramsListEvent = (ParamsListEvent)ipcEvent!;
        Assert.That(paramsListEvent.Params["ParamAngleX"].Value, Is.EqualTo(12f));
        Assert.That(paramsListEvent.Params["ParamAngleX"].Min, Is.EqualTo(-30f));
        Assert.That(paramsListEvent.Params["ParamAngleX"].Max, Is.EqualTo(30f));
        Assert.That(paramsListEvent.Params["ParamEyeLOpen"].Value, Is.EqualTo(0.5f));
    }

    static void AssertCommandJson(IpcCommand command, string expectedJson)
    {
        using StringWriter writer = new();
        using StringReader reader = new("");
        using PetProcess process = new(writer, reader);

        process.SendInput(command);

        Assert.That(writer.ToString().Trim(), Is.EqualTo(expectedJson));
    }
}
