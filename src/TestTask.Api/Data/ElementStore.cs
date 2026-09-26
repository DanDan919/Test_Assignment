using Dapper;
using Npgsql;

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
        INSERT INTO elements (attribute_value, html)
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
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
