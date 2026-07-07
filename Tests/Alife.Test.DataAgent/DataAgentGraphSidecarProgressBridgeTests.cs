using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphSidecarProgressBridgeTests
{
    [Test]
    public void PublishMapsAcceptedSidecarProgressThroughSink()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentOrchestrationResult result = NewResult();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            result,
            [
                new DataAgentGraphSidecarProgressEvent(
                    request.RequestId,
                    request.SessionId,
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphSidecarProgressStatus.Completed,
                    "planner_suggested",
                    "planner ready",
                    Now().AddMinutes(-5),
                    new Dictionary<string, string>
                    {
                        ["stage"] = "planner"
                    })
            ]);

        DataAgentProgressEvent progress = sink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(1));
            Assert.That(summary.RejectedCount, Is.EqualTo(0));
            Assert.That(progress.SessionId, Is.EqualTo("session-1"));
            Assert.That(progress.Kind, Is.EqualTo(DataAgentProgressEventKind.Planner));
            Assert.That(progress.Phase, Is.EqualTo(DataAgentProgressEventPhase.Completed));
            Assert.That(progress.Status, Is.EqualTo(DataAgentProgressEventStatus.Succeeded));
            Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
            Assert.That(progress.TurnCount, Is.EqualTo(1));
            Assert.That(progress.CreatedAt, Is.EqualTo(Now()));
            Assert.That(progress.ExecutedSql, Is.False);
            Assert.That(progress.QueryAllowed, Is.True);
            Assert.That(progress.Terminal, Is.False);
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
            Assert.That(progress.Facts["node"], Is.EqualTo(DataAgentWorkflowNodeNames.QueryPlanner));
            Assert.That(progress.Facts["request_id"], Is.EqualTo(request.RequestId));
            Assert.That(progress.Facts["message"], Is.EqualTo("planner ready"));
            Assert.That(progress.Facts["stage"], Is.EqualTo("planner"));
        });
    }

    [Test]
    public void PublishRejectsCrossSessionResultWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentOrchestrationResult staleResult = NewResult() with
        {
            SessionId = "session-2",
            Checkpoint = NewResult().Checkpoint with
            {
                SessionId = "session-2"
            }
        };

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            staleResult,
            [
                SafeEvent(request)
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsCrossSessionResponseWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentOrchestrationResult staleResult = NewResult() with
        {
            Response = NewResult().Response with
            {
                SessionId = "session-2"
            }
        };

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            staleResult,
            [
                SafeEvent(request)
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [TestCase("source")]
    [TestCase("node")]
    [TestCase("request_id")]
    [TestCase("message")]
    [TestCase("Source")]
    [TestCase("NODE")]
    [TestCase("Request_Id")]
    [TestCase("Message")]
    public void PublishRejectsReservedFactKeyWithoutPublishing(string reservedFactKey)
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Message = string.Empty,
                    Facts = new Dictionary<string, string>
                    {
                        [reservedFactKey] = "sidecar controlled"
                    }
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [TestCase("host")]
    [TestCase("Host")]
    [TestCase("db_host")]
    [TestCase("SERVER_NAME")]
    [TestCase("username")]
    [TestCase("user_id")]
    [TestCase("uid")]
    [TestCase("data_source")]
    [TestCase("datasource")]
    [TestCase("dsn")]
    [TestCase("database")]
    [TestCase("db")]
    public void PublishRejectsSensitiveFactKeyWithoutPublishing(string sensitiveFactKey)
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Message = string.Empty,
                    Facts = new Dictionary<string, string>
                    {
                        [sensitiveFactKey] = "sidecar controlled"
                    }
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [TestCase("host=prod-db.internal")]
    [TestCase("server=prod-db.internal")]
    [TestCase("data source=prod-db.internal")]
    [TestCase("username=service_user")]
    [TestCase("user id=service_user")]
    [TestCase("uid=service_user")]
    public void PublishRejectsConnectionLikeFactValueWithoutPublishing(string unsafeFactValue)
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Message = string.Empty,
                    Facts = new Dictionary<string, string>
                    {
                        ["stage"] = unsafeFactValue
                    }
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishHandshakeProgressGuardsNullRequestAndResultBeforeAdaptingProgress()
    {
        DataAgentGraphSidecarProgressBridge bridge = new(clock: Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        IReadOnlyList<DataAgentGraphHandshakeProgress> progress =
        [
            new DataAgentGraphHandshakeProgress(
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                DataAgentGraphHandshakeProgressStatus.Started,
                "scenario_started")
        ];

        ArgumentNullException? nullRequest = Assert.Throws<ArgumentNullException>(() =>
            bridge.PublishHandshakeProgress(null!, NewResult(), progress));
        ArgumentNullException? nullResult = Assert.Throws<ArgumentNullException>(() =>
            bridge.PublishHandshakeProgress(request, null!, progress));

        Assert.Multiple(() =>
        {
            Assert.That(nullRequest?.ParamName, Is.EqualTo("request"));
            Assert.That(nullResult?.ParamName, Is.EqualTo("result"));
        });
    }

    [Test]
    public void PublishRejectsUnknownNodeWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    NodeName = "unknown_node"
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsUndefinedStatusWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Status = (DataAgentGraphSidecarProgressStatus)999
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsUnsafeReasonCodeWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    ReasonCode = "planner suggested"
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsUnsafeMessageAndFactsBeforeFormatting()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult unsafeMessage = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Message = "SELECT * FROM engineering_gate"
                }
            ]);
        DataAgentGraphSidecarProgressBridgeResult unsafeFactKey = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Facts = new Dictionary<string, string>
                    {
                        ["hidden_context"] = "[hidden_context]secret[/hidden_context]"
                    }
                }
            ]);
        DataAgentGraphSidecarProgressBridgeResult unsafeFactValue = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Facts = new Dictionary<string, string>
                    {
                        ["stage"] = "Bearer sk-test123456"
                    }
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(unsafeMessage.AcceptedCount, Is.EqualTo(0));
            Assert.That(unsafeMessage.RejectedCount, Is.EqualTo(1));
            Assert.That(unsafeFactKey.AcceptedCount, Is.EqualTo(0));
            Assert.That(unsafeFactKey.RejectedCount, Is.EqualTo(1));
            Assert.That(unsafeFactValue.AcceptedCount, Is.EqualTo(0));
            Assert.That(unsafeFactValue.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishFailsClosedForOverBudgetInput()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        Dictionary<string, string> tooManyFacts = Enumerable.Range(0, 9)
            .ToDictionary(index => $"fact_{index}", index => $"value_{index}");
        DataAgentGraphSidecarProgressEvent[] tooManyEvents = Enumerable.Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 1)
            .Select(_ => SafeEvent(request))
            .ToArray();

        DataAgentGraphSidecarProgressBridgeResult factSummary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Facts = tooManyFacts
                }
            ]);
        DataAgentGraphSidecarProgressBridgeResult eventSummary = bridge.Publish(
            request,
            NewResult(),
            tooManyEvents);

        Assert.Multiple(() =>
        {
            Assert.That(factSummary.AcceptedCount, Is.EqualTo(0));
            Assert.That(factSummary.RejectedCount, Is.EqualTo(1));
            Assert.That(eventSummary.AcceptedCount, Is.EqualTo(0));
            Assert.That(eventSummary.RejectedCount, Is.EqualTo(DataAgentGraphHandshakeLimits.MaxProgressEvents + 1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishHandshakeProgressMapsResponseNodeProgress()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.PublishHandshakeProgress(
            request,
            NewResult(),
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.ScenarioKnowledge,
                    DataAgentGraphHandshakeProgressStatus.Started,
                    "scenario_started",
                    "scenario context ready",
                    new Dictionary<string, string>
                    {
                        ["stage"] = "scenario"
                    })
            ]);

        DataAgentProgressEvent progress = sink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(1));
            Assert.That(summary.RejectedCount, Is.EqualTo(0));
            Assert.That(progress.Kind, Is.EqualTo(DataAgentProgressEventKind.SchemaContext));
            Assert.That(progress.Phase, Is.EqualTo(DataAgentProgressEventPhase.Started));
            Assert.That(progress.Status, Is.EqualTo(DataAgentProgressEventStatus.Running));
            Assert.That(progress.ExecutedSql, Is.False);
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
            Assert.That(progress.Facts["node"], Is.EqualTo(DataAgentWorkflowNodeNames.ScenarioKnowledge));
            Assert.That(progress.Facts["message"], Is.EqualTo("scenario context ready"));
            Assert.That(progress.Facts["stage"], Is.EqualTo("scenario"));
        });
    }

    static DataAgentGraphSidecarProgressEvent SafeEvent(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphSidecarProgressEvent(
            request.RequestId,
            request.SessionId,
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentGraphSidecarProgressStatus.Completed,
            "planner_suggested",
            "planner ready",
            Now().AddMinutes(-5),
            new Dictionary<string, string>
            {
                ["stage"] = "planner"
            });
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "graph-handshake-session-1-turn-1",
            "session-1",
            "turn-1",
            "owner",
            "Which gates failed?",
            "scenario_context=deterministic_csharp",
            "route_present=true;route_allows_query=true;route_reason_code=route_allowed",
            "status=Active;executed_sql=false;terminal=false",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentOrchestrationResult NewResult()
    {
        DataAgentAnswer answer = new(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
        DataAgentAnalysisResponse response = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            answer,
            answer.Summary,
            answer.Context,
            Accepted: true,
            RejectedReason: string.Empty);
        DataAgentOrchestrationCheckpoint checkpoint = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            "document_index",
            TurnCount: 1,
            CanContinue: true,
            CanSummarize: true,
            Terminal: false);
        DataAgentToolRouteContext routeContext = new(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-test",
            "analysis_start",
            "route_allowed",
            string.Empty);

        return new DataAgentOrchestrationResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_ready", ExecutedSql: false)
            ],
            checkpoint,
            response,
            routeContext);
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
    }

    sealed class RecordingProgressSink : IDataAgentProgressSink
    {
        public List<DataAgentProgressEvent> Events { get; } = [];

        public void Publish(DataAgentProgressEvent? progressEvent)
        {
            if (progressEvent is not null)
                Events.Add(progressEvent);
        }
    }
}
