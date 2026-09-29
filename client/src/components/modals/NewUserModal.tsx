import React, { useState } from 'react';
import { useUrlParams } from '../../hooks/useUrlParams';
import { apiRequest } from '../../services/apiClient';
import { X, UserPlus, Shield, CheckCircle2, AlertCircle } from 'lucide-react';

interface Props {
  onUserCreated?: () => void;
}

export const NewUserModal: React.FC<Props> = ({ onUserCreated }) => {
  const { closeModal } = useUrlParams();
  const [email, setEmail] = useState('');
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [systemRole, setSystemRole] = useState<'user' | 'admin'>('user');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !password.trim() || !fullName.trim()) {
      setError('Por favor completa todos los campos obligatorios.');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      const res = await apiRequest('/auth/register', {
        method: 'POST',
        body: JSON.stringify({
          email: email.trim(),
          fullName: fullName.trim(),
          password,
          systemRole,
        }),
      });

      if (res.success) {
        setSuccess(true);
        onUserCreated?.();
        setTimeout(() => closeModal(), 1500);
      } else {
        setError(res.message || 'Error al registrar el usuario.');
      }
    } catch (err: any) {
      setError(err?.message || 'Error de comunicación.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-in fade-in duration-150">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl flex flex-col relative overflow-hidden">
        {/* Header */}
        <div className="p-6 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-purple-500/10 border border-purple-500/20 flex items-center justify-center text-purple-400">
              <UserPlus className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-semibold text-white">Registrar Nuevo Usuario</h3>
              <p className="text-xs text-slate-400">
                Acción restringida exclusivamente para administradores
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

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {error && (
            <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-xs flex items-center gap-2">
              <AlertCircle className="w-4 h-4 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {success && (
            <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 text-xs flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 shrink-0" />
              <span>¡Usuario creado correctamente! Cerrando...</span>
            </div>
          )}

          <div>
            <label className="block text-xs font-medium text-slate-300 mb-1.5">Nombre Completo</label>
            <input
              type="text"
              required
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder="Ej. Juan Pérez"
              className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-800 text-white text-xs focus:outline-none focus:border-purple-500 transition"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-300 mb-1.5">Correo Electrónico</label>
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="usuario@offline.local"
              className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-800 text-white text-xs focus:outline-none focus:border-purple-500 transition"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-300 mb-1.5">Contraseña Inicial</label>
            <input
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-800 text-white text-xs focus:outline-none focus:border-purple-500 transition"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-300 mb-1.5">Rol de Sistema</label>
            <div className="grid grid-cols-2 gap-2.5">
              <button
                type="button"
                onClick={() => setSystemRole('user')}
                className={`p-2.5 rounded-xl border text-xs font-medium flex items-center justify-center gap-2 transition ${
                  systemRole === 'user'
                    ? 'bg-blue-600/10 border-blue-500 text-white'
                    : 'bg-slate-950 border-slate-800 text-slate-400 hover:border-slate-700'
                }`}
              >
                <span>Usuario Estándar</span>
              </button>

              <button
                type="button"
                onClick={() => setSystemRole('admin')}
                className={`p-2.5 rounded-xl border text-xs font-medium flex items-center justify-center gap-2 transition ${
                  systemRole === 'admin'
                    ? 'bg-purple-600/10 border-purple-500 text-purple-300'
                    : 'bg-slate-950 border-slate-800 text-slate-400 hover:border-slate-700'
                }`}
              >
                <Shield className="w-3.5 h-3.5" />
                <span>Administrador</span>
              </button>
            </div>
            <p className="text-[11px] text-slate-500 mt-1">
              {systemRole === 'admin' 
                ? 'Tendrá acceso al changelog, auditoría HTTP y alta de nuevos usuarios.' 
                : 'Acceso restringido a sus espacios de trabajo asignados.'}
            </p>
          </div>

          <div className="pt-2 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => closeModal()}
              className="px-4 py-2 rounded-xl text-xs font-medium text-slate-400 hover:text-white hover:bg-slate-800 transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading || success}
              className="px-4 py-2 rounded-xl bg-purple-600 hover:bg-purple-500 text-white text-xs font-semibold shadow-lg shadow-purple-600/20 disabled:opacity-50 transition"
            >
              {loading ? 'Creando...' : 'Crear Usuario'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
