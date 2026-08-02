namespace Alife.Function.DeskPet;

public record DeskPetServiceConfig
{
    public string ModelName { get; set; } = "Mao";
    public string? ClientExecutablePath { get; set; }
    public bool EnableEmotionParameterSync { get; set; } = true;
    public int EmotionSyncIntervalMilliseconds { get; set; } = 250;
}
