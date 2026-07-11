namespace Alife.Function.LocalRuntime;

public enum LocalAccountHealth { Healthy, Degraded, Unavailable, Draining }
public enum CapabilityKind { Speech, Vision, Browser, LangGraph }
public enum DurableTaskState { Queued, Starting, Ready, Running, Completed, TimedOut, Failed, Cancelled, Degraded }
public enum SafeReasonCode { None, Busy, DeadlineExceeded, DependencyUnavailable, HealthProbeFailed, RestartRecoveryRequired, ConfigurationRejected }
public sealed record LocalAccountSlot(string Id, Uri OneBotUrl, string RuntimeRoot, string StorageRoot, string TempRoot);
public sealed record LocalProductionPlan(IReadOnlyList<LocalAccountSlot> Accounts, int MaxQueueDepth, TimeSpan DrainTimeout, TimeSpan IdleTimeout);
public sealed record SafeLocalStatus(string Overall, IReadOnlyDictionary<string, LocalAccountHealth> Accounts, IReadOnlyDictionary<CapabilityKind, string> Capabilities, SafeReasonCode Reason);
public sealed class LocalProductionConfigurationException(string message) : Exception(message);
