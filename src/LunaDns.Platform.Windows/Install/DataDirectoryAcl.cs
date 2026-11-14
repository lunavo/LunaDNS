using System.Security.AccessControl;
using System.Security.Principal;

namespace LunaDns.Platform.Windows.Install;

/// <summary>ACL của %ProgramData%\LunaDNS (spec 4.6, KH7 K9).</summary>
public interface IDataDirectoryAcl
{
    /// <summary>Đặt ACL cho thư mục và đưa mọi file, thư mục con về chỉ kế thừa. Cần quyền Admin.</summary>
    void Apply(string directory);

    /// <summary>Thư mục tồn tại, không kế thừa, và không ai ngoài SYSTEM/Administrators ghi được nó hay thứ bên trong.</summary>
    bool IsCompliant(string directory);
}

/// <summary>
/// Journal là đầu vào của task dọn dẹp chạy bằng SYSTEM (KH7 K7), nên chỉ SYSTEM và Administrators được ghi.
/// Users được đọc để mở thư mục log từ Explorer. Owner là Administrators: owner luôn đổi được DACL.
/// </summary>
public sealed class DataDirectoryAcl : IDataDirectoryAcl
{
    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    // Quyền cho phép ghi, xoá, đổi ACL hoặc owner; cộng GENERIC_ALL (0x10000000) và GENERIC_WRITE (0x40000000) chưa ánh xạ.
    private const FileSystemRights DangerousRights = FileSystemRights.Write | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership
        | (FileSystemRights)0x50000000;

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public static DirectorySecurity Build()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    public void Apply(string directory)
    {
        var root = new DirectoryInfo(directory);
        root.SetAccessControl(Build());
        foreach (var child in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            switch (child)
            {
                case FileInfo file:
                    file.SetAccessControl(InheritOnly(new FileSecurity()));
                    break;
                case DirectoryInfo dir:
                    dir.SetAccessControl(InheritOnly(new DirectorySecurity()));
                    break;
            }
        }
    }

    public bool IsCompliant(string directory)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists) return false;
        var security = root.GetAccessControl();
        if (!security.AreAccessRulesProtected || !IsSafe(security)) return false;
        foreach (var child in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            FileSystemSecurity childSecurity = child switch
            {
                FileInfo file => file.GetAccessControl(),
                DirectoryInfo dir => dir.GetAccessControl(),
                _ => throw new InvalidOperationException(child.GetType().Name),
            };
            if (!IsSafe(childSecurity)) return false;
        }
        return true;
    }

    /// <summary>DACL rỗng, không bảo vệ: chỉ còn quyền kế thừa từ thư mục cha. Owner đưa về Administrators.</summary>
    private static T InheritOnly<T>(T security) where T : FileSystemSecurity
    {
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        security.SetOwner(Administrators);
        return security;
    }

    private static bool IsSafe(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (owner != LocalSystem && owner != Administrators) return false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference == LocalSystem || rule.IdentityReference == Administrators) continue;
            if ((rule.FileSystemRights & DangerousRights) != 0) return false;
        }
        return true;
    }
}
