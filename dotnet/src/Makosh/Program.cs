using Makosh;
using Makosh.Core;
using Makosh.Windows;
using Microsoft.Extensions.Logging;

var settings = MakoshSettings.Load();
Directory.CreateDirectory(settings.DataDirectory);
Directory.CreateDirectory(settings.InboxDirectory);

var testing = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Testing", StringComparison.OrdinalIgnoreCase);
var builder = testing
    ? WebApplication.CreateBuilder(args)
    : WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

if (!testing)
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
builder.Services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
builder.Services.AddSingleton<IOcrService, WindowsOcrService>();
builder.Services.AddSingleton<IJpegEncoder, WindowsJpegEncoder>();
builder.Services.AddSingleton(sp => new VisionRateLimiter(sp.GetRequiredService<MakoshSettings>().VisionMaxPerHour));
builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<MakoshSettings>();
    IVisionClient? vision = !string.IsNullOrWhiteSpace(settings.ApiKey) ? new OpenAIChatClient(settings) : null;
    return new ScreenLook(
        sp.GetRequiredService<IScreenCapture>(),
        sp.GetRequiredService<IOcrService>(),
        sp.GetRequiredService<IJpegEncoder>(),
        sp.GetRequiredService<VisionRateLimiter>(),
        settings,
        vision);
});
builder.Services.AddSingleton(sp => new LocalToolServices
{
    Paths = sp.GetRequiredService<PathGuard>(),
    Apps = sp.GetRequiredService<IAppHost>(),
    Shell = sp.GetRequiredService<IShell>(),
    Keyboard = sp.GetRequiredService<IKeyboard>(),
    Devices = sp.GetRequiredService<DeviceRegistry>(),
    Files = sp.GetRequiredService<IFileSender>(),
    Screen = sp.GetRequiredService<ScreenLook>(),
    AppsCatalog = AppCatalog.Load(sp.GetRequiredService<MakoshSettings>().DataDirectory),
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
builder.Services.AddSingleton<WindowsTtsService>();
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("makosh.tts");
    return new SileroTtsService(sp.GetRequiredService<MakoshSettings>(), msg => log.LogInformation("{Message}", msg));
});
builder.Services.AddSingleton(sp => TtsRuntime.FromSettings(sp.GetRequiredService<MakoshSettings>()));
builder.Services.AddSingleton<ITtsService>(sp => new CompositeTtsService(
    sp.GetRequiredService<SileroTtsService>(),
    sp.GetRequiredService<WindowsTtsService>(),
    sp.GetRequiredService<TtsRuntime>(),
    msg => sp.GetRequiredService<ILoggerFactory>().CreateLogger("makosh.tts").LogWarning("{Message}", msg)));
builder.Services.AddSingleton<TtsPlayback>();

var app = builder.Build();
if (!testing)
{
    var runtime = app.Services.GetRequiredService<TtsRuntime>();
    var tts = app.Services.GetRequiredService<ITtsService>();
    var chosen = TtsVoicePicker.Select(tts.GetVoices(), runtime.Voice);
    if (chosen is not null)
    {
        runtime.Voice = chosen.Id;
        app.Logger.LogInformation(
            "TTS engine={Engine} voice {Name} culture={Culture} gender={Gender} pitch={Pitch} sileroReady={Ready}",
            runtime.Engine,
            chosen.Name,
            chosen.Culture,
            chosen.Gender,
            tts.SupportsPitch,
            tts.EngineId == TtsEngines.Silero && tts.IsAvailable);
    }
    else
    {
        app.Logger.LogInformation("TTS: no installed voices, using synthesizer default");
    }
}
app.UseWebSockets();
app.MapMakoshHub();
app.UseDefaultFiles();
app.UseStaticFiles();
if (!testing && !string.Equals(Environment.GetEnvironmentVariable("MAKOSH_DESKTOP"), "0", StringComparison.Ordinal))
{
    var lifetime = app.Lifetime;
    var ttsPlayback = app.Services.GetRequiredService<TtsPlayback>();
    lifetime.ApplicationStarted.Register(() =>
    {
        var ui = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MakoshDeskForm(settings, lifetime.StopApplication, ttsPlayback));
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.IsBackground = true;
        ui.Start();
    });
}

app.Run();

static string BindHost(string host) =>
    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;

public partial class Program;
