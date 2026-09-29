import React, { useEffect, useState } from 'react';
import { apiRequest } from '../services/apiClient';
import { useAuth } from '../context/AuthContext';
import { Navigate } from 'react-router-dom';
import { 
  ShieldCheck, Activity, FileText, CheckCircle2, 
  AlertTriangle, RefreshCw, Eye, Hash, Clock, Globe
} from 'lucide-react';

interface RequestLog {
  id: string;
  requestId: string;
  userId?: string;
  userEmail?: string;
  clientIp: string;
  userAgent: string;
  httpMethod: string;
  requestPath: string;
  requestHeadersJson?: string;
  requestBodyJson?: string;
  statusCode: number;
  responseHeadersJson?: string;
  responseBodyJson?: string;
  executionDurationMs: number;
  createdAt: string;
}

interface ChangeLog {
  id: string;
  requestId?: string;
  userId: string;
  userEmail: string;
  userFullName: string;
  entityType: string;
  entityId: string;
  action: 'INSERT' | 'UPDATE' | 'DELETE';
  stateBeforeJson?: string;
  stateAfterJson?: string;
  diffJson?: string;
  tamperHash: string;
  previousHash?: string;
  createdAt: string;
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

  // Guard if not admin
  if (!isAdmin) {
    return <Navigate to="/workspaces" replace />;
  }

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
          className="flex items-center gap-2 px-3.5 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 text-slate-300 border border-slate-800 text-xs font-medium transition self-start sm:self-auto"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${(loadingChangelog || loadingRequests) ? 'animate-spin text-purple-400' : ''}`} />
          Refrescar Registros
        </button>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-2 border-b border-slate-800 pb-3">
        <button
          onClick={() => setActiveTab('changelog')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition ${
            activeTab === 'changelog'
              ? 'bg-purple-600 text-white shadow-lg shadow-purple-600/20'
              : 'text-slate-400 hover:text-white hover:bg-slate-900'
          }`}
        >
          <Activity className="w-4 h-4" />
          Changelog de Entidades (Diffing & Hashes)
        </button>

        <button
          onClick={() => setActiveTab('requests')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition ${
            activeTab === 'requests'
              ? 'bg-purple-600 text-white shadow-lg shadow-purple-600/20'
              : 'text-slate-400 hover:text-white hover:bg-slate-900'
          }`}
        >
          <Globe className="w-4 h-4" />
          Peticiones HTTP (Request / Response)
        </button>
      </div>

      {/* TAB 1: CHANGELOG */}
      {activeTab === 'changelog' && (
        <div className="space-y-4">
          {loadingChangelog ? (
            <div className="p-12 text-center text-slate-500 text-xs">Cargando registros de auditoría desde base de datos independiente...</div>
          ) : changeLogs.length === 0 ? (
            <div className="p-12 rounded-2xl bg-slate-900/60 border border-slate-800 text-center text-slate-400 text-xs">
              No se han registrado modificaciones de entidades todavía.
            </div>
          ) : (
            <div className="border border-slate-800 rounded-2xl overflow-hidden bg-slate-900 shadow-xl">
              <table className="w-full text-xs text-left">
                <thead className="bg-slate-950 border-b border-slate-800 text-slate-400">
                  <tr>
                    <th className="p-3">Fecha / Hora</th>
                    <th className="p-3">Usuario</th>
                    <th className="p-3">Acción</th>
                    <th className="p-3">Entidad</th>
                    <th className="p-3">ID Entidad</th>
                    <th className="p-3">Integridad Hash SHA-256</th>
                    <th className="p-3 text-right">Detalles</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60 text-slate-300">
                  {changeLogs.map((log) => {
                    const verification = verifications[log.id];

                    return (
                      <tr key={log.id} className="hover:bg-slate-800/40 transition">
                        <td className="p-3 text-slate-400 whitespace-nowrap">
                          {new Date(log.createdAt).toLocaleString()}
                        </td>
                        <td className="p-3">
                          <div className="font-semibold text-white">{log.userFullName || 'Sistema'}</div>
                          <div className="text-[10px] text-slate-500 font-mono">{log.userEmail}</div>
                        </td>
                        <td className="p-3">
                          <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${
                            log.action === 'INSERT' ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' :
                            log.action === 'UPDATE' ? 'bg-blue-500/10 text-blue-400 border-blue-500/20' :
                            'bg-red-500/10 text-red-400 border-red-500/20'
                          }`}>
                            {log.action}
                          </span>
                        </td>
                        <td className="p-3 font-medium uppercase text-[11px] text-slate-300">
                          {log.entityType}
                        </td>
                        <td className="p-3 font-mono text-[10px] text-slate-400 truncate max-w-[120px]" title={log.entityId}>
                          {log.entityId}
                        </td>
                        <td className="p-3">
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
                        <td className="p-3 text-right">
                          <button
                            type="button"
                            onClick={() => setSelectedChangeLog(log)}
                            className="p-1.5 rounded-lg bg-slate-950 hover:bg-slate-800 text-slate-400 hover:text-white transition"
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
          )}
        </div>
      )}

      {/* TAB 2: REQUESTS */}
      {activeTab === 'requests' && (
        <div className="space-y-4">
          {loadingRequests ? (
            <div className="p-12 text-center text-slate-500 text-xs">Cargando trazas HTTP...</div>
          ) : requestLogs.length === 0 ? (
            <div className="p-12 rounded-2xl bg-slate-900/60 border border-slate-800 text-center text-slate-400 text-xs">
              No hay trazas de peticiones HTTP registradas.
            </div>
          ) : (
            <div className="border border-slate-800 rounded-2xl overflow-hidden bg-slate-900 shadow-xl">
              <table className="w-full text-xs text-left">
                <thead className="bg-slate-950 border-b border-slate-800 text-slate-400">
                  <tr>
                    <th className="p-3">Hora</th>
                    <th className="p-3">Método</th>
                    <th className="p-3">Ruta</th>
                    <th className="p-3">Estado</th>
                    <th className="p-3">Duración</th>
                    <th className="p-3">IP Cliente</th>
                    <th className="p-3">Usuario</th>
                    <th className="p-3 text-right">Detalles</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60 text-slate-300">
                  {requestLogs.map((req) => (
                    <tr key={req.id} className="hover:bg-slate-800/40 transition">
                      <td className="p-3 text-slate-400 whitespace-nowrap">
                        {new Date(req.createdAt).toLocaleTimeString()}
                      </td>
                      <td className="p-3">
                        <span className={`px-2 py-0.5 rounded font-mono text-[10px] font-bold ${
                          req.httpMethod === 'GET' ? 'bg-blue-500/10 text-blue-400' :
                          req.httpMethod === 'POST' ? 'bg-emerald-500/10 text-emerald-400' :
                          req.httpMethod === 'PUT' ? 'bg-amber-500/10 text-amber-400' :
                          'bg-red-500/10 text-red-400'
                        }`}>
                          {req.httpMethod}
                        </span>
                      </td>
                      <td className="p-3 font-mono text-[11px] text-white">
                        {req.requestPath}
                      </td>
                      <td className="p-3">
                        <span className={`font-mono font-bold ${
                          req.statusCode < 300 ? 'text-emerald-400' :
                          req.statusCode < 400 ? 'text-blue-400' :
                          'text-red-400'
                        }`}>
                          {req.statusCode}
                        </span>
                      </td>
                      <td className="p-3 font-mono text-slate-400">
                        {req.executionDurationMs} ms
                      </td>
                      <td className="p-3 font-mono text-slate-500">
                        {req.clientIp}
                      </td>
                      <td className="p-3 text-slate-400">
                        {req.userEmail || 'Anónimo'}
                      </td>
                      <td className="p-3 text-right">
                        <button
                          type="button"
                          onClick={() => setSelectedRequest(req)}
                          className="p-1.5 rounded-lg bg-slate-950 hover:bg-slate-800 text-slate-400 hover:text-white transition"
                          title="Ver Headers y Payloads"
                        >
                          <Eye className="w-3.5 h-3.5" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Modal Detail for Changelog Diffing */}
      {selectedChangeLog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl max-h-[90vh] overflow-hidden flex flex-col shadow-2xl">
            <div className="p-5 border-b border-slate-800 flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <FileText className="w-5 h-5 text-purple-400" />
                <h3 className="text-base font-bold text-white">
                  Detalle de Modificación: {selectedChangeLog.entityType.toUpperCase()} ({selectedChangeLog.action})
                </h3>
              </div>
              <button
                onClick={() => setSelectedChangeLog(null)}
                className="text-slate-400 hover:text-white px-2 py-1 text-sm font-semibold"
              >
                ✕
              </button>
            </div>

            <div className="p-6 overflow-y-auto space-y-4 text-xs">
              <div className="grid grid-cols-2 gap-3 p-3.5 rounded-xl bg-slate-950 border border-slate-800">
                <div>
                  <span className="text-slate-500">Autor:</span> <span className="text-white font-medium">{selectedChangeLog.userFullName}</span>
                </div>
                <div>
                  <span className="text-slate-500">Fecha:</span> <span className="text-white font-mono">{new Date(selectedChangeLog.createdAt).toLocaleString()}</span>
                </div>
                <div className="col-span-2 truncate">
                  <span className="text-slate-500">Tamper Hash SHA-256:</span> <span className="text-purple-400 font-mono text-[10px]">{selectedChangeLog.tamperHash}</span>
                </div>
              </div>

              {selectedChangeLog.diffJson && (
                <div>
                  <div className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] mb-1.5">
                    Diffing de Atributos Alterados:
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-blue-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap">
                    {selectedChangeLog.diffJson}
                  </pre>
                </div>
              )}

              {selectedChangeLog.stateAfterJson && (
                <div>
                  <div className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] mb-1.5">
                    Estado Posterior (Payload resultante):
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-slate-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap">
                    {selectedChangeLog.stateAfterJson}
                  </pre>
                </div>
              )}
            </div>

            <div className="p-4 border-t border-slate-800 bg-slate-950/60 flex justify-end">
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

      {/* Modal Detail for Request Logs */}
      {selectedRequest && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl max-h-[90vh] overflow-hidden flex flex-col shadow-2xl">
            <div className="p-5 border-b border-slate-800 flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <Globe className="w-5 h-5 text-blue-400" />
                <h3 className="text-base font-bold text-white">
                  Traza HTTP: {selectedRequest.httpMethod} {selectedRequest.requestPath} ({selectedRequest.statusCode})
                </h3>
              </div>
              <button
                onClick={() => setSelectedRequest(null)}
                className="text-slate-400 hover:text-white px-2 py-1 text-sm font-semibold"
              >
                ✕
              </button>
            </div>

            <div className="p-6 overflow-y-auto space-y-4 text-xs">
              <div>
                <div className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] mb-1.5">
                  Headers del Request:
                </div>
                <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-slate-400 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap">
                  {selectedRequest.requestHeadersJson || 'Sin headers capturados'}
                </pre>
              </div>

              {selectedRequest.requestBodyJson && (
                <div>
                  <div className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] mb-1.5">
                    Payload del Request (Sanitizado):
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-emerald-400 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap">
                    {selectedRequest.requestBodyJson}
                  </pre>
                </div>
              )}

              {selectedRequest.responseBodyJson && (
                <div>
                  <div className="font-semibold text-slate-300 uppercase tracking-wider text-[11px] mb-1.5">
                    Payload del Response:
                  </div>
                  <pre className="p-3.5 rounded-xl bg-slate-950 border border-slate-800 text-blue-300 font-mono text-[11px] overflow-x-auto whitespace-pre-wrap">
                    {selectedRequest.responseBodyJson}
                  </pre>
                </div>
              )}
            </div>

            <div className="p-4 border-t border-slate-800 bg-slate-950/60 flex justify-end">
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
