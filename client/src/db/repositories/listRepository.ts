import { sqliteClient } from '../sqliteClient';
import { TaskList } from '../schema';
import { outboxRepository } from './outboxRepository';

export const localListRepository = {
  async getByWorkspace(workspaceId: string): Promise<TaskList[]> {
    return sqliteClient.query<TaskList>(`
      SELECT id, workspace_id, name, color, position, created_by, created_at, 
             updated_by, updated_at, version, is_deleted
      FROM lists
      WHERE workspace_id = ? AND is_deleted = 0
      ORDER BY position ASC, created_at ASC;
    `, [workspaceId]);
  },

  async getById(id: string): Promise<TaskList | null> {
    return sqliteClient.querySingle<TaskList>(`
      SELECT id, workspace_id, name, color, position, created_by, created_at, 
             updated_by, updated_at, version, is_deleted
      FROM lists
      WHERE id = ? AND is_deleted = 0;
    `, [id]);
  },

  async save(list: Partial<TaskList> & { id?: string; workspace_id: string; name: string }, isSync = false): Promise<TaskList> {
    const existing = list.id ? await this.getById(list.id) : null;
    const now = new Date().toISOString();
    const id = list.id || crypto.randomUUID();

    if (existing) {
      const updated: TaskList = {
        ...existing,
        name: list.name.trim(),
        color: list.color || existing.color,
        position: list.position !== undefined ? list.position : existing.position,
        updated_at: now,
        version: isSync ? (list.version || existing.version) : existing.version + 1,
        is_deleted: list.is_deleted !== undefined ? list.is_deleted : existing.is_deleted
      };

      await sqliteClient.execute(`
        UPDATE lists
        SET name = ?, color = ?, position = ?, updated_at = ?, version = ?, is_deleted = ?
        WHERE id = ?;
      `, [updated.name, updated.color, updated.position, updated.updated_at, updated.version, updated.is_deleted, id]);

      if (!isSync) {
        await outboxRepository.addMutation({
          entity: 'list',
          operation: 'UPDATE',
          entity_id: id,
          workspace_id: updated.workspace_id,
          client_timestamp: now,
          version: updated.version,
          payload_json: JSON.stringify({ name: updated.name, color: updated.color, position: updated.position })
        });
      }

      return updated;
    } else {
      const created: TaskList = {
        id,
        workspace_id: list.workspace_id,
        name: list.name.trim(),
        color: list.color || '#3B82F6',
        position: list.position ?? 0,
        created_by: list.created_by || 'current_user',
        created_at: list.created_at || now,
        updated_by: list.updated_by || 'current_user',
        updated_at: list.updated_at || now,
        version: list.version || 1,
        is_deleted: 0
      };

      if (created.workspace_id) {
        const wsExists = await sqliteClient.querySingle<{ id: string }>(
          'SELECT id FROM workspaces WHERE id = ?;',
          [created.workspace_id]
        );
        if (!wsExists) {
          await sqliteClient.execute(
            `INSERT OR IGNORE INTO workspaces (id, name, owner_id, created_by, created_at, updated_at, version, is_deleted)
             VALUES (?, 'Workspace', 'system', 'system', ?, ?, 1, 0);`,
            [created.workspace_id, now, now]
          );
        }
      }

      await sqliteClient.execute(`
        INSERT INTO lists (id, workspace_id, name, color, position, created_by, created_at, updated_by, updated_at, version, is_deleted)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
      `, [
        created.id, created.workspace_id, created.name, created.color, created.position,
        created.created_by, created.created_at, created.updated_by, created.updated_at,
        created.version, created.is_deleted
      ]);

      if (!isSync) {
        await outboxRepository.addMutation({
          entity: 'list',
          operation: 'INSERT',
          entity_id: id,
          workspace_id: created.workspace_id,
          client_timestamp: now,
          version: created.version,
          payload_json: JSON.stringify({
            workspaceId: created.workspace_id,
            name: created.name,
            color: created.color,
            position: created.position
          })
        });
      }

      return created;
    }
  },

  async delete(id: string, isSync = false): Promise<void> {
    const existing = await this.getById(id);
    if (!existing) return;

    const now = new Date().toISOString();
    const newVersion = existing.version + 1;

    await sqliteClient.execute(`
      UPDATE lists 
      SET is_deleted = 1, updated_at = ?, version = ?
      WHERE id = ?;
    `, [now, newVersion, id]);

    if (!isSync) {
      await outboxRepository.addMutation({
        entity: 'list',
        operation: 'DELETE',
        entity_id: id,
        workspace_id: existing.workspace_id,
        client_timestamp: now,
        version: newVersion,
        payload_json: JSON.stringify({ id })
      });
    }
  }
};
