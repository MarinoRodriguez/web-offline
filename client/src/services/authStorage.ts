export interface AuthUser {
  id: string;
  email: string;
  fullName: string;
  systemRole: string; // "user" | "admin"
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresInMinutes: number;
}

const TOKEN_KEY = 'wtm_access_token';
const REFRESH_KEY = 'wtm_refresh_token';
const USER_KEY = 'wtm_user';

export const authStorage = {
  getAccessToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  },

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_KEY);
  },

  getUser(): AuthUser | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  },

  setAuth(tokens: { accessToken: string; refreshToken?: string }, user: AuthUser): void {
    localStorage.setItem(TOKEN_KEY, tokens.accessToken);
    if (tokens.refreshToken) {
      localStorage.setItem(REFRESH_KEY, tokens.refreshToken);
    }
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  },

  clearAuth(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
  },

  isAuthenticated(): boolean {
    return !!this.getAccessToken();
  }
};
