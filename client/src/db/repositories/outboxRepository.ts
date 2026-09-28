import { sqliteClient } from '../sqliteClient';
import { SyncOutboxItem } from '../schema';

export const outboxRepository = {
  async addMutation(mutation: Omit<SyncOutboxItem, 'id' | 'created_at' | 'retry_count'>): Promise<string> {
    const id = crypto.randomUUID();
    const now = new Date().toISOString();

    await sqliteClient.execute(`
      INSERT INTO sync_outbox (
        id, entity, operation, entity_id, workspace_id, 
        client_timestamp, version, payload_json, created_at, retry_count, last_error
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 0, NULL);
    `, [
      id,
      mutation.entity,
      mutation.operation,
      mutation.entity_id,
      mutation.workspace_id || null,
      mutation.client_timestamp,
      mutation.version || 1,
      mutation.payload_json,
      now
    ]);

    return id;
  },

  async getPendingMutations(): Promise<SyncOutboxItem[]> {
    return sqliteClient.query<SyncOutboxItem>(`
      SELECT id, entity, operation, entity_id, workspace_id,
             client_timestamp, version, payload_json, created_at, retry_count, last_error
      FROM sync_outbox
      ORDER BY created_at ASC;
    `);
  },

  async removeMutations(ids: string[]): Promise<void> {
    if (!ids.length) return;
    const placeholders = ids.map(() => '?').join(',');
    await sqliteClient.execute(`
      DELETE FROM sync_outbox WHERE id IN (${placeholders});
    `, ids);
  },

  async markFailed(id: string, errorMessage: string): Promise<void> {
    await sqliteClient.execute(`
      UPDATE sync_outbox 
      SET retry_count = retry_count + 1, last_error = ?
      WHERE id = ?;
    `, [errorMessage, id]);
  },

  async countPending(): Promise<number> {
    const result = await sqliteClient.querySingle<{ count: number }>(`
      SELECT COUNT(1) as count FROM sync_outbox;
    `);
    return result?.count || 0;
  }
};
