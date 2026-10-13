using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Gemini.Extension.Services
{
    public sealed class StorageService : IDisposable
    {
        private SqliteConnection connection;
        private readonly string dbPath;

        public StorageService()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(localAppData, "GeminiVS");
            Directory.CreateDirectory(dir);
            dbPath = Path.Combine(dir, "gemini_vs.db");
        }

        public async Task InitializeAsync()
        {
            try
            {
                connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                string initSql = @"
                    CREATE TABLE IF NOT EXISTS sessions (
                        id TEXT PRIMARY KEY,
                        workspace_id TEXT NOT NULL,
                        title TEXT NOT NULL,
                        created_at INTEGER NOT NULL,
                        updated_at INTEGER NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS messages (
                        id TEXT PRIMARY KEY,
                        session_id TEXT NOT NULL,
                        role TEXT NOT NULL,
                        content TEXT NOT NULL,
                        model TEXT,
                        created_at INTEGER NOT NULL,
                        FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS prompt_templates (
                        id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        content TEXT NOT NULL,
                        category TEXT,
                        created_at INTEGER NOT NULL
                    );

                    CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                        content,
                        content='messages',
                        content_rowid='rowid'
                    );
                ";

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = initSql;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] SQLite storage initialization warning: {ex.Message}");
            }
        }

        public async Task<List<SessionRecord>> GetSessionsAsync(string workspaceId)
        {
            var list = new List<SessionRecord>();
            if (connection == null || connection.State != System.Data.ConnectionState.Open) return list;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, workspace_id, title, created_at, updated_at FROM sessions WHERE workspace_id = @wsId ORDER BY updated_at DESC";
                cmd.Parameters.AddWithValue("@wsId", workspaceId ?? "default");

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new SessionRecord
                        {
                            Id = reader.GetString(0),
                            WorkspaceId = reader.GetString(1),
                            Title = reader.GetString(2),
                            CreatedAt = reader.GetInt64(3),
                            UpdatedAt = reader.GetInt64(4)
                        });
                    }
                }
            }
            return list;
        }

        public async Task<SessionRecord> CreateSessionAsync(string workspaceId, string title)
        {
            string id = Guid.NewGuid().ToString();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            title = string.IsNullOrWhiteSpace(title) ? "New Chat" : title;

            if (connection == null || connection.State != System.Data.ConnectionState.Open)
            {
                return new SessionRecord { Id = id, WorkspaceId = workspaceId, Title = title, CreatedAt = now, UpdatedAt = now };
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO sessions (id, workspace_id, title, created_at, updated_at) VALUES (@id, @wsId, @title, @now, @now)";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@wsId", workspaceId ?? "default");
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            return new SessionRecord { Id = id, WorkspaceId = workspaceId, Title = title, CreatedAt = now, UpdatedAt = now };
        }

        public async Task<List<MessageRecord>> GetMessagesAsync(string sessionId)
        {
            var list = new List<MessageRecord>();
            if (connection == null || connection.State != System.Data.ConnectionState.Open) return list;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, session_id, role, content, model, created_at FROM messages WHERE session_id = @sid ORDER BY created_at ASC";
                cmd.Parameters.AddWithValue("@sid", sessionId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new MessageRecord
                        {
                            Id = reader.GetString(0),
                            SessionId = reader.GetString(1),
                            Role = reader.GetString(2),
                            Content = reader.GetString(3),
                            Model = reader.IsDBNull(4) ? null : reader.GetString(4),
                            CreatedAt = reader.GetInt64(5)
                        });
                    }
                }
            }
            return list;
        }

        public async Task SaveMessageAsync(MessageRecord msg)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open) return;

            long now = msg.CreatedAt > 0 ? msg.CreatedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO messages (id, session_id, role, content, model, created_at)
                    VALUES (@id, @sid, @role, @content, @model, @now);
                    UPDATE sessions SET updated_at = @now WHERE id = @sid;
                ";
                cmd.Parameters.AddWithValue("@id", msg.Id ?? Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@sid", msg.SessionId);
                cmd.Parameters.AddWithValue("@role", msg.Role);
                cmd.Parameters.AddWithValue("@content", msg.Content);
                cmd.Parameters.AddWithValue("@model", (object)msg.Model ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task RenameSessionAsync(string sessionId, string title)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open) return;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "UPDATE sessions SET title = @title, updated_at = @now WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            if (connection == null || connection.State != System.Data.ConnectionState.Open) return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM messages WHERE session_id = @id; DELETE FROM sessions WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", sessionId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<SearchResultItem>> SearchHistoryAsync(string query)
        {
            var list = new List<SearchResultItem>();
            if (string.IsNullOrWhiteSpace(query) || connection == null || connection.State != System.Data.ConnectionState.Open) return list;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT m.session_id, s.title, m.content, m.created_at
                    FROM messages_fts f
                    JOIN messages m ON f.rowid = m.rowid
                    JOIN sessions s ON m.session_id = s.id
                    WHERE messages_fts MATCH @q
                    ORDER BY m.created_at DESC LIMIT 30;
                ";
                cmd.Parameters.AddWithValue("@q", query);

                try
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new SearchResultItem
                            {
                                SessionId = reader.GetString(0),
                                SessionTitle = reader.GetString(1),
                                Snippet = reader.GetString(2),
                                CreatedAt = reader.GetInt64(3)
                            });
                        }
                    }
                }
                catch
                {
                    // Fallback to LIKE if FTS query syntax error
                    cmd.CommandText = @"
                        SELECT m.session_id, s.title, m.content, m.created_at
                        FROM messages m
                        JOIN sessions s ON m.session_id = s.id
                        WHERE m.content LIKE @likeQ
                        ORDER BY m.created_at DESC LIMIT 30;
                    ";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@likeQ", $"%{query}%");

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new SearchResultItem
                            {
                                SessionId = reader.GetString(0),
                                SessionTitle = reader.GetString(1),
                                Snippet = reader.GetString(2),
                                CreatedAt = reader.GetInt64(3)
                            });
                        }
                    }
                }
            }
            return list;
        }

        public async Task<List<PromptTemplateItem>> GetPromptTemplatesAsync()
        {
            var list = new List<PromptTemplateItem>();
            if (connection != null && connection.State == System.Data.ConnectionState.Open)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, title, content, category FROM prompt_templates ORDER BY title ASC";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new PromptTemplateItem
                            {
                                Id = reader.GetString(0),
                                Title = reader.GetString(1),
                                Content = reader.GetString(2),
                                Category = reader.IsDBNull(3) ? null : reader.GetString(3)
                            });
                        }
                    }
                }
            }

            // Provide default templates if empty
            if (list.Count == 0)
            {
                list.Add(new PromptTemplateItem { Id = "tpl-explain", Title = "Explain Code", Content = "Explain how this code works, including its complexity and any potential edge cases.", Category = "Code" });
                list.Add(new PromptTemplateItem { Id = "tpl-refactor", Title = "Refactor & Clean", Content = "Refactor this code to follow modern C# / .NET conventions, improve readability, and adhere to SOLID principles.", Category = "Refactor" });
                list.Add(new PromptTemplateItem { Id = "tpl-tests", Title = "Generate Unit Tests", Content = "Write comprehensive unit tests for this code using xUnit/NUnit and FluentAssertions.", Category = "Testing" });
                list.Add(new PromptTemplateItem { Id = "tpl-perf", Title = "Optimize Performance", Content = "Analyze this code for potential memory allocations, CPU bottlenecks, and suggest high-performance optimizations.", Category = "Performance" });
            }

            return list;
        }

        public async Task<string> ExportSessionMarkdownAsync(string sessionId)
        {
            var messages = await GetMessagesAsync(sessionId);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Gemini Chat Export");
            sb.AppendLine($"*Exported on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*\n");

            foreach (var m in messages)
            {
                string header = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "### 👤 User" : $"### 🤖 Gemini ({m.Model ?? "Assistant"})";
                sb.AppendLine(header);
                sb.AppendLine();
                sb.AppendLine(m.Content);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            connection?.Dispose();
        }
    }

    public class SearchResultItem
    {
        public string SessionId { get; set; }
        public string SessionTitle { get; set; }
        public string Snippet { get; set; }
        public long CreatedAt { get; set; }
    }

    public class PromptTemplateItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
    }

    public class SessionRecord
    {
        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string Title { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    public class MessageRecord
    {
        public string Id { get; set; }
        public string SessionId { get; set; }
        public string Role { get; set; }
        public string Content { get; set; }
        public string Model { get; set; }
        public long CreatedAt { get; set; }
    }
}
