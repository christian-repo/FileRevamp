using System.Reflection;
using FileRevamp.Commands;
using Spectre.Console.Cli;

var app = new CommandApp<RenameCommand>();
app.Configure(config =>
{
    config.SetApplicationName("filerevamp");
    config.SetApplicationVersion(
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev");

    config.AddExample(".", "--remove", "_draft_", "--dry-run");
    config.AddExample("./exports", "--remove", ".*?new_", "--replace", ".->-");
    config.AddExample("./exports", "--removeBeg", "_{*}", "--dry-run");
    config.AddExample("./exports", "--removeEnd", "_{*}");
    config.AddExample("./exports", "--removeBeg", "_{*}new_{*}", "--removeEnd", "_{*}");
});

return app.Run(args);
