namespace KenticoMigrator.Run;

/// <summary>The command line of contracts/cli.md: a verb, <c>--profile</c>, two options and <c>--Key=Value</c> configuration overrides.</summary>
public sealed record CliArguments(
    string Command,
    string Profile,
    bool OverwriteEdited,
    string? RunDir,
    IReadOnlyList<string> ConfigArguments)
{
    public const string Counts = "counts";
    public const string Plan = "plan";
    public const string Migrate = "migrate";

    private static readonly string[] Commands = [Counts, Plan, Migrate];

    /// <summary>
    /// Parses by hand: the configuration command-line provider would take the argument after a flag such as
    /// <c>--overwrite-edited</c> as its value. Everything of the form <c>--Section:Key=Value</c> is handed on unchanged.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> args, out CliArguments? parsed, out string? error)
    {
        parsed = null;
        if (args.Count == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"A command is required: {string.Join(", ", Commands)}.";
            return false;
        }

        if (!Commands.Contains(args[0], StringComparer.Ordinal))
        {
            error = $"Unknown command '{args[0]}'. Use one of: {string.Join(", ", Commands)}.";
            return false;
        }

        string? profile = null;
        string? runDir = null;
        var overwrite = false;
        var config = new List<string>();

        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            var equals = arg.IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? arg[..equals] : arg;
            switch (name)
            {
                case "--overwrite-edited" when equals < 0:
                    overwrite = true;
                    break;
                case "--profile" or "--run-dir":
                    string? value;
                    if (equals > 0)
                    {
                        value = arg[(equals + 1)..];
                    }
                    else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        value = args[++i];
                    }
                    else
                    {
                        error = $"{name} needs a value.";
                        return false;
                    }

                    if (name == "--profile")
                    {
                        profile = value;
                    }
                    else
                    {
                        runDir = value;
                    }

                    break;
                default:
                    if (equals > 0 && name.StartsWith("--", StringComparison.Ordinal) && name.Length > 2)
                    {
                        config.Add(arg);
                        break;
                    }

                    error = $"Unknown option '{name}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(profile))
        {
            error = "--profile <path> is required.";
            return false;
        }

        parsed = new CliArguments(args[0], profile, overwrite, runDir, config);
        error = null;
        return true;
    }
}
