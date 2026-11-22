namespace LunaDns.Core.Dns;

/// <summary>Máy DoH trả lời gần nhất (F4). <see cref="Fallback"/>: không phải máy đầu của chuỗi cấu hình.</summary>
public sealed record DohActivity(string Server, bool Fallback);
