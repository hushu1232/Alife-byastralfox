namespace Alife.Function.DataAgent;

public sealed class DataAgentQueryPlanValidator(DataAgentCatalog catalog)
{
    static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "=",
        "!=",
        "<>",
        ">",
        ">=",
        "<",
        "<=",
        "contains"
    };

    static readonly HashSet<string> AllowedDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc"
    };

    public DataAgentValidationResult Validate(DataAgentQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        List<string> errors = [];

        if (catalog.HasDataset(plan.Dataset) == false)
        {
            errors.Add($"unknown_dataset:{plan.Dataset}");
            return DataAgentValidationResult.FromErrors(errors);
        }

        foreach (string field in plan.Select)
        {
            if (catalog.HasField(plan.Dataset, field) == false)
                errors.Add($"unknown_select_field:{plan.Dataset}.{field}");
        }

        foreach (DataAgentFilter filter in plan.Filters)
        {
            if (catalog.HasField(plan.Dataset, filter.Field) == false)
                errors.Add($"unknown_filter_field:{plan.Dataset}.{filter.Field}");

            if (AllowedOperators.Contains(filter.Operator) == false)
                errors.Add($"unsupported_operator:{filter.Operator}");
        }

        foreach (DataAgentOrderBy orderBy in plan.OrderBy)
        {
            if (catalog.HasField(plan.Dataset, orderBy.Field) == false)
                errors.Add($"unknown_order_field:{plan.Dataset}.{orderBy.Field}");

            if (AllowedDirections.Contains(orderBy.Direction) == false)
                errors.Add($"unsupported_order_direction:{orderBy.Direction}");
        }

        if (plan.Limit is < 1 or > 100)
            errors.Add($"invalid_limit:{plan.Limit}");

        return DataAgentValidationResult.FromErrors(errors);
    }
}

public sealed record DataAgentValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static DataAgentValidationResult FromErrors(IReadOnlyList<string> errors)
    {
        return new DataAgentValidationResult(errors.Count == 0, errors);
    }
}
