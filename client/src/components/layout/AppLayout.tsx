import React, { useEffect, useState } from 'react';
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { sqliteClient } from '../../db/sqliteClient';
import { outboxRepository } from '../../db/repositories/outboxRepository';
import { ModalRoot } from '../modals/ModalRoot';
import { 
  Database, Wifi, WifiOff, Briefcase, LogOut, 
  ShieldCheck, RefreshCw, CheckCircle2 
} from 'lucide-react';

export const AppLayout: React.FC = () => {
  const { user, logout, isAdmin } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [isOnline, setIsOnline] = useState<boolean>(navigator.onLine);
  const [storageType, setStorageType] = useState<string>('Detecting...');
  const [outboxCount, setOutboxCount] = useState<number>(0);
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  useEffect(() => {
    const handleOnline = () => setIsOnline(true);
    const handleOffline = () => setIsOnline(false);

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    async function checkDb() {
      try {
        const res = await sqliteClient.initDb();
        setStorageType(res.storageType);
        const count = await outboxRepository.countPending();
        setOutboxCount(count);
      } catch (err) {
        console.error('Error checking DB:', err);
      }
    }

    checkDb();

    // Poll outbox count every 3 seconds for reactive badge updates
    const interval = setInterval(async () => {
      if (sqliteClient.isReady()) {
        const count = await outboxRepository.countPending();
        setOutboxCount(count);
      }
    }, 3000);

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      clearInterval(interval);
    };
  }, [refreshTrigger]);

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const handleTriggerRefresh = () => {
    setRefreshTrigger(prev => prev + 1);
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      {/* Top Navbar */}
      <header className="border-b border-slate-800/80 bg-slate-900/90 backdrop-blur sticky top-0 z-40 px-6 py-3.5 flex items-center justify-between">
        <div className="flex items-center gap-6">
          <Link to="/workspaces" className="flex items-center gap-3 group">
            <div className="w-9 h-9 rounded-xl bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400 group-hover:scale-105 transition shadow-lg shadow-blue-500/10">
              <Database className="w-5 h-5" />
            </div>
            <div>
              <div className="text-sm font-bold text-white flex items-center gap-2">
                Offline Task Manager
                <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20">
                  v2.0
                </span>
              </div>
              <div className="text-[11px] text-slate-400">SQLite Wasm + OPFS + Sync Batch</div>
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
          </nav>
        </div>

        {/* Right Info Badges & User Profile */}
        <div className="flex items-center gap-3">
          {/* Storage Type */}
          <div className="hidden lg:flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-950 border border-slate-800 text-xs">
            <span className="text-slate-500">Storage:</span>
            <span className="font-medium text-emerald-400 flex items-center gap-1">
              <CheckCircle2 className="w-3 h-3" />
              {storageType}
            </span>
          </div>

          {/* Online/Offline status */}
          <div className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium border ${
            isOnline 
              ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' 
              : 'bg-amber-500/10 text-amber-400 border-amber-500/20'
          }`}>
            {isOnline ? <Wifi className="w-3.5 h-3.5" /> : <WifiOff className="w-3.5 h-3.5" />}
            <span>{isOnline ? 'Online' : 'Offline'}</span>
          </div>

          {/* Pending Outbox Count */}
          <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-950 border border-slate-800 text-xs" title="Mutaciones locales en cola pendientes de sincronizar">
            <span className="text-slate-500">Outbox:</span>
            <span className={`font-mono font-bold ${outboxCount > 0 ? 'text-amber-400' : 'text-blue-400'}`}>
              {outboxCount}
            </span>
          </div>

          {/* User Menu */}
          {user && (
            <div className="flex items-center gap-2 pl-2 border-l border-slate-800">
              <div className="text-right hidden sm:block">
                <div className="text-xs font-semibold text-white flex items-center gap-1.5 justify-end">
                  {user.fullName}
                  {isAdmin && (
                    <span className="flex items-center gap-0.5 text-[10px] bg-purple-500/10 text-purple-400 border border-purple-500/20 px-1.5 py-0.2 rounded">
                      <ShieldCheck className="w-2.5 h-2.5" /> admin
                    </span>
                  )}
                </div>
                <div className="text-[10px] text-slate-400 font-mono">{user.email}</div>
              </div>

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

      {/* Main Outlet */}
      <main className="flex-1 flex flex-col">
        <Outlet context={{ onRefreshData: handleTriggerRefresh }} />
      </main>

      {/* Central Modal Coordinator driven by URL query params */}
      <ModalRoot onRefreshData={handleTriggerRefresh} />
    </div>
  );
};
