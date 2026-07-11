namespace Alife.Function.DataAgent;

public sealed class DataAgentV44ProductionShadowException : Exception
{
    public DataAgentV44ProductionShadowException(string reasonCode, bool networkAttempted)
        : base(reasonCode)
    {
        ReasonCode = reasonCode;
        NetworkAttempted = networkAttempted;
    }

    public string ReasonCode { get; }

    public bool NetworkAttempted { get; }
}

public sealed record DataAgentV44ProductionShadowSnapshot(
    int ActiveCalls,
    int ConsecutiveFailures,
    bool CircuitOpen,
    string LastReasonCode);

public sealed class DataAgentV44ProductionShadowClient : IDataAgentGraphSidecarClient, IDisposable
{
    const string DisabledReason = "production_shadow_disabled";
    const string KillSwitchReason = "production_shadow_kill_switch_active";
    const string ValueGateReason = "production_shadow_value_gate_failed";
    const string CircuitOpenReason = "production_shadow_circuit_open";
    const string BusyReason = "production_shadow_busy";
    const string TimeoutReason = "production_shadow_timeout";
    const string UnavailableReason = "production_shadow_unavailable";
    const string AcceptedReason = "production_shadow_accepted";

    readonly IDataAgentGraphSidecarClient innerClient;
    readonly DataAgentV44ProductionShadowOptions options;
    readonly Func<DateTimeOffset> clock;
    readonly SemaphoreSlim concurrency;
    readonly object stateLock = new();
    int activeCalls;
    int consecutiveFailures;
    DateTimeOffset? circuitOpenUntil;
    string lastReasonCode = DisabledReason;
    bool disposed;

    public DataAgentV44ProductionShadowClient(
        IDataAgentGraphSidecarClient innerClient,
        DataAgentV44ProductionShadowOptions options,
        Func<DateTimeOffset>? clock = null)
    {
        this.innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        concurrency = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
    }

    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfNotReady();
        ThrowIfCircuitOpen();

        if (concurrency.Wait(0) == false)
            throw Failure(BusyReason, networkAttempted: false, recordReason: true);

        lock (stateLock)
            activeCalls++;

        try
        {
            DataAgentGraphHandshakeResponse response = innerClient.TryHandshake(request);
            lock (stateLock)
            {
                consecutiveFailures = 0;
                circuitOpenUntil = null;
                lastReasonCode = AcceptedReason;
            }

            return response;
        }
        catch (TimeoutException)
        {
            RecordTransportFailure(TimeoutReason);
            throw Failure(TimeoutReason, networkAttempted: true, recordReason: false);
        }
        catch (DataAgentV44ProductionShadowException)
        {
            throw;
        }
        catch (Exception)
        {
            RecordTransportFailure(UnavailableReason);
            throw Failure(UnavailableReason, networkAttempted: true, recordReason: false);
        }
        finally
        {
            lock (stateLock)
                activeCalls--;
            concurrency.Release();
        }
    }

    public DataAgentV44ProductionShadowSnapshot GetSnapshot()
    {
        lock (stateLock)
        {
            return new DataAgentV44ProductionShadowSnapshot(
                activeCalls,
                consecutiveFailures,
                IsCircuitOpen(clock()),
                lastReasonCode);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        concurrency.Dispose();
        disposed = true;
    }

    void ThrowIfNotReady()
    {
        if (options.Enabled == false)
            throw Failure(DisabledReason, networkAttempted: false, recordReason: true);
        if (options.KillSwitchActive)
            throw Failure(KillSwitchReason, networkAttempted: false, recordReason: true);
        if (options.ValueGatePassed == false)
            throw Failure(ValueGateReason, networkAttempted: false, recordReason: true);
    }

    void ThrowIfCircuitOpen()
    {
        lock (stateLock)
        {
            if (IsCircuitOpen(clock()) == false)
                return;

            lastReasonCode = CircuitOpenReason;
        }

        throw Failure(CircuitOpenReason, networkAttempted: false, recordReason: false);
    }

    bool IsCircuitOpen(DateTimeOffset now)
    {
        if (circuitOpenUntil is null)
            return false;
        if (now < circuitOpenUntil.Value)
            return true;

        circuitOpenUntil = null;
        return false;
    }

    void RecordTransportFailure(string reasonCode)
    {
        lock (stateLock)
        {
            consecutiveFailures++;
            lastReasonCode = reasonCode;
            if (consecutiveFailures >= options.FailureThreshold)
                circuitOpenUntil = clock().Add(options.CircuitOpenDuration);
        }
    }

    DataAgentV44ProductionShadowException Failure(
        string reasonCode,
        bool networkAttempted,
        bool recordReason)
    {
        if (recordReason)
        {
            lock (stateLock)
                lastReasonCode = reasonCode;
        }

        return new DataAgentV44ProductionShadowException(reasonCode, networkAttempted);
    }
}
