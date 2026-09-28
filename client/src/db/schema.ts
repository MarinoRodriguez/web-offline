export interface Workspace {
  id: string;
  name: string;
  description?: string | null;
  owner_id: string;
  created_by: string;
  created_at: string;
  updated_by?: string | null;
  updated_at: string;
  version: number;
  is_deleted: number; // 0 or 1
  role_in_workspace?: string;
}

export interface WorkspaceMember {
  workspace_id: string;
  user_id: string;
  role: string;
  joined_at: string;
}

export interface TaskList {
  id: string;
  workspace_id: string;
  name: string;
  color: string;
  position: number;
  created_by: string;
  created_at: string;
  updated_by?: string | null;
  updated_at: string;
  version: number;
  is_deleted: number;
}

export interface TaskItem {
  id: string;
  list_id: string;
  workspace_id: string;
  parent_task_id?: string | null;
  title: string;
  description?: string | null;
  status: 'TODO' | 'IN_PROGRESS' | 'DONE' | 'CANCELLED';
  priority: 'LOW' | 'MEDIUM' | 'HIGH' | 'URGENT';
  due_date?: string | null;
  position: number;
  created_by: string;
  created_at: string;
  updated_by?: string | null;
  updated_at: string;
  version: number;
  is_deleted: number;
  
  // Computed client-side
  subtasks?: TaskItem[];
  subtask_count?: number;
  completed_subtask_count?: number;
  progress_percentage?: number;
}

export interface SyncOutboxItem {
  id: string;
  entity: 'workspace' | 'list' | 'task';
  operation: 'INSERT' | 'UPDATE' | 'DELETE';
  entity_id: string;
  workspace_id?: string | null;
  client_timestamp: string;
  version: number;
  payload_json: string;
  created_at: string;
  retry_count: number;
  last_error?: string | null;
}

export const INITIAL_SCHEMA_SQL = `
  PRAGMA foreign_keys = ON;

  CREATE TABLE IF NOT EXISTS workspaces (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    owner_id TEXT NOT NULL,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_by TEXT,
    updated_at TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0
  );

  CREATE TABLE IF NOT EXISTS workspace_members (
    workspace_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    role TEXT NOT NULL DEFAULT 'Editor',
    joined_at TEXT NOT NULL,
    PRIMARY KEY (workspace_id, user_id),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
  );

  CREATE TABLE IF NOT EXISTS lists (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    name TEXT NOT NULL,
    color TEXT NOT NULL DEFAULT '#3B82F6',
    position INTEGER NOT NULL DEFAULT 0,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_by TEXT,
    updated_at TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
  );

  CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY,
    list_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    parent_task_id TEXT,
    title TEXT NOT NULL,
    description TEXT,
    status TEXT NOT NULL DEFAULT 'TODO',
    priority TEXT NOT NULL DEFAULT 'MEDIUM',
    due_date TEXT,
    position INTEGER NOT NULL DEFAULT 0,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_by TEXT,
    updated_at TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (list_id) REFERENCES lists(id) ON DELETE CASCADE,
    FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
    FOREIGN KEY (parent_task_id) REFERENCES tasks(id) ON DELETE CASCADE
  );

  CREATE TABLE IF NOT EXISTS sync_outbox (
    id TEXT PRIMARY KEY,
    entity TEXT NOT NULL,
    operation TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    workspace_id TEXT,
    client_timestamp TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT
  );

  CREATE TABLE IF NOT EXISTS sync_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
  );

  CREATE INDEX IF NOT EXISTS idx_local_tasks_list ON tasks(list_id);
  CREATE INDEX IF NOT EXISTS idx_local_tasks_workspace ON tasks(workspace_id);
  CREATE INDEX IF NOT EXISTS idx_local_tasks_parent ON tasks(parent_task_id);
  CREATE INDEX IF NOT EXISTS idx_local_lists_workspace ON lists(workspace_id);
  CREATE INDEX IF NOT EXISTS idx_local_outbox_created ON sync_outbox(created_at);
`;
