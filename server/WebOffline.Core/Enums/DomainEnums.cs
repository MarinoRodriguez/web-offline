namespace WebOffline.Core.Enums;

public enum SystemRole
{
    User,
    Admin
}

public enum WorkspaceRole
{
    Viewer,
    Editor,
    Owner
}

public enum TaskItemStatus
{
    TODO,
    IN_PROGRESS,
    DONE,
    CANCELLED
}

public enum TaskPriority
{
    LOW,
    MEDIUM,
    HIGH,
    URGENT
}

public enum SyncAction
{
    CREATE,
    UPDATE,
    DELETE
}
