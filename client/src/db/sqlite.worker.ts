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
    });

    if ('opfs' in sqlite3) {
      try {
        db = new sqlite3.oo1.OpfsDb('/task_manager_local.db');
        storageType = 'OPFS (Persistent)';
      } catch (opfsErr) {
        console.warn('OPFS initialization failed, falling back to memory/virtual DB:', opfsErr);
        db = new sqlite3.oo1.DB('/task_manager_local.db', 'ct');
        storageType = 'Virtual (Fallback)';
      }
    } else {
      console.warn('OPFS not supported in this environment, using standard SQLite DB:');
      db = new sqlite3.oo1.DB('/task_manager_local.db', 'ct');
      storageType = 'Virtual (No OPFS)';
    }

    // Execute Initial Schema
    db.exec(INITIAL_SCHEMA_SQL);
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
