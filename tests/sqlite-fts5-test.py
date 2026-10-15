import sqlite3
import sys

def run_tests():
    con = sqlite3.connect(':memory:')
    con.execute('PRAGMA foreign_keys = ON;')
    
    # 1. Initialize schema matching StorageService.cs
    con.execute('''CREATE TABLE sessions (
        id TEXT PRIMARY KEY,
        workspace_id TEXT NOT NULL,
        title TEXT NOT NULL,
        created_at INTEGER NOT NULL,
        updated_at INTEGER NOT NULL
    )''')
    
    con.execute('''CREATE TABLE messages (
        id TEXT PRIMARY KEY,
        session_id TEXT NOT NULL,
        role TEXT NOT NULL,
        content TEXT NOT NULL,
        model TEXT,
        created_at INTEGER NOT NULL,
        FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
    )''')
    
    con.execute('''CREATE VIRTUAL TABLE messages_fts USING fts5(
        content,
        content='messages',
        content_rowid='rowid'
    )''')
    
    con.execute('''CREATE TRIGGER messages_ai AFTER INSERT ON messages BEGIN
        INSERT INTO messages_fts(rowid, content) VALUES (new.rowid, new.content);
    END''')
    
    con.execute('''CREATE TRIGGER messages_ad AFTER DELETE ON messages BEGIN
        INSERT INTO messages_fts(messages_fts, rowid, content) VALUES('delete', old.rowid, old.content);
    END''')

    # 2. Insert test session and messages
    con.execute("INSERT INTO sessions VALUES ('sess-001', 'ws-test', 'End to End Session', 1729000000, 1729000000)")
    con.execute("INSERT INTO messages VALUES ('m-001', 'sess-001', 'user', 'How do I optimize SQLite queries with FTS5 virtual tables?', 'gemini-3.8-flash', 1729000001)")
    con.execute("INSERT INTO messages VALUES ('m-002', 'sess-001', 'assistant', 'To optimize FTS5 in SQLite, use external content tables and triggers for zero-duplication indexing.', 'gemini-3.8-flash', 1729000002)")
    con.commit()

    # 3. Test FTS5 exact phrase search
    res = con.execute("SELECT m.id, m.content, m.role FROM messages_fts f JOIN messages m ON f.rowid = m.rowid WHERE messages_fts MATCH '\"zero-duplication\"'").fetchall()
    if len(res) != 1:
        print("FAIL: FTS5 query expected 1 result, got {0}".format(len(res)))
        sys.exit(1)
    if 'zero-duplication' not in res[0][1]:
        print("FAIL: FTS5 match content mismatch")
        sys.exit(1)

    # 4. Test session rename
    con.execute("UPDATE sessions SET title = 'Renamed Session Title' WHERE id = 'sess-001'")
    updated_title = con.execute("SELECT title FROM sessions WHERE id = 'sess-001'").fetchone()[0]
    if updated_title != 'Renamed Session Title':
        print("FAIL: Session rename failed")
        sys.exit(1)

    # 5. Test cascading deletion
    con.execute("DELETE FROM sessions WHERE id = 'sess-001'")
    remaining_msgs = con.execute("SELECT COUNT(*) FROM messages WHERE session_id = 'sess-001'").fetchone()[0]
    if remaining_msgs != 0:
        print("FAIL: Cascading delete failed, {0} messages remain".format(remaining_msgs))
        sys.exit(1)

    remaining_fts = con.execute("SELECT COUNT(*) FROM messages_fts").fetchone()[0]
    if remaining_fts != 0:
        print("FAIL: FTS5 index cleanup trigger failed, {0} rows remain".format(remaining_fts))
        sys.exit(1)

    print("SQLITE_FTS5_ALL_PASSED")

if __name__ == '__main__':
    run_tests()
