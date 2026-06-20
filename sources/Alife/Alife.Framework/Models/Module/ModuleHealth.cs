namespace Alife.Framework;

public enum ModuleHealthStatus
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record ModuleHealth(
    string Name,
    ModuleHealthStatus Status,
    string Summary);

public interface IModuleHealthReporter
{
    ModuleHealth GetHealth();
}
