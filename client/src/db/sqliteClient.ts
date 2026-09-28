// Client-side manager communicating with sqlite.worker.ts via typed Promises

interface WorkerResponse {
  id: string;
  success: boolean;
  data?: any;
  error?: string;
}

class SqliteClient {
  private worker: Worker | null = null;
  private pendingRequests = new Map<string, { resolve: (val: any) => void; reject: (err: any) => void }>();
  private initialized = false;
  private initPromise: Promise<{ storageType: string }> | null = null;
  public storageType = 'unknown';

  private getWorker(): Worker {
    if (!this.worker) {
      this.worker = new Worker(new URL('./sqlite.worker.ts', import.meta.url), {
        type: 'module',
      });

      this.worker.onmessage = (event: MessageEvent<WorkerResponse>) => {
        const { id, success, data, error } = event.data;
        const request = this.pendingRequests.get(id);
        if (request) {
          this.pendingRequests.delete(id);
          if (success) {
            request.resolve(data);
          } else {
            request.reject(new Error(error || 'Worker error without message'));
          }
        }
      };

      this.worker.onerror = (err) => {
        console.error('[SqliteClient] Worker error:', err);
      };
    }
    return this.worker;
  }

  private sendRequest<T = any>(type: 'INIT' | 'EXEC' | 'BATCH', payload: any = {}): Promise<T> {
    const id = crypto.randomUUID();
    const worker = this.getWorker();

    return new Promise<T>((resolve, reject) => {
      this.pendingRequests.set(id, { resolve, reject });
      worker.postMessage({ id, type, ...payload });
    });
  }

  public async initDb(): Promise<{ storageType: string }> {
    if (this.initialized) {
      return { storageType: this.storageType };
    }
    if (this.initPromise) {
      return this.initPromise;
    }

    this.initPromise = (async () => {
      try {
        const result = await this.sendRequest<{ storageType: string }>('INIT');
        this.storageType = result.storageType;
        this.initialized = true;
        console.info(`[SqliteClient] Connected to SQLite Worker. Storage: ${this.storageType}`);
        return result;
      } catch (err) {
        this.initPromise = null;
        throw err;
      }
    })();

    return this.initPromise;
  }

  public async query<T = any>(sql: string, params?: any[] | Record<string, any>): Promise<T[]> {
    await this.initDb();
    const result = await this.sendRequest<{ rows: T[] }>('EXEC', { sql, params });
    return result.rows || [];
  }

  public async querySingle<T = any>(sql: string, params?: any[] | Record<string, any>): Promise<T | null> {
    const rows = await this.query<T>(sql, params);
    return rows.length > 0 ? rows[0] : null;
  }

  public async execute(sql: string, params?: any[] | Record<string, any>): Promise<{ changes: number }> {
    await this.initDb();
    const result = await this.sendRequest<{ changes: number }>('EXEC', { sql, params });
    return { changes: result.changes ?? 0 };
  }

  public async batch(statements: Array<{ sql: string; params?: any[] | Record<string, any> }>): Promise<{ changes: number }> {
    await this.initDb();
    const result = await this.sendRequest<{ changes: number }>('BATCH', { statements });
    return { changes: result.changes ?? 0 };
  }

  public isReady(): boolean {
    return this.initialized;
  }
}

export const sqliteClient = new SqliteClient();
