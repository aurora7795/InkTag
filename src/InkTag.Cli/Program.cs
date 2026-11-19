using System;
using InkTag.Cli;

int exitCode = await CliApp.RunAsync(args);
if (exitCode != 0)
{
    Environment.Exit(exitCode);
}
return exitCode;
