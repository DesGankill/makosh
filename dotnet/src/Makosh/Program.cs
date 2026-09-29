using Makosh.Core;
using Makosh.Windows;

var settings = MakoshSettings.Load();
var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.WebHost.UseUrls($"http://{BindHost(settings.Host)}:{settings.Port}");
}

var app = builder.Build();

app.MapGet("/api/health", () => Results.Json(new
{
    ok = "makosh",
    device = settings.DeviceName,
    runtime = "dotnet",
    windows = WindowsRuntime.TargetFrameworkMoniker,
}));

app.MapGet("/api/token-check", (HttpContext http) =>
{
    http.Request.Headers.TryGetValue("x-makosh-token", out var header);
    if (!MakoshSettings.TokenMatches(header.ToString(), settings.Token))
    {
        return Results.Json(new { detail = "Неверный токен" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Json(new { ok = true });
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

static string BindHost(string host) =>
    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;

public partial class Program;
