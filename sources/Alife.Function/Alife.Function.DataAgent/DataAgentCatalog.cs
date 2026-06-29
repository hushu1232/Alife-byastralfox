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
            DataAgentDataset.Create(
                "engineering_gate",
                ["id", "name", "category", "required", "status", "evidence_path", "last_checked_at", "source"],
                new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = DataAgentFieldType.Integer,
                    ["required"] = DataAgentFieldType.Boolean
                }),
            DataAgentDataset.Create(
                "runtime_readiness_check",
                ["id", "capability", "account", "endpoint", "status", "required", "failure_reason", "last_checked_at", "evidence_path"],
                new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = DataAgentFieldType.Integer,
                    ["required"] = DataAgentFieldType.Boolean
                }),
            DataAgentDataset.Create(
                "module_capability",
                ["id", "module_name", "capability_name", "required", "status", "test_project", "evidence_path"],
                new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = DataAgentFieldType.Integer,
                    ["required"] = DataAgentFieldType.Boolean
                }),
            DataAgentDataset.Create(
                "test_run",
                ["id", "suite_name", "passed", "failed", "skipped", "total", "ran_at", "command"],
                new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = DataAgentFieldType.Integer,
                    ["passed"] = DataAgentFieldType.Integer,
                    ["failed"] = DataAgentFieldType.Integer,
                    ["skipped"] = DataAgentFieldType.Integer,
                    ["total"] = DataAgentFieldType.Integer
                }),
            DataAgentDataset.Create(
                "document_index",
                ["id", "path", "doc_type", "title", "summary", "tags", "updated_at"],
                new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = DataAgentFieldType.Integer
                }),
            DataAgentDataset.Create(
                "query_audit",
                ["id", "question", "dataset", "query_plan_json", "generated_sql", "validated", "rejected_reason", "row_count", "elapsed_ms", "created_at"],
                new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = DataAgentFieldType.Integer,
                    ["validated"] = DataAgentFieldType.Boolean,
                    ["row_count"] = DataAgentFieldType.Integer,
                    ["elapsed_ms"] = DataAgentFieldType.Integer
                })
        ]);
    }

    public bool HasDataset(string name) => datasets.ContainsKey(name);

    public IReadOnlyList<DataAgentDataset> Datasets => datasets.Values
        .OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool HasField(string datasetName, string fieldName)
    {
        return datasets.TryGetValue(datasetName, out DataAgentDataset? dataset) &&
               dataset.Fields.Contains(fieldName);
    }

    public bool IsTextField(string datasetName, string fieldName)
    {
        return datasets.TryGetValue(datasetName, out DataAgentDataset? dataset) &&
               dataset.Fields.Contains(fieldName) &&
               dataset.IsTextField(fieldName);
    }

    public DataAgentDataset GetDataset(string name)
    {
        if (datasets.TryGetValue(name, out DataAgentDataset? dataset))
            return dataset;

        throw new InvalidOperationException($"Unknown DataAgent dataset '{name}'.");
    }
}

public enum DataAgentFieldType
{
    Text,
    Boolean,
    Integer
}

public sealed record DataAgentDataset(
    string Name,
    IReadOnlySet<string> Fields,
    IReadOnlyDictionary<string, DataAgentFieldType> FieldTypes)
{
    public static DataAgentDataset Create(
        string name,
        IEnumerable<string> fields,
        IReadOnlyDictionary<string, DataAgentFieldType>? fieldTypes = null)
    {
        Dictionary<string, DataAgentFieldType> normalizedFieldTypes = fieldTypes is null
            ? new Dictionary<string, DataAgentFieldType>(StringComparer.OrdinalIgnoreCase)
            : fieldTypes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return new DataAgentDataset(
            name,
            fields.ToHashSet(StringComparer.OrdinalIgnoreCase),
            normalizedFieldTypes);
    }

    public bool IsTextField(string fieldName)
    {
        return GetFieldType(fieldName) == DataAgentFieldType.Text;
    }

    public DataAgentFieldType GetFieldType(string fieldName)
    {
        if (Fields.Contains(fieldName) == false)
            throw new InvalidOperationException($"Unknown DataAgent field '{Name}.{fieldName}'.");

        return FieldTypes.TryGetValue(fieldName, out DataAgentFieldType fieldType)
            ? fieldType
            : DataAgentFieldType.Text;
    }
}
