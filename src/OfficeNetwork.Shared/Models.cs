namespace OfficeNetwork.Shared;

public enum OfficeRole
{
    ServerAdministrator = 0,
    Director = 1,
    Editor = 2,
    NewsSourcing = 3,
    Admin = 4
}
public enum OfficeFolder { WorkingFiles, SubmittedFiles, FinalFiles }

public sealed record OfficeUser(Guid Id, string UserName, string DisplayName, OfficeRole Role, bool IsEnabled = true);
public sealed record CreateUserRequest(string UserName, string DisplayName, OfficeRole Role, string Password);
public sealed record UpdateUserRequest(string UserName, string DisplayName, OfficeRole Role, bool IsEnabled, string? NewPassword = null);
public sealed record LoginRequest(string UserName, string Password);
public sealed record LoginResult(Guid UserId, string UserName, string DisplayName, OfficeRole Role, string Token);
public sealed record ChatMessage(Guid Id, Guid SenderId, string SenderName, Guid? RecipientId, OfficeRole? RecipientRole, string Text, DateTimeOffset SentAt, bool IsBroadcast = false);
public sealed record PresenceInfo(Guid UserId, string DisplayName, OfficeRole Role, bool IsOnline);
public sealed record FolderPermission(OfficeRole Role, OfficeFolder Folder, bool CanRead, bool CanWrite, bool CanDelete, bool CanManagePermissions);
public sealed record OfficeFileItem(string Name, string FullPath, long Size, DateTimeOffset ModifiedAt, string? OwnerUserName = null, string Status = "Working", DateTimeOffset? SubmittedAt = null, string? DirectorMinute = null, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null);
public sealed record ReturnFileRequest(string OwnerUserName, string FileName, string DirectorMinute);
public sealed record RenameFileRequest(string FileName, string NewFileName, string? OwnerUserName = null);
public sealed record WorkflowEvent(DateTimeOffset At, string Action, string FileName, string OwnerUserName, string ActorName, string? Note = null);
public sealed record WorkAssignment(Guid Id, Guid AssignedToUserId, string AssignedToUserName, string AssignedToDisplayName, Guid AssignedByUserId, string AssignedByDisplayName, string Title, string Instructions, string? FileName, DateTimeOffset AssignedAt, DateTimeOffset? DueAt, string Status, DateTimeOffset? CompletedAt = null);
public sealed record CreateAssignmentRequest(Guid AssignedToUserId, string Title, string Instructions, DateTimeOffset? DueAt);
public sealed record CompleteAssignmentRequest(Guid AssignmentId);
public sealed record ServerConfiguration(string RootPath, string ServerName, int ChatPort);
public sealed record ChangeStorageRequest(string RootPath, bool CopyExistingFiles);
