export type ConnectionStatus = 'online' | 'server_unreachable' | 'offline';

export interface ConnectivityState {
  isNetworkConnected: boolean;
  isServerReachable: boolean;
  status: ConnectionStatus;
  statusText: string;
}

type ConnectivityListener = (state: ConnectivityState) => void;

class ConnectivityService {
  private isNetworkConnected: boolean = typeof navigator !== 'undefined' ? navigator.onLine : true;
  private isServerReachable: boolean = true;
  private listeners: Set<ConnectivityListener> = new Set();
  private healthCheckInterval: any = null;
  private isCheckingHealth: boolean = false;

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('online', this.handleNetworkOnline);
      window.addEventListener('offline', this.handleNetworkOffline);
      // Run initial background health check
      this.checkHealth();
    }
  }

  private handleNetworkOnline = () => {
    this.isNetworkConnected = true;
    this.notify();
    // Verify if server is also reachable
    this.checkHealth();
  };

  private handleNetworkOffline = () => {
    this.isNetworkConnected = false;
    this.stopHealthCheckPolling();
    this.notify();
  };

  public getState(): ConnectivityState {
    let status: ConnectionStatus = 'online';
    let statusText = 'Online';

    if (!this.isNetworkConnected) {
      status = 'offline';
      statusText = 'Sin red local/internet';
    } else if (!this.isServerReachable) {
      status = 'server_unreachable';
      statusText = 'Servidor Offline (502 / Caído)';
    }

    return {
      isNetworkConnected: this.isNetworkConnected,
      isServerReachable: this.isServerReachable,
      status,
      statusText,
    };
  }

  public isOnline(): boolean {
    return this.isNetworkConnected && this.isServerReachable;
  }

  public subscribe(listener: ConnectivityListener): () => void {
    this.listeners.add(listener);
    listener(this.getState());
    return () => {
      this.listeners.delete(listener);
    };
  }

  private notify() {
    const state = this.getState();
    for (const listener of this.listeners) {
      try {
        listener(state);
      } catch (err) {
        console.error('[ConnectivityService] Listener error:', err);
      }
    }
  }

  public reportApiSuccess(): void {
    if (!this.isServerReachable) {
      this.isServerReachable = true;
      this.stopHealthCheckPolling();
      this.notify();
      window.dispatchEvent(new CustomEvent('app:server-restored'));
    }
  }

  public reportApiFailure(statusCode?: number, isNetworkError: boolean = false): void {
    const isServerDown = isNetworkError || statusCode === 502 || statusCode === 503 || statusCode === 504 || statusCode === 0;
    if (isServerDown && this.isServerReachable) {
      this.isServerReachable = false;
      this.notify();
      this.startHealthCheckPolling();
      window.dispatchEvent(new CustomEvent('app:server-unreachable', { detail: { statusCode } }));
    }
  }

  public async checkHealth(): Promise<boolean> {
    if (!this.isNetworkConnected || this.isCheckingHealth) {
      return this.isOnline();
    }

    this.isCheckingHealth = true;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 3500);

    try {
      const res = await fetch('/api/health', {
        method: 'GET',
        cache: 'no-store',
        signal: controller.signal,
      });

      clearTimeout(timeoutId);

      // Status 200 or any non-502/503/504 response means the server is actively responding
      const reachable = res.ok || (res.status >= 200 && res.status < 500);
      if (reachable) {
        this.reportApiSuccess();
        return true;
      } else {
        this.reportApiFailure(res.status, false);
        return false;
      }
    } catch {
      clearTimeout(timeoutId);
      this.reportApiFailure(502, true);
      return false;
    } finally {
      this.isCheckingHealth = false;
    }
  }

  private startHealthCheckPolling(): void {
    if (this.healthCheckInterval) return;
    // Ping every 8 seconds when server is down to detect recovery
    this.healthCheckInterval = setInterval(async () => {
      if (this.isNetworkConnected) {
        const ok = await this.checkHealth();
        if (ok) {
          this.stopHealthCheckPolling();
        }
      }
    }, 8000);
  }

  private stopHealthCheckPolling(): void {
    if (this.healthCheckInterval) {
      clearInterval(this.healthCheckInterval);
      this.healthCheckInterval = null;
    }
  }
}

export const connectivity = new ConnectivityService();
