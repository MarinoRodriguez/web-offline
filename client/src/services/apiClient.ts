import { authStorage } from './authStorage';
import { connectivity } from './connectivity';

export interface ApiResponse<T = any> {
  success: boolean;
  data?: T;
  message?: string;
  errors?: string[];
  statusCode: number;
  timestamp: string;
}

let refreshPromise: Promise<string | null> | null = null;

async function attemptTokenRefresh(): Promise<string | null> {
  if (refreshPromise) {
    return refreshPromise;
  }

  const refreshToken = authStorage.getRefreshToken();
  if (!refreshToken || refreshToken === 'offline-local-token') {
    return null;
  }

  refreshPromise = (async () => {
    try {
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });

      if (!res.ok) {
        console.warn(`[apiClient] Token refresh failed with status ${res.status}. Session invalidated.`);
        authStorage.clearAuth();
        window.dispatchEvent(new CustomEvent('app:session-expired'));
        return null;
      }

      const json = await res.json();
      if (!json.success || !json.data?.accessToken) {
        authStorage.clearAuth();
        window.dispatchEvent(new CustomEvent('app:session-expired'));
        return null;
      }

      const { accessToken, refreshToken: newRefreshToken, user } = json.data;
      const currentUser = user || authStorage.getUser();
      authStorage.setAuth(
        { accessToken, refreshToken: newRefreshToken || refreshToken },
        currentUser
      );
      console.info('[apiClient] Access token successfully refreshed in background.');
      return accessToken as string;
    } catch (err) {
      console.warn('[apiClient] Network error during token refresh:', err);
      return null;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

export async function apiRequest<T = any>(
  endpoint: string,
  options: RequestInit = {}
): Promise<ApiResponse<T>> {
  let token = authStorage.getAccessToken();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> || {}),
  };

  if (token && token !== 'offline-local-token') {
    headers['Authorization'] = `Bearer ${token}`;
  }

  const url = endpoint.startsWith('http') ? endpoint : `/api${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;

  let response: Response;
  try {
    response = await fetch(url, {
      ...options,
      headers,
    });
  } catch (netErr: any) {
    connectivity.reportApiFailure(0, true);
    throw new Error('Sin conexión con el servidor backend (Red no disponible / Conexión rechazada).');
  }

  // Handle gateway/proxy errors (e.g. Vite proxy returning 502 Bad Gateway when backend is down)
  if (response.status === 502 || response.status === 503 || response.status === 504) {
    connectivity.reportApiFailure(response.status, false);
    throw new Error(`El servidor backend no está disponible (${response.status} Bad Gateway / Fuera de línea).`);
  }

  // Handle 401 Unauthorized: automatically attempt token refresh and retry
  const isAuthEndpoint = url.includes('/auth/login') || url.includes('/auth/refresh');
  if (response.status === 401 && !isAuthEndpoint) {
    const newToken = await attemptTokenRefresh();
    if (newToken) {
      // Retry request once with the refreshed access token
      headers['Authorization'] = `Bearer ${newToken}`;
      try {
        response = await fetch(url, {
          ...options,
          headers,
        });
      } catch (retryErr: any) {
        connectivity.reportApiFailure(0, true);
        throw new Error('Sin conexión con el servidor backend tras renovar sesión.');
      }
    } else {
      throw new Error('Sesión expirada o no autorizada (401). Inicia sesión nuevamente.');
    }
  }

  const data = await response.json().catch(() => ({
    success: false,
    message: response.ok 
      ? 'Respuesta vacía del servidor.' 
      : `Error HTTP ${response.status} del servidor.`,
    statusCode: response.status,
  }));

  if (!response.ok) {
    throw new Error(data.message || data.errors?.[0] || `Error HTTP ${response.status}`);
  }

  connectivity.reportApiSuccess();
  return data;
}
