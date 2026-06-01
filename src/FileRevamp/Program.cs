using FileRevamp.Commands;
using Spectre.Console.Cli;

var app = new CommandApp<RenameCommand>();
app.Configure(config =>
{
    config.SetApplicationName("filerevamp");

    config.AddExample(".", "--remove", "_{*}new_{*}", "--dry-run");
    config.AddExample("./exports", "--remove", "_{*}new_{*}", "--replace", ".->-");
});

return app.Run(args);
