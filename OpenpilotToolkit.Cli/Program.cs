using OpenpilotSdk.Hardware;
using OpenpilotSdk.OpenPilot.Fork;
using OpenpilotSdk.Runtime;

return await OpenpilotCli.RunAsync(args);

internal static class OpenpilotCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (!options.IsValid || options.ShowHelp || string.IsNullOrWhiteSpace(options.Command))
        {
            WriteHelp();
            if (!string.IsNullOrWhiteSpace(options.Error))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(options.Error);
            }

            return string.IsNullOrWhiteSpace(options.Error) ? 0 : 1;
        }

        try
        {
            OpenpilotHost.Configure(options.GetOption("ssh-key"));

            return options.Command switch
            {
                "discover" => await DiscoverAsync(),
                "routes" => await ListRoutesAsync(options),
                "install-fork" => await InstallForkAsync(options),
                "reboot" => await ExecuteDeviceActionAsync(options, device => device.RebootAsync(), "Reboot command sent."),
                "shutdown" => await ExecuteDeviceActionAsync(options, device => device.ShutdownAsync(), "Shutdown command sent."),
                _ => Fail($"Unknown command '{options.Command}'.")
            };
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static async Task<int> DiscoverAsync()
    {
        var discoveredAny = false;

        await foreach (var device in OpenpilotDevice.DiscoverAsync())
        {
            discoveredAny = true;
            Console.WriteLine(
                $"{device.IpAddress}\t{device.DeviceType}\t{(device.IsAuthenticated ? "authenticated" : "reachable")}\t{device.HostName ?? "-"}");
        }

        return discoveredAny ? 0 : Fail("No devices were discovered.");
    }

    private static async Task<int> ListRoutesAsync(CliOptions options)
    {
        var device = await ResolveDeviceAsync(options);
        var limit = options.GetIntOption("limit") ?? 10;
        var count = 0;

        await foreach (var route in device.GetRoutesAsync())
        {
            count++;
            Console.WriteLine($"{route}\tsegments={route.Segments.Count}");
            if (count >= limit)
            {
                break;
            }
        }

        return count > 0 ? 0 : Fail($"No routes were found on {device.IpAddress}.");
    }

    private static async Task<int> InstallForkAsync(CliOptions options)
    {
        var owner = options.GetRequiredOption("owner");
        var branch = options.GetRequiredOption("branch");
        var repository = options.GetOption("repo") ?? "openpilot";
        var device = await ResolveDeviceAsync(options);

        var progress = new Progress<InstallProgress>(item =>
        {
            Console.WriteLine($"{item.Progress,3}% {item.ProgressText}");
        });

        var result = await device.InstallForkAsync(owner, branch, progress, repository);
        Console.WriteLine(result.Message);

        return result.Success ? 0 : 1;
    }

    private static async Task<int> ExecuteDeviceActionAsync(
        CliOptions options,
        Func<OpenpilotDevice, Task<bool>> action,
        string successMessage)
    {
        var device = await ResolveDeviceAsync(options);
        var succeeded = await action(device);

        if (!succeeded)
        {
            return Fail($"The device action failed for {device.IpAddress}.");
        }

        Console.WriteLine(successMessage);
        return 0;
    }

    private static async Task<OpenpilotDevice> ResolveDeviceAsync(CliOptions options)
    {
        var host = options.GetOption("host");
        if (!string.IsNullOrWhiteSpace(host))
        {
            return await OpenpilotDevice.GetOpenpilotDeviceAsync(host)
                   ?? throw new InvalidOperationException($"Unable to resolve an openpilot device at '{host}'.");
        }

        await foreach (var device in OpenpilotDevice.DiscoverAsync())
        {
            if (device.IsAuthenticated)
            {
                return device;
            }
        }

        throw new InvalidOperationException("No authenticated device was found. Pass --host to target a device explicitly.");
    }

    private static void WriteHelp()
    {
        Console.WriteLine("OpenpilotToolkit CLI");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  discover");
        Console.WriteLine("  routes [--host <ip-or-hostname>] [--limit <count>]");
        Console.WriteLine("  install-fork --owner <github-user> --branch <branch> [--repo <repository>] [--host <ip-or-hostname>]");
        Console.WriteLine("  reboot [--host <ip-or-hostname>]");
        Console.WriteLine("  shutdown [--host <ip-or-hostname>]");
        Console.WriteLine();
        Console.WriteLine("Global options:");
        Console.WriteLine("  --ssh-key <path>    Override the private key used for SSH.");
        Console.WriteLine();
        Console.WriteLine("SSH key lookup order:");
        Console.WriteLine("  1. --ssh-key");
        Console.WriteLine("  2. OPENPILOT_SSH_KEY");
        Console.WriteLine("  3. ~/.ssh/id_ed25519, ~/.ssh/id_rsa, ~/.ssh/id_ecdsa, ~/.ssh/comma");
        Console.WriteLine("  4. OpenpilotToolkit app data / bundled opensshkey");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

internal sealed class CliOptions
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }

    public string? Error { get; private set; }

    public bool IsValid => string.IsNullOrWhiteSpace(Error);

    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--help" or "-h")
            {
                options.ShowHelp = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var nameValue = arg[2..].Split('=', 2, StringSplitOptions.None);
                if (nameValue.Length == 2)
                {
                    options._options[nameValue[0]] = nameValue[1];
                    continue;
                }

                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options.Error = $"Missing value for option '{arg}'.";
                    return options;
                }

                options._options[nameValue[0]] = args[++index];
                continue;
            }

            if (string.IsNullOrWhiteSpace(options.Command))
            {
                options.Command = arg;
                continue;
            }

            options.Error = $"Unexpected argument '{arg}'.";
            return options;
        }

        return options;
    }

    public string? GetOption(string name)
    {
        return _options.TryGetValue(name, out var value) ? value : null;
    }

    public int? GetIntOption(string name)
    {
        var value = GetOption(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, out var parsedValue) && parsedValue > 0)
        {
            return parsedValue;
        }

        throw new InvalidOperationException($"Option '--{name}' must be a positive integer.");
    }

    public string GetRequiredOption(string name)
    {
        return GetOption(name)
               ?? throw new InvalidOperationException($"Option '--{name}' is required for the '{Command}' command.");
    }
}
