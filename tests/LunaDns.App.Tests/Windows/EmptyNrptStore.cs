using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.App.Tests;

internal sealed class EmptyNrptStore : INrptStore
{
    public IReadOnlyList<NrptRule> List() => [];

    public void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName) =>
        throw new NotSupportedException("Báo cáo chẩn đoán không được thêm luật NRPT.");

    public void Remove(string name) => throw new NotSupportedException("Báo cáo chẩn đoán không được gỡ luật NRPT.");
}
