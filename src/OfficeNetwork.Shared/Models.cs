namespace OfficeNetwork.Shared;

public enum OfficeRole
{
    Director,
    Editor,
    NewsSourcing
}

public enum OfficeFolder
{
    WorkingFiles,
    SubmittedFiles,
    FinalFiles
}

public sealed record OfficeUser(
    Guid Id,
    string UserName,
    string DisplayName,
    OfficeRole Role,
    bool IsEnabled = true);

public sealed record ChatMessage(
    Guid Id,
    Guid SenderId,
    string SenderName,
    Guid? RecipientId,
    OfficeRole? RecipientRole,
    string Text,
    DateTimeOffset SentAt,
    bool IsBroadcast = false);

public sealed record PresenceInfo(
    Guid UserId,
    string DisplayName,
    OfficeRole Role,
    bool IsOnline);

public sealed record FolderPermission(
    OfficeRole Role,
    OfficeFolder Folder,
    bool CanRead,
    bool CanWrite,
    bool CanDelete,
    bool CanManagePermissions);
