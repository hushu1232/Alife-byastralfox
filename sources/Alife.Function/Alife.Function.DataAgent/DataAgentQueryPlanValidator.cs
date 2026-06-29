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

        DataAgentDataset dataset = catalog.GetDataset(plan.Dataset);

        foreach (string field in plan.Select)
        {
            if (catalog.HasField(plan.Dataset, field) == false)
                errors.Add($"unknown_select_field:{plan.Dataset}.{field}");
        }

        foreach (DataAgentFilter filter in plan.Filters)
        {
            bool fieldExists = catalog.HasField(plan.Dataset, filter.Field);
            if (fieldExists == false)
            {
                errors.Add($"unknown_filter_field:{plan.Dataset}.{filter.Field}");
            }
            else if (filter.Operator.Equals("contains", StringComparison.OrdinalIgnoreCase) &&
                     catalog.IsTextField(plan.Dataset, filter.Field) == false)
            {
                errors.Add($"unsupported_operator_for_field:contains:{plan.Dataset}.{filter.Field}");
            }
            else
            {
                DataAgentFieldType fieldType = dataset.GetFieldType(filter.Field);
                if (IsFilterValueCompatible(fieldType, filter.Value) == false)
                    errors.Add($"invalid_filter_value_type:{plan.Dataset}.{filter.Field}:{FieldTypeName(fieldType)}");
            }

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

    static bool IsFilterValueCompatible(DataAgentFieldType fieldType, object? value)
    {
        if (value is null)
            return false;

        return fieldType switch
        {
            DataAgentFieldType.Text => value is string,
            DataAgentFieldType.Boolean => value is bool,
            DataAgentFieldType.Integer => IsInt32Compatible(value),
            _ => false
        };
    }

    static bool IsInt32Compatible(object value)
    {
        return value switch
        {
            byte => true,
            sbyte => true,
            short => true,
            ushort => true,
            int => true,
            uint unsigned => unsigned <= int.MaxValue,
            long signed => signed is >= int.MinValue and <= int.MaxValue,
            ulong unsigned => unsigned <= int.MaxValue,
            _ => false
        };
    }

    static string FieldTypeName(DataAgentFieldType fieldType)
    {
        return fieldType switch
        {
            DataAgentFieldType.Text => "text",
            DataAgentFieldType.Boolean => "boolean",
            DataAgentFieldType.Integer => "integer",
            _ => fieldType.ToString().ToLowerInvariant()
        };
    }
}

public sealed record DataAgentValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static DataAgentValidationResult FromErrors(IReadOnlyList<string> errors)
    {
        return new DataAgentValidationResult(errors.Count == 0, errors);
    }
}
