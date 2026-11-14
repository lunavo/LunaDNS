namespace LunaDns.Core.Platform;

/// <summary>Giá trị AutoConfigURL của một người dùng (theo SID) trước khi LunaDNS ghi <see cref="NewValue"/>.</summary>
public sealed record AutoProxySnapshot(string UserSid, bool HadPreviousValue, string? PreviousValue, bool PreviousWasExpandString, string NewValue);
