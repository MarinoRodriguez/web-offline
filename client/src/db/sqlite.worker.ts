import sqlite3InitModule from '@sqlite.org/sqlite-wasm';
import { INITIAL_SCHEMA_SQL } from './schema';

let db: any = null;
let isInitialized = false;
let initPromise: Promise<void> | null = null;
let storageType = 'unknown';

interface WorkerRequest {
  id: string;
  type: 'INIT' | 'EXEC' | 'BATCH';
  sql?: string;
  params?: any[] | Record<string, any>;
  statements?: Array<{ sql: string; params?: any[] | Record<string, any> }>;
}

async function initializeDatabase(): Promise<void> {
  if (isInitialized && db) return;

  try {
    const sqlite3 = await (sqlite3InitModule as any)({
      print: console.log,
      printErr: console.error,
      locateFile: (file: string) => `/${file}`,
    });

    if ('opfs' in sqlite3) {
      try {
        db = new sqlite3.oo1.OpfsDb('/task_manager_local.db');
        storageType = 'OPFS (Persistent)';
        console.info('[SQLite Worker] Successfully initialized persistent database in OPFS (/task_manager_local.db)');
      } catch (opfsErr: any) {
        console.error('[SQLite Worker] OPFS instantiation failed:', opfsErr);
        db = new sqlite3.oo1.DB('/task_manager_local.db', 'ct');
        storageType = 'Virtual (Fallback)';
      }
    } else {
      console.warn('[SQLite Worker] OPFS not supported in this browser context (requires COOP/COEP isolation). Using Virtual DB.');
      db = new sqlite3.oo1.DB('/task_manager_local.db', 'ct');
      storageType = 'Virtual (No OPFS)';
    }

    // Execute Initial Schema
    db.exec(INITIAL_SCHEMA_SQL);

    // Migration: Ensure tasks.list_id is nullable
    try {
      const tableInfo: any[] = [];
      db.exec({
        sql: "PRAGMA table_info(tasks);",
        rowMode: 'object',
        resultRows: tableInfo
      });
      const listIdCol = tableInfo.find((c: any) => c.name === 'list_id');
      if (listIdCol && Number(listIdCol.notnull) === 1) {
        db.exec(`
          PRAGMA foreign_keys = OFF;
          ALTER TABLE tasks RENAME TO _tasks_old;
          CREATE TABLE tasks (
            id TEXT PRIMARY KEY,
            list_id TEXT,
            workspace_id TEXT NOT NULL,
            parent_task_id TEXT,
            title TEXT NOT NULL,
            description TEXT,
            status TEXT NOT NULL DEFAULT 'TODO',
            priority TEXT NOT NULL DEFAULT 'MEDIUM',
            due_date TEXT,
            position INTEGER NOT NULL DEFAULT 0,
            created_by TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_by TEXT,
            updated_at TEXT NOT NULL,
            version INTEGER NOT NULL DEFAULT 1,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY (list_id) REFERENCES lists(id) ON DELETE SET NULL,
            FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
            FOREIGN KEY (parent_task_id) REFERENCES tasks(id) ON DELETE CASCADE
          );
          INSERT INTO tasks SELECT * FROM _tasks_old;
          DROP TABLE _tasks_old;
          PRAGMA foreign_keys = ON;
        `);
      }
    } catch (migErr) {
      console.warn('[SQLite Worker] Migration check note:', migErr);
    }

    isInitialized = true;
    console.info(`[SQLite Worker] Database initialized successfully using ${storageType}`);
  } catch (err: any) {
    console.error('[SQLite Worker] Initialization error:', err);
    throw new Error(`Failed to initialize SQLite Wasm: ${err?.message || err}`);
  }
}

self.onmessage = async (event: MessageEvent<WorkerRequest>) => {
  const { id, type, sql, params, statements } = event.data;

  try {
    if (type === 'INIT') {
      if (!initPromise) {
        initPromise = initializeDatabase();
      }
      await initPromise;
      self.postMessage({ id, success: true, data: { storageType } });
      return;
    }

    if (!isInitialized || !db) {
      if (!initPromise) {
        initPromise = initializeDatabase();
      }
      await initPromise;
    }

    if (type === 'EXEC') {
      if (!sql) {
        throw new Error('SQL query is required for EXEC');
      }

      const rows: any[] = [];
      db.exec({
        sql,
        bind: params || [],
        rowMode: 'object',
        resultRows: rows,
      });

      const changes = db.changes();
      self.postMessage({ id, success: true, data: { rows, changes } });
      return;
    }

    if (type === 'BATCH') {
      if (!statements || !Array.isArray(statements)) {
        throw new Error('Statements array is required for BATCH');
      }

      let totalChanges = 0;
      db.transaction((database: any) => {
        for (const stmt of statements) {
          database.exec({
            sql: stmt.sql,
            bind: stmt.params || [],
          });
          totalChanges += database.changes();
        }
      });

      self.postMessage({ id, success: true, data: { changes: totalChanges } });
      return;
    }

    throw new Error(`Unknown worker request type: ${type}`);
  } catch (err: any) {
    console.error(`[SQLite Worker] Error executing ${type}:`, err);
    self.postMessage({
      id,
      success: false,
      error: err?.message || String(err),
    });
  }
};
