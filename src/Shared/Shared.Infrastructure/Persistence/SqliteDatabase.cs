using Microsoft.Data.Sqlite;

namespace Shared.Infrastructure.Persistence;

public class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string? databasePath = null)
    {
        var path = databasePath
            ?? Environment.GetEnvironmentVariable("SQLITE_DB_PATH")
            ?? Path.Combine(ResolveDefaultDirectory(), "data", "baseorder.db");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        Initialize();
    }

    private static string ResolveDefaultDirectory()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "baseOrder.slnx")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        return Directory.GetCurrentDirectory();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA busy_timeout = 5000;";
            command.ExecuteNonQuery();
        }

        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        const string schema = """
            CREATE TABLE IF NOT EXISTS Orders (
                OrderId         TEXT PRIMARY KEY,
                Symbol          TEXT NOT NULL,
                Side            TEXT NOT NULL,
                Quantity        INTEGER NOT NULL,
                Price           TEXT NOT NULL,
                Status          TEXT NOT NULL,
                RejectReason    TEXT,
                CurrentExposure TEXT,
                CreatedAt       TEXT NOT NULL,
                ProcessedAt     TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status);

            CREATE TABLE IF NOT EXISTS OutboxMessages (
                Id          TEXT PRIMARY KEY,
                Topic       TEXT NOT NULL,
                EventType   TEXT NOT NULL,
                Payload     TEXT NOT NULL,
                OccurredOn  TEXT NOT NULL,
                PublishedOn TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Outbox_Unpublished ON OutboxMessages(PublishedOn) WHERE PublishedOn IS NULL;

            CREATE TABLE IF NOT EXISTS Exposures (
                Symbol    TEXT PRIMARY KEY,
                Value     TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;

        using var command = connection.CreateCommand();
        command.CommandText = schema;
        command.ExecuteNonQuery();
    }
}
