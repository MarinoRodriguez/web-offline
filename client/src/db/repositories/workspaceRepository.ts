import { sqliteClient } from '../sqliteClient';
import { Workspace } from '../schema';
import { outboxRepository } from './outboxRepository';

export const localWorkspaceRepository = {
  async getAll(): Promise<Workspace[]> {
    return sqliteClient.query<Workspace>(`
      SELECT id, name, description, owner_id, created_by, created_at, 
             updated_by, updated_at, version, is_deleted
      FROM workspaces
      WHERE is_deleted = 0
      ORDER BY name ASC;
    `);
  },

  async getById(id: string, includeDeleted = false): Promise<Workspace | null> {
    return sqliteClient.querySingle<Workspace>(`
      SELECT id, name, description, owner_id, created_by, created_at, 
             updated_by, updated_at, version, is_deleted
      FROM workspaces
      WHERE id = ? ${includeDeleted ? '' : 'AND is_deleted = 0'};
    `, [id]);
  },

  async save(ws: Partial<Workspace> & { id?: string; name: string }, isSync = false): Promise<Workspace> {
    const existing = ws.id ? await this.getById(ws.id, true) : null;
    const now = new Date().toISOString();
    const id = ws.id || crypto.randomUUID();

    if (existing) {
      const updated: Workspace = {
        ...existing,
        name: ws.name.trim(),
        description: ws.description !== undefined ? ws.description : existing.description,
        updated_at: now,
        version: isSync ? (ws.version || existing.version) : existing.version + 1,
        is_deleted: ws.is_deleted !== undefined ? ws.is_deleted : existing.is_deleted
      };

      await sqliteClient.execute(`
        UPDATE workspaces
        SET name = ?, description = ?, updated_at = ?, version = ?, is_deleted = ?
        WHERE id = ?;
      `, [updated.name, updated.description, updated.updated_at, updated.version, updated.is_deleted, id]);

      if (!isSync) {
        await outboxRepository.addMutation({
          entity: 'workspace',
          operation: 'UPDATE',
          entity_id: id,
          workspace_id: id,
          client_timestamp: now,
          version: updated.version,
          payload_json: JSON.stringify({ name: updated.name, description: updated.description })
        });
      }

      return updated;
    } else {
      const created: Workspace = {
        id,
        name: ws.name.trim(),
        description: ws.description || null,
        owner_id: ws.owner_id || 'current_user',
        created_by: ws.created_by || 'current_user',
        created_at: ws.created_at || now,
        updated_by: ws.updated_by || 'current_user',
        updated_at: ws.updated_at || now,
        version: ws.version || 1,
        is_deleted: 0
      };

      await sqliteClient.execute(`
        INSERT INTO workspaces (id, name, description, owner_id, created_by, created_at, updated_by, updated_at, version, is_deleted)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(id) DO UPDATE SET
          name = excluded.name,
          description = excluded.description,
          owner_id = excluded.owner_id,
          updated_by = excluded.updated_by,
          updated_at = excluded.updated_at,
          version = excluded.version,
          is_deleted = excluded.is_deleted;
      `, [
        created.id, created.name, created.description, created.owner_id,
        created.created_by, created.created_at, created.updated_by, created.updated_at,
        created.version, created.is_deleted
      ]);

      if (!isSync) {
        await outboxRepository.addMutation({
          entity: 'workspace',
          operation: 'INSERT',
          entity_id: id,
          workspace_id: id,
          client_timestamp: now,
          version: created.version,
          payload_json: JSON.stringify({ name: created.name, description: created.description })
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
      UPDATE workspaces 
      SET is_deleted = 1, updated_at = ?, version = ?
      WHERE id = ?;
    `, [now, newVersion, id]);

    if (!isSync) {
      await outboxRepository.addMutation({
        entity: 'workspace',
        operation: 'DELETE',
        entity_id: id,
        workspace_id: id,
        client_timestamp: now,
        version: newVersion,
        payload_json: JSON.stringify({ id })
      });
    }
  }
};
