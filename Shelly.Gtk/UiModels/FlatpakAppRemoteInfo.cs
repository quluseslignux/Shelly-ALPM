using System.Text.Json.Serialization;

namespace Shelly.Gtk.UiModels;

public class FlatpakRemoteRefInfo
{
    [JsonPropertyName("download_size")]
    public ulong DownloadSize { get; set; }
    [JsonPropertyName("installed_size")]
    public ulong InstalledSize { get; set; }
}