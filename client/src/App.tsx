import React, { useEffect, useState } from 'react';
import { sqliteClient } from './db/sqliteClient';
import { outboxRepository } from './db/repositories/outboxRepository';
import { Database, Wifi, WifiOff, RefreshCw, CheckCircle2 } from 'lucide-react';

export function App() {
  const [dbStatus, setDbStatus] = useState<'initializing' | 'ready' | 'error'>('initializing');
  const [storageType, setStorageType] = useState<string>('Detecting...');
  const [isOnline, setIsOnline] = useState<boolean>(navigator.onLine);
  const [pendingOutboxCount, setPendingOutboxCount] = useState<number>(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const handleOnline = () => setIsOnline(true);
    const handleOffline = () => setIsOnline(false);

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    async function init() {
      try {
        const res = await sqliteClient.initDb();
        setStorageType(res.storageType);
        setDbStatus('ready');
        const count = await outboxRepository.countPending();
        setPendingOutboxCount(count);
      } catch (err: any) {
        console.error('Failed to init SQLite:', err);
        setDbStatus('error');
        setErrorMessage(err?.message || 'Error inicializando SQLite Wasm');
      }
    }

    init();

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, []);

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      {/* Top Navigation Bar */}
      <header className="border-b border-slate-800 bg-slate-900/80 backdrop-blur px-6 py-4 flex items-center justify-between sticky top-0 z-50">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400 font-bold shadow-lg shadow-blue-500/10">
            <Database className="w-5 h-5 text-blue-400" />
          </div>
          <div>
            <h1 className="text-lg font-semibold tracking-tight text-white flex items-center gap-2">
              Offline Task Manager
              <span className="text-xs px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20 font-mono">
                SQLite Wasm + OPFS
              </span>
            </h1>
            <p className="text-xs text-slate-400">Offline-first con sincronización en lotes</p>
          </div>
        </div>

        {/* Network & Storage Badges */}
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-800/80 border border-slate-700/60 text-xs">
            <span className="text-slate-400">Almacenamiento:</span>
            <span className="font-medium text-emerald-400 flex items-center gap-1.5">
              <CheckCircle2 className="w-3.5 h-3.5" />
              {storageType}
            </span>
          </div>

          <div className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border ${
            isOnline 
              ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' 
              : 'bg-amber-500/10 text-amber-400 border-amber-500/20'
          }`}>
            {isOnline ? <Wifi className="w-3.5 h-3.5" /> : <WifiOff className="w-3.5 h-3.5" />}
            {isOnline ? 'Online' : 'Offline'}
          </div>

          <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-800/80 border border-slate-700/60 text-xs">
            <span className="text-slate-400">Outbox:</span>
            <span className="font-mono font-semibold text-blue-400">{pendingOutboxCount}</span>
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto p-6">
        {dbStatus === 'initializing' && (
          <div className="flex flex-col items-center justify-center p-12 text-center">
            <RefreshCw className="w-8 h-8 text-blue-500 animate-spin mb-4" />
            <p className="text-sm font-medium text-slate-300">Inicializando motor SQLite Wasm & OPFS...</p>
            <p className="text-xs text-slate-500 mt-1">Cargando Web Worker y tablas relacionales locales</p>
          </div>
        )}

        {dbStatus === 'error' && (
          <div className="p-6 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400">
            <h3 className="font-semibold text-base mb-1">Error al iniciar SQLite Wasm</h3>
            <p className="text-sm text-red-300">{errorMessage}</p>
          </div>
        )}

        {dbStatus === 'ready' && (
          <div className="space-y-6">
            <div className="p-6 rounded-2xl bg-slate-900 border border-slate-800 shadow-xl">
              <h2 className="text-base font-semibold text-white mb-2">Base de Datos Local Lista</h2>
              <p className="text-sm text-slate-400">
                El motor relacional SQLite compilado a WebAssembly está ejecutándose en un hilo dedicado (Web Worker) con persistencia en el sistema de archivos del navegador (OPFS).
              </p>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}

export default App;
