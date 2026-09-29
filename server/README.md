# Web-Offline Server (.NET 9 Web API)

Backend de sincronización y auditoría construido con **ASP.NET Core 9.0 (C#)** y **SQLite** mediante acceso de alto rendimiento con **Dapper**. Proporciona el contrato unificado de sincronización por lotes (`/api/sync`), control de concurrencia optimista, rotación de tokens de refresco y una plataforma integral de auditoría y trazabilidad HTTP.

---

## 🏛️ Arquitectura de la Solución

El backend sigue los principios de **Clean Architecture / Onion Architecture**, separando estrictamente responsabilidades en tres capas:

```
server/
├── WebOffline.Api/                  # Capa de Presentación / Entrada HTTP
│   ├── Controllers/
│   │   ├── AuditController.cs       # Endpoints de consulta de auditoría y logs HTTP
│   │   ├── AuthController.cs        # Registro, Login, Refresco y Logout
│   │   ├── BaseApiController.cs     # Controlador base con utilidades de usuario y claims
│   │   ├── ListsController.cs       # Operaciones REST directas sobre listas
│   │   ├── SyncController.cs        # Endpoint principal de sincronización por lotes (/api/sync)
│   │   ├── TasksController.cs       # Operaciones REST directas sobre tareas
│   │   └── WorkspacesController.cs  # Operaciones REST directas sobre espacios de trabajo
│   ├── Middleware/
│   │   └── AuditLoggingMiddleware.cs # Interceptor global de auditoría y trazabilidad HTTP
│   ├── Program.cs                   # Configuración de servicios, DI, CORS, JWT y pipeline
│   └── appsettings.json             # Cadenas de conexión y configuración de JWT
│
├── WebOffline.Core/                 # Capa de Dominio y Lógica de Negocio
│   ├── Common/
│   │   └── Result.cs                # Monad Result para manejo funcional de éxitos y errores
│   ├── DTOs/                        # Modelos de transferencia de datos
│   │   ├── AuthDtos.cs              # DTOs de login, registro y tokens
│   │   └── SyncDtos.cs              # DTOs de mutaciones, lotes y respuestas de sincronización
│   ├── Entities/                    # Entidades del dominio
│   │   ├── AuditLog.cs              # Registro de cambios por entidad (Changelog / Diffing)
│   │   ├── HttpRequestLog.cs        # Registro de peticiones HTTP (Trazabilidad completa)
│   │   ├── RefreshToken.cs          # Token de refresco con rotación y revocación
│   │   ├── TaskItem.cs              # Tareas, estados, prioridades y subtareas
│   │   ├── TaskList.cs              # Listas de tareas en workspaces
│   │   ├── User.cs                  # Cuentas de usuario y roles
│   │   ├── Workspace.cs             # Espacios de trabajo
│   │   └── WorkspaceMember.cs       # Miembros de espacios y permisos
│   └── Interfaces/                  # Contratos de repositorios y servicios
│       ├── IAuditRepository.cs
│       ├── IAuthService.cs
│       ├── ISyncService.cs
│       ├── ITaskRepository.cs
│       └── IWorkspaceRepository.cs
│
└── WebOffline.Infrastructure/       # Capa de Acceso a Datos e Infraestructura Externa
    ├── Auth/
    │   └── AuthService.cs           # Emisión de JWT, hash BCrypt y rotación de tokens
    ├── Data/
    │   ├── DbConnectionFactory.cs   # Factoría de conexiones SQLite (WAL mode, foreign keys)
    │   └── DbInitializer.cs         # Creación de tablas SQLite y semillas de datos
    ├── Repositories/
    │   ├── AuditRepository.cs       # Persistencia y consultas de changelog y peticiones HTTP
    │   ├── TaskRepository.cs        # Repositorio Dapper para tareas y subtareas
    │   └── WorkspaceRepository.cs   # Repositorio Dapper para espacios y listas
    └── Services/
        └── SyncService.cs           # Motor de procesamiento de mutaciones y resolución de conflictos
```

---

## 🔒 Autenticación y Seguridad

- **JSON Web Tokens (JWT)**:
  - Tokens de acceso firmados con HMAC-SHA256 (`Bearer`).
  - Expiración corta (configurable, típicamente 15 a 60 minutos).
- **Refresh Token Rotation (RTR)**:
  - Los tokens de refresco se almacenan en la tabla `refresh_tokens` de SQLite.
  - Al solicitar renovación vía `POST /api/auth/refresh`, el token anterior se revoca inmediatamente y se emite un nuevo par de Access Token + Refresh Token, impidiendo ataques de repetición.
- **Hash de Contraseñas**:
  - Implementado mediante `BCrypt.Net-Next` con factor de coste de trabajo seguro.

---

## 🔄 Motor de Sincronización del Servidor (`SyncService.cs`)

El endpoint central de sincronización es `POST /api/sync`. Procesa lotes completos en una sola transacción atómica por lote:

### Contrato de Solicitud (`SyncBatchRequest`):
```json
{
  "clientTimestamp": "2026-09-29T10:00:00Z",
  "lastSyncedAt": "2026-09-29T09:30:00Z",
  "mutations": [
    {
      "mutationId": "f7a301bb-59f2-45e3-8f0a-3a2e1d0f5e4c",
      "entity": "task",
      "operation": "UPDATE",
      "entityId": "e2d3c4b5-a6f7-4819-b0c1-d2e3f4a5b6c7",
      "workspaceId": "c447a972-81ed-4e29-946c-cdc28e12698a",
      "clientTimestamp": "2026-09-29T09:45:00Z",
      "version": 3,
      "payloadJson": "{\"title\":\"Nueva versión de la tarea\",\"status\":\"IN_PROGRESS\"}"
    }
  ]
}
```

### Contrato de Respuesta (`SyncBatchResponse`):
```json
{
  "syncedAt": "2026-09-29T10:00:02.123Z",
  "appliedCount": 1,
  "rejections": [],
  "changes": {
    "workspaces": [],
    "lists": [],
    "tasks": [ ... ],
    "tombstones": [
      {
        "entityType": "task",
        "entityId": "a1b2c3d4-...",
        "deletedAt": "2026-09-29T09:50:00Z"
      }
    ]
  }
}
```

### Control de Concurrencia Optimista (OCC) y Resolución de Conflictos (HTTP 409):
1. **Verificación de Versión**: Al aplicar una mutación `UPDATE` o `DELETE`, el servidor compara la versión enviada por el cliente con la versión persistida en la base de datos central.
2. **Conflicto Detectado**: Si la versión del servidor es mayor que la del cliente, la mutación se rechaza con código `409 Conflict`.
3. **Auto-Reconciliación sin Limbo**: En lugar de dejar la entidad en estado de error indeterminado, el servidor **incluye automáticamente la versión más reciente de la entidad en el array de `changes`**. De este modo, el cliente actualiza su base de datos local con la verdad del servidor de forma limpia y transparente.

---

## 📊 Sistema de Auditoría y Observabilidad

El backend cuenta con dos niveles de observabilidad exhaustiva:

### 1. Changelog a Nivel de Entidad (`AuditLog`)
- Registra cada acción (`CREATE`, `UPDATE`, `DELETE`, `SYNC_CREATE`, etc.).
- Almacena el estado anterior (`old_values_json`) y el nuevo estado (`new_values_json`).
- Permite al panel de administración visualizar la fecha/hora exacta con zona horaria, descripción humana y la comparación lado a lado (Diffing) de los cambios efectuados.

### 2. Trazabilidad de Peticiones HTTP (`AuditLoggingMiddleware.cs` & `HttpRequestLog`)
Un middleware global intercepta cada solicitud entrante antes y después de su ejecución:
- **Identificador de Correlación (`correlation_id`)**: Permite trazar la petición de extremo a extremo.
- **Identidad del Usuario**: Vincula el `user_id` y `user_email` extraídos del token JWT (o marca `ANONYMOUS` si no está autenticado).
- **Detalle de Red**: IP del cliente (`client_ip`) y User-Agent completo.
- **Ruta y Consulta**: Método HTTP, URL, Query String.
- **Headers Completos**: Cabeceras de la petición (`request_headers`) y cabeceras de la respuesta (`response_headers`) serializadas en formato JSON estructurado.
- **Rendimiento**: Medición precisa de la duración en milisegundos (`duration_ms`) mediante `Stopwatch`.
- **Resultado**: Código de estado HTTP final (`status_code`).

Los administradores pueden consultar estos registros con paginación en `/api/audit/requests` y visualizarlos en el panel de auditoría del cliente.

---

## 🗃️ Base de Datos SQLite y Configuración

La conexión a la base de datos se gestiona a través de `DbConnectionFactory.cs`:
- **Modo WAL (Write-Ahead Logging)**: Habilitado para permitir múltiples lectores concurrentes y un escritor sin bloqueos (`PRAGMA journal_mode = WAL;`).
- **Claves Foráneas Activas**: Se asegura la integridad referencial en el motor central (`PRAGMA foreign_keys = ON;`).
- **Archivo de Datos**: Por defecto se almacena en `server/WebOffline.Api/weboffline.db`.

---

## 💻 Comandos de Ejecución

```bash
# Restaurar dependencias de NuGet
dotnet restore

# Compilar la solución en modo Debug
dotnet build

# Ejecutar la API
dotnet run --project WebOffline.Api

# Ejecutar pruebas unitarias / integración (si están configuradas)
dotnet test
```
