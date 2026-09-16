using BuildInformation = Coord.BuildInfo.BuildInfo;
using Coord.Client;
using Coord.Config;
using Coord.Host;
using Spectre.Console;

namespace Coord.Application;

public static class CliApplication
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintRootHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "host" => RunHost(args[1..]),
            "client" => RunClient(args[1..]),
            "version" or "--version" or "-v" => PrintVersion(),
            _ => UnknownCommand(args[0])
        };
    }

    private static int RunHost(string[] args)
    {
        if (args.Any(IsHelp))
        {
            AnsiConsole.WriteLine("Usage: coord host [--config path] [--no-ui]");
            return 0;
        }
        var options = ParseOptions(args);
        var config = ConfigLoader.Load(options.GetValueOrDefault("config"));
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var host = new HostServer(config);
        AnsiConsole.MarkupLine($"Hosting on [green]{config.Network.Address}[/] (room [yellow]{host.RoomCode}[/]).");
        if (options.ContainsKey("no-ui"))
            host.RunAsync(stop.Token).GetAwaiter().GetResult();
        else
        {
            var network = host.RunAsync(stop.Token);
            TerminalUi.RunHost(host);
            stop.Cancel();
            network.GetAwaiter().GetResult();
        }
        return 0;
    }

    private static int RunClient(string[] args)
    {
        if (args.Any(IsHelp))
        {
            AnsiConsole.WriteLine("Usage: coord client [--config path] [--name player] [--room CODE] [--no-ui]");
            return 0;
        }
        var options = ParseOptions(args);
        var config = ConfigLoader.Load(options.GetValueOrDefault("config"));
        var name = options.GetValueOrDefault("name") ?? Environment.UserName;
        var room = options.GetValueOrDefault("room") ?? "COORD";
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var client = new CoordClient(config);
        AnsiConsole.MarkupLine($"Connecting to [green]{config.Network.Address}[/]...");
        if (options.ContainsKey("no-ui"))
            client.RunAsync(name, room, stop.Token).GetAwaiter().GetResult();
        else
        {
            var network = client.RunAsync(name, room, stop.Token);
            TerminalUi.RunClient(client);
            stop.Cancel();
            network.GetAwaiter().GetResult();
        }
        if (client.Admission is { Accepted: false } rejected)
        {
            AnsiConsole.MarkupLine($"[red]Admission rejected:[/] {rejected.Reason}");
            return 1;
        }
        AnsiConsole.MarkupLine("[green]Admitted.[/] Press Ctrl+C to disconnect.");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            if (key == "no-ui") { result[key] = "true"; continue; }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                result[key] = args[++i];
        }
        return result;
    }

    private static int PrintVersion()
    {
        AnsiConsole.WriteLine(BuildInformation.Version);
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {command}");
        AnsiConsole.MarkupLine("Use [bold]coord --help[/] for usage.");
        return 2;
    }

    private static void PrintRootHelp()
    {
        AnsiConsole.MarkupLine("[bold]Coord[/]: cooperative game hosting and clients");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Usage:");
        AnsiConsole.MarkupLine("  coord host       Start a host");
        AnsiConsole.MarkupLine("  coord client     Connect as a client");
        AnsiConsole.MarkupLine("  coord version    Print the version");
        AnsiConsole.MarkupLine("  coord --help     Show this help");
    }

    private static bool IsHelp(string value) =>
        value is "help" or "--help" or "-h";
}
