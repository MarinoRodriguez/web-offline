import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { syncEngine, SyncResult } from '../services/syncEngine';
import { metaRepository, outboxRepository } from '../db';
import { useAuth } from './AuthContext';

interface SyncContextType {
  isSyncing: boolean;
  lastSyncedAt: string | null;
  pendingOutboxCount: number;
  syncError: string | null;
  lastSyncResult: SyncResult | null;
  syncNow: (targetWorkspaceId?: string) => Promise<SyncResult>;
}

const SyncContext = createContext<SyncContextType | undefined>(undefined);

export const SyncProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { isAuthenticated } = useAuth();
  const [isSyncing, setIsSyncing] = useState(false);
  const [lastSyncedAt, setLastSyncedAt] = useState<string | null>(null);
  const [pendingOutboxCount, setPendingOutboxCount] = useState(0);
  const [syncError, setSyncError] = useState<string | null>(null);
  const [lastSyncResult, setLastSyncResult] = useState<SyncResult | null>(null);

  const refreshMetaAndOutbox = useCallback(async () => {
    try {
      const [lastSync, pending] = await Promise.all([
        metaRepository.getLastSyncedAt(),
        outboxRepository.countPending(),
      ]);
      setLastSyncedAt(lastSync);
      setPendingOutboxCount(pending);
    } catch (err) {
      // Ignored if DB is still warming up
    }
  }, []);

  const handleSyncNow = useCallback(async (targetWorkspaceId?: string): Promise<SyncResult> => {
    setIsSyncing(true);
    setSyncError(null);
    try {
      const result = await syncEngine.syncNow(targetWorkspaceId);
      setLastSyncResult(result);
      if (!result.success) {
        setSyncError(result.error || 'Fallo al sincronizar');
      }
      await refreshMetaAndOutbox();
      return result;
    } catch (err: any) {
      const msg = err?.message || 'Error inesperado de sincronización';
      setSyncError(msg);
      return {
        success: false,
        pushedCount: 0,
        pulledCount: 0,
        rejections: [],
        error: msg,
      };
    } finally {
      setIsSyncing(false);
    }
  }, [refreshMetaAndOutbox]);

  useEffect(() => {
    refreshMetaAndOutbox();

    if (isAuthenticated) {
      syncEngine.startAutoSync(30);
      // Perform initial sync on login/mount
      handleSyncNow();
    }

    const onDataSynced = () => {
      refreshMetaAndOutbox();
    };

    window.addEventListener('app:data-synced', onDataSynced);

    return () => {
      syncEngine.stopAutoSync();
      window.removeEventListener('app:data-synced', onDataSynced);
    };
  }, [isAuthenticated, refreshMetaAndOutbox, handleSyncNow]);

  return (
    <SyncContext.Provider
      value={{
        isSyncing,
        lastSyncedAt,
        pendingOutboxCount,
        syncError,
        lastSyncResult,
        syncNow: handleSyncNow,
      }}
    >
      {children}
    </SyncContext.Provider>
  );
};

export function useSync() {
  const context = useContext(SyncContext);
  if (!context) {
    throw new Error('useSync must be used within a SyncProvider');
  }
  return context;
}
