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

    public DatabaseManager()
    {
        var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerLessToy", "AccessHistory");
        if (!Directory.Exists(dbFolder))
        {
            Directory.CreateDirectory(dbFolder);
        }

        var dbPath = Path.Combine(dbFolder, "history.db");
        _connectionString = $"Data Source={dbPath}";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

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
    }

    public void RecordAccess(string path, string fileName, SourceType sourceType)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var sql = @"
            INSERT INTO AccessRecords (Path, FileName, SourceType, AccessCount, LastAccessTime, IsHiddenFromMain)
            VALUES (@Path, @FileName, @SourceType, 1, @LastAccessTime, 0)
            ON CONFLICT(Path) DO UPDATE SET 
                AccessCount = AccessCount + 1,
                LastAccessTime = @LastAccessTime;";

        connection.Execute(sql, new 
        { 
            Path = path, 
            FileName = fileName, 
            SourceType = (int)sourceType, 
            LastAccessTime = DateTime.UtcNow 
        });
    }

    public void SetHiddenStatus(int id, bool isHidden)
    {
        using var connection = new SqliteConnection(_connectionString);
        var sql = "UPDATE AccessRecords SET IsHiddenFromMain = @IsHidden WHERE Id = @Id";
        connection.Execute(sql, new { IsHidden = isHidden ? 1 : 0, Id = id });
    }

    public List<AccessRecord> GetTopRecordsForMain(SourceType sourceType, int limit = 20)
    {
        using var connection = new SqliteConnection(_connectionString);
        var sql = @"
            SELECT * FROM AccessRecords 
            WHERE SourceType = @SourceType AND IsHiddenFromMain = 0
            ORDER BY AccessCount DESC, LastAccessTime DESC
            LIMIT @Limit";
        return connection.Query<AccessRecord>(sql, new { SourceType = (int)sourceType, Limit = limit }).ToList();
    }

    public List<AccessRecord> SearchRecords(string query, int limitPerSource = 10)
    {
        using var connection = new SqliteConnection(_connectionString);
        
        // 検索キーワードをスペースで分割し、OR条件とする
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
        parameters.Add("GlobalLimit", limitPerSource * 3); // ある程度の余裕を持たせる
        for (int i = 0; i < keywords.Length; i++)
        {
            parameters.Add($"K{i}", $"%{keywords[i]}%");
        }

        var allResults = connection.Query<AccessRecord>(sql, parameters).ToList();
        
        // SourceType ごとにグループ化し、件数を制限する
        var finalResults = new List<AccessRecord>();
        foreach (SourceType type in Enum.GetValues(typeof(SourceType)))
        {
            finalResults.AddRange(allResults.Where(r => r.SourceType == type).Take(limitPerSource));
        }

        return finalResults;
    }
}
