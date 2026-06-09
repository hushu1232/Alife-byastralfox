using Alife.Function.Emotion;

namespace Alife.Test.Framework;

public class PadEmotionEngineTests
{
    [Test]
    public void PredefinedEventsAdjustPadValuesAndClampToRange()
    {
        PADEmotionEngine engine = new();

        engine.ApplyEventType(EmotionEventType.Complimented);
        engine.ApplyEventType(EmotionEventType.Fed);
        engine.ModulatePAD(2f, 2f, 2f);

        Assert.That(engine.RawPleasure, Is.EqualTo(1f));
        Assert.That(engine.RawArousal, Is.EqualTo(1f));
        Assert.That(engine.RawDominance, Is.EqualTo(1f));
        Assert.That(PADEmotionEngine.GetPredefinedEvent(EmotionEventType.Petted).PleasureDelta, Is.EqualTo(0.3f));
    }

    [Test]
    public void UpdateDecaysRawPadValuesTowardConfiguredBaselines()
    {
        PADEmotionEngine engine = new()
        {
            Configuration = new EmotionConfig
            {
                InitialPleasure = 0.8f,
                InitialArousal = -0.6f,
                InitialDominance = 0.8f,
                PleasureBaseline = 0.2f,
                ArousalBaseline = 0.1f,
                DominanceBaseline = 0.1f,
                PleasureDecayRate = 0.1f,
                ArousalDecayRate = 0.1f,
                DominanceDecayRate = 0.1f,
                SmoothTime = 0.15f
            }
        };
        float elapsedSeconds = 1f;

        engine.OnUpdate(ref elapsedSeconds);

        Assert.That(engine.RawPleasure, Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(engine.RawArousal, Is.EqualTo(-0.5f).Within(0.0001f));
        Assert.That(engine.RawDominance, Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(engine.Pleasure, Is.LessThan(0.8f));
    }

    [Test]
    public void PadMappingAndPromptContextMatchAstralFoxEmotionModel()
    {
        Assert.That(PADEmotionEngine.PADToEmotion(0.7f, 0.5f, 0.4f), Is.EqualTo(PetEmotion.Happy));
        Assert.That(PADEmotionEngine.PADToEmotion(-0.7f, -0.4f, -0.5f), Is.EqualTo(PetEmotion.Sad));
        Assert.That(PADEmotionEngine.PADToEmotion(0.1f, -0.3f, -0.5f), Is.EqualTo(PetEmotion.Shy));
        Assert.That(PADEmotionEngine.PADToEmotion(-0.6f, 0.5f, 0.5f), Is.EqualTo(PetEmotion.Angry));

        PADEmotionEngine engine = new();
        engine.ApplyEventType(EmotionEventType.Petted);

        string context = engine.GetEmotionPromptContext();

        Assert.That(context, Does.Contain("当前情绪"));
        Assert.That(context, Does.Contain("心情"));
        Assert.That(context, Does.Contain("精力"));
    }

    [Test]
    public void EmotionParameterMapperConvertsPadToLive2DParameters()
    {
        EmotionParameterMapper mapper = new();

        Dictionary<string, float> parameters = mapper.MapEmotionToParams(0.6f, 0.5f, 0.2f);

        Assert.That(parameters["ParamAngleX"], Is.EqualTo(6f).Within(0.0001f));
        Assert.That(parameters["ParamAngleY"], Is.EqualTo(2.5f).Within(0.0001f));
        Assert.That(parameters["ParamMouthOpenY"], Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(parameters["ParamEyeLOpen"], Is.EqualTo(0.85f).Within(0.0001f));
        Assert.That(mapper.MapEmotionToExpression(PetEmotion.Happy), Is.EqualTo("happy"));
    }

    [Test]
    public void EmotionLive2DDriverPushesCurrentPadParametersToSink()
    {
        PADEmotionEngine engine = new()
        {
            Configuration = new EmotionConfig
            {
                InitialPleasure = 0.4f,
                InitialArousal = 0.5f,
                InitialDominance = 0.2f
            }
        };
        CapturingEmotionParameterSink sink = new();
        EmotionLive2DParameterDriver driver = new(engine, sink);

        Dictionary<string, float> parameters = driver.PushCurrentState();

        Assert.That(sink.LastParameters, Is.SameAs(parameters));
        Assert.That(parameters["ParamAngleX"], Is.EqualTo(4f).Within(0.0001f));
        Assert.That(parameters["ParamAngleY"], Is.EqualTo(2.5f).Within(0.0001f));
        Assert.That(parameters["ParamMouthOpenY"], Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(parameters["ParamEyeLOpen"], Is.EqualTo(0.85f).Within(0.0001f));
    }

    sealed class CapturingEmotionParameterSink : IEmotionParameterSink
    {
        public Dictionary<string, float>? LastParameters { get; private set; }

        public void SetParams(Dictionary<string, float> parameters)
        {
            LastParameters = parameters;
        }
    }
}
