using System.Text;

namespace Alife.Function.DataAgent;

public sealed record DataAgentLlmPlannerPrompt(
    string System,
    string Schema,
    string Contract,
    string User);

public sealed class LlmDataAgentPlannerPromptFormatter
{
    static readonly string[] AllowedOperators = ["=", "!=", "<>", ">", ">=", "<", "<=", "contains"];

    public DataAgentLlmPlannerPrompt Format(
        DataAgentQueryRequest request,
        DataAgentCatalog catalog,
        DataAgentSchemaSnapshot schemaSnapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(schemaSnapshot);

        if (schemaSnapshot.CatalogMatchesDatabase == false)
            throw new InvalidOperationException("DataAgent LLM planner requires catalog and SQLite schema to match.");

        return new DataAgentLlmPlannerPrompt(
            BuildSystem(),
            BuildSchema(catalog, schemaSnapshot),
            BuildContract(),
            BuildUser(request));
    }

    static string BuildSystem()
    {
        StringBuilder builder = new();
        builder.AppendLine("You are the DataAgent LLM planner.");
        builder.AppendLine("Return JSON only.");
        builder.AppendLine("Do not output SQL.");
        builder.AppendLine("Use only approved datasets, fields, and operators from the schema section.");
        builder.Append("Allowed operators: ");
        builder.AppendJoin(", ", AllowedOperators);
        builder.Append('.');
        return builder.ToString();
    }

    static string BuildSchema(DataAgentCatalog catalog, DataAgentSchemaSnapshot schemaSnapshot)
    {
        StringBuilder builder = new();
        builder.AppendLine("Approved schema:");

        foreach (DataAgentDatasetSchema datasetSchema in schemaSnapshot.Datasets
            .Where(dataset => catalog.HasDataset(dataset.Name) && dataset.ExistsInDatabase && dataset.FieldsMatch)
            .OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase))
        {
            string[] fields = datasetSchema.DatabaseFields
                .Where(field => catalog.HasField(datasetSchema.Name, field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (fields.Length == 0)
                continue;

            builder.Append("- ");
            builder.Append(datasetSchema.Name);
            builder.Append(": ");
            builder.AppendJoin(", ", fields);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    static string BuildContract()
    {
        StringBuilder builder = new();
        builder.AppendLine("Return exactly one JSON object matching one of these examples.");
        builder.AppendLine("""{"type":"plan","dataset":"document_index","intent":"find_documents","select":["path","summary"],"filters":[{"field":"summary","operator":"contains","value":"DataAgent"}],"orderBy":[{"field":"updated_at","direction":"desc"}],"limit":20}""");
        builder.Append("""{"type":"clarification","question":"Which dataset should I use?","options":["document_index","test_run"],"reason":"The question is ambiguous."}""");
        return builder.ToString();
    }

    static string BuildUser(DataAgentQueryRequest request)
    {
        StringBuilder builder = new();
        builder.AppendLine("Question: " + request.Question);
        builder.AppendLine("Role: " + request.Role);
        builder.AppendLine("Locale: " + request.Locale);
        builder.Append("AllowLiveSources: ");
        builder.Append(request.AllowLiveSources);
        return builder.ToString();
    }
}
