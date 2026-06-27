namespace Alife.Function.DataAgent;

public sealed class DataAgentSqlCompiler(DataAgentCatalog catalog)
{
    public DataAgentCompiledSql Compile(DataAgentQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        DataAgentDataset dataset = catalog.GetDataset(plan.Dataset);
        List<DataAgentSqlParameter> parameters = [];

        string selectClause = string.Join(", ", plan.Select.Select(field => KnownField(dataset, field)));
        string sql = $"SELECT {selectClause} FROM {dataset.Name}";

        if (plan.Filters.Count > 0)
        {
            IEnumerable<string> filters = plan.Filters.Select(filter => CompileFilter(dataset, filter, parameters));
            sql += " WHERE " + string.Join(" AND ", filters);
        }

        if (plan.OrderBy.Count > 0)
        {
            IEnumerable<string> orderBy = plan.OrderBy.Select(order =>
                $"{KnownField(dataset, order.Field)} {order.Direction.ToUpperInvariant()}");
            sql += " ORDER BY " + string.Join(", ", orderBy);
        }

        sql += $" LIMIT {plan.Limit}";

        return new DataAgentCompiledSql(sql, parameters);
    }

    static string CompileFilter(DataAgentDataset dataset, DataAgentFilter filter, List<DataAgentSqlParameter> parameters)
    {
        string field = KnownField(dataset, filter.Field);
        string parameterName = $"@p{parameters.Count}";

        if (filter.Operator.Equals("contains", StringComparison.OrdinalIgnoreCase))
        {
            parameters.Add(new DataAgentSqlParameter(parameterName, $"%{filter.Value}%"));
            return $"{field} LIKE {parameterName}";
        }

        string sqlOperator = filter.Operator == "!=" ? "<>" : filter.Operator;
        parameters.Add(new DataAgentSqlParameter(parameterName, filter.Value));
        return $"{field} {sqlOperator} {parameterName}";
    }

    static string KnownField(DataAgentDataset dataset, string field)
    {
        if (dataset.Fields.Contains(field))
            return field;

        throw new InvalidOperationException($"Unknown DataAgent field '{dataset.Name}.{field}'.");
    }
}

public sealed record DataAgentCompiledSql(string Sql, IReadOnlyList<DataAgentSqlParameter> Parameters);

public sealed record DataAgentSqlParameter(string Name, object? Value);
