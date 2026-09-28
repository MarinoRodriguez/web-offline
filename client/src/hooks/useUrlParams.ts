import { useSearchParams } from 'react-router-dom';
import { useCallback } from 'react';

export type TaskViewMode = 'kanban' | 'list';
export type TaskFilterStatus = 'all' | 'pending' | 'done' | 'urgent';

export function useUrlParams() {
  const [searchParams, setSearchParams] = useSearchParams();

  // Modal helpers
  const modal = searchParams.get('modal');

  const openModal = useCallback((modalName: string, extraParams?: Record<string, string | number | null | undefined>) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('modal', modalName);
      if (extraParams) {
        Object.entries(extraParams).forEach(([k, v]) => {
          if (v === null || v === undefined) {
            next.delete(k);
          } else {
            next.set(k, String(v));
          }
        });
      }
      return next;
    }, { replace: false });
  }, [setSearchParams]);

  const closeModal = useCallback((keysToRemove: string[] = []) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.delete('modal');
      keysToRemove.forEach((k) => next.delete(k));
      return next;
    }, { replace: false });
  }, [setSearchParams]);

  const getParam = useCallback((key: string): string | null => {
    return searchParams.get(key);
  }, [searchParams]);

  const setParam = useCallback((key: string, value: string | null | undefined) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value === null || value === undefined || value === '') {
        next.delete(key);
      } else {
        next.set(key, value);
      }
      return next;
    }, { replace: true });
  }, [setSearchParams]);

  // Task Filter params
  const viewMode = (searchParams.get('view') as TaskViewMode) || 'kanban';
  const filterStatus = (searchParams.get('filter') as TaskFilterStatus) || 'all';
  const searchQuery = searchParams.get('search') || '';
  const selectedListId = searchParams.get('listId');
  const selectedTaskId = searchParams.get('taskId');

  const setViewMode = useCallback((view: TaskViewMode) => {
    setParam('view', view === 'kanban' ? null : view);
  }, [setParam]);

  const setFilterStatus = useCallback((filter: TaskFilterStatus) => {
    setParam('filter', filter === 'all' ? null : filter);
  }, [setParam]);

  const setSearchQuery = useCallback((search: string) => {
    setParam('search', search.trim() ? search : null);
  }, [setParam]);

  const setSelectedListId = useCallback((listId: string | null) => {
    setParam('listId', listId);
  }, [setParam]);

  return {
    searchParams,
    modal,
    openModal,
    closeModal,
    getParam,
    setParam,
    viewMode,
    setViewMode,
    filterStatus,
    setFilterStatus,
    searchQuery,
    setSearchQuery,
    selectedListId,
    setSelectedListId,
    selectedTaskId
  };
}
