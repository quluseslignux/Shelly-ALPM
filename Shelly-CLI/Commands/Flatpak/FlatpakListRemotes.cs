using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using PackageManager.Flatpak;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Shelly_CLI.Commands.Flatpak;

public class FlatpakListRemotes : Command<DefaultSettings>
{
    public override int Execute([NotNull] CommandContext context,[NotNull] DefaultSettings settings)
    {
        var manager = new FlatpakManager();

        var remotes = manager.ListRemotesWithDetails();

        AnsiConsole.MarkupLine($"[blue]Remotes:[/]");

        foreach (var remote in remotes)
        {
            var scopeColor = remote.Scope == "system" ? "green" : "yellow";
            AnsiConsole.MarkupLine($"{remote.Name.EscapeMarkup()} [{scopeColor}]({remote.Scope})[/]");
        }

        return 0;
    }
}
