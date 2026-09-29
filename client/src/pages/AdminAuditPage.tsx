import React, { useEffect, useState, useMemo } from 'react';
import { apiRequest } from '../services/apiClient';
import { useAuth } from '../context/AuthContext';
import { Navigate } from 'react-router-dom';
import { 
  ShieldCheck, Activity, FileText, CheckCircle2, 
  AlertTriangle, RefreshCw, Eye, Hash, Clock, Globe,
  Copy, Check, Search, Terminal, Laptop, User, Calendar,
  ArrowRight, Shield
} from 'lucide-react';

interface RequestLog {
  id: string;
  correlationId: string;
  userId?: string | null;
  userEmail?: string | null;
  clientIp?: string | null;
  userAgent?: string | null;
  httpMethod: string;
  path: string;
  queryString?: string | null;
  requestHeaders?: string | null;
  requestBody?: string | null;
  statusCode: number;
  responseHeaders?: string | null;
  responseBody?: string | null;
  durationMs: number;
  createdAt: string;

  // Aliases / fallback compatibility
  requestId?: string;
  requestPath?: string;
  requestHeadersJson?: string;
  requestBodyJson?: string;
  responseHeadersJson?: string;
  responseBodyJson?: string;
  executionDurationMs?: number;
}

interface ChangeLog {
  id: string;
  timestamp?: string;
  createdAt?: string;
  userId: string;
  userEmail: string;
  userFullName?: string;
  entityType: string;
  entityId: string;
  action: string;
  description?: string;
  oldValuesJson?: string;
  stateBeforeJson?: string;
  newValuesJson?: string;
  stateAfterJson?: string;
  diffJson?: string;
  tamperHash: string;
  prevHash?: string;
  previousHash?: string;
}

interface VerificationResult {
  id: string;
  isValid: boolean;
  status: string;
  message: string;
}

export const AdminAuditPage: React.FC = () => {
  const { isAdmin } = useAuth();
  const [activeTab, setActiveTab] = useState<'changelog' | 'requests'>('changelog');
  const [searchQuery, setSearchQuery] = useState('');

  // Changelogs state
  const [changeLogs, setChangeLogs] = useState<ChangeLog[]>([]);
  const [loadingChangelog, setLoadingChangelog] = useState(false);
  const [selectedChangeLog, setSelectedChangeLog] = useState<ChangeLog | null>(null);
  const [verifications, setVerifications] = useState<Record<string, VerificationResult>>({});
  const [verifyingId, setVerifyingId] = useState<string | null>(null);

  // Request logs state
  const [requestLogs, setRequestLogs] = useState<RequestLog[]>([]);
  const [loadingRequests, setLoadingRequests] = useState(false);
  const [selectedRequest, setSelectedRequest] = useState<RequestLog | null>(null);

  // Copy feedback state
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  // Guard if not admin
  if (!isAdmin) {
    return <Navigate to="/workspaces" replace />;
  }

  const copyToClipboard = (text: string, key: string) => {
    navigator.clipboard.writeText(text);
    setCopiedKey(key);
    setTimeout(() => setCopiedKey(null), 2000);
  };

  const formatDateSafe = (dateVal?: string | null): string => {
    if (!dateVal) return '-';
    try {
      let d = new Date(dateVal);
      if (isNaN(d.getTime())) {
        // 1. Clean sub-millisecond nanoseconds: .1234567 -> .123
        const cleaned = dateVal.replace(/\.(\d{3})\d+([+-Z]|$)/i, '.$1$2');
        d = new Date(cleaned);
      }
      if (isNaN(d.getTime())) {
        // 2. Strip fractional seconds completely if still failing:
        const cleanedNoFrac = dateVal.replace(/\.\d+([+-Z]|$)/i, '$1');
        d = new Date(cleanedNoFrac);
      }
      return !isNaN(d.getTime()) ? d.toLocaleString() : String(dateVal);
    } catch {
      return String(dateVal);
    }
  };

  const formatJsonOrText = (raw?: string | null): string => {
    if (!raw) return '';
    try {
      let parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
      while (typeof parsed === 'string') {
        try {
          parsed = JSON.parse(parsed);
        } catch {
          break;
        }
      }
      return JSON.stringify(parsed, null, 2);
    } catch {
      return raw;
    }
  };

  const parseDiffJson = (raw?: string | null): {
    type?: string;
    changedFields?: Record<string, { from?: any; to?: any }>;
    state?: Record<string, any>;
  } | null => {
    if (!raw) return null;
    try {
      let parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
      while (typeof parsed === 'string') {
        try {
          parsed = JSON.parse(parsed);
        } catch {
          break;
        }
      }
      return parsed && typeof parsed === 'object' ? parsed : null;
    } catch {
      return null;
    }
  };

  const getHttpMethodBadge = (method: string) => {
    switch (method?.toUpperCase()) {
      case 'GET':
        return 'bg-blue-500/10 text-blue-400 border-blue-500/20';
      case 'POST':
        return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20';
      case 'PUT':
      case 'PATCH':
        return 'bg-amber-500/10 text-amber-400 border-amber-500/20';
      case 'DELETE':
        return 'bg-rose-500/10 text-rose-400 border-rose-500/20';
      default:
        return 'bg-slate-500/10 text-slate-400 border-slate-500/20';
    }
  };

  const getStatusCodeBadge = (statusCode: number) => {
    if (statusCode < 300) {
      return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20';
    }
    if (statusCode < 400) {
      return 'bg-cyan-500/10 text-cyan-400 border-cyan-500/20';
    }
    if (statusCode < 500) {
      return 'bg-amber-500/10 text-amber-400 border-amber-500/20';
    }
    return 'bg-rose-500/10 text-rose-400 border-rose-500/20';
  };

  const getActionBadge = (action: string) => {
    const upper = (action || '').toUpperCase();
    if (upper.includes('CREATE') || upper.includes('INSERT') || upper.includes('REGISTER')) {
      return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20';
    }
    if (upper.includes('UPDATE') || upper.includes('MODIFY')) {
      return 'bg-blue-500/10 text-blue-400 border-blue-500/20';
    }
    if (upper.includes('DELETE') || upper.includes('REMOVE')) {
      return 'bg-rose-500/10 text-rose-400 border-rose-500/20';
    }
    return 'bg-purple-500/10 text-purple-400 border-purple-500/20';
  };

  const loadChangeLogs = async () => {
    try {
      setLoadingChangelog(true);
      const res = await apiRequest<{ items: ChangeLog[]; total: number }>('/audit/changelog?pageSize=50');
      if (res.success && res.data) {
        setChangeLogs(res.data.items || []);
      }
    } catch (err) {
      console.error('Error fetching changelogs:', err);
    } finally {
      setLoadingChangelog(false);
    }
  };

  const loadRequestLogs = async () => {
    try {
      setLoadingRequests(true);
      const res = await apiRequest<{ items: RequestLog[]; total: number }>('/audit/requests?pageSize=50');
      if (res.success && res.data) {
        setRequestLogs(res.data.items || []);
      }
    } catch (err) {
      console.error('Error fetching requests:', err);
    } finally {
      setLoadingRequests(false);
    }
  };

  useEffect(() => {
    if (activeTab === 'changelog') {
      loadChangeLogs();
    } else {
      loadRequestLogs();
    }
  }, [activeTab]);

  const verifyIntegrity = async (id: string) => {
    try {
      setVerifyingId(id);
      const res = await apiRequest<VerificationResult>(`/audit/changelog/${id}/verify`);
      if (res.success && res.data) {
        setVerifications(prev => ({ ...prev, [id]: res.data! }));
      }
    } catch (err) {
      console.error('Error verifying integrity:', err);
    } finally {
      setVerifyingId(null);
    }
  };

  // Filtered lists
  const filteredRequests = useMemo(() => {
    if (!searchQuery.trim()) return requestLogs;
    const query = searchQuery.toLowerCase().trim();
    return requestLogs.filter((r) => {
      const path = (r.path || r.requestPath || '').toLowerCase();
      const qs = (r.queryString || '').toLowerCase();
      const method = (r.httpMethod || '').toLowerCase();
      const status = String(r.statusCode);
      const email = (r.userEmail || '').toLowerCase();
      const ip = (r.clientIp || '').toLowerCase();
      const corr = (r.correlationId || r.requestId || '').toLowerCase();
      return (
        path.includes(query) ||
        qs.includes(query) ||
        method.includes(query) ||
        status.includes(query) ||
        email.includes(query) ||
        ip.includes(query) ||
        corr.includes(query)
      );
    });
  }, [requestLogs, searchQuery]);

  const filteredChangeLogs = useMemo(() => {
    if (!searchQuery.trim()) return changeLogs;
    const query = searchQuery.toLowerCase().trim();
    return changeLogs.filter((c) => {
      const entity = (c.entityType || '').toLowerCase();
      const entityId = (c.entityId || '').toLowerCase();
      const action = (c.action || '').toLowerCase();
      const email = (c.userEmail || '').toLowerCase();
      const desc = (c.description || '').toLowerCase();
      return (
        entity.includes(query) ||
        entityId.includes(query) ||
        action.includes(query) ||
        email.includes(query) ||
        desc.includes(query)
      );
    });
  }, [changeLogs, searchQuery]);

  return (
    <div className="flex-1 max-w-7xl w-full mx-auto p-6 md:p-8 space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight flex items-center gap-2.5">
              <ShieldCheck className="w-7 h-7 text-purple-400" />
              Módulo de Auditoría y Changelog
            </h1>
            <span className="px-2 py-0.5 rounded-full bg-purple-500/10 text-purple-400 border border-purple-500/20 text-xs font-mono font-medium">
              Solo Lectura • Admin
            </span>
          </div>
          <p className="text-xs text-slate-400 mt-1">
            Registro inmutable de peticiones HTTP, trazabilidad de mutaciones y verificación criptográfica SHA-256
          </p>
        </div>

        <button
          onClick={() => activeTab === 'changelog' ? loadChangeLogs() : loadRequestLogs()}
          className="flex items-center gap-2 px-3.5 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 text-slate-300 border border-slate-800 text-xs font-medium transition self-start sm:self-auto shadow-sm"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${(loadingChangelog || loadingRequests) ? 'animate-spin text-purple-400' : ''}`} />
          Refrescar Registros
        </button>
      </div>

      {/* Tabs and Search Controls */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-3 border-b border-slate-800 pb-3">
        <div className="flex items-center gap-2">
          <button
            onClick={() => { setActiveTab('changelog'); setSearchQuery(''); }}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition ${
              activeTab === 'changelog'
                ? 'bg-purple-600 text-white shadow-lg shadow-purple-600/20'
                : 'text-slate-400 hover:text-white hover:bg-slate-900'
            }`}
          >
            <Activity className="w-4 h-4" />
            Changelog de Entidades ({changeLogs.length})
          </button>

          <button
            onClick={() => { setActiveTab('requests'); setSearchQuery(''); }}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition ${
              activeTab === 'requests'
                ? 'bg-purple-600 text-white shadow-lg shadow-purple-600/20'
                : 'text-slate-400 hover:text-white hover:bg-slate-900'
            }`}
          >
            <Globe className="w-4 h-4" />
            Peticiones HTTP ({requestLogs.length})
          </button>
        </div>

        {/* Quick Filter */}
        <div className="relative min-w-[260px]">
          <Search className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            placeholder={activeTab === 'requests' ? 'Filtrar por ruta, método, estado, IP, correlación...' : 'Filtrar por entidad, acción, usuario...'}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-1.5 rounded-xl bg-slate-900 border border-slate-800 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-purple-500 transition"
          />
        </div>
      </div>

      {/* TAB 1: CHANGELOG */}
      {activeTab === 'changelog' && (
        <div className="space-y-4">
          {loadingChangelog ? (
            <div className="p-12 text-center text-slate-500 text-xs">Cargando registros de auditoría desde base de datos independiente...</div>
          ) : filteredChangeLogs.length === 0 ? (
            <div className="p-12 rounded-2xl bg-slate-900/60 border border-slate-800 text-center text-slate-400 text-xs">
              {searchQuery ? 'No se encontraron registros que coincidan con la búsqueda.' : 'No se han registrado modificaciones de entidades todavía.'}
            </div>
          ) : (
            <div className="border border-slate-800 rounded-2xl overflow-hidden bg-slate-900 shadow-xl">
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-left">
                  <thead className="bg-slate-950 border-b border-slate-800 text-slate-400">
                    <tr>
                      <th className="p-3 whitespace-nowrap">Fecha / Hora</th>
                      <th className="p-3">Usuario</th>
                      <th className="p-3">Acción</th>
                      <th className="p-3">Entidad</th>
                      <th className="p-3">Descripción</th>
                      <th className="p-3">Integridad Hash SHA-256</th>
                      <th className="p-3 text-right">Detalles</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 text-slate-300">
                    {filteredChangeLogs.map((log) => {
                      const verification = verifications[log.id];
                      const logDate = log.timestamp || log.createdAt;

                      return (
                        <tr key={log.id} className="hover:bg-slate-800/40 transition">
                          <td className="p-3 text-slate-400 whitespace-nowrap font-mono text-[11px]">
                            {formatDateSafe(log.timestamp || log.createdAt)}
                          </td>
                          <td className="p-3">
                            <div className="font-semibold text-white">{log.userFullName || log.userEmail || 'Sistema'}</div>
                            <div className="text-[10px] text-slate-500 font-mono">{log.userEmail}</div>
                          </td>
                          <td className="p-3 whitespace-nowrap">
                            <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${getActionBadge(log.action)}`}>
                              {log.action}
                            </span>
                          </td>
                          <td className="p-3 whitespace-nowrap">
                            <span className="font-medium uppercase text-[11px] text-slate-300 px-2 py-0.5 rounded bg-slate-950 border border-slate-800">
                              {log.entityType}
                            </span>
                          </td>
                          <td className="p-3 text-slate-400 text-[11px] max-w-xs truncate" title={log.description || log.entityId}>
                            {log.description || log.entityId}
                          </td>
                          <td className="p-3 whitespace-nowrap">
                            {verification ? (
                              <div className={`flex items-center gap-1.5 text-[11px] font-semibold ${
                                verification.isValid ? 'text-emerald-400' : 'text-red-400'
                              }`}>
                                {verification.isValid ? <CheckCircle2 className="w-3.5 h-3.5" /> : <AlertTriangle className="w-3.5 h-3.5" />}
                                <span>{verification.isValid ? 'Verificado Intacto' : '¡Alterado!'}</span>
                              </div>
                            ) : (
                              <button
                                type="button"
                                onClick={() => verifyIntegrity(log.id)}
                                disabled={verifyingId === log.id}
                                className="flex items-center gap-1 px-2.5 py-1 rounded-lg bg-slate-950 hover:bg-slate-800 text-slate-300 border border-slate-800 text-[10px] font-mono transition"
                              >
                                <Hash className="w-3 h-3 text-purple-400" />
                                {verifyingId === log.id ? 'Validando...' : 'Validar Hash'}
                              </button>
                            )}
                          </td>
                          <td className="p-3 text-right whitespace-nowrap">
                            <button
                              type="button"
                              onClick={() => setSelectedChangeLog(log)}
                              className="p-1.5 rounded-lg bg-slate-950 hover:bg-slate-800 text-slate-400 hover:text-white transition border border-slate-800"
                              title="Ver Diffing y Payload"
                            >
                              <Eye className="w-3.5 h-3.5" />
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* TAB 2: REQUESTS */}
      {activeTab === 'requests' && (
        <div className="space-y-4">
          {loadingRequests ? (
            <div className="p-12 text-center text-slate-500 text-xs">Cargando trazas HTTP...</div>
          ) : filteredRequests.length === 0 ? (
            <div className="p-12 rounded-2xl bg-slate-900/60 border border-slate-800 text-center text-slate-400 text-xs">
              {searchQuery ? 'No se encontraron peticiones que coincidan con la búsqueda.' : 'No hay trazas de peticiones HTTP registradas.'}
            </div>
          ) : (
            <div className="border border-slate-800 rounded-2xl overflow-hidden bg-slate-900 shadow-xl">
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-left">
                  <thead className="bg-slate-950 border-b border-slate-800 text-slate-400">
                    <tr>
                      <th className="p-3 whitespace-nowrap">Fecha / Hora</th>
                      <th className="p-3">Método</th>
                      <th className="p-3">Ruta (Path)</th>
                      <th className="p-3">Estado</th>
                      <th className="p-3">Duración</th>
                      <th className="p-3">Correlation ID</th>
                      <th className="p-3">IP / Cliente</th>
                      <th className="p-3">Usuario</th>
                      <th className="p-3 text-right">Detalles</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 text-slate-300">
                    {filteredRequests.map((req) => {
                      const path = req.path || req.requestPath || '/';
                      const duration = req.durationMs ?? req.executionDurationMs ?? 0;
                      const correlation = req.correlationId || req.requestId || req.id;

                      return (
                        <tr key={req.id} className="hover:bg-slate-800/40 transition">
                          <td className="p-3 text-slate-400 whitespace-nowrap font-mono text-[11px]">
                            {formatDateSafe(req.createdAt)}
                          </td>
                          <td className="p-3 whitespace-nowrap">
                            <span className={`px-2 py-0.5 rounded font-mono text-[10px] font-bold border ${getHttpMethodBadge(req.httpMethod)}`}>
                              {req.httpMethod}
                            </span>
                          </td>
                          <td className="p-3">
                            <div className="font-mono text-[11px] text-white flex items-center gap-1.5 flex-wrap">
                              <span>{path}</span>
                              {req.queryString && (
                                <span className="text-slate-400 text-[10px]">{req.queryString}</span>
                              )}
                            </div>
                          </td>
                          <td className="p-3 whitespace-nowrap">
                            <span className={`px-2 py-0.5 rounded-full font-mono text-[10px] font-bold border ${getStatusCodeBadge(req.statusCode)}`}>
                              {req.statusCode}
                            </span>
                          </td>
                          <td className="p-3 font-mono text-slate-400 whitespace-nowrap">
                            {duration} ms
                          </td>
                          <td className="p-3 font-mono text-[10px] text-slate-400 truncate max-w-[120px]" title={correlation}>
                            {correlation}
                          </td>
                          <td className="p-3 font-mono text-slate-400 whitespace-nowrap">
                            {req.clientIp || '-'}
                          </td>
                          <td className="p-3 text-slate-400 whitespace-nowrap">
                            {req.userEmail || (req.userId ? `ID: ${req.userId.substring(0, 8)}...` : 'Anónimo')}
                          </td>
                          <td className="p-3 text-right whitespace-nowrap">
                            <button
                              type="button"
                              onClick={() => setSelectedRequest(req)}
                              className="p-1.5 rounded-lg bg-slate-950 hover:bg-slate-800 text-slate-400 hover:text-white transition border border-slate-800"
                              title="Ver Headers, Payloads y Trazabilidad completa"
                            >
                              <Eye className="w-3.5 h-3.5" />
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Modal Detail for Changelog Diffing */}
      {selectedChangeLog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-4xl max-h-[92vh] overflow-hidden flex flex-col shadow-2xl">
            <div className="p-5 border-b border-slate-800 flex items-center justify-between bg-slate-950/80">
              <div className="flex items-center gap-2.5">
                <FileText className="w-5 h-5 text-purple-400" />
                <h3 className="text-base font-bold text-white flex items-center gap-2">
                  <span>Modificación: {selectedChangeLog.entityType.toUpperCase()}</span>
                  <span className={`px-2 py-0.5 rounded-full text-xs font-bold border ${getActionBadge(selectedChangeLog.action)}`}>
                    {selectedChangeLog.action}
                  </span>
                </h3>
              </div>
              <button
                onClick={() => setSelectedChangeLog(null)}
                className="text-slate-400 hover:text-white px-2 py-1 text-sm font-semibold rounded-lg hover:bg-slate-800 transition"
              >
                ✕
              </button>
            </div>

            <div className="p-6 overflow-y-auto space-y-5 text-xs">
              {/* Description Banner */}
              {selectedChangeLog.description && (
                <div className="p-3.5 rounded-xl bg-purple-500/10 border border-purple-500/20 text-purple-200">
                  <span className="font-semibold text-purple-300">Descripción:</span> {selectedChangeLog.description}
                </div>
              )}

              {/* Metadata Grid */}
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3 p-4 rounded-xl bg-slate-950 border border-slate-800">
                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">ID Registro:</span>
                  <span className="text-slate-300 font-mono text-[11px] truncate block" title={selectedChangeLog.id}>{selectedChangeLog.id}</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">ID Entidad:</span>
                  <span className="text-slate-300 font-mono text-[11px] truncate block" title={selectedChangeLog.entityId}>{selectedChangeLog.entityId}</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Fecha / Hora:</span>
                  <span className="text-white font-mono text-[11px]">
                    {formatDateSafe(selectedChangeLog.timestamp || selectedChangeLog.createdAt)}
                  </span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Usuario / Email:</span>
                  <span className="text-white font-medium block truncate" title={selectedChangeLog.userEmail}>
                    {selectedChangeLog.userFullName ? `${selectedChangeLog.userFullName} (${selectedChangeLog.userEmail})` : selectedChangeLog.userEmail}
                  </span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">User ID:</span>
                  <span className="text-slate-400 font-mono text-[11px] truncate block" title={selectedChangeLog.userId}>{selectedChangeLog.userId || 'N/A'}</span>
                </div>
                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Prev Hash:</span>
                  <span className="text-slate-400 font-mono text-[10px] truncate block" title={selectedChangeLog.prevHash || selectedChangeLog.previousHash || 'Genesis'}>
                    {selectedChangeLog.prevHash || selectedChangeLog.previousHash || 'Genesis (Sin bloque previo)'}
                  </span>
                </div>
                <div className="col-span-1 md:col-span-2 lg:col-span-3">
                  <div className="flex items-center justify-between">
                    <span className="text-slate-500 text-[10px] uppercase font-bold tracking-wider">Tamper Hash SHA-256 (Inmutable):</span>
                    <button
                      type="button"
                      onClick={() => copyToClipboard(selectedChangeLog.tamperHash, 'tamperHash')}
                      className="text-purple-400 hover:text-purple-300 flex items-center gap-1 text-[10px] transition"
                    >
                      {copiedKey === 'tamperHash' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                      <span>{copiedKey === 'tamperHash' ? 'Copiado' : 'Copiar Hash'}</span>
                    </button>
                  </div>
                  <span className="text-purple-400 font-mono text-[11px] break-all block mt-0.5">{selectedChangeLog.tamperHash}</span>
                </div>
              </div>

              {/* Visual Diffing Table & Creation Notice */}
              {(() => {
                const parsedDiff = parseDiffJson(selectedChangeLog.diffJson);
                if (!parsedDiff) return null;

                if (parsedDiff.changedFields && Object.keys(parsedDiff.changedFields).length > 0) {
                  return (
                    <div className="space-y-2">
                      <div className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] flex items-center gap-2">
                        <Activity className="w-3.5 h-3.5 text-purple-400" />
                        <span>Tabla Comparativa de Campos Modificados:</span>
                      </div>
                      <div className="rounded-xl overflow-hidden border border-slate-800 bg-slate-950">
                        <table className="w-full text-left text-xs">
                          <thead className="bg-slate-900 border-b border-slate-800 text-slate-400 font-mono text-[10px] uppercase">
                            <tr>
                              <th className="p-2.5">Propiedad</th>
                              <th className="p-2.5">Valor Anterior (Old)</th>
                              <th className="p-2.5">Valor Posterior (New)</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-slate-800/60 font-mono text-[11px]">
                            {Object.entries(parsedDiff.changedFields).map(([prop, change]) => (
                              <tr key={prop} className="hover:bg-slate-900/40">
                                <td className="p-2.5 text-purple-300 font-semibold">{prop}</td>
                                <td className="p-2.5 text-rose-400 bg-rose-500/5">
                                  <span className="line-through">{typeof change.from === 'object' ? JSON.stringify(change.from) : String(change.from ?? 'null')}</span>
                                </td>
                                <td className="p-2.5 text-emerald-400 bg-emerald-500/5 font-bold">
                                  <span>{typeof change.to === 'object' ? JSON.stringify(change.to) : String(change.to ?? 'null')}</span>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  );
                }

                if (parsedDiff.type === 'CREATED') {
                  return (
                    <div className="p-3.5 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-300 flex items-center gap-2.5 font-mono text-[11px]">
                      <CheckCircle2 className="w-4 h-4 text-emerald-400 flex-shrink-0" />
                      <div>
                        <span className="font-bold">CREACIÓN DE ENTIDAD:</span> Registro creado desde cero (sin versiones o mutaciones anteriores).
                      </div>
                    </div>
                  );
                }

                return null;
              })()}

              {/* Formatted diffJson */}
              {selectedChangeLog.diffJson && (
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-full bg-blue-400"></span>
                      Estructura Diffing (diffJson formateado):
                    </span>
                    <button
                      type="button"
                      onClick={() => copyToClipboard(formatJsonOrText(selectedChangeLog.diffJson), 'diffJson')}
                      className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                    >
                      {copiedKey === 'diffJson' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                      <span>{copiedKey === 'diffJson' ? 'Copiado' : 'Copiar Diff'}</span>
                    </button>
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-blue-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-64">
                    {formatJsonOrText(selectedChangeLog.diffJson)}
                  </pre>
                </div>
              )}

              {/* Formatted newValuesJson (Resulting Payload) */}
              {(selectedChangeLog.newValuesJson || selectedChangeLog.stateAfterJson) && (
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-full bg-emerald-400"></span>
                      Payload Posterior / Estado Resultante (newValuesJson formateado):
                    </span>
                    <button
                      type="button"
                      onClick={() => copyToClipboard(formatJsonOrText(selectedChangeLog.newValuesJson || selectedChangeLog.stateAfterJson), 'newValues')}
                      className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                    >
                      {copiedKey === 'newValues' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                      <span>{copiedKey === 'newValues' ? 'Copiado' : 'Copiar Payload'}</span>
                    </button>
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-emerald-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-72">
                    {formatJsonOrText(selectedChangeLog.newValuesJson || selectedChangeLog.stateAfterJson)}
                  </pre>
                </div>
              )}

              {/* Formatted oldValuesJson (Previous Payload) */}
              {(selectedChangeLog.oldValuesJson || selectedChangeLog.stateBeforeJson) ? (
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-full bg-amber-400"></span>
                      Payload Anterior / Estado Previo (oldValuesJson formateado):
                    </span>
                    <button
                      type="button"
                      onClick={() => copyToClipboard(formatJsonOrText(selectedChangeLog.oldValuesJson || selectedChangeLog.stateBeforeJson), 'oldValues')}
                      className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                    >
                      {copiedKey === 'oldValues' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                      <span>{copiedKey === 'oldValues' ? 'Copiado' : 'Copiar'}</span>
                    </button>
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-amber-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-64">
                    {formatJsonOrText(selectedChangeLog.oldValuesJson || selectedChangeLog.stateBeforeJson)}
                  </pre>
                </div>
              ) : (
                <div className="p-3 rounded-xl bg-slate-950/80 border border-slate-800 text-slate-500 font-mono text-[11px]">
                  Sin estado anterior (oldValuesJson: null) - La entidad fue creada nueva en este punto.
                </div>
              )}
            </div>

            <div className="p-4 border-t border-slate-800 bg-slate-950/80 flex items-center justify-between">
              <button
                type="button"
                onClick={() => copyToClipboard(JSON.stringify(selectedChangeLog, null, 2), 'rawChangeLog')}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-slate-900 hover:bg-slate-800 text-slate-300 border border-slate-800 text-xs font-mono transition"
              >
                {copiedKey === 'rawChangeLog' ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5 text-purple-400" />}
                <span>{copiedKey === 'rawChangeLog' ? 'Copiado al portapapeles' : 'Copiar JSON Crudo'}</span>
              </button>

              <button
                onClick={() => setSelectedChangeLog(null)}
                className="px-4 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-white font-semibold text-xs transition"
              >
                Cerrar
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Modal Detail for Request Logs - Shows ALL Properties */}
      {selectedRequest && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-5xl max-h-[92vh] overflow-hidden flex flex-col shadow-2xl">
            {/* Modal Header */}
            <div className="p-5 border-b border-slate-800 flex items-center justify-between bg-slate-950/80">
              <div className="flex items-center gap-2.5">
                <Globe className="w-5 h-5 text-blue-400" />
                <div className="flex items-center gap-2 flex-wrap">
                  <h3 className="text-base font-bold text-white font-mono">
                    {selectedRequest.httpMethod} {selectedRequest.path || selectedRequest.requestPath}
                  </h3>
                  <span className={`px-2 py-0.5 rounded-full font-mono text-xs font-bold border ${getStatusCodeBadge(selectedRequest.statusCode)}`}>
                    HTTP {selectedRequest.statusCode}
                  </span>
                  <span className="text-xs text-slate-400 font-mono">
                    ({selectedRequest.durationMs ?? selectedRequest.executionDurationMs ?? 0} ms)
                  </span>
                </div>
              </div>
              <button
                onClick={() => setSelectedRequest(null)}
                className="text-slate-400 hover:text-white px-2 py-1 text-sm font-semibold rounded-lg hover:bg-slate-800 transition"
              >
                ✕
              </button>
            </div>

            {/* Modal Body with All Properties */}
            <div className="p-6 overflow-y-auto space-y-5 text-xs">
              {/* Detailed Metadata Grid */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 p-4 rounded-xl bg-slate-950 border border-slate-800">
                <div>
                  <div className="flex items-center justify-between">
                    <span className="text-slate-500 text-[10px] uppercase font-bold tracking-wider">Correlation ID:</span>
                    <button
                      type="button"
                      onClick={() => copyToClipboard(selectedRequest.correlationId || selectedRequest.requestId || selectedRequest.id, 'correlationId')}
                      className="text-slate-400 hover:text-white transition"
                    >
                      {copiedKey === 'correlationId' ? <Check className="w-2.5 h-2.5 text-emerald-400" /> : <Copy className="w-2.5 h-2.5" />}
                    </button>
                  </div>
                  <span className="text-white font-mono text-[11px] truncate block mt-0.5" title={selectedRequest.correlationId || selectedRequest.requestId || selectedRequest.id}>
                    {selectedRequest.correlationId || selectedRequest.requestId || selectedRequest.id}
                  </span>
                </div>

                <div>
                  <div className="flex items-center justify-between">
                    <span className="text-slate-500 text-[10px] uppercase font-bold tracking-wider">Log ID:</span>
                    <button
                      type="button"
                      onClick={() => copyToClipboard(selectedRequest.id, 'logId')}
                      className="text-slate-400 hover:text-white transition"
                    >
                      {copiedKey === 'logId' ? <Check className="w-2.5 h-2.5 text-emerald-400" /> : <Copy className="w-2.5 h-2.5" />}
                    </button>
                  </div>
                  <span className="text-slate-400 font-mono text-[11px] truncate block mt-0.5" title={selectedRequest.id}>
                    {selectedRequest.id}
                  </span>
                </div>

                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Fecha / Hora (CreatedAt):</span>
                  <span className="text-white font-mono text-[11px] block mt-0.5">
                    {formatDateSafe(selectedRequest.createdAt)}
                  </span>
                </div>

                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Duración de Ejecución:</span>
                  <span className="text-cyan-400 font-mono text-[11px] font-semibold block mt-0.5">
                    {selectedRequest.durationMs ?? selectedRequest.executionDurationMs ?? 0} ms
                  </span>
                </div>

                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Ruta (Path):</span>
                  <span className="text-white font-mono text-[11px] block mt-0.5 truncate" title={selectedRequest.path || selectedRequest.requestPath}>
                    {selectedRequest.path || selectedRequest.requestPath}
                  </span>
                </div>

                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Query String:</span>
                  <span className="text-slate-300 font-mono text-[11px] block mt-0.5 truncate" title={selectedRequest.queryString || '(Ninguno)'}>
                    {selectedRequest.queryString || '(Ninguno)'}
                  </span>
                </div>

                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">IP del Cliente:</span>
                  <span className="text-slate-300 font-mono text-[11px] block mt-0.5">
                    {selectedRequest.clientIp || 'N/A'}
                  </span>
                </div>

                <div>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">Usuario / Email:</span>
                  <span className="text-white font-medium block mt-0.5 truncate" title={selectedRequest.userEmail || 'Anónimo'}>
                    {selectedRequest.userEmail || 'Anónimo'}
                  </span>
                </div>

                {selectedRequest.userId && (
                  <div className="col-span-1 sm:col-span-2">
                    <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">User ID:</span>
                    <span className="text-slate-400 font-mono text-[11px] block mt-0.5 truncate" title={selectedRequest.userId}>
                      {selectedRequest.userId}
                    </span>
                  </div>
                )}

                <div className={`col-span-1 sm:col-span-2 ${selectedRequest.userId ? 'lg:col-span-2' : 'lg:col-span-4'}`}>
                  <span className="text-slate-500 block text-[10px] uppercase font-bold tracking-wider">User-Agent:</span>
                  <span className="text-slate-400 font-mono text-[11px] block mt-0.5 break-all" title={selectedRequest.userAgent || 'N/A'}>
                    {selectedRequest.userAgent || 'N/A'}
                  </span>
                </div>
              </div>

              {/* REQUEST SECTION: Headers & Body */}
              <div className="space-y-3">
                <div className="flex items-center gap-2 border-b border-slate-800 pb-1 text-slate-300 font-semibold uppercase tracking-wider text-[11px]">
                  <ArrowRight className="w-3.5 h-3.5 text-blue-400" />
                  <span>Datos de la Petición (Request)</span>
                </div>

                {/* Request Headers */}
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-slate-400 font-mono text-[10px] uppercase font-bold">Request Headers:</span>
                    {(selectedRequest.requestHeaders || selectedRequest.requestHeadersJson) && (
                      <button
                        type="button"
                        onClick={() => copyToClipboard(formatJsonOrText(selectedRequest.requestHeaders || selectedRequest.requestHeadersJson), 'reqHeaders')}
                        className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                      >
                        {copiedKey === 'reqHeaders' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                        <span>{copiedKey === 'reqHeaders' ? 'Copiado' : 'Copiar Headers'}</span>
                      </button>
                    )}
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-slate-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-56">
                    {formatJsonOrText(selectedRequest.requestHeaders || selectedRequest.requestHeadersJson) || '(Sin headers registrados)'}
                  </pre>
                </div>

                {/* Request Body */}
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-slate-400 font-mono text-[10px] uppercase font-bold">Request Body (Payload):</span>
                    {(selectedRequest.requestBody || selectedRequest.requestBodyJson) && (
                      <button
                        type="button"
                        onClick={() => copyToClipboard(formatJsonOrText(selectedRequest.requestBody || selectedRequest.requestBodyJson), 'reqBody')}
                        className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                      >
                        {copiedKey === 'reqBody' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                        <span>{copiedKey === 'reqBody' ? 'Copiado' : 'Copiar Body'}</span>
                      </button>
                    )}
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-emerald-400 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-56">
                    {formatJsonOrText(selectedRequest.requestBody || selectedRequest.requestBodyJson) || '(Cuerpo de petición vacío / null)'}
                  </pre>
                </div>
              </div>

              {/* RESPONSE SECTION: Headers & Body */}
              <div className="space-y-3 pt-2">
                <div className="flex items-center gap-2 border-b border-slate-800 pb-1 text-slate-300 font-semibold uppercase tracking-wider text-[11px]">
                  <ArrowRight className="w-3.5 h-3.5 text-cyan-400 rotate-180" />
                  <span>Datos de la Respuesta (Response)</span>
                </div>

                {/* Response Headers */}
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-slate-400 font-mono text-[10px] uppercase font-bold">Response Headers:</span>
                    {(selectedRequest.responseHeaders || selectedRequest.responseHeadersJson) && (
                      <button
                        type="button"
                        onClick={() => copyToClipboard(formatJsonOrText(selectedRequest.responseHeaders || selectedRequest.responseHeadersJson), 'resHeaders')}
                        className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                      >
                        {copiedKey === 'resHeaders' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                        <span>{copiedKey === 'resHeaders' ? 'Copiado' : 'Copiar Headers'}</span>
                      </button>
                    )}
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-slate-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-56">
                    {formatJsonOrText(selectedRequest.responseHeaders || selectedRequest.responseHeadersJson) || '(Sin headers registrados)'}
                  </pre>
                </div>

                {/* Response Body */}
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-slate-400 font-mono text-[10px] uppercase font-bold">Response Body (Payload):</span>
                    {(selectedRequest.responseBody || selectedRequest.responseBodyJson) && (
                      <button
                        type="button"
                        onClick={() => copyToClipboard(formatJsonOrText(selectedRequest.responseBody || selectedRequest.responseBodyJson), 'resBody')}
                        className="text-slate-400 hover:text-white flex items-center gap-1 text-[10px] transition"
                      >
                        {copiedKey === 'resBody' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                        <span>{copiedKey === 'resBody' ? 'Copiado' : 'Copiar Response'}</span>
                      </button>
                    )}
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-cyan-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap max-h-72">
                    {formatJsonOrText(selectedRequest.responseBody || selectedRequest.responseBodyJson) || '(Cuerpo de respuesta vacío / null)'}
                  </pre>
                </div>
              </div>
            </div>

            {/* Modal Footer with Quick Copy of Entire Log */}
            <div className="p-4 border-t border-slate-800 bg-slate-950/80 flex items-center justify-between">
              <button
                type="button"
                onClick={() => copyToClipboard(JSON.stringify(selectedRequest, null, 2), 'rawRequestLog')}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-slate-900 hover:bg-slate-800 text-slate-300 border border-slate-800 text-xs font-mono transition"
              >
                {copiedKey === 'rawRequestLog' ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5 text-blue-400" />}
                <span>{copiedKey === 'rawRequestLog' ? 'Copiado al portapapeles' : 'Copiar Registro JSON Completo'}</span>
              </button>

              <button
                onClick={() => setSelectedRequest(null)}
                className="px-4 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-white font-semibold text-xs transition"
              >
                Cerrar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
