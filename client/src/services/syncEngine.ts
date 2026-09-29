import { apiRequest } from './apiClient';
import { authStorage } from './authStorage';
import { 
  outboxRepository, 
  metaRepository, 
  localWorkspaceRepository, 
  localListRepository, 
  localTaskRepository 
} from '../db';

export interface SyncBatchResponse {
  syncedAt: string;
  appliedCount: number;
  rejections: Array<{
    mutationId: string;
    entity: string;
    entityId: string;
    reason: string;
    statusCode: number;
  }>;
  changes: {
    workspaces: Array<any>;
    lists: Array<any>;
    tasks: Array<any>;
    tombstones: Array<{
      entityType: string;
      entityId: string;
      workspaceId?: string;
      deletedAt: string;
    }>;
  };
}

export interface SyncResult {
  success: boolean;
  pushedCount: number;
  pulledCount: number;
  rejections: Array<{ mutationId: string; reason: string }>;
  error?: string;
}

class SyncEngine {
  private isSyncing = false;
  private autoSyncInterval: any = null;

  public async syncNow(targetWorkspaceId?: string): Promise<SyncResult> {
    if (this.isSyncing) {
      return { success: false, pushedCount: 0, pulledCount: 0, rejections: [], error: 'Sincronización ya en curso' };
    }

    if (!navigator.onLine) {
      return { success: false, pushedCount: 0, pulledCount: 0, rejections: [], error: 'Dispositivo sin conexión a internet.' };
    }

    if (!authStorage.isAuthenticated()) {
      return { success: false, pushedCount: 0, pulledCount: 0, rejections: [], error: 'Sesión no iniciada.' };
    }

    this.isSyncing = true;
    try {
      // 1. Gather pending mutations from local outbox
      const pendingMutations = await outboxRepository.getPendingMutations();
      const lastSyncedAt = await metaRepository.getLastSyncedAt();

      const mutationsPayload = pendingMutations.map((m) => {
        let payloadObj = {};
        try {
          payloadObj = JSON.parse(m.payload_json || '{}');
        } catch {
          payloadObj = {};
        }

        return {
          id: m.id,
          entity: m.entity,
          operation: m.operation,
          entityId: m.entity_id,
          workspaceId: m.workspace_id || null,
          clientTimestamp: m.client_timestamp,
          version: m.version,
          payload: payloadObj,
        };
      });

      // 2. Dispatch batch request to backend
      const response = await apiRequest<SyncBatchResponse>('/sync/batch', {
        method: 'POST',
        body: JSON.stringify({
          lastSyncedAt: lastSyncedAt || null,
          workspaceId: targetWorkspaceId || null,
          mutations: mutationsPayload,
        }),
      });

      if (!response.success || !response.data) {
        throw new Error(response.message || 'Error en respuesta de sincronización');
      }

      const syncData = response.data;
      const rejections = syncData.rejections || [];
      const rejectedMutationIds = new Set(rejections.map((r) => r.mutationId));

      // 3. Remove successfully applied mutations from local outbox
      const appliedMutationIds = pendingMutations
        .map((m) => m.id)
        .filter((id) => !rejectedMutationIds.has(id));

      if (appliedMutationIds.length > 0) {
        await outboxRepository.removeMutations(appliedMutationIds);
      }

      // 4. Handle rejections (log & flag in outbox)
      for (const rej of rejections) {
        console.warn(`[SyncEngine] Mutation ${rej.mutationId} rejected (${rej.statusCode}): ${rej.reason}`);
        await outboxRepository.markFailed(rej.mutationId, `[${rej.statusCode}] ${rej.reason}`);
      }

      // 5. Apply Pulled Changes into Local SQLite (with isSync = true to prevent echo in outbox)
      const changes = syncData.changes;
      let pulledCount = 0;

      // Workspaces
      if (changes.workspaces && changes.workspaces.length > 0) {
        for (const ws of changes.workspaces) {
          await localWorkspaceRepository.save({
            id: ws.id,
            name: ws.name,
            description: ws.description,
            owner_id: ws.ownerId,
            created_by: ws.createdBy,
            created_at: ws.createdAt,
            updated_by: ws.updatedBy,
            updated_at: ws.updatedAt,
            version: ws.version,
            is_deleted: ws.isDeleted ? 1 : 0,
          }, true);
          pulledCount++;
        }
      }

      // Lists
      if (changes.lists && changes.lists.length > 0) {
        for (const l of changes.lists) {
          await localListRepository.save({
            id: l.id,
            workspace_id: l.workspaceId,
            name: l.name,
            color: l.color,
            position: l.position,
            created_by: l.createdBy,
            created_at: l.createdAt,
            updated_by: l.updatedBy,
            updated_at: l.updatedAt,
            version: l.version,
            is_deleted: l.isDeleted ? 1 : 0,
          }, true);
          pulledCount++;
        }
      }

      // Tasks
      if (changes.tasks && changes.tasks.length > 0) {
        for (const t of changes.tasks) {
          await localTaskRepository.save({
            id: t.id,
            list_id: t.listId,
            workspace_id: t.workspaceId,
            parent_task_id: t.parentTaskId,
            title: t.title,
            description: t.description,
            status: t.status,
            priority: t.priority,
            due_date: t.dueDate,
            position: t.position,
            created_by: t.createdBy,
            created_at: t.createdAt,
            updated_by: t.updatedBy,
            updated_at: t.updatedAt,
            version: t.version,
            is_deleted: t.isDeleted ? 1 : 0,
          }, true);
          pulledCount++;
        }
      }

      // Tombstones (Hard/Soft Deletions)
      if (changes.tombstones && changes.tombstones.length > 0) {
        for (const tomb of changes.tombstones) {
          if (tomb.entityType === 'task') {
            await localTaskRepository.delete(tomb.entityId, true);
          } else if (tomb.entityType === 'list') {
            await localListRepository.delete(tomb.entityId, true);
          } else if (tomb.entityType === 'workspace') {
            await localWorkspaceRepository.delete(tomb.entityId, true);
          }
          pulledCount++;
        }
      }

      // 6. Persist new last_synced_at UTC timestamp
      await metaRepository.setLastSyncedAt(syncData.syncedAt);

      // 7. Notify app components that local DB has fresh synced data
      window.dispatchEvent(new CustomEvent('app:data-synced', {
        detail: {
          syncedAt: syncData.syncedAt,
          pushedCount: appliedMutationIds.length,
          pulledCount,
        }
      }));

      return {
        success: true,
        pushedCount: appliedMutationIds.length,
        pulledCount,
        rejections: rejections.map(r => ({ mutationId: r.mutationId, reason: r.reason })),
      };
    } catch (err: any) {
      console.error('[SyncEngine] Error executing batch sync:', err);
      return {
        success: false,
        pushedCount: 0,
        pulledCount: 0,
        rejections: [],
        error: err?.message || 'Error de comunicación con el backend.',
      };
    } finally {
      this.isSyncing = false;
    }
  }

  public startAutoSync(intervalSeconds = 30): void {
    if (this.autoSyncInterval) return;

    // Listen to network regain event
    window.addEventListener('online', () => {
      console.info('[SyncEngine] Connectivity restored, executing background sync...');
      setTimeout(() => this.syncNow(), 1500);
    });

    // Periodic sync
    this.autoSyncInterval = setInterval(() => {
      if (navigator.onLine && authStorage.isAuthenticated()) {
        this.syncNow();
      }
    }, intervalSeconds * 1000);

    console.info(`[SyncEngine] Auto-sync started (interval: ${intervalSeconds}s)`);
  }

  public stopAutoSync(): void {
    if (this.autoSyncInterval) {
      clearInterval(this.autoSyncInterval);
      this.autoSyncInterval = null;
    }
  }

  public getIsSyncing(): boolean {
    return this.isSyncing;
  }
}

export const syncEngine = new SyncEngine();
