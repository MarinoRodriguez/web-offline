import React, { createContext, useContext, useState, useEffect } from 'react';
import { authStorage, AuthUser } from '../services/authStorage';

interface AuthContextType {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isAdmin: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  loading: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<AuthUser | null>(authStorage.getUser());
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setUser(authStorage.getUser());
    setLoading(false);

    const handleSessionExpired = () => {
      setUser(null);
    };

    window.addEventListener('app:session-expired', handleSessionExpired);
    return () => {
      window.removeEventListener('app:session-expired', handleSessionExpired);
    };
  }, []);

  const login = async (email: string, password: string) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });

    const data = await res.json();
    if (!res.ok || !data.success) {
      throw new Error(data.message || data.errors?.[0] || 'Error al iniciar sesión');
    }

    const { user: loggedUser, accessToken, refreshToken } = data.data;
    authStorage.setAuth({ accessToken, refreshToken }, loggedUser);
    setUser(loggedUser);
  };

  const logout = async () => {
    const token = authStorage.getAccessToken();
    if (token) {
      try {
        await fetch('/api/auth/logout', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${token}`,
          },
        });
      } catch (err) {
        console.warn('Logout network error (proceeding locally):', err);
      }
    }
    authStorage.clearAuth();
    setUser(null);
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isAdmin: user?.systemRole?.toLowerCase() === 'admin',
        login,
        logout,
        loading,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
