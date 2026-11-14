using System.Security.AccessControl;
using System.Security.Principal;
using LunaDns.Platform.Windows.Install;

namespace LunaDns.Platform.Windows.Tests.Install;

public sealed class DataDirectoryAclTests
{
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string UsersSid = "S-1-5-32-545";

    [Fact]
    public void Build_is_protected_and_has_exactly_three_rules()
    {
        var security = DataDirectoryAcl.Build();

        Assert.True(security.AreAccessRulesProtected);
        var rules = Rules(security);
        Assert.Equal([SystemSid, AdministratorsSid, UsersSid], rules.Select(r => r.IdentityReference.Value).Order());
        Assert.All(rules, r => Assert.Equal(AccessControlType.Allow, r.AccessControlType));
        Assert.All(rules, r => Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, r.InheritanceFlags));
    }

    [Fact]
    public void Build_grants_full_control_only_to_system_and_administrators()
    {
        var rules = Rules(DataDirectoryAcl.Build());

        Assert.Equal(FileSystemRights.FullControl, Single(rules, SystemSid).FileSystemRights);
        Assert.Equal(FileSystemRights.FullControl, Single(rules, AdministratorsSid).FileSystemRights);
        Assert.Equal(FileSystemRights.ReadAndExecute, Single(rules, UsersSid).FileSystemRights & ~FileSystemRights.Synchronize);
    }

    [Fact]
    public void Build_sets_owner_to_administrators()
    {
        Assert.Equal(AdministratorsSid, DataDirectoryAcl.Build().GetOwner(typeof(SecurityIdentifier))!.Value);
    }

    [Fact]
    public void Temp_directory_with_default_acl_is_not_compliant()
    {
        using var temp = new TempDirectory();

        Assert.False(new DataDirectoryAcl().IsCompliant(temp.Path));
    }

    [Fact]
    public void Missing_directory_is_not_compliant()
    {
        using var temp = new TempDirectory();

        Assert.False(new DataDirectoryAcl().IsCompliant(temp.PathOf("missing")));
    }

    [AdminFact]
    public void Apply_makes_directory_and_existing_children_compliant()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), $"LunaDNS.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        File.WriteAllText(Path.Combine(dir, "journal.json"), "[]");
        File.WriteAllText(Path.Combine(dir, "logs", "a.log"), "x");
        try
        {
            var acl = new DataDirectoryAcl();
            Assert.False(acl.IsCompliant(dir));

            acl.Apply(dir);

            Assert.True(acl.IsCompliant(dir));
            File.WriteAllText(Path.Combine(dir, "new.json"), "{}");
            Assert.True(acl.IsCompliant(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static List<FileSystemAccessRule> Rules(FileSystemSecurity security) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();

    private static FileSystemAccessRule Single(List<FileSystemAccessRule> rules, string sid) =>
        Assert.Single(rules, r => r.IdentityReference.Value == sid);
}
