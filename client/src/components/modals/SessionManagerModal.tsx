import React, { useEffect, useState } from 'react';
import { useUrlParams } from '../../hooks/useUrlParams';
import { apiRequest } from '../../services/apiClient';
import { X, Smartphone, Globe, Shield, Trash2, AlertCircle, CheckCircle2 } from 'lucide-react';

interface UserSession {
  id: string;
  ipAddress?: string;
  userAgent?: string;
  createdAt: string;
  expiresAt: string;
  isCurrent: boolean;
}

export const SessionManagerModal: React.FC = () => {
  const { closeModal } = useUrlParams();
  const [sessions, setSessions] = useState<UserSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [revokingId, setRevokingId] = useState<string | null>(null);
  const [revokingAll, setRevokingAll] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadSessions = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await apiRequest<UserSession[]>('/auth/sessions');
      if (res.success && res.data) {
        setSessions(res.data);
      } else {
        setError(res.message || 'No se pudieron cargar las sesiones activas.');
      }
    } catch (err: any) {
      setError(err?.message || 'Error al comunicarse con el servidor.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadSessions();
  }, []);

  const handleRevokeSession = async (sessionId: string) => {
    try {
      setRevokingId(sessionId);
      setError(null);
      const res = await apiRequest(`/auth/revoke-session/${sessionId}`, { method: 'POST' });
      if (res.success) {
        setMessage('Sesión revocada exitosamente.');
        await loadSessions();
      } else {
        setError(res.message || 'Error al revocar la sesión.');
      }
    } catch (err: any) {
      setError(err?.message || 'Error de red.');
    } finally {
      setRevokingId(null);
    }
  };

  const handleRevokeAllOther = async () => {
    if (!confirm('¿Deseas cerrar todas las demás sesiones activas excepto esta?')) return;
    try {
      setRevokingAll(true);
      setError(null);
      const res = await apiRequest('/auth/revoke-all', { method: 'POST' });
      if (res.success) {
        setMessage('Todas las demás sesiones fueron revocadas.');
        await loadSessions();
      } else {
        setError(res.message || 'Error al revocar sesiones.');
      }
    } catch (err: any) {
      setError(err?.message || 'Error de red.');
    } finally {
      setRevokingAll(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-in fade-in duration-150">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-xl max-h-[90vh] shadow-2xl flex flex-col relative overflow-hidden">
        {/* Header */}
        <div className="p-6 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-blue-500/10 border border-blue-500/20 flex items-center justify-center text-blue-400">
              <Shield className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-lg font-semibold text-white">Gestor de Sesiones Activas</h3>
              <p className="text-xs text-slate-400">
                Controla los dispositivos y navegadores con acceso a tu cuenta
              </p>
            </div>
          </div>

          <button
            onClick={() => closeModal()}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-4 flex-1">
          {error && (
            <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-xs flex items-center gap-2">
              <AlertCircle className="w-4 h-4 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {message && (
            <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 text-xs flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 shrink-0" />
              <span>{message}</span>
            </div>
          )}

          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-300 uppercase tracking-wider">
              Dispositivos Conectados ({sessions.length})
            </span>
            {sessions.filter(s => !s.isCurrent).length > 0 && (
              <button
                type="button"
                onClick={handleRevokeAllOther}
                disabled={revokingAll}
                className="text-xs text-red-400 hover:text-red-300 font-medium transition"
              >
                {revokingAll ? 'Cerrando...' : 'Cerrar todas las demás'}
              </button>
            )}
          </div>

          {loading ? (
            <div className="p-8 text-center text-slate-500 text-xs">Cargando sesiones desde el servidor...</div>
          ) : sessions.length === 0 ? (
            <div className="p-8 text-center text-slate-500 text-xs">No hay sesiones activas encontradas.</div>
          ) : (
            <div className="space-y-2.5">
              {sessions.map((s) => (
                <div
                  key={s.id}
                  className={`p-3.5 rounded-xl border transition flex items-center justify-between ${
                    s.isCurrent 
                      ? 'bg-blue-600/10 border-blue-500/40 text-white' 
                      : 'bg-slate-950 border-slate-800 text-slate-300 hover:border-slate-700'
                  }`}
                >
                  <div className="flex items-center gap-3">
                    <div className={`p-2 rounded-lg ${s.isCurrent ? 'bg-blue-500/20 text-blue-400' : 'bg-slate-900 text-slate-500'}`}>
                      {s.userAgent?.includes('Mobile') ? <Smartphone className="w-4 h-4" /> : <Globe className="w-4 h-4" />}
                    </div>
                    <div>
                      <div className="text-xs font-semibold flex items-center gap-2">
                        <span>{s.userAgent?.split(' ')[0] || 'Navegador Web'}</span>
                        {s.isCurrent && (
                          <span className="text-[10px] bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 px-1.5 py-0.2 rounded font-mono">
                            Sesión Actual
                          </span>
                        )}
                      </div>
                      <div className="text-[11px] text-slate-500 font-mono flex items-center gap-2 mt-0.5">
                        <span>IP: {s.ipAddress || 'Local'}</span>
                        <span>•</span>
                        <span>Iniciada: {new Date(s.createdAt).toLocaleDateString()}</span>
                      </div>
                    </div>
                  </div>

                  {!s.isCurrent && (
                    <button
                      type="button"
                      disabled={revokingId === s.id}
                      onClick={() => handleRevokeSession(s.id)}
                      className="p-1.5 rounded-lg text-slate-400 hover:text-red-400 hover:bg-slate-800 transition"
                      title="Revocar sesión"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-slate-800 bg-slate-950/60 flex justify-end">
          <button
            type="button"
            onClick={() => closeModal()}
            className="px-4 py-2 rounded-xl text-xs font-medium text-slate-300 hover:text-white hover:bg-slate-800 transition"
          >
            Cerrar
          </button>
        </div>
      </div>
    </div>
  );
};
