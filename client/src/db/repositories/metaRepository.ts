import { sqliteClient } from '../sqliteClient';

export const metaRepository = {
  async get(key: string): Promise<string | null> {
    const row = await sqliteClient.querySingle<{ value: string }>(`
      SELECT value FROM sync_meta WHERE key = ?;
    `, [key]);
    return row ? row.value : null;
  },

  async set(key: string, value: string): Promise<void> {
    await sqliteClient.execute(`
      INSERT INTO sync_meta (key, value) VALUES (?, ?)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value;
    `, [key, value]);
  },

  async getLastSyncedAt(): Promise<string | null> {
    return this.get('last_synced_at');
  },

  async setLastSyncedAt(timestamp: string): Promise<void> {
    await this.set('last_synced_at', timestamp);
  }
};
