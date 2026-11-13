namespace LunaDns.Core.Tls;

public readonly record struct SniLocation(int Offset, int Length, string Host);
