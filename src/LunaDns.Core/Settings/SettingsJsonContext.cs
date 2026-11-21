using System.Text.Json.Serialization;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Settings;

/// <summary>Serializer sinh sẵn, để bản publish trimmed (KH6) không cần reflection cho settings.json.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(DomainSelection))]
[JsonSerializable(typeof(CustomDoh))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
