namespace LunaDns.Core.Dns;

/// <summary>Truy vấn đã đọc, chỉ gồm các trường stub cần. MaxUdpPayload lấy từ OPT (RFC 6891), mặc định 512.</summary>
public sealed record DnsQuery(ushort Id, int Opcode, DnsQuestion Question, int MaxUdpPayload);
