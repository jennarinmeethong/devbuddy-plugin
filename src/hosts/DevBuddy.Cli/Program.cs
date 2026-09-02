using System.CommandLine;
using DevBuddy.Cli;

// The console host. Migrations, the one-time bootstrap, and every operation the pipeline knows,
// reachable from a terminal.
//
// It is not a fourth implementation of anything. `run` hands a name and a JSON body to the same
// dispatcher the API and the MCP server use, and the named commands below are shortcuts that
// build that body for you. Two things sit outside the pipeline, both deliberately: applying
// migrations, which happens before there is a schema to authorise against, and the bootstrap,
// which creates the first membership there is.

RootCommand root = CommandSurface.Build();
return await root.Parse(args).InvokeAsync();
