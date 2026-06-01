using FileRevamp.Commands;
using FileRevamp.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

// Register IAnsiConsole so RenameCommand can be injected in production and tests.
// In production: AnsiConsole.Console (the global static, backed by the real terminal).
// In tests: CommandAppTester injects its TestConsole automatically.
var registrar = new TypeRegistrar();
registrar.RegisterInstance(typeof(IAnsiConsole), AnsiConsole.Console);

var app = new CommandApp<RenameCommand>(registrar);
app.Configure(config =>
{
    config.SetApplicationName("filerevamp");

    config.AddExample(".", "--remove", "_{*}new_{*}", "--dry-run");
    config.AddExample("./exports", "--remove", "_{*}new_{*}", "--replace", ".->-");
});

return app.Run(args);
