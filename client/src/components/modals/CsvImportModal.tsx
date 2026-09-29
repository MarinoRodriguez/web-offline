import React, { useState } from 'react';
import { useUrlParams } from '../../hooks/useUrlParams';
import { localListRepository, localTaskRepository } from '../../db';
import Papa from 'papaparse';
import { X, UploadCloud, FileSpreadsheet, Download, CheckCircle2, AlertTriangle, ArrowRight } from 'lucide-react';

type EntityType = 'tasks' | 'lists';

interface Props {
  onImportCompleted?: () => void;
}

export const CsvImportModal: React.FC<Props> = ({ onImportCompleted }) => {
  const { closeModal, getParam } = useUrlParams();
  const workspaceId = getParam('workspaceId') || '';

  const [selectedEntity, setSelectedEntity] = useState<EntityType>('tasks');
  const [parsedData, setParsedData] = useState<any[]>([]);
  const [columns, setColumns] = useState<string[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  const [importing, setImporting] = useState(false);
  const [successCount, setSuccessCount] = useState<number | null>(null);

  const sampleCsvs: Record<EntityType, string> = {
    lists: `name,color,position\n"Sprint Backlog","#3B82F6",1\n"En Curso","#F59E0B",2\n"Terminado","#10B981",3`,
    tasks: `title,description,status,priority,due_date\n"Diseñar Wireframes","Bocetos de interfaz","TODO","HIGH","2026-10-15"\n"Revisar accesibilidad","Contraste y navegación","IN_PROGRESS","MEDIUM","2026-10-20"`
  };

  const handleDownloadTemplate = () => {
    const csvContent = sampleCsvs[selectedEntity];
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', `plantilla_${selectedEntity}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setErrors([]);
    setSuccessCount(null);

    Papa.parse(file, {
      header: true,
      skipEmptyLines: true,
      complete: (results) => {
        if (results.errors && results.errors.length > 0) {
          setErrors(results.errors.map(err => `Fila ${err.row}: ${err.message}`));
        }

        if (results.meta.fields) {
          setColumns(results.meta.fields);
        }

        setParsedData(results.data);
      },
      error: (err) => {
        setErrors([`Error al procesar CSV: ${err.message}`]);
      }
    });
  };

  const handleCommitImport = async () => {
    if (!parsedData.length || !workspaceId) return;

    try {
      setImporting(true);
      setErrors([]);
      let imported = 0;

      if (selectedEntity === 'lists') {
        for (const row of parsedData) {
          if (!row.name?.trim()) continue;
          await localListRepository.save({
            workspace_id: workspaceId,
            name: row.name.trim(),
            color: row.color?.trim() || '#3B82F6',
            position: row.position ? parseInt(row.position, 10) : 0
          });
          imported++;
        }
      } else if (selectedEntity === 'tasks') {
        const lists = await localListRepository.getByWorkspace(workspaceId);
        const defaultListId = lists[0]?.id || null;

        for (const row of parsedData) {
          if (!row.title?.trim()) continue;
          await localTaskRepository.save({
            list_id: defaultListId,
            workspace_id: workspaceId,
            title: row.title.trim(),
            description: row.description?.trim() || null,
            status: (['TODO', 'IN_PROGRESS', 'DONE', 'CANCELLED'].includes(row.status?.toUpperCase()) ? row.status.toUpperCase() : 'TODO') as any,
            priority: (['LOW', 'MEDIUM', 'HIGH', 'URGENT'].includes(row.priority?.toUpperCase()) ? row.priority.toUpperCase() : 'MEDIUM') as any,
            due_date: row.due_date ? new Date(row.due_date).toISOString() : null
          });
          imported++;
        }
      }

      setSuccessCount(imported);
      onImportCompleted?.();
    } catch (err: any) {
      setErrors([err?.message || 'Error importando registros a la base de datos local.']);
    } finally {
      setImporting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-in fade-in duration-150">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-3xl max-h-[90vh] shadow-2xl flex flex-col relative overflow-hidden">
        {/* Header */}
        <div className="p-6 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
              <FileSpreadsheet className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-lg font-semibold text-white">Importación Masiva de Datos CSV</h3>
              <p className="text-xs text-slate-400">
                Importa registros directamente al almacenamiento SQLite local (con cola outbox)
              </p>
            </div>
          </div>

          <button
            onClick={() => closeModal(['workspaceId'])}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-6 flex-1">
          {/* Step 1: Select Entity Type (Explicit, No Inferring) */}
          <div>
            <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
              1. Selecciona el Tipo de Entidad a Importar
            </label>
            <div className="grid grid-cols-2 gap-3">
              {(['tasks', 'lists'] as const).map((ent) => (
                <button
                  key={ent}
                  type="button"
                  onClick={() => {
                    setSelectedEntity(ent);
                    setParsedData([]);
                    setColumns([]);
                    setSuccessCount(null);
                  }}
                  className={`p-3 rounded-xl border text-left flex items-center justify-between transition ${
                    selectedEntity === ent
                      ? 'bg-blue-600/10 border-blue-500 text-white ring-1 ring-blue-500'
                      : 'bg-slate-950 border-slate-800 text-slate-400 hover:border-slate-700'
                  }`}
                >
                  <div>
                    <div className="text-sm font-semibold capitalize">
                      {ent === 'tasks' ? 'Tareas' : 'Listas de Proyecto'}
                    </div>
                    <div className="text-xs text-slate-400">
                      {ent === 'tasks' ? 'Importar tareas con prioridad y estado' : 'Importar columnas y listas'}
                    </div>
                  </div>
                  {selectedEntity === ent && <CheckCircle2 className="w-4 h-4 text-blue-400" />}
                </button>
              ))}
            </div>
          </div>

          {/* Step 2: Download Template & Upload */}
          <div className="flex flex-col sm:flex-row gap-3 items-stretch sm:items-center justify-between p-4 rounded-xl bg-slate-950 border border-slate-800">
            <div>
              <div className="text-xs font-semibold text-white">Plantilla oficial CSV</div>
              <div className="text-xs text-slate-400">Descarga la estructura recomendada para {selectedEntity}</div>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={handleDownloadTemplate}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium transition"
              >
                <Download className="w-3.5 h-3.5" />
                Descargar Plantilla
              </button>

              <label className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold cursor-pointer transition shadow-md shadow-blue-600/20">
                <UploadCloud className="w-3.5 h-3.5" />
                Subir Archivo CSV
                <input
                  type="file"
                  accept=".csv,text/csv"
                  onChange={handleFileUpload}
                  className="hidden"
                />
              </label>
            </div>
          </div>

          {/* Feedback messages */}
          {errors.length > 0 && (
            <div className="p-3.5 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-xs space-y-1">
              <div className="font-semibold flex items-center gap-1.5">
                <AlertTriangle className="w-4 h-4" />
                Errores detectados en el archivo:
              </div>
              {errors.map((e, idx) => (
                <div key={idx} className="pl-5">{e}</div>
              ))}
            </div>
          )}

          {successCount !== null && (
            <div className="p-3.5 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 text-xs flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 shrink-0" />
              <span>¡Se importaron con éxito <strong>{successCount}</strong> registros al almacenamiento local SQLite y se encolaron en el outbox!</span>
            </div>
          )}

          {/* Step 3: Interactive Data Preview */}
          {parsedData.length > 0 && (
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs font-semibold text-slate-300 uppercase tracking-wider">
                  Vista Previa ({parsedData.length} filas leídas)
                </span>
                <span className="text-xs text-slate-500">Valida los datos antes de confirmar</span>
              </div>

              <div className="border border-slate-800 rounded-xl overflow-x-auto max-h-52 bg-slate-950">
                <table className="w-full text-xs text-left">
                  <thead className="bg-slate-900 border-b border-slate-800 text-slate-400 sticky top-0">
                    <tr>
                      <th className="p-2.5 w-10">#</th>
                      {columns.map((c) => (
                        <th key={c} className="p-2.5 font-semibold text-slate-300">{c}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 text-slate-300">
                    {parsedData.slice(0, 10).map((row, idx) => (
                      <tr key={idx} className="hover:bg-slate-900/50">
                        <td className="p-2.5 text-slate-500 font-mono">{idx + 1}</td>
                        {columns.map((c) => (
                          <td key={c} className="p-2.5 truncate max-w-xs">{row[c] || '—'}</td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {parsedData.length > 10 && (
                <div className="text-[11px] text-slate-500 mt-1.5 text-right">
                  Mostrando las primeras 10 filas de {parsedData.length} en total.
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-slate-800 bg-slate-950/60 flex items-center justify-between">
          <button
            type="button"
            onClick={() => closeModal(['workspaceId'])}
            className="px-4 py-2 rounded-xl text-xs font-medium text-slate-400 hover:text-white hover:bg-slate-800 transition"
          >
            Cerrar
          </button>

          {parsedData.length > 0 && successCount === null && (
            <button
              type="button"
              disabled={importing}
              onClick={handleCommitImport}
              className="flex items-center gap-2 px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow-lg shadow-emerald-600/20 disabled:opacity-50 transition"
            >
              <span>{importing ? 'Importando a SQLite local...' : `Confirmar Importación (${parsedData.length} registros)`}</span>
              <ArrowRight className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
