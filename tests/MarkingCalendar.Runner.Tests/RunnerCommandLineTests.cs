using MarkingCalendar.Runner;

namespace MarkingCalendar.Runner.Tests;

public sealed class RunnerCommandLineTests
{
    [Fact]
    public void Parse_ReadsCheckOptions()
    {
        var command = RunnerCommandLine.Parse([
            "check",
            "--data", "./data",
            "--source", "https://example.test/calendar",
            "--dry-run",
            "--accept-anomaly"
        ]);

        var check = Assert.IsType<CheckCommand>(command);
        Assert.Equal("./data", check.DataDirectory);
        Assert.Equal(new Uri("https://example.test/calendar"), check.SourceUrl);
        Assert.True(check.DryRun);
        Assert.True(check.AcceptAnomaly);
    }

    [Fact]
    public void Parse_ReadsTelegramRenderOptions()
    {
        var command = RunnerCommandLine.Parse([
            "render-telegram",
            "--batch", "batch-1",
            "--data", "./data"
        ]);

        var render = Assert.IsType<RenderTelegramCommand>(command);
        Assert.Equal("batch-1", render.BatchId);
        Assert.Equal("./data", render.DataDirectory);
    }

    [Fact]
    public void Parse_ReadsGroupValidationOptions()
    {
        var command = RunnerCommandLine.Parse(["validate-groups", "--data", "./data"]);

        var validate = Assert.IsType<ValidateGroupsCommand>(command);
        Assert.Equal("./data", validate.DataDirectory);
    }

    [Theory]
    [InlineData()]
    [InlineData("check")]
    [InlineData("check", "--data")]
    [InlineData("unknown", "--data", "./data")]
    [InlineData("check", "--data", "./data", "--mystery")]
    [InlineData("render-telegram", "--data", "./data")]
    [InlineData("render-telegram", "--batch", "batch-1")]
    [InlineData("validate-groups")]
    public void Parse_RejectsIncompleteOrUnknownArguments(params string[] args)
    {
        var error = Assert.Throws<RunnerCommandLineException>(() => RunnerCommandLine.Parse(args));

        Assert.NotEmpty(error.Message);
    }
}
