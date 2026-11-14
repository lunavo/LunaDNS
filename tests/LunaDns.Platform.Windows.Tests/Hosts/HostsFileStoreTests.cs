using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;

namespace LunaDns.Platform.Windows.Tests.Hosts;

public sealed class HostsFileStoreTests : IDisposable
{
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Dacl(string path) =>
        new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    [Fact]
    public void Write_keeps_attributes_and_explicit_acl_and_leaves_no_temp_file()
    {
        var path = _dir.PathOf("hosts");
        File.WriteAllBytes(path, "old"u8.ToArray());
        var info = new FileInfo(path);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);
        File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Archive);
        var daclBefore = Dacl(path);

        new HostsFileStore(path).Write("new"u8.ToArray());

        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Archive, File.GetAttributes(path));
        Assert.Equal(daclBefore, Dacl(path));
        Assert.False(File.Exists(path + ".lunadns.tmp"));
    }

    [Fact]
    public void Editor_round_trip_through_store_is_byte_identical()
    {
        var path = _dir.PathOf("hosts");
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Máy chủ nội bộ\r\n127.0.0.1 localhost")];
        File.WriteAllBytes(path, original);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var store = new HostsFileStore(path);
        var snapshot = HostsEditor.Plan(store.Read(), Pubg);

        store.Write(HostsEditor.Apply(store.Read(), snapshot));
        Assert.Contains(HostsEditor.Marker, Encoding.UTF8.GetString(store.Read()));
        store.Write(HostsEditor.Revert(store.Read(), snapshot));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(path));
    }

    [Fact]
    public void Read_of_missing_file_is_configurator_error()
    {
        var store = new HostsFileStore(_dir.PathOf("missing"));

        Assert.Throws<ConfiguratorException>(() => store.Read());
    }

    [Fact]
    public void Default_path_points_to_existing_hosts_file()
    {
        var path = HostsFileStore.DefaultPath();

        Assert.EndsWith("hosts", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
    }
}
