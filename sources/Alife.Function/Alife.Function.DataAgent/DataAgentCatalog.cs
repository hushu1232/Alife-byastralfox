namespace Alife.Function.DataAgent;

public sealed class DataAgentCatalog
{
    readonly Dictionary<string, DataAgentDataset> datasets;

    DataAgentCatalog(IEnumerable<DataAgentDataset> datasets)
    {
        this.datasets = datasets.ToDictionary(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static DataAgentCatalog CreateDefault()
    {
        return new DataAgentCatalog(
        [
            DataAgentDataset.Create("engineering_gate", ["id", "name", "category", "required", "status", "evidence_path", "last_checked_at", "source"]),
            DataAgentDataset.Create("runtime_readiness_check", ["id", "capability", "account", "endpoint", "status", "required", "failure_reason", "last_checked_at", "evidence_path"]),
            DataAgentDataset.Create("module_capability", ["id", "module_name", "capability_name", "required", "status", "test_project", "evidence_path"]),
            DataAgentDataset.Create("test_run", ["id", "suite_name", "passed", "failed", "skipped", "total", "ran_at", "command"]),
            DataAgentDataset.Create("document_index", ["id", "path", "doc_type", "title", "summary", "tags", "updated_at"]),
            DataAgentDataset.Create("query_audit", ["id", "question", "dataset", "query_plan_json", "generated_sql", "validated", "rejected_reason", "row_count", "elapsed_ms", "created_at"])
        ]);
    }

    public bool HasDataset(string name) => datasets.ContainsKey(name);

    public bool HasField(string datasetName, string fieldName)
    {
        return datasets.TryGetValue(datasetName, out DataAgentDataset? dataset) &&
               dataset.Fields.Contains(fieldName);
    }

    public DataAgentDataset GetDataset(string name)
    {
        if (datasets.TryGetValue(name, out DataAgentDataset? dataset))
            return dataset;

        throw new InvalidOperationException($"Unknown DataAgent dataset '{name}'.");
    }
}

public sealed record DataAgentDataset(string Name, IReadOnlySet<string> Fields)
{
    public static DataAgentDataset Create(string name, IEnumerable<string> fields)
    {
        return new DataAgentDataset(
            name,
            fields.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
