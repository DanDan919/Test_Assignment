using Dapper;
using Npgsql;
using System.Text;
using TestTask.Api.Validation;

namespace TestTask.Api.Data;

public sealed record ElementRecord(string AttributeValue, string Html);

public interface IElementStore
{
    Task SaveAsync(
        IReadOnlyList<ElementRecord> elements,
        CancellationToken cancellationToken);
}

public sealed class PostgresElementStore(NpgsqlDataSource dataSource) : IElementStore
{
    private const string InsertElementSql = """
        INSERT INTO public.elements (attribute_value, html)
        VALUES (@AttributeValue, @Html);
        """;

    public async Task SaveAsync(
        IReadOnlyList<ElementRecord> elements,
        CancellationToken cancellationToken)
    {
        if (elements.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Serialize budget checks with writes by this API, without deleting existing data.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(166830975);",
            transaction: transaction, commandTimeout: 10, cancellationToken: cancellationToken));
        var estimatedBytes = elements.Sum(element =>
            (long)Encoding.UTF8.GetByteCount(element.Html) + Encoding.UTF8.GetByteCount(element.AttributeValue) + 128);
        var withinBudget = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT pg_total_relation_size('public.elements') + @BatchBytes <= @Budget;",
            new { BatchBytes = estimatedBytes * 2 + 8192, Budget = ProcessingLimits.DatabaseBytes },
            transaction, commandTimeout: 10, cancellationToken: cancellationToken));
        if (!withinBudget)
        {
            throw new ProcessingLimitException();
        }

        foreach (var element in elements)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertElementSql,
                new
                {
                    element.AttributeValue,
                    element.Html
                },
                transaction,
                commandTimeout: 10,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
