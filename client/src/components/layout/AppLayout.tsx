import React, { useEffect, useState } from 'react';
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { useSync } from '../../context/SyncContext';
import { useUrlParams } from '../../hooks/useUrlParams';
import { sqliteClient } from '../../db/sqliteClient';
import { connectivity, ConnectivityState } from '../../services/connectivity';
import { ModalRoot } from '../modals/ModalRoot';
import { 
  Database, Wifi, WifiOff, Briefcase, LogOut, 
  ShieldCheck, RefreshCw, CheckCircle2, AlertCircle,
  Key, UserPlus
} from 'lucide-react';

export const AppLayout: React.FC = () => {
  const { user, logout, isAdmin } = useAuth();
  const { isSyncing, syncNow, lastSyncedAt, pendingOutboxCount, syncError } = useSync();
  const { openModal } = useUrlParams();
  const navigate = useNavigate();
  const location = useLocation();

  const [connectivityState, setConnectivityState] = useState<ConnectivityState>(connectivity.getState());
  const [storageType, setStorageType] = useState<string>('Detecting...');
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  useEffect(() => {
    const unsubscribe = connectivity.subscribe((state) => {
      setConnectivityState(state);
    });

    const handleDataSynced = () => {
      handleTriggerRefresh();
    };

    window.addEventListener('app:data-synced', handleDataSynced);

    async function checkDb() {
      try {
        const res = await sqliteClient.initDb();
        setStorageType(res.storageType);
      } catch (err) {
        console.error('Error checking DB:', err);
      }
    }

    checkDb();

    return () => {
      unsubscribe();
      window.removeEventListener('app:data-synced', handleDataSynced);
    };
  }, []);

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const handleTriggerRefresh = () => {
    setRefreshTrigger(prev => prev + 1);
  };

  const handleManualSync = async () => {
    await syncNow();
    handleTriggerRefresh();
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      {/* Top Navbar */}
      <header className="border-b border-slate-800/80 bg-slate-900/90 backdrop-blur sticky top-0 z-40 px-6 py-3 flex items-center justify-between">
        <div className="flex items-center gap-6">
          <Link to="/workspaces" className="flex items-center gap-3 group">
            <div className="w-9 h-9 rounded-xl bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400 group-hover:scale-105 transition shadow-lg shadow-blue-500/10">
              <Database className="w-5 h-5" />
            </div>
            <div>
              <div className="text-sm font-bold text-white flex items-center gap-2">
                Offline Task Manager
                <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20">
                  SQLite Wasm + OPFS
                </span>
              </div>
              <div className="text-[11px] text-slate-400">Push/Pull Batch Sync</div>
            </div>
          </Link>

          <nav className="hidden md:flex items-center gap-1 border-l border-slate-800 pl-6">
            <Link
              to="/workspaces"
              className={`flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium transition ${
                location.pathname.startsWith('/workspaces')
                  ? 'bg-blue-600/10 text-blue-400 border border-blue-500/20'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800/60'
              }`}
            >
              <Briefcase className="w-3.5 h-3.5" />
              Workspaces
            </Link>

            {isAdmin && (
              <Link
                to="/admin/audit"
                className={`flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium transition ${
                  location.pathname.startsWith('/admin/audit')
                    ? 'bg-purple-600/10 text-purple-400 border border-purple-500/20'
                    : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800/60'
                }`}
              >
                <ShieldCheck className="w-3.5 h-3.5" />
                Auditoría
              </Link>
            )}
          </nav>
        </div>

        {/* Right Info Badges & Actions */}
        <div className="flex items-center gap-3">
          {/* Storage Type */}
          <div 
            className={`hidden sm:flex items-center gap-1.5 px-3 py-1.5 rounded-lg border text-xs ${
              storageType.includes('OPFS')
                ? 'bg-slate-950 border-slate-800'
                : 'bg-amber-500/10 border-amber-500/30'
            }`}
            title={
              storageType.includes('OPFS')
                ? 'Base de datos SQLite persistente en disco (OPFS)'
                : 'Almacenamiento volátil en memoria. Al recargar se perderán los datos sin OPFS.'
            }
          >
            <span className="text-slate-500">Storage:</span>
            <span className={`font-medium flex items-center gap-1 ${
              storageType.includes('OPFS') ? 'text-emerald-400' : 'text-amber-400 font-bold'
            }`}>
              {storageType.includes('OPFS') ? <CheckCircle2 className="w-3 h-3" /> : <AlertCircle className="w-3 h-3 text-amber-400" />}
              {storageType}
            </span>
          </div>

          {/* Online/Offline status */}
          <div 
            className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
              connectivityState.status === 'online'
                ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' 
                : connectivityState.status === 'server_unreachable'
                ? 'bg-amber-500/10 text-amber-400 border-amber-500/30'
                : 'bg-rose-500/10 text-rose-400 border-rose-500/30'
            }`}
            title={
              connectivityState.status === 'online'
                ? 'Conexión activa con el backend .NET'
                : connectivityState.status === 'server_unreachable'
                ? 'Servidor backend no responde (502 Bad Gateway / Desconectado). Modo offline local activo.'
                : 'Dispositivo sin conexión a internet ni red local.'
            }
          >
            {connectivityState.status === 'online' ? (
              <>
                <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
                <Wifi className="w-3.5 h-3.5" />
                <span>Online</span>
              </>
            ) : connectivityState.status === 'server_unreachable' ? (
              <>
                <span className="w-2 h-2 rounded-full bg-amber-400" />
                <WifiOff className="w-3.5 h-3.5" />
                <span>Offline</span>
              </>
            ) : (
              <>
                <span className="w-2 h-2 rounded-full bg-rose-400" />
                <WifiOff className="w-3.5 h-3.5" />
                <span>Sin red</span>
              </>
            )}
          </div>

          {/* Pending Outbox Count */}
          <div 
            className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-slate-950 border border-slate-800 text-xs" 
            title="Mutaciones locales en cola pendientes de sincronizar con el backend"
          >
            <span className="text-slate-500">Outbox:</span>
            <span className={`font-mono font-bold ${pendingOutboxCount > 0 ? 'text-amber-400' : 'text-blue-400'}`}>
              {pendingOutboxCount}
            </span>
          </div>

          {/* Manual Sync Button */}
          <button
            type="button"
            onClick={handleManualSync}
            disabled={isSyncing}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-blue-600/10 hover:bg-blue-600/20 border border-blue-500/30 text-blue-400 text-xs font-medium disabled:opacity-50 transition shadow-sm"
            title={lastSyncedAt ? `Última sincronización: ${new Date(lastSyncedAt).toLocaleTimeString()}` : 'Sin sincronizar aún'}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin text-blue-400' : ''}`} />
            <span>{isSyncing ? 'Sincronizando...' : 'Sincronizar'}</span>
          </button>

          {/* User Menu */}
          {user && (
            <div className="flex items-center gap-2 pl-2 border-l border-slate-800">
              <div className="text-right hidden sm:block">
                <div className="text-xs font-semibold text-white flex items-center gap-1.5 justify-end">
                  {user.fullName}
                  {isAdmin && (
                    <span className="flex items-center gap-0.5 text-[10px] bg-purple-500/10 text-purple-400 border border-purple-500/20 px-1.5 py-0.2 rounded font-mono">
                      <ShieldCheck className="w-2.5 h-2.5" /> admin
                    </span>
                  )}
                </div>
                <div className="text-[10px] text-slate-400 font-mono">{user.email}</div>
              </div>

              {isAdmin && (
                <button
                  type="button"
                  onClick={() => openModal('new-user')}
                  className="p-2 rounded-lg text-slate-400 hover:text-purple-400 hover:bg-slate-800/80 transition"
                  title="Registrar nuevo usuario"
                >
                  <UserPlus className="w-4 h-4" />
                </button>
              )}

              <button
                type="button"
                onClick={() => openModal('session-manager')}
                className="p-2 rounded-lg text-slate-400 hover:text-blue-400 hover:bg-slate-800/80 transition"
                title="Gestionar sesiones activas"
              >
                <Key className="w-4 h-4" />
              </button>

              <button
                onClick={handleLogout}
                className="p-2 rounded-lg text-slate-400 hover:text-red-400 hover:bg-slate-800/80 transition"
                title="Cerrar sesión"
              >
                <LogOut className="w-4 h-4" />
              </button>
            </div>
          )}
        </div>
      </header>

      {/* Sync Error Notice Banner */}
      {syncError && (
        <div className="bg-amber-500/10 border-b border-amber-500/20 px-6 py-2 text-xs text-amber-400 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-4 h-4 shrink-0" />
            <span>Aviso de sincronización: {syncError}</span>
          </div>
          <span className="text-[11px] text-amber-500">Los cambios locales permanecen seguros en tu dispositivo</span>
        </div>
      )}

      {/* Main Content Outlet */}
      <main className="flex-1 flex flex-col">
        <Outlet context={{ onRefreshData: handleTriggerRefresh, refreshTrigger }} />
      </main>

      {/* Central Modal Coordinator driven by URL query params */}
      <ModalRoot onRefreshData={handleTriggerRefresh} />
    </div>
  );
};
