using DevBuddy.Client;

// The `devbuddy` client (Phase 14, A4; ADR-0015). Keeps a person's machine token per checkout and
// hands it to Claude Code and Codex as a header helper. It talks to a DevBuddy server only over
// HTTPS, as an assistant does.

ClientContext context = ClientContext.ForThisProcess();

return await Commands.RunAsync(context, args);
