import React, { useEffect, useState, useCallback } from 'react';
import { useUrlParams } from '../../hooks/useUrlParams';
import { localTaskRepository } from '../../db';
import { TaskItem } from '../../db/schema';
import { X, CheckCircle2, Circle, Plus, Trash2, AlertCircle, Calendar, Flag } from 'lucide-react';

interface Props {
  onTaskUpdated?: () => void;
}

const getStatusBadge = (status?: string) => {
  switch (status) {
    case 'DONE':
      return { label: 'Completada', className: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' };
    case 'IN_PROGRESS':
      return { label: 'En curso', className: 'bg-indigo-500/10 text-indigo-400 border-indigo-500/20' };
    case 'CANCELLED':
      return { label: 'Cancelada', className: 'bg-rose-500/10 text-rose-400 border-rose-500/20' };
    case 'TODO':
    default:
      return { label: 'Por hacer', className: 'bg-slate-500/10 text-slate-300 border-slate-700/60' };
  }
};

export const TaskDetailModal: React.FC<Props> = ({ onTaskUpdated }) => {
  const { closeModal, getParam, openModal } = useUrlParams();
  const taskId = getParam('taskId');

  const [task, setTask] = useState<TaskItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [newSubtaskTitle, setNewSubtaskTitle] = useState('');
  const [subtaskLoading, setSubtaskLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadTask = useCallback(async () => {
    if (!taskId) return;
    try {
      setLoading(true);
      const data = await localTaskRepository.getById(taskId);
      setTask(data);
    } catch (err: any) {
      setError(err?.message || 'Error cargando la tarea.');
    } finally {
      setLoading(false);
    }
  }, [taskId]);

  useEffect(() => {
    loadTask();
  }, [loadTask]);

  if (!taskId) return null;

  const handleStatusChange = async (newStatus: TaskItem['status']) => {
    if (!task) return;
    setError(null);

    try {
      await localTaskRepository.updateStatus(task.id, newStatus);
      await loadTask();
      onTaskUpdated?.();
    } catch (err: any) {
      setError(err?.message || 'Error al actualizar el estado de la tarea.');
    }
  };

  const handleToggleSubtask = async (subtask: TaskItem) => {
    const nextStatus = subtask.status === 'DONE' ? 'TODO' : 'DONE';
    setError(null);
    try {
      await localTaskRepository.updateStatus(subtask.id, nextStatus);
      await loadTask();
      onTaskUpdated?.();
    } catch (err: any) {
      setError(err?.message || 'Error al actualizar subtarea.');
    }
  };

  const handleAddSubtask = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newSubtaskTitle.trim() || !task) return;

    try {
      setSubtaskLoading(true);
      setError(null);
      await localTaskRepository.save({
        list_id: task.list_id,
        workspace_id: task.workspace_id,
        parent_task_id: task.id,
        title: newSubtaskTitle.trim(),
        status: 'TODO',
        priority: 'MEDIUM',
      });
      setNewSubtaskTitle('');
      await loadTask();
      onTaskUpdated?.();
    } catch (err: any) {
      setError(err?.message || 'Error al agregar subtarea.');
    } finally {
      setSubtaskLoading(false);
    }
  };

  const handleDeleteSubtask = async (subtaskId: string) => {
    try {
      await localTaskRepository.delete(subtaskId);
      await loadTask();
      onTaskUpdated?.();
    } catch (err: any) {
      setError(err?.message || 'Error al eliminar subtarea.');
    }
  };

  const handleDeleteTask = async () => {
    if (!task) return;
    if (!confirm('¿Estás seguro de que deseas eliminar esta tarea y todas sus subtareas?')) return;

    try {
      await localTaskRepository.delete(task.id);
      onTaskUpdated?.();
      closeModal(['taskId']);
    } catch (err: any) {
      setError(err?.message || 'Error al eliminar tarea.');
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-in fade-in duration-150">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl max-h-[90vh] shadow-2xl flex flex-col relative overflow-hidden">
        {/* Header */}
        <div className="p-6 border-b border-slate-800 flex items-start justify-between">
          <div className="space-y-1 pr-6 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              {(() => {
                const st = getStatusBadge(task?.status);
                return (
                  <span className={`text-[10px] uppercase tracking-wider font-bold px-2 py-0.5 rounded-full border ${st.className}`}>
                    {st.label}
                  </span>
                );
              })()}

              <span className={`text-[10px] uppercase tracking-wider font-bold px-2 py-0.5 rounded-full border ${
                task?.priority === 'URGENT' ? 'bg-red-500/10 text-red-400 border-red-500/20' :
                task?.priority === 'HIGH' ? 'bg-amber-500/10 text-amber-400 border-amber-500/20' :
                task?.priority === 'LOW' ? 'bg-slate-500/10 text-slate-400 border-slate-500/20' :
                'bg-blue-500/10 text-blue-400 border-blue-500/20'
              }`}>
                {task?.priority}
              </span>

              {task?.due_date && (
                <span className="text-xs text-slate-400 flex items-center gap-1">
                  <Calendar className="w-3.5 h-3.5" />
                  {new Date(task.due_date).toLocaleDateString()}
                </span>
              )}
            </div>
            <h2 className="text-lg font-semibold text-white leading-snug">{task?.title || 'Cargando tarea...'}</h2>
          </div>

          <button
            onClick={() => closeModal(['taskId'])}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body content */}
        <div className="p-6 overflow-y-auto space-y-6 flex-1">
          {error && (
            <div className="p-3.5 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-xs flex items-center gap-2.5">
              <AlertCircle className="w-4 h-4 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {/* Description */}
          {task?.description && (
            <div>
              <h4 className="text-xs font-medium text-slate-400 uppercase tracking-wider mb-1.5">Descripción</h4>
              <p className="text-sm text-slate-300 leading-relaxed bg-slate-950/60 p-3.5 rounded-xl border border-slate-800/80">
                {task.description}
              </p>
            </div>
          )}

          {/* Status selector */}
          <div>
            <h4 className="text-xs font-medium text-slate-400 uppercase tracking-wider mb-2">Estado de la Tarea</h4>
            <div className="grid grid-cols-4 gap-2">
              {(['TODO', 'IN_PROGRESS', 'DONE', 'CANCELLED'] as const).map((st) => (
                <button
                  key={st}
                  type="button"
                  onClick={() => handleStatusChange(st)}
                  className={`py-2 px-3 rounded-xl text-xs font-medium transition border ${
                    task?.status === st
                      ? 'bg-blue-600 border-blue-500 text-white shadow-lg shadow-blue-600/20'
                      : 'bg-slate-950 border-slate-800 text-slate-400 hover:text-white hover:border-slate-700'
                  }`}
                >
                  {st === 'TODO' ? 'Por Hacer' : st === 'IN_PROGRESS' ? 'En Progreso' : st === 'DONE' ? 'Completada' : 'Cancelada'}
                </button>
              ))}
            </div>
          </div>

          {/* Subtasks Section with Rule Visualizer */}
          <div className="border-t border-slate-800 pt-6">
            <div className="flex items-center justify-between mb-3">
              <div>
                <h4 className="text-sm font-semibold text-white flex items-center gap-2">
                  Subtareas (Mini-tasks)
                  <span className="text-xs font-mono px-2 py-0.5 rounded-full bg-slate-800 text-slate-300">
                    {task?.completed_subtask_count || 0} / {task?.subtask_count || 0}
                  </span>
                </h4>
                <p className="text-xs text-slate-400 mt-0.5">
                  Regla de integridad: Todas las subtareas deben completarse para finalizar esta tarea padre.
                </p>
              </div>
              <span className="text-xs font-bold text-blue-400 font-mono">
                {task?.progress_percentage || 0}%
              </span>
            </div>

            {/* Progress Bar */}
            <div className="w-full bg-slate-800 h-2 rounded-full overflow-hidden mb-4">
              <div
                className="bg-blue-500 h-full transition-all duration-300 rounded-full"
                style={{ width: `${task?.progress_percentage || 0}%` }}
              />
            </div>

            {/* Subtasks List */}
            <div className="space-y-2 mb-4">
              {task?.subtasks && task.subtasks.length > 0 ? (
                task.subtasks.map((st) => (
                  <div
                    key={st.id}
                    className="flex items-center justify-between p-3 rounded-xl bg-slate-950 border border-slate-800/80 hover:border-slate-700/80 transition group"
                  >
                    <button
                      type="button"
                      onClick={() => handleToggleSubtask(st)}
                      className="flex items-center gap-3 text-left flex-1"
                    >
                      {st.status === 'DONE' ? (
                        <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                      ) : (
                        <Circle className="w-4 h-4 text-slate-500 shrink-0 hover:text-blue-400 transition" />
                      )}
                      <span className={`text-xs ${st.status === 'DONE' ? 'line-through text-slate-500' : 'text-slate-200'}`}>
                        {st.title}
                      </span>
                    </button>

                    <button
                      type="button"
                      onClick={() => handleDeleteSubtask(st.id)}
                      className="opacity-0 group-hover:opacity-100 p-1 text-slate-500 hover:text-red-400 transition"
                      title="Eliminar subtarea"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                  </div>
                ))
              ) : (
                <p className="text-xs text-slate-500 italic py-2">No hay subtareas registradas para esta tarea.</p>
              )}
            </div>

            {/* Quick add subtask form */}
            <form onSubmit={handleAddSubtask} className="flex gap-2">
              <input
                type="text"
                value={newSubtaskTitle}
                onChange={(e) => setNewSubtaskTitle(e.target.value)}
                placeholder="Agregar una nueva mini-tarea y presionar Enter..."
                className="flex-1 px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-800 text-white text-xs focus:outline-none focus:border-blue-500 transition"
              />
              <button
                type="submit"
                disabled={subtaskLoading || !newSubtaskTitle.trim()}
                className="px-3 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-white text-xs font-medium disabled:opacity-50 transition flex items-center gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                Agregar
              </button>
            </form>
          </div>
        </div>

        {/* Footer actions */}
        <div className="p-4 border-t border-slate-800 bg-slate-950/60 flex items-center justify-between">
          <button
            type="button"
            onClick={handleDeleteTask}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs text-red-400 hover:bg-red-500/10 transition"
          >
            <Trash2 className="w-3.5 h-3.5" />
            Eliminar Tarea
          </button>

          <button
            type="button"
            onClick={() => closeModal(['taskId'])}
            className="px-4 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-white text-xs font-medium transition"
          >
            Cerrar
          </button>
        </div>
      </div>
    </div>
  );
};
