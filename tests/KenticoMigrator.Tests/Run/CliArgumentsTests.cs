using KenticoMigrator.Run;

namespace KenticoMigrator.Tests.Run;

public sealed class CliArgumentsTests
{
    private static CliArguments Parse(params string[] args)
    {
        Assert.True(CliArguments.TryParse(args, out var parsed, out var error), error);
        return parsed!;
    }

    private static string Fail(params string[] args)
    {
        Assert.False(CliArguments.TryParse(args, out _, out var error));
        return error!;
    }

    [Fact]
    public void Reads_command_profile_and_options()
    {
        var cli = Parse("migrate", "--profile", "profiles/sample.json", "--overwrite-edited", "--run-dir", "out/run1");

        Assert.Equal("migrate", cli.Command);
        Assert.Equal("profiles/sample.json", cli.Profile);
        Assert.True(cli.OverwriteEdited);
        Assert.Equal("out/run1", cli.RunDir);
    }

    [Fact]
    public void Flag_before_another_option_does_not_swallow_it()
    {
        var cli = Parse("plan", "--overwrite-edited", "--profile", "p.json");

        Assert.True(cli.OverwriteEdited);
        Assert.Equal("p.json", cli.Profile);
    }

    [Fact]
    public void Equals_form_and_config_overrides_are_accepted()
    {
        var cli = Parse("counts", "--profile=p.json", "--Umbraco:BaseUrl=https://localhost:1", "--Kentico:MediaRoot=media");

        Assert.Equal("p.json", cli.Profile);
        Assert.Equal(["--Umbraco:BaseUrl=https://localhost:1", "--Kentico:MediaRoot=media"], cli.ConfigArguments);
        Assert.False(cli.OverwriteEdited);
        Assert.Null(cli.RunDir);
    }

    [Fact]
    public void Missing_or_unknown_command_is_refused()
    {
        Assert.Contains("command", Fail("--profile", "p.json"));
        Assert.Contains("frobnicate", Fail("frobnicate", "--profile", "p.json"));
    }

    [Fact]
    public void Profile_is_required()
    {
        Assert.Contains("--profile", Fail("migrate"));
        Assert.Contains("--profile", Fail("migrate", "--profile"));
    }

    [Fact]
    public void Unknown_option_is_refused()
    {
        Assert.Contains("--delete", Fail("migrate", "--profile", "p.json", "--delete"));
    }

    [Fact]
    public void Flag_with_a_value_is_refused_instead_of_ignored()
    {
        Assert.Contains("--overwrite-edited", Fail("migrate", "--profile", "p.json", "--overwrite-edited=true"));
    }
}
