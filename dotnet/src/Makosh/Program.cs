using Makosh;
using Makosh.Core;
using Makosh.Windows;

var builder = WebApplication.CreateBuilder(args);
var settings = MakoshSettings.Load();
Directory.CreateDirectory(settings.DataDirectory);
Directory.CreateDirectory(settings.InboxDirectory);

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.WebHost.UseUrls($"http://{BindHost(settings.Host)}:{settings.Port}");
}

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton(sp => PathGuard.ForUser(sp.GetRequiredService<MakoshSettings>().DataDirectory));
builder.Services.AddSingleton<WindowsDesktop>();
builder.Services.AddSingleton<IAppHost>(sp => sp.GetRequiredService<WindowsDesktop>());
builder.Services.AddSingleton<IShell>(sp => sp.GetRequiredService<WindowsDesktop>());
builder.Services.AddSingleton<IKeyboard>(sp => sp.GetRequiredService<WindowsDesktop>());
builder.Services.AddSingleton<IFileSender, HubFileSender>();
builder.Services.AddSingleton(sp => new LocalToolServices
{
    Paths = sp.GetRequiredService<PathGuard>(),
    Apps = sp.GetRequiredService<IAppHost>(),
    Shell = sp.GetRequiredService<IShell>(),
    Keyboard = sp.GetRequiredService<IKeyboard>(),
    Devices = sp.GetRequiredService<DeviceRegistry>(),
    Files = sp.GetRequiredService<IFileSender>(),
});
builder.Services.AddSingleton(sp =>
{
    var data = sp.GetRequiredService<MakoshSettings>().DataDirectory;
    Directory.CreateDirectory(data);
    return new Memory(Path.Combine(data, "memory.sqlite"));
});
builder.Services.AddSingleton(sp => Agent.Create(
    sp.GetRequiredService<Memory>(),
    sp.GetRequiredService<MakoshSettings>(),
    sp.GetRequiredService<LocalToolServices>()));

var app = builder.Build();
app.UseWebSockets();
app.MapMakoshHub();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

static string BindHost(string host) =>
    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;

public partial class Program;
