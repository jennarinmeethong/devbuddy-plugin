#!/usr/bin/env node
import { contextForProcess } from "./context.js";
import { run } from "./cli.js";

process.exitCode = await run(contextForProcess(), process.argv.slice(2));
