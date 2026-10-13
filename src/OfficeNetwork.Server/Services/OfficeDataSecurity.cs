using System.Security.AccessControl;
using System.Security.Principal;

namespace OfficeNetwork.Server.Services;

public static class OfficeDataSecurity
{
    // Database credentials, full archives and rollback databases belong only to the server operator.
    public static void Protect(string dataFolder)
    {
        OfficeRestoreService.RejectLinkedAncestors(dataFolder);
        Directory.CreateDirectory(dataFolder);
        if(!OperatingSystem.IsWindows())return;
        using var identity=WindowsIdentity.GetCurrent();
        var sid=identity.User??throw new InvalidOperationException("Cannot identify the Windows server account.");
        var permissions=new DirectorySecurity();
        permissions.SetAccessRuleProtection(true,false);
        var inherit=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
        foreach(var principal in new[]{sid,new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
            permissions.AddAccessRule(new FileSystemAccessRule(principal,FileSystemRights.FullControl,inherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(dataFolder).SetAccessControl(permissions);
    }
}
