using System.Text.Json.Serialization;

namespace LunaDns.Core.Settings;

/// <summary>Serializer sinh sẵn, để bản publish trimmed (KH6) không cần reflection cho settings.json.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
