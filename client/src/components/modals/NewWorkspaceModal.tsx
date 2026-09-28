import React, { useState } from 'react';
import { useUrlParams } from '../../hooks/useUrlParams';
import { localWorkspaceRepository } from '../../db';
import { X, Briefcase, Plus } from 'lucide-react';

interface Props {
  onWorkspaceCreated?: () => void;
}

export const NewWorkspaceModal: React.FC<Props> = ({ onWorkspaceCreated }) => {
  const { closeModal } = useUrlParams();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setError('El nombre del espacio de trabajo es requerido.');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      await localWorkspaceRepository.save({
        name: name.trim(),
        description: description.trim() || null,
      });
      onWorkspaceCreated?.();
      closeModal();
    } catch (err: any) {
      setError(err?.message || 'Error al guardar el espacio de trabajo.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-in fade-in duration-150">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl p-6 relative">
        <button
          onClick={() => closeModal()}
          className="absolute top-4 right-4 p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
        >
          <X className="w-5 h-5" />
        </button>

        <div className="flex items-center gap-3 mb-6">
          <div className="w-10 h-10 rounded-xl bg-blue-500/10 border border-blue-500/20 flex items-center justify-center text-blue-400">
            <Briefcase className="w-5 h-5" />
          </div>
          <div>
            <h3 className="text-lg font-semibold text-white">Nuevo Espacio de Trabajo</h3>
            <p className="text-xs text-slate-400">Organiza proyectos, listas y tareas en equipo</p>
          </div>
        </div>

        {error && (
          <div className="mb-4 p-3 rounded-lg bg-red-500/10 border border-red-500/30 text-red-400 text-xs">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-300 mb-1.5">Nombre del Workspace *</label>
            <input
              type="text"
              autoFocus
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="ej. Marketing, Desarrollo, Operaciones"
              className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-sm focus:outline-none focus:border-blue-500 transition"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-300 mb-1.5">Descripción (opcional)</label>
            <textarea
              rows={3}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Detalles sobre el propósito de este espacio..."
              className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950 border border-slate-800 text-white text-sm focus:outline-none focus:border-blue-500 transition resize-none"
            />
          </div>

          <div className="flex items-center justify-end gap-3 pt-3">
            <button
              type="button"
              onClick={() => closeModal()}
              className="px-4 py-2 rounded-xl text-xs font-medium text-slate-400 hover:text-white hover:bg-slate-800 transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold shadow-lg shadow-blue-600/20 disabled:opacity-50 transition"
            >
              <Plus className="w-4 h-4" />
              {loading ? 'Guardando...' : 'Crear Workspace'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
