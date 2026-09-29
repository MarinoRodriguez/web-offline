# Offline-First Task Management System (Web-Offline)

Sistema empresarial de gestión de tareas y espacios de trabajo diseñado bajo la filosofía **Offline-First / Local-First**. La aplicación permite trabajar sin interrupciones incluso sin conexión a internet o con el servidor backend apagado, persistiendo los datos localmente en una base de datos **SQLite Wasm** sobre el **Origin Private File System (OPFS)** del navegador y sincronizándose bidireccionalmente mediante el patrón **Transactional Outbox**.

---

## 🌟 Características Principales

- **Arquitectura Local-First Real**: Las lecturas y escrituras se ejecutan inmediatamente sobre una base de datos SQLite embebida en el navegador. La aplicación nunca se bloquea por latencia de red.
- **Persistencia Avanzada OPFS (Multi-Tier)**:
  - **Tier 1 (Principal)**: `OpfsSAHPoolDb` (Storage Access Handle Pool de SQLite Oficial), ofreciendo rendimiento nativo en disco sin bloqueos.
  - **Tier 2 (Fallback)**: `OpfsDb` tradicional en OPFS.
  - **Tier 3 (Memoria)**: En navegadores sin soporte OPFS, degradación segura a memoria.
- **Sincronización Bidireccional por Lotes (Push/Pull Batch Sync)**:
  - **Patrón Outbox**: Todas las operaciones locales (INSERT, UPDATE, DELETE) se registran en una cola de mutaciones con marca de tiempo y versión.
  - **Concurrencia Optimista (OCC)**: Detección y resolución de conflictos mediante versionado numérico incremental. En caso de conflicto (HTTP 409), el backend reenvía la versión más reciente para auto-reconciliación.
  - **Tombstones**: Propagación segura de eliminaciones entre clientes desconectados.
  - **Protección de Integridad**: Desactivación temporal de claves foráneas y sanitización de huérfanos durante la ingestión de lotes para evitar bloqueos por dependencias circulares o datos heredados.
- **Instalabilidad PWA Completa (Progressive Web App)**:
  - Service Worker con precaching de assets, scripts y el binario `sqlite3.wasm` (hasta 25 MB).
  - Íconos adaptativos (192x192, 512x512, maskable, apple-touch-icon, favicon).
  - Botón nativo "Instalar App" en la interfaz y detección de modo standalone.
- **Gestión Avanzada de Tareas y Espacios**:
  - Múltiples Workspaces con control de miembros y roles (Admin, Editor, Viewer).
  - Listas Kanban personalizables con colores y ordenamiento posicional.
  - Columna unificada **"Tareas sin lista"** para tareas rápidas o importadas sin clasificar.
  - Subtareas con jerarquía en árbol, cálculo de porcentaje de progreso y bloqueo de completado si hay subtareas pendientes.
- **Importador CSV Inteligente**:
  - Detección automática de delimitadores (coma o punto y coma).
  - Mapeo automático de columnas y enrutamiento a "Tareas sin lista" cuando no se especifica lista.
- **Auditoría y Trazabilidad Empresarial**:
  - **Changelog**: Registro de auditoría entidad por entidad con comparación visual de diferencias (Diffing JSON / tabla comparativa).
  - **Logs de Peticiones HTTP**: Registro completo de cada petición recibida por la API (Correlation ID, IP, User Agent, Query String, Headers de petición y respuesta, duración en milisegundos y código de estado).
  - Modal detallado con copia rápida al portapapeles y formateo JSON.

---

## 🏗️ Estructura del Repositorio

```
web-offline/
├── client/                     # Frontend SPA en React 19 + TypeScript + Vite + Tailwind CSS
│   ├── public/                 # Binarios WebAssembly SQLite, proxies OPFS e íconos PWA
│   ├── src/
│   │   ├── components/         # Modales, vistas de tablero Kanban y layout principal
│   │   ├── context/            # Proveedores de estado global (AuthContext, SyncContext)
│   │   ├── db/                 # Repositorios locales SQLite, Worker WebAssembly y cliente
│   │   ├── hooks/              # Hooks personalizados (URL params, estado)
│   │   ├── pages/              # Páginas de Workspaces, Tableros, Auditoría y Login
│   │   └── services/           # Motor de sincronización, detección de conectividad y API
│   ├── vite.config.ts          # Configuración de Vite, VitePWA y Workbox
│   └── README.md               # Documentación detallada del Frontend
│
├── server/                     # Backend API en .NET 9 (C#)
│   ├── WebOffline.Api/         # Controladores REST, Middleware de Auditoría y configuración
│   ├── WebOffline.Core/        # Entidades de Dominio, DTOs de Sincronización e Interfaces
│   ├── WebOffline.Infrastructure/ # Acceso a datos SQLite (Dapper), Repositorios, Servicios y Seeds
│   ├── WebOffline.sln          # Solución .NET
│   └── README.md               # Documentación detallada del Backend
│
└── README.md                   # Esta guía general del proyecto
```

---

## 🚀 Requisitos Previos

1. **Node.js**: Versión 18 o superior (se recomienda 20 LTS o 22).
2. **.NET SDK**: Versión 9.0 o superior.
3. **Navegador Moderno**: Google Chrome, Microsoft Edge, Brave u Opera para soporte completo de OPFS SAHPool y PWA.

---

## 🛠️ Puesta en Marcha

### 1. Iniciar el Backend (.NET 9)

```bash
cd server
dotnet restore
dotnet run --project WebOffline.Api
```

- La API iniciará en `http://localhost:5287` (HTTP) y `https://localhost:7142` (HTTPS).
- La base de datos SQLite del servidor se inicializará y sembrará automáticamente en `server/WebOffline.Api/weboffline.db`.

### 2. Iniciar el Frontend (React + Vite)

En otra terminal:

```bash
cd client
npm install
npm run dev
```

- El frontend estará disponible en `http://localhost:5173`.
- Las cabeceras requeridas para WebAssembly con subprocesos y OPFS (`Cross-Origin-Opener-Policy: same-origin` y `Cross-Origin-Embedder-Policy: require-corp`) están preconfiguradas en el servidor de desarrollo de Vite.

---

## 👥 Credenciales de Acceso por Defecto

El inicializador de base de datos (`DbInitializer.cs`) crea los siguientes usuarios de demostración:

| Email | Contraseña | Rol | Descripción |
| :--- | :--- | :--- | :--- |
| `admin@offline.local` | `Admin123!` | Administrador | Acceso completo a auditoría, changelog y trazabilidad HTTP |
| `mrodriguez@mail.com` | `Admin123!` | Editor | Usuario estándar de pruebas en workspaces |

---

## 🔄 Protocolo de Sincronización (Sync Protocol)

El motor sincroniza a través de un único endpoint batch atómico: `POST /api/sync`.

### Flujo de Sincronización:
1. **Push**: El cliente recopila todas las mutaciones locales pendientes registradas en la tabla `sync_outbox`.
2. **OCC Check**: El servidor evalúa secuencialmente cada mutación validando la versión (`Version`). Si la versión coincide, se aplica la mutación, se incrementa la versión y se registra en el log de auditoría. Si el servidor tiene una versión más nueva, rechaza con conflicto `409` e incluye el estado actual.
3. **Pull**: El servidor recopila todas las entidades modificadas (`workspaces`, `lists`, `tasks`) y `tombstones` cuya fecha de actualización sea posterior a `lastSyncedAt`.
4. **Local Apply**: El cliente desactiva claves foráneas (`PRAGMA foreign_keys = OFF`), aplica en SQLite los registros recibidos mediante `INSERT ... ON CONFLICT(id) DO UPDATE` y reactiva las claves foráneas en bloque `finally`.
5. **Clean**: Las mutaciones aplicadas exitosamente se purgan de `sync_outbox` y se actualiza `last_synced_at`.

---

## 📱 Instalación PWA

Para instalar la aplicación como software de escritorio o móvil independiente:
1. Abre la aplicación en Google Chrome o Microsoft Edge en `http://localhost:5173`.
2. Haz clic en el botón azul **"Instalar App"** situado en la barra de navegación superior (o en el ícono de instalación de la barra de direcciones del navegador).
3. La aplicación se abrirá en una ventana nativa independiente y continuará funcionando al 100% incluso desconectando la red o apagando el servidor backend.
