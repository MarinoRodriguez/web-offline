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

  async getById(id: string): Promise<TaskItem | null> {
    const task = await sqliteClient.querySingle<TaskItem>(`
      SELECT id, list_id, workspace_id, parent_task_id, title, description,
             status, priority, due_date, position, created_by, created_at,
             updated_by, updated_at, version, is_deleted
      FROM tasks
      WHERE id = ? AND is_deleted = 0;
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
    const existing = task.id ? await this.getById(task.id) : null;
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

      await sqliteClient.execute(`
        UPDATE tasks
        SET list_id = ?, workspace_id = ?, parent_task_id = ?, title = ?, description = ?,
            status = ?, priority = ?, due_date = ?, position = ?, updated_at = ?, version = ?, is_deleted = ?
        WHERE id = ?;
      `, [
        updated.list_id || null, updated.workspace_id, updated.parent_task_id || null,
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

      await sqliteClient.execute(`
        INSERT INTO tasks (
          id, list_id, workspace_id, parent_task_id, title, description,
          status, priority, due_date, position, created_by, created_at,
          updated_by, updated_at, version, is_deleted
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
      `, [
        created.id, created.list_id || null, created.workspace_id, created.parent_task_id,
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
