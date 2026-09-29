import React from 'react';
import { useUrlParams } from '../../hooks/useUrlParams';
import { NewWorkspaceModal } from './NewWorkspaceModal';
import { NewListModal } from './NewListModal';
import { NewTaskModal } from './NewTaskModal';
import { TaskDetailModal } from './TaskDetailModal';
import { CsvImportModal } from './CsvImportModal';
import { SessionManagerModal } from './SessionManagerModal';
import { NewUserModal } from './NewUserModal';

interface Props {
  onRefreshData?: () => void;
}

export const ModalRoot: React.FC<Props> = ({ onRefreshData }) => {
  const { modal } = useUrlParams();

  if (!modal) return null;

  switch (modal) {
    case 'new-workspace':
      return <NewWorkspaceModal onWorkspaceCreated={onRefreshData} />;
    case 'new-list':
      return <NewListModal onListCreated={onRefreshData} />;
    case 'new-task':
      return <NewTaskModal onTaskCreated={onRefreshData} />;
    case 'task-detail':
      return <TaskDetailModal onTaskUpdated={onRefreshData} />;
    case 'import-csv':
      return <CsvImportModal onImportCompleted={onRefreshData} />;
    case 'session-manager':
      return <SessionManagerModal />;
    case 'new-user':
      return <NewUserModal onUserCreated={onRefreshData} />;
    default:
      return null;
  }
};
