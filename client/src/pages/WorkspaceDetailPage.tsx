import React, { useEffect, useState, useCallback } from 'react';
import { useParams, Link, useOutletContext } from 'react-router-dom';
import { 
  localWorkspaceRepository, localListRepository, localTaskRepository,
  Workspace, TaskList, TaskItem 
} from '../db';
import { useUrlParams } from '../hooks/useUrlParams';
import { 
  Plus, LayoutGrid, List as ListIcon, Filter, Search, 
  ArrowLeft, FileSpreadsheet, CheckCircle2, Circle, 
  Calendar, Layers, CheckSquare
} from 'lucide-react';

export const WorkspaceDetailPage: React.FC = () => {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const { onRefreshData, refreshTrigger } = useOutletContext<{ onRefreshData: () => void; refreshTrigger?: number }>();

  const {
    openModal,
    viewMode,
    setViewMode,
    filterStatus,
    setFilterStatus,
    searchQuery,
    setSearchQuery,
    selectedListId,
    setSelectedListId
  } = useUrlParams();

  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [lists, setLists] = useState<TaskList[]>([]);
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [loading, setLoading] = useState(true);

  const loadData = useCallback(async () => {
    if (!workspaceId) return;
    try {
      setLoading(true);
      const [ws, listData, taskData] = await Promise.all([
        localWorkspaceRepository.getById(workspaceId),
        localListRepository.getByWorkspace(workspaceId),
        localTaskRepository.getByWorkspace(workspaceId)
      ]);
      setWorkspace(ws);
      setLists(listData);
      setTasks(taskData);
    } catch (err) {
      console.error('Error loading workspace data:', err);
    } finally {
      setLoading(false);
    }
  }, [workspaceId]);

  useEffect(() => {
    loadData();
  }, [loadData, refreshTrigger]);

  // Filter tasks based on URL parameters
  const filteredTasks = tasks.filter(task => {
    if (selectedListId && task.list_id !== selectedListId) return false;

    if (filterStatus === 'pending' && task.status === 'DONE') return false;
    if (filterStatus === 'done' && task.status !== 'DONE') return false;
    if (filterStatus === 'urgent' && task.priority !== 'URGENT') return false;

    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      const matchTitle = task.title.toLowerCase().includes(q);
      const matchDesc = task.description?.toLowerCase().includes(q);
      if (!matchTitle && !matchDesc) return false;
    }

    return true;
  });

  const handleQuickStatusToggle = async (e: React.MouseEvent, task: TaskItem) => {
    e.stopPropagation();
    const nextStatus = task.status === 'DONE' ? 'TODO' : 'DONE';
    try {
      await localTaskRepository.updateStatus(task.id, nextStatus);
      await loadData();
      onRefreshData?.();
    } catch (err: any) {
      alert(err?.message || 'Error al actualizar estado');
    }
  };

  return (
    <div className="flex-1 flex flex-col max-w-7xl w-full mx-auto p-6 space-y-6">
      {/* Breadcrumb & Actions Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2 text-xs text-slate-400">
            <Link to="/workspaces" className="hover:text-blue-400 flex items-center gap-1 transition">
              <ArrowLeft className="w-3.5 h-3.5" />
              Workspaces
            </Link>
            <span>/</span>
            <span className="text-slate-200 font-medium">{workspace?.name || 'Cargando...'}</span>
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight">{workspace?.name}</h1>
          {workspace?.description && (
            <p className="text-xs text-slate-400">{workspace.description}</p>
          )}
        </div>

        {/* Global Action Buttons */}
        <div className="flex items-center gap-2.5 self-start sm:self-auto flex-wrap">
          <button
            onClick={() => openModal('import-csv', { workspaceId })}
            className="flex items-center gap-1.5 px-3 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 text-slate-300 border border-slate-800 text-xs font-medium transition"
          >
            <FileSpreadsheet className="w-4 h-4 text-emerald-400" />
            Importar CSV
          </button>

          <button
            onClick={() => openModal('new-list', { workspaceId })}
            className="flex items-center gap-1.5 px-3 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 text-slate-300 border border-slate-800 text-xs font-medium transition"
          >
            <Layers className="w-4 h-4 text-blue-400" />
            Nueva Lista
          </button>

          <button
            onClick={() => openModal('new-task', { workspaceId, listId: lists[0]?.id || '' })}
            disabled={lists.length === 0}
            className="flex items-center gap-1.5 px-3.5 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold shadow-lg shadow-blue-600/20 disabled:opacity-50 transition"
          >
            <Plus className="w-4 h-4" />
            Nueva Tarea
          </button>
        </div>
      </div>

      {/* URL Filter & View Controls Toolbar */}
      <div className="p-3.5 rounded-2xl bg-slate-900 border border-slate-800 flex flex-col md:flex-row items-stretch md:items-center justify-between gap-3 shadow-lg shadow-black/10">
        {/* Search Input */}
        <div className="relative flex-1 max-w-sm">
          <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-2.5" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Buscar por título o descripción..."
            className="w-full pl-9 pr-3.5 py-1.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-xs focus:outline-none focus:border-blue-500 transition placeholder:text-slate-500"
          />
        </div>

        {/* Filters and View Mode Switchers */}
        <div className="flex items-center gap-2 flex-wrap">
          {/* Status Filter */}
          <div className="flex items-center gap-1 p-1 rounded-xl bg-slate-950 border border-slate-800">
            {(['all', 'pending', 'done', 'urgent'] as const).map((st) => (
              <button
                key={st}
                type="button"
                onClick={() => setFilterStatus(st)}
                className={`px-2.5 py-1 rounded-lg text-xs font-medium capitalize transition ${
                  filterStatus === st
                    ? 'bg-blue-600 text-white shadow'
                    : 'text-slate-400 hover:text-white'
                }`}
              >
                {st === 'all' ? 'Todas' : st === 'pending' ? 'Pendientes' : st === 'done' ? 'Completadas' : 'Urgentes'}
              </button>
            ))}
          </div>

          {/* List Selector Filter */}
          {lists.length > 0 && (
            <select
              value={selectedListId || ''}
              onChange={(e) => setSelectedListId(e.target.value || null)}
              className="px-2.5 py-1.5 rounded-xl bg-slate-950 border border-slate-800 text-slate-300 text-xs focus:outline-none focus:border-blue-500 transition"
            >
              <option value="">Todas las listas</option>
              {lists.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          )}

          {/* View Mode Toggle: Kanban vs List */}
          <div className="flex items-center gap-1 p-1 rounded-xl bg-slate-950 border border-slate-800">
            <button
              type="button"
              onClick={() => setViewMode('kanban')}
              className={`p-1.5 rounded-lg transition ${
                viewMode === 'kanban' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
              }`}
              title="Vista Tablero Kanban"
            >
              <LayoutGrid className="w-4 h-4" />
            </button>
            <button
              type="button"
              onClick={() => setViewMode('list')}
              className={`p-1.5 rounded-lg transition ${
                viewMode === 'list' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
              }`}
              title="Vista Lista Tabular"
            >
              <ListIcon className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>

      {/* Main Board Canvas */}
      {loading ? (
        <div className="p-12 text-center text-slate-500 text-xs">Cargando tablero desde SQLite...</div>
      ) : lists.length === 0 ? (
        <div className="p-12 rounded-2xl bg-slate-900/60 border border-slate-800 text-center space-y-3">
          <div className="w-12 h-12 rounded-2xl bg-blue-500/10 text-blue-400 flex items-center justify-center mx-auto">
            <Layers className="w-6 h-6" />
          </div>
          <h3 className="text-sm font-semibold text-white">No hay listas en este workspace</h3>
          <p className="text-xs text-slate-400 max-w-sm mx-auto">
            Crea una lista (ej. "Por Hacer", "En Curso", "Hecho") o importa tareas mediante un archivo CSV.
          </p>
          <div className="flex justify-center gap-3 pt-2">
            <button
              onClick={() => openModal('new-list', { workspaceId })}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold transition"
            >
              <Plus className="w-4 h-4" />
              Crear Lista
            </button>
          </div>
        </div>
      ) : viewMode === 'kanban' ? (
        /* KANBAN VIEW */
        <div className="flex-1 flex gap-5 overflow-x-auto pb-4 items-start min-h-[500px]">
          {lists.map((list) => {
            const listTasks = filteredTasks.filter(t => t.list_id === list.id);

            return (
              <div
                key={list.id}
                className="w-80 shrink-0 bg-slate-900/90 border border-slate-800/80 rounded-2xl p-4 flex flex-col max-h-[calc(100vh-250px)] shadow-xl"
              >
                {/* Column Header */}
                <div className="flex items-center justify-between mb-3 pb-2 border-b border-slate-800">
                  <div className="flex items-center gap-2 truncate">
                    <span className="w-3 h-3 rounded-full" style={{ backgroundColor: list.color }} />
                    <h3 className="text-sm font-semibold text-white truncate">{list.name}</h3>
                    <span className="text-xs font-mono text-slate-500 px-1.5 py-0.2 rounded-full bg-slate-950">
                      {listTasks.length}
                    </span>
                  </div>

                  <button
                    onClick={() => openModal('new-task', { workspaceId, listId: list.id })}
                    className="p-1 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
                    title="Añadir tarea a esta lista"
                  >
                    <Plus className="w-4 h-4" />
                  </button>
                </div>

                {/* Tasks List Container */}
                <div className="overflow-y-auto space-y-3 flex-1 pr-1">
                  {listTasks.map((task) => (
                    <div
                      key={task.id}
                      onClick={() => openModal('task-detail', { taskId: task.id })}
                      className="p-4 rounded-xl bg-slate-950 border border-slate-800/80 hover:border-blue-500/50 hover:bg-slate-950/90 transition cursor-pointer shadow-md group relative"
                    >
                      {/* Priority badge & Due date */}
                      <div className="flex items-center justify-between mb-2">
                        <span className={`text-[10px] uppercase font-bold tracking-wider px-2 py-0.5 rounded-full border ${
                          task.priority === 'URGENT' ? 'bg-red-500/10 text-red-400 border-red-500/20' :
                          task.priority === 'HIGH' ? 'bg-amber-500/10 text-amber-400 border-amber-500/20' :
                          task.priority === 'LOW' ? 'bg-slate-500/10 text-slate-400 border-slate-500/20' :
                          'bg-blue-500/10 text-blue-400 border-blue-500/20'
                        }`}>
                          {task.priority}
                        </span>

                        {task.due_date && (
                          <span className="text-[10px] text-slate-500 flex items-center gap-1">
                            <Calendar className="w-3 h-3" />
                            {new Date(task.due_date).toLocaleDateString()}
                          </span>
                        )}
                      </div>

                      {/* Title & Quick Status Toggle */}
                      <div className="flex items-start gap-2.5 mb-2">
                        <button
                          type="button"
                          onClick={(e) => handleQuickStatusToggle(e, task)}
                          className="mt-0.5 text-slate-500 hover:text-blue-400 transition"
                          title="Alternar estado completado"
                        >
                          {task.status === 'DONE' ? (
                            <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                          ) : (
                            <Circle className="w-4 h-4" />
                          )}
                        </button>
                        <h4 className={`text-xs font-semibold leading-snug ${
                          task.status === 'DONE' ? 'line-through text-slate-500' : 'text-slate-100'
                        }`}>
                          {task.title}
                        </h4>
                      </div>

                      {/* Mini-tasks / Subtasks Progress Bar Indicator */}
                      {(task.subtask_count || 0) > 0 && (
                        <div className="pt-2 mt-2 border-t border-slate-800/60">
                          <div className="flex items-center justify-between text-[10px] text-slate-400 mb-1 font-mono">
                            <span className="flex items-center gap-1">
                              <CheckSquare className="w-3 h-3 text-blue-400" />
                              {task.completed_subtask_count} / {task.subtask_count} subtareas
                            </span>
                            <span className="font-semibold text-blue-400">{task.progress_percentage}%</span>
                          </div>
                          <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
                            <div
                              className="bg-blue-500 h-full rounded-full transition-all"
                              style={{ width: `${task.progress_percentage}%` }}
                            />
                          </div>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        /* LIST VIEW */
        <div className="border border-slate-800 rounded-2xl overflow-hidden bg-slate-900 shadow-xl">
          <table className="w-full text-xs text-left">
            <thead className="bg-slate-950 border-b border-slate-800 text-slate-400">
              <tr>
                <th className="p-3 w-10"></th>
                <th className="p-3 font-semibold text-slate-300">Tarea</th>
                <th className="p-3 font-semibold text-slate-300">Lista</th>
                <th className="p-3 font-semibold text-slate-300">Prioridad</th>
                <th className="p-3 font-semibold text-slate-300">Subtareas</th>
                <th className="p-3 font-semibold text-slate-300">Fecha Límite</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60 text-slate-300">
              {filteredTasks.map((task) => {
                const list = lists.find(l => l.id === task.list_id);

                return (
                  <tr
                    key={task.id}
                    onClick={() => openModal('task-detail', { taskId: task.id })}
                    className="hover:bg-slate-800/40 cursor-pointer transition"
                  >
                    <td className="p-3" onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        onClick={(e) => handleQuickStatusToggle(e, task)}
                        className="text-slate-500 hover:text-blue-400 transition"
                      >
                        {task.status === 'DONE' ? (
                          <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                        ) : (
                          <Circle className="w-4 h-4" />
                        )}
                      </button>
                    </td>
                    <td className="p-3 font-medium text-white">
                      <span className={task.status === 'DONE' ? 'line-through text-slate-500' : ''}>
                        {task.title}
                      </span>
                    </td>
                    <td className="p-3">
                      <span className="flex items-center gap-1.5">
                        <span className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: list?.color }} />
                        {list?.name}
                      </span>
                    </td>
                    <td className="p-3">
                      <span className={`text-[10px] uppercase font-bold px-2 py-0.5 rounded-full border ${
                        task.priority === 'URGENT' ? 'bg-red-500/10 text-red-400 border-red-500/20' :
                        task.priority === 'HIGH' ? 'bg-amber-500/10 text-amber-400 border-amber-500/20' :
                        task.priority === 'LOW' ? 'bg-slate-500/10 text-slate-400 border-slate-500/20' :
                        'bg-blue-500/10 text-blue-400 border-blue-500/20'
                      }`}>
                        {task.priority}
                      </span>
                    </td>
                    <td className="p-3 font-mono">
                      {(task.subtask_count || 0) > 0 ? (
                        <span className="text-blue-400">
                          {task.completed_subtask_count}/{task.subtask_count} ({task.progress_percentage}%)
                        </span>
                      ) : (
                        <span className="text-slate-600">—</span>
                      )}
                    </td>
                    <td className="p-3 text-slate-400">
                      {task.due_date ? new Date(task.due_date).toLocaleDateString() : '—'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};
