using System.Reflection;
using FileRevamp.Commands;
using Spectre.Console.Cli;

var app = new CommandApp<RenameCommand>();
app.Configure(config =>
{
    config.SetApplicationName("filerevamp");
    config.SetApplicationVersion(
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev");

    config.AddExample(".", "--remove", "_{*}new_{*}", "--dry-run");
    config.AddExample("./exports", "--remove", "_{*}new_{*}", "--replace", ".->-");
});

return app.Run(args);
