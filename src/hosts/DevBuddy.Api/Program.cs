// Phase 0 scaffold. The real surface — auth, administration, lifecycle, audit, health —
// is built in Phases 4, 5, 7 and 8. Nothing here is a working endpoint yet.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Problem(
    title: "DevBuddy API is scaffolding only",
    detail: "No endpoints are implemented yet. See docs/plan.md, Phase 7.",
    statusCode: StatusCodes.Status501NotImplemented));

app.Run();
