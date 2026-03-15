using System.Text.Json.Serialization;

namespace Shelly.Gtk.UiModels;

public class FlatpakRemoteDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}
