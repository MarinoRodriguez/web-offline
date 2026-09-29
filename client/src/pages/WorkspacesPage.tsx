import React, { useEffect, useState, useCallback } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { localWorkspaceRepository, Workspace } from '../db';
import { useUrlParams } from '../hooks/useUrlParams';
import { Briefcase, Plus, ArrowRight, Trash2, Search, FolderKanban } from 'lucide-react';

export const WorkspacesPage: React.FC = () => {
  const navigate = useNavigate();
  const { openModal } = useUrlParams();
  const { onRefreshData, refreshTrigger } = useOutletContext<{ onRefreshData: () => void; refreshTrigger?: number }>();
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchTerm, setSearchTerm] = useState('');

  const loadWorkspaces = useCallback(async () => {
    try {
      setLoading(true);
      const data = await localWorkspaceRepository.getAll();
      setWorkspaces(data);
    } catch (err) {
      console.error('Error loading workspaces:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadWorkspaces();
  }, [loadWorkspaces, refreshTrigger]);

  const handleDelete = async (e: React.MouseEvent, id: string) => {
    e.stopPropagation();
    if (!confirm('¿Eliminar este espacio de trabajo?')) return;
    await localWorkspaceRepository.delete(id);
    await loadWorkspaces();
    onRefreshData?.();
  };

  const filteredWorkspaces = workspaces.filter(ws =>
    ws.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
    (ws.description && ws.description.toLowerCase().includes(searchTerm.toLowerCase()))
  );

  return (
    <div className="flex-1 max-w-7xl w-full mx-auto p-6 md:p-8 space-y-6">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight flex items-center gap-2.5">
            <FolderKanban className="w-7 h-7 text-blue-500" />
            Mis Espacios de Trabajo
          </h1>
          <p className="text-xs text-slate-400 mt-1">
            Gestiona tus proyectos organizados por áreas de trabajo
          </p>
        </div>

        <button
          onClick={() => openModal('new-workspace')}
          className="flex items-center gap-2 px-4 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold shadow-lg shadow-blue-600/20 transition self-start sm:self-auto"
        >
          <Plus className="w-4 h-4" />
          Nuevo Workspace
        </button>
      </div>

      {/* Search Bar */}
      <div className="relative max-w-md">
        <Search className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
        <input
          type="text"
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          placeholder="Buscar workspaces..."
          className="w-full pl-10 pr-4 py-2 rounded-xl bg-slate-900 border border-slate-800 text-white text-xs focus:outline-none focus:border-blue-500 transition placeholder:text-slate-500"
        />
      </div>

      {/* Workspaces Grid */}
      {loading ? (
        <div className="p-12 text-center text-slate-500 text-xs">Cargando espacios de trabajo desde SQLite local...</div>
      ) : filteredWorkspaces.length === 0 ? (
        <div className="p-12 rounded-2xl bg-slate-900/60 border border-slate-800 text-center space-y-3">
          <div className="w-12 h-12 rounded-2xl bg-blue-500/10 text-blue-400 flex items-center justify-center mx-auto">
            <Briefcase className="w-6 h-6" />
          </div>
          <h3 className="text-sm font-semibold text-white">No hay workspaces disponibles</h3>
          <p className="text-xs text-slate-400 max-w-sm mx-auto">
            Comienza creando un espacio de trabajo para organizar tus listas y tareas.
          </p>
          <button
            onClick={() => openModal('new-workspace')}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold transition"
          >
            <Plus className="w-4 h-4" />
            Crear mi primer Workspace
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {filteredWorkspaces.map((ws) => (
            <div
              key={ws.id}
              onClick={() => navigate(`/workspaces/${ws.id}`)}
              className="p-5 rounded-2xl bg-slate-900 border border-slate-800/80 hover:border-blue-500/50 hover:bg-slate-900/90 transition cursor-pointer shadow-lg shadow-black/20 flex flex-col justify-between group"
            >
              <div>
                <div className="flex items-start justify-between gap-2 mb-2">
                  <h3 className="text-base font-semibold text-white group-hover:text-blue-400 transition truncate">
                    {ws.name}
                  </h3>
                  <button
                    onClick={(e) => handleDelete(e, ws.id)}
                    className="opacity-0 group-hover:opacity-100 p-1 rounded-lg text-slate-500 hover:text-red-400 hover:bg-slate-800 transition"
                    title="Eliminar workspace"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
                <p className="text-xs text-slate-400 line-clamp-2 leading-relaxed">
                  {ws.description || 'Sin descripción.'}
                </p>
              </div>

              <div className="pt-4 mt-4 border-t border-slate-800/60 flex items-center justify-between text-xs text-slate-500">
                <span>Versión {ws.version}</span>
                <span className="flex items-center gap-1 text-blue-400 group-hover:translate-x-1 transition-transform">
                  Ver tablero <ArrowRight className="w-3.5 h-3.5" />
                </span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
