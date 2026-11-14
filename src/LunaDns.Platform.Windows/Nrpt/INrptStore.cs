namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>Kho luật NRPT local. Bản thật: NrptCimStore. Không có thao tác sửa luật: chỉ thêm và gỡ theo Name.</summary>
public interface INrptStore
{
    IReadOnlyList<NrptRule> List();
    void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName);
    void Remove(string name);
}
