using System.Diagnostics.CodeAnalysis;
using PackageManager.Flatpak;
using Spectre.Console.Cli;

namespace Shelly_CLI.Commands.Flatpak;

public class GetFromRemote :  Command<FlatpakListRemoteAppStreamSettings>
{
    public override int Execute([NotNull] CommandContext context, [NotNull] FlatpakListRemoteAppStreamSettings settings)
    {
        var result = new FlatpakManager().GetAvailableAppsFromRemote(settings.AppStreamName);

        using var stdout = Console.OpenStandardOutput();
        using var writer = new System.IO.StreamWriter(stdout, System.Text.Encoding.UTF8);
        writer.WriteLine(System.Text.Json.JsonSerializer.Serialize<List<FlatpakPackageDto>>(result, ShellyCLIJsonContext.Default.ListFlatpakPackageDto));
        writer.Flush();
        return 0;
    }
}