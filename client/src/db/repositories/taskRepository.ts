import { sqliteClient } from '../sqliteClient';
import { TaskItem } from '../schema';
import { outboxRepository } from './outboxRepository';

export const localTaskRepository = {
  async getByWorkspace(workspaceId: string): Promise<TaskItem[]> {
    const allTasks = await sqliteClient.query<TaskItem>(`
      SELECT id, list_id, workspace_id, parent_task_id, title, description,
             status, priority, due_date, position, created_by, created_at,
             updated_by, updated_at, version, is_deleted
      FROM tasks
      WHERE workspace_id = ? AND is_deleted = 0
      ORDER BY position ASC, created_at ASC;
    `, [workspaceId]);

    return this.buildHierarchy(allTasks);
  },

  async getByList(listId: string): Promise<TaskItem[]> {
    const allTasks = await sqliteClient.query<TaskItem>(`
      SELECT id, list_id, workspace_id, parent_task_id, title, description,
             status, priority, due_date, position, created_by, created_at,
             updated_by, updated_at, version, is_deleted
      FROM tasks
      WHERE list_id = ? AND is_deleted = 0
      ORDER BY position ASC, created_at ASC;
    `, [listId]);

    return this.buildHierarchy(allTasks);
  },

  async getById(id: string, includeDeleted = false): Promise<TaskItem | null> {
    const task = await sqliteClient.querySingle<TaskItem>(`
      SELECT id, list_id, workspace_id, parent_task_id, title, description,
             status, priority, due_date, position, created_by, created_at,
             updated_by, updated_at, version, is_deleted
      FROM tasks
      WHERE id = ? ${includeDeleted ? '' : 'AND is_deleted = 0'};
    `, [id]);

    if (!task) return null;

    const subtasks = await sqliteClient.query<TaskItem>(`
      SELECT id, list_id, workspace_id, parent_task_id, title, description,
             status, priority, due_date, position, created_by, created_at,
             updated_by, updated_at, version, is_deleted
      FROM tasks
      WHERE parent_task_id = ? AND is_deleted = 0
      ORDER BY position ASC, created_at ASC;
    `, [id]);

    const subtaskCount = subtasks.length;
    const completedCount = subtasks.filter(s => s.status === 'DONE').length;
    const progress = subtaskCount > 0 ? Math.round((completedCount / subtaskCount) * 100) : (task.status === 'DONE' ? 100 : 0);

    return {
      ...task,
      subtasks,
      subtask_count: subtaskCount,
      completed_subtask_count: completedCount,
      progress_percentage: progress
    };
  },

  async getSubtasks(parentTaskId: string): Promise<TaskItem[]> {
    return sqliteClient.query<TaskItem>(`
      SELECT id, list_id, workspace_id, parent_task_id, title, description,
             status, priority, due_date, position, created_by, created_at,
             updated_by, updated_at, version, is_deleted
      FROM tasks
      WHERE parent_task_id = ? AND is_deleted = 0
      ORDER BY position ASC, created_at ASC;
    `, [parentTaskId]);
  },

  async save(task: Partial<TaskItem> & { id?: string; list_id?: string | null; title: string }, isSync = false): Promise<TaskItem> {
    const existing = task.id ? await this.getById(task.id, true) : null;
    const now = new Date().toISOString();
    const id = task.id || crypto.randomUUID();

    if (existing) {
      const targetStatus = task.status || existing.status;

      // Business Rule: Check if completing parent task with incomplete subtasks
      if (targetStatus === 'DONE' && existing.status !== 'DONE') {
        const pendingCount = await this.countPendingSubtasks(id);
        if (pendingCount > 0) {
          throw new Error(`No se puede completar la tarea: existen ${pendingCount} subtareas pendientes que deben completarse primero.`);
        }
      }

      const updated: TaskItem = {
        ...existing,
        list_id: task.list_id !== undefined ? task.list_id : existing.list_id,
        workspace_id: task.workspace_id || existing.workspace_id,
        parent_task_id: task.parent_task_id !== undefined ? task.parent_task_id : existing.parent_task_id,
        title: task.title.trim(),
        description: task.description !== undefined ? task.description : existing.description,
        status: targetStatus,
        priority: task.priority || existing.priority,
        due_date: task.due_date !== undefined ? task.due_date : existing.due_date,
        position: task.position !== undefined ? task.position : existing.position,
        updated_at: now,
        version: isSync ? (task.version || existing.version) : existing.version + 1,
        is_deleted: task.is_deleted !== undefined ? task.is_deleted : existing.is_deleted
      };

      // Sanitize list_id: verify referenced list exists locally, otherwise set null (tareas sin lista)
      let validListId = updated.list_id || null;
      if (validListId) {
        const listExists = await sqliteClient.querySingle<{ id: string }>(
          'SELECT id FROM lists WHERE id = ?;',
          [validListId]
        );
        if (!listExists) {
          validListId = null;
        }
      }

      // Sanitize parent_task_id: verify parent task exists locally, otherwise set null
      let validParentTaskId = updated.parent_task_id || null;
      if (validParentTaskId) {
        const parentExists = await sqliteClient.querySingle<{ id: string }>(
          'SELECT id FROM tasks WHERE id = ?;',
          [validParentTaskId]
        );
        if (!parentExists) {
          validParentTaskId = null;
        }
      }

      // Ensure workspace exists locally before updating task
      if (updated.workspace_id) {
        const wsExists = await sqliteClient.querySingle<{ id: string }>(
          'SELECT id FROM workspaces WHERE id = ?;',
          [updated.workspace_id]
        );
        if (!wsExists) {
          await sqliteClient.execute(
            `INSERT OR IGNORE INTO workspaces (id, name, owner_id, created_by, created_at, updated_at, version, is_deleted)
             VALUES (?, 'Workspace', 'system', 'system', ?, ?, 1, 0);`,
            [updated.workspace_id, now, now]
          );
        }
      }

      updated.list_id = validListId;
      updated.parent_task_id = validParentTaskId;

      await sqliteClient.execute(`
        UPDATE tasks
        SET list_id = ?, workspace_id = ?, parent_task_id = ?, title = ?, description = ?,
            status = ?, priority = ?, due_date = ?, position = ?, updated_at = ?, version = ?, is_deleted = ?
        WHERE id = ?;
      `, [
        validListId, updated.workspace_id, validParentTaskId,
        updated.title, updated.description || null, updated.status, updated.priority,
        updated.due_date || null, updated.position, updated.updated_at, updated.version,
        updated.is_deleted, id
      ]);

      if (!isSync) {
        await outboxRepository.addMutation({
          entity: 'task',
          operation: 'UPDATE',
          entity_id: id,
          workspace_id: updated.workspace_id,
          client_timestamp: now,
          version: updated.version,
          payload_json: JSON.stringify({
            listId: updated.list_id || null,
            workspaceId: updated.workspace_id,
            parentTaskId: updated.parent_task_id,
            title: updated.title,
            description: updated.description,
            status: updated.status,
            priority: updated.priority,
            dueDate: updated.due_date,
            position: updated.position
          })
        });
      }

      return updated;
    } else {
      const created: TaskItem = {
        id,
        list_id: task.list_id || null,
        workspace_id: task.workspace_id || '',
        parent_task_id: task.parent_task_id || null,
        title: task.title.trim(),
        description: task.description || null,
        status: task.status || 'TODO',
        priority: task.priority || 'MEDIUM',
        due_date: task.due_date || null,
        position: task.position ?? 0,
        created_by: task.created_by || 'current_user',
        created_at: task.created_at || now,
        updated_by: task.updated_by || 'current_user',
        updated_at: task.updated_at || now,
        version: task.version || 1,
        is_deleted: 0
      };

      // Sanitize list_id: verify referenced list exists locally, otherwise set null (tareas sin lista)
      let validListId = created.list_id || null;
      if (validListId) {
        const listExists = await sqliteClient.querySingle<{ id: string }>(
          'SELECT id FROM lists WHERE id = ?;',
          [validListId]
        );
        if (!listExists) {
          validListId = null;
        }
      }

      // Sanitize parent_task_id: verify parent task exists locally, otherwise set null
      let validParentTaskId = created.parent_task_id || null;
      if (validParentTaskId) {
        const parentExists = await sqliteClient.querySingle<{ id: string }>(
          'SELECT id FROM tasks WHERE id = ?;',
          [validParentTaskId]
        );
        if (!parentExists) {
          validParentTaskId = null;
        }
      }

      // Ensure workspace exists locally before inserting task
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

      created.list_id = validListId;
      created.parent_task_id = validParentTaskId;

      await sqliteClient.execute(`
        INSERT INTO tasks (
          id, list_id, workspace_id, parent_task_id, title, description,
          status, priority, due_date, position, created_by, created_at,
          updated_by, updated_at, version, is_deleted
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(id) DO UPDATE SET
          list_id = excluded.list_id,
          workspace_id = excluded.workspace_id,
          parent_task_id = excluded.parent_task_id,
          title = excluded.title,
          description = excluded.description,
          status = excluded.status,
          priority = excluded.priority,
          due_date = excluded.due_date,
          position = excluded.position,
          updated_by = excluded.updated_by,
          updated_at = excluded.updated_at,
          version = excluded.version,
          is_deleted = excluded.is_deleted;
      `, [
        created.id, created.list_id, created.workspace_id, created.parent_task_id,
        created.title, created.description, created.status, created.priority,
        created.due_date, created.position, created.created_by, created.created_at,
        created.updated_by, created.updated_at, created.version, created.is_deleted
      ]);

      if (!isSync) {
        await outboxRepository.addMutation({
          entity: 'task',
          operation: 'INSERT',
          entity_id: id,
          workspace_id: created.workspace_id,
          client_timestamp: now,
          version: created.version,
          payload_json: JSON.stringify({
            listId: created.list_id || null,
            workspaceId: created.workspace_id,
            parentTaskId: created.parent_task_id,
            title: created.title,
            description: created.description,
            status: created.status,
            priority: created.priority,
            dueDate: created.due_date,
            position: created.position
          })
        });
      }

      return created;
    }
  },

  async updateStatus(id: string, status: TaskItem['status'], isSync = false): Promise<TaskItem> {
    const existing = await this.getById(id);
    if (!existing) {
      throw new Error(`Tarea con ID '${id}' no encontrada.`);
    }

    if (status === 'DONE' && existing.status !== 'DONE') {
      const pendingCount = await this.countPendingSubtasks(id);
      if (pendingCount > 0) {
        throw new Error(`No se puede completar la tarea: existen ${pendingCount} subtareas pendientes que deben completarse primero.`);
      }
    }

    const now = new Date().toISOString();
    const newVersion = isSync ? existing.version : existing.version + 1;

    await sqliteClient.execute(`
      UPDATE tasks 
      SET status = ?, updated_at = ?, version = ?
      WHERE id = ?;
    `, [status, now, newVersion, id]);

    if (!isSync) {
      await outboxRepository.addMutation({
        entity: 'task',
        operation: 'UPDATE',
        entity_id: id,
        workspace_id: existing.workspace_id,
        client_timestamp: now,
        version: newVersion,
        payload_json: JSON.stringify({
          status
        })
      });
    }

    return {
      ...existing,
      status,
      updated_at: now,
      version: newVersion
    };
  },

  async delete(id: string, isSync = false): Promise<void> {
    const existing = await this.getById(id);
    if (!existing) return;

    const now = new Date().toISOString();
    const newVersion = existing.version + 1;

    await sqliteClient.execute(`
      UPDATE tasks 
      SET is_deleted = 1, updated_at = ?, version = ?
      WHERE id = ? OR parent_task_id = ?;
    `, [now, newVersion, id, id]);

    if (!isSync) {
      await outboxRepository.addMutation({
        entity: 'task',
        operation: 'DELETE',
        entity_id: id,
        workspace_id: existing.workspace_id,
        client_timestamp: now,
        version: newVersion,
        payload_json: JSON.stringify({ id })
      });
    }
  },

  async countPendingSubtasks(parentTaskId: string): Promise<number> {
    const result = await sqliteClient.querySingle<{ count: number }>(`
      SELECT COUNT(1) as count 
      FROM tasks 
      WHERE parent_task_id = ? AND status != 'DONE' AND is_deleted = 0;
    `, [parentTaskId]);

    return result?.count || 0;
  },

  buildHierarchy(allTasks: TaskItem[]): TaskItem[] {
    const topLevel = allTasks.filter(t => !t.parent_task_id);
    const childrenMap = new Map<string, TaskItem[]>();

    for (const task of allTasks) {
      if (task.parent_task_id) {
        if (!childrenMap.has(task.parent_task_id)) {
          childrenMap.set(task.parent_task_id, []);
        }
        childrenMap.get(task.parent_task_id)!.push(task);
      }
    }

    return topLevel.map(parent => {
      const subtasks = childrenMap.get(parent.id) || [];
      const subtaskCount = subtasks.length;
      const completedCount = subtasks.filter(s => s.status === 'DONE').length;
      const progress = subtaskCount > 0 ? Math.round((completedCount / subtaskCount) * 100) : (parent.status === 'DONE' ? 100 : 0);

      return {
        ...parent,
        subtasks,
        subtask_count: subtaskCount,
        completed_subtask_count: completedCount,
        progress_percentage: progress
      };
    });
  }
};
