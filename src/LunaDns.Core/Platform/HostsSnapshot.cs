namespace LunaDns.Core.Platform;

/// <summary>
/// Các dòng LunaDNS thêm vào hosts (đã định dạng, kèm "# LunaDNS"), kiểu xuống dòng của file,
/// và việc LunaDNS có phải tự thêm dấu xuống dòng cho dòng cuối cũ hay không.
/// </summary>
public sealed record HostsSnapshot(IReadOnlyList<string> Lines, string LineEnding, bool AddedNewline);
