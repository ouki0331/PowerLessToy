using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PowerLessToy.AccessHistory.App;

public enum SourceType
{
    Local = 0,
    SMB = 1,
    WSL = 2
}

public class AccessRecord
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public SourceType SourceType { get; set; }
    public int AccessCount { get; set; }
    public DateTime LastAccessTime { get; set; }
    public bool IsHiddenFromMain { get; set; }
}

public class DatabaseManager
{
    private readonly string _connectionString;
    public string DatabasePath { get; }

    public DatabaseManager()
    {
        var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerLessToy", "AccessHistory");
        try
        {
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
                AccessHistoryLogger.LogInfo($"Created database directory: '{dbFolder}'");
            }
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to create database directory: '{dbFolder}'", ex);
            throw;
        }

        DatabasePath = Path.Combine(dbFolder, "history.db");
        _connectionString = $"Data Source={DatabasePath}";
        AccessHistoryLogger.LogInfo($"Database configured at: '{DatabasePath}'");

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            AccessHistoryLogger.LogInfo($"Opening SQLite connection to '{DatabasePath}' for schema initialization...");
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            AccessHistoryLogger.LogInfo("SQLite connection successfully opened.");

            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS AccessRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Path TEXT UNIQUE NOT NULL,
                    FileName TEXT NOT NULL,
                    SourceType INTEGER NOT NULL,
                    AccessCount INTEGER NOT NULL DEFAULT 1,
                    LastAccessTime DATETIME NOT NULL,
                    IsHiddenFromMain INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IDX_AccessRecords_Path ON AccessRecords(Path);
                CREATE INDEX IF NOT EXISTS IDX_AccessRecords_SourceType ON AccessRecords(SourceType);
            ";
            connection.Execute(createTableSql);
            AccessHistoryLogger.LogInfo("Database schema verified/created successfully.");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"SQLite initialization failed for database '{DatabasePath}'", ex);
            throw;
        }
    }

    public bool RecordAccess(string path, string fileName, SourceType sourceType)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AccessHistoryLogger.LogWarn("RecordAccess skipped: Path is null or empty");
            return false;
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var sql = @"
                INSERT INTO AccessRecords (Path, FileName, SourceType, AccessCount, LastAccessTime, IsHiddenFromMain)
                VALUES (@Path, @FileName, @SourceType, 1, @LastAccessTime, 0)
                ON CONFLICT(Path) DO UPDATE SET 
                    AccessCount = AccessCount + 1,
                    LastAccessTime = @LastAccessTime;";

            int rows = connection.Execute(sql, new 
            { 
                Path = path, 
                FileName = fileName, 
                SourceType = (int)sourceType, 
                LastAccessTime = DateTime.UtcNow 
            });

            AccessHistoryLogger.LogInfo($"[RecordAccess SUCCESS] Path='{path}', FileName='{fileName}', SourceType={sourceType}, RowsAffected={rows}");
            return true;
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"[RecordAccess FAILED] Could not record access for Path='{path}', FileName='{fileName}', SourceType={sourceType}", ex);
            return false;
        }
    }

    public void SetHiddenStatus(int id, bool isHidden)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var sql = "UPDATE AccessRecords SET IsHiddenFromMain = @IsHidden WHERE Id = @Id";
            int rows = connection.Execute(sql, new { IsHidden = isHidden ? 1 : 0, Id = id });
            AccessHistoryLogger.LogInfo($"Record Id={id} hidden status set to {isHidden} (Rows={rows})");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to update hidden status for Record Id={id}", ex);
        }
    }

    public List<AccessRecord> GetTopRecordsForMain(SourceType sourceType, int limit = 20)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var sql = @"
                SELECT * FROM AccessRecords 
                WHERE SourceType = @SourceType AND IsHiddenFromMain = 0
                ORDER BY AccessCount DESC, LastAccessTime DESC
                LIMIT @Limit";
            var results = connection.Query<AccessRecord>(sql, new { SourceType = (int)sourceType, Limit = limit }).ToList();
            AccessHistoryLogger.LogDebug($"Loaded {results.Count} records for SourceType={sourceType} (Limit={limit})");
            return results;
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to load top records for SourceType={sourceType}", ex);
            return new List<AccessRecord>();
        }
    }

    public List<AccessRecord> SearchRecords(string query, int limitPerSource = 10)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            
            var keywords = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (keywords.Length == 0) return new List<AccessRecord>();

            var conditions = new List<string>();
            foreach (var i in Enumerable.Range(0, keywords.Length))
            {
                conditions.Add($"(FileName LIKE @K{i} OR Path LIKE @K{i})");
            }
            var whereClause = string.Join(" OR ", conditions);

            var sql = $@"
                SELECT * FROM AccessRecords 
                WHERE {whereClause}
                ORDER BY 
                    IsHiddenFromMain ASC, 
                    AccessCount DESC, 
                    LastAccessTime DESC
                LIMIT @GlobalLimit";

            var parameters = new DynamicParameters();
            parameters.Add("GlobalLimit", limitPerSource * 3);
            for (int i = 0; i < keywords.Length; i++)
            {
                parameters.Add($"K{i}", $"%{keywords[i]}%");
            }

            var allResults = connection.Query<AccessRecord>(sql, parameters).ToList();
            
            var finalResults = new List<AccessRecord>();
            foreach (SourceType type in Enum.GetValues(typeof(SourceType)))
            {
                finalResults.AddRange(allResults.Where(r => r.SourceType == type).Take(limitPerSource));
            }

            AccessHistoryLogger.LogDebug($"Search query='{query}' returned {finalResults.Count} results");
            return finalResults;
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to execute search for query='{query}'", ex);
            return new List<AccessRecord>();
        }
    }
}
