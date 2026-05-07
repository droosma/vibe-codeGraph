namespace CodeGraph.Core.IO.Sqlite;

internal static class SqliteSchema
{
    public const string CreateTables = """
        CREATE TABLE IF NOT EXISTS metadata (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS nodes (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            kind INTEGER NOT NULL,
            file_path TEXT NOT NULL DEFAULT '',
            start_line INTEGER NOT NULL DEFAULT 0,
            end_line INTEGER NOT NULL DEFAULT 0,
            signature TEXT NOT NULL DEFAULT '',
            doc_comment TEXT,
            containing_type_id TEXT,
            containing_namespace_id TEXT,
            accessibility INTEGER NOT NULL DEFAULT 0,
            assembly_name TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS node_metadata (
            node_id TEXT NOT NULL,
            key TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY (node_id, key),
            FOREIGN KEY (node_id) REFERENCES nodes(id)
        );

        CREATE TABLE IF NOT EXISTS edges (
            rowid INTEGER PRIMARY KEY AUTOINCREMENT,
            from_id TEXT NOT NULL,
            to_id TEXT NOT NULL,
            type INTEGER NOT NULL,
            is_external INTEGER NOT NULL DEFAULT 0,
            package_source TEXT,
            source_link TEXT,
            resolution TEXT,
            confidence INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS edge_metadata (
            edge_rowid INTEGER NOT NULL,
            key TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY (edge_rowid, key),
            FOREIGN KEY (edge_rowid) REFERENCES edges(rowid)
        );

        CREATE INDEX IF NOT EXISTS idx_edges_from ON edges(from_id);
        CREATE INDEX IF NOT EXISTS idx_edges_to ON edges(to_id);
        CREATE INDEX IF NOT EXISTS idx_edges_type ON edges(type);
        CREATE INDEX IF NOT EXISTS idx_nodes_kind ON nodes(kind);
        CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(name);
        CREATE INDEX IF NOT EXISTS idx_nodes_assembly ON nodes(assembly_name);
        CREATE INDEX IF NOT EXISTS idx_nodes_namespace ON nodes(containing_namespace_id);
        """;
}
