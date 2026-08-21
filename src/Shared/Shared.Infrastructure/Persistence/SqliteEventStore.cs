using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shared.Domain.Events;

namespace Shared.Infrastructure.Persistence;

public class SqliteEventStore
{
    private readonly SqliteDatabase _database;

    public SqliteEventStore(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task CreatePendingOrderWithOutboxAsync(
        string orderId, string symbol, string side, int quantity, decimal price,
        DateTime createdAt, string topic, object createdEvent)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        InsertOrder(connection, transaction, orderId, symbol, side, quantity, price, createdAt);
        InsertOutboxMessage(connection, transaction, topic, createdEvent);

        transaction.Commit();
        await Task.CompletedTask;
    }

    public async Task<OrderRow?> GetOrderAsync(string orderId)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OrderId, Symbol, Side, Quantity, Price, Status, RejectReason, CurrentExposure, CreatedAt, ProcessedAt
            FROM Orders WHERE OrderId = @orderId
            """;
        command.Parameters.AddWithValue("@orderId", orderId);

        using var reader = await command.ExecuteReaderAsync();
        if (!reader.Read())
            return null;

        return MapOrder(reader);
    }

    public async Task<bool> TryCompleteOrderWithOutboxAsync(
        string orderId, bool isAccepted, string? rejectReason, decimal currentExposure,
        DateTime processedAt, string topic, object processedEvent)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Orders
                SET Status = @status, RejectReason = @rejectReason, CurrentExposure = @currentExposure, ProcessedAt = @processedAt
                WHERE OrderId = @orderId AND Status = 'Pending'
                """;
            command.Parameters.AddWithValue("@status", isAccepted ? "Accepted" : "Rejected");
            command.Parameters.AddWithValue("@rejectReason", (object?)rejectReason ?? DBNull.Value);
            command.Parameters.AddWithValue("@currentExposure", currentExposure.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@processedAt", processedAt.ToString("O"));
            command.Parameters.AddWithValue("@orderId", orderId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                transaction.Rollback();
                return false;
            }
        }

        InsertOutboxMessage(connection, transaction, topic, processedEvent);
        transaction.Commit();
        return true;
    }

    public async Task UpdateOrderResultAsync(OrderProcessedEvent evt)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders
            SET Status = @status, RejectReason = @rejectReason, CurrentExposure = @currentExposure, ProcessedAt = @processedAt
            WHERE OrderId = @orderId AND Status = 'Pending'
            """;
        command.Parameters.AddWithValue("@status", evt.IsAccepted ? "Accepted" : "Rejected");
        command.Parameters.AddWithValue("@rejectReason", (object?)evt.RejectReason ?? DBNull.Value);
        command.Parameters.AddWithValue("@currentExposure", evt.CurrentExposure.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@processedAt", evt.ProcessedAt.ToString("O"));
        command.Parameters.AddWithValue("@orderId", evt.OrderId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<OutboxRow>> GetUnpublishedOutboxAsync(int batchSize)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Topic, EventType, Payload, OccurredOn
            FROM OutboxMessages
            WHERE PublishedOn IS NULL
            ORDER BY OccurredOn
            LIMIT @batchSize
            """;
        command.Parameters.AddWithValue("@batchSize", batchSize);

        var messages = new List<OutboxRow>();
        using var reader = await command.ExecuteReaderAsync();
        while (reader.Read())
        {
            messages.Add(new OutboxRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTime.Parse(reader.GetString(4))));
        }

        return messages;
    }

    public async Task MarkOutboxPublishedAsync(string id)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboxMessages SET PublishedOn = @publishedOn WHERE Id = @id";
        command.Parameters.AddWithValue("@publishedOn", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<string, decimal>> GetExposuresAsync()
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Symbol, Value FROM Exposures";

        var exposures = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        using var reader = await command.ExecuteReaderAsync();
        while (reader.Read())
        {
            exposures[reader.GetString(0)] =
                decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        }

        return exposures;
    }

    public async Task UpsertExposureAsync(string symbol, decimal value)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Exposures (Symbol, Value, UpdatedAt)
            VALUES (@symbol, @value, @updatedAt)
            ON CONFLICT(Symbol) DO UPDATE SET Value = @value, UpdatedAt = @updatedAt
            """;
        command.Parameters.AddWithValue("@symbol", symbol);
        command.Parameters.AddWithValue("@value", value.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    private static void InsertOrder(
        SqliteConnection connection, SqliteTransaction transaction,
        string orderId, string symbol, string side, int quantity, decimal price, DateTime createdAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Orders (OrderId, Symbol, Side, Quantity, Price, Status, CreatedAt)
            VALUES (@orderId, @symbol, @side, @quantity, @price, 'Pending', @createdAt)
            """;
        command.Parameters.AddWithValue("@orderId", orderId);
        command.Parameters.AddWithValue("@symbol", symbol);
        command.Parameters.AddWithValue("@side", side);
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@price", price.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));

        if (command.ExecuteNonQuery() == 0)
            throw new InvalidOperationException($"Failed to persist order {orderId}");
    }

    private static void InsertOutboxMessage(
        SqliteConnection connection, SqliteTransaction transaction,
        string topic, object payload)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO OutboxMessages (Id, Topic, EventType, Payload, OccurredOn)
            VALUES (@id, @topic, @eventType, @payload, @occurredOn)
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@topic", topic);
        command.Parameters.AddWithValue("@eventType", payload.GetType().AssemblyQualifiedName ?? payload.GetType().Name);
        command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(payload));
        command.Parameters.AddWithValue("@occurredOn", DateTime.UtcNow.ToString("O"));

        if (command.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Failed to enqueue outbox message");
    }

    private static OrderRow MapOrder(SqliteDataReader reader)
    {
        return new OrderRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            DateTime.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)));
    }
}

public record OrderRow(
    string OrderId,
    string Symbol,
    string Side,
    long Quantity,
    decimal Price,
    string Status,
    string? RejectReason,
    decimal? CurrentExposure,
    DateTime CreatedAt,
    DateTime? ProcessedAt);

public record OutboxRow(
    string Id,
    string Topic,
    string EventType,
    string Payload,
    DateTime OccurredOn);
