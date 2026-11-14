using System.Globalization;
using LunaDns.Core.Platform;
using Microsoft.Management.Infrastructure;

namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>
/// Luật NRPT local qua CIM, lớp root\Microsoft\Windows\DNS:PS_DnsClientNrptRule. Đây chính là lớp mà
/// Add/Get/Remove-DnsClientNrptRule gọi (đọc từ PS_DnsClientNRPTRule_v1.0.0.cdxml). Không chạy PowerShell (spec 3).
/// Get không cần quyền Admin; Add và Remove thì cần.
/// </summary>
public sealed class NrptCimStore : INrptStore
{
    private const string CimNamespace = @"root\Microsoft\Windows\DNS";
    private const string CimClass = "PS_DnsClientNrptRule";

    public IReadOnlyList<NrptRule> List() => Invoke("Get", _ => { }, ReadRules);

    public void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName)
    {
        ArgumentNullException.ThrowIfNull(namespaces);
        Invoke("Add", p =>
        {
            p.Add(CimMethodParameter.Create("Namespace", namespaces.ToArray(), CimType.StringArray, CimFlags.In));
            p.Add(CimMethodParameter.Create("NameServers", new[] { nameServer }, CimType.StringArray, CimFlags.In));
            p.Add(CimMethodParameter.Create("Comment", comment, CimType.String, CimFlags.In));
            p.Add(CimMethodParameter.Create("DisplayName", displayName, CimType.String, CimFlags.In));
        }, _ => true);
    }

    public void Remove(string name) =>
        Invoke("Remove", p =>
        {
            p.Add(CimMethodParameter.Create("Name", name, CimType.String, CimFlags.In));
            p.Add(CimMethodParameter.Create("Force", true, CimType.Boolean, CimFlags.In));
        }, _ => true);

    private static T Invoke<T>(string method, Action<CimMethodParametersCollection> fill, Func<CimMethodResult, T> read)
    {
        try
        {
            using var session = CimSession.Create(null);
            using var parameters = new CimMethodParametersCollection();
            fill(parameters);
            var result = session.InvokeMethod(CimNamespace, CimClass, method, parameters);
            var code = Convert.ToUInt32(result.ReturnValue?.Value ?? 0u, CultureInfo.InvariantCulture);
            if (code != 0)
                throw new ConfiguratorException(ConfiguratorError.NrptFailed, $"Windows từ chối thao tác NRPT '{method}' (mã {code}).");
            return read(result);
        }
        catch (CimException ex) when (ex.NativeErrorCode == NativeErrorCode.AccessDenied)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied, "Sửa luật DNS (NRPT) cần quyền Admin.", ex);
        }
        catch (CimException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.NrptFailed, "Không sửa được luật DNS (NRPT) của Windows.", ex);
        }
    }

    private static IReadOnlyList<NrptRule> ReadRules(CimMethodResult result)
    {
        if (result.OutParameters["cmdletOutput"]?.Value is not CimInstance[] instances) return [];
        var rules = new List<NrptRule>(instances.Length);
        foreach (var instance in instances)
        {
            using (instance) rules.Add(ToRule(instance));
        }
        return rules;
    }

    private static NrptRule ToRule(CimInstance instance)
    {
        var p = instance.CimInstanceProperties;
        return new NrptRule(
            p["Name"]?.Value as string ?? string.Empty,
            p["Namespace"]?.Value as string[] ?? [],
            p["NameServers"]?.Value as string[] ?? [],
            p["Comment"]?.Value as string,
            p["DisplayName"]?.Value as string);
    }
}
