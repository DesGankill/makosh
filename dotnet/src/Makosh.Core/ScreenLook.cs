namespace Makosh.Core;

public sealed class ScreenLook
{
    readonly IScreenCapture _capture;
    readonly IOcrService _ocr;
    readonly IJpegEncoder _jpeg;
    readonly VisionRateLimiter _limiter;
    readonly MakoshSettings _settings;
    readonly IVisionClient? _vision;

    public ScreenLook(
        IScreenCapture capture,
        IOcrService ocr,
        IJpegEncoder jpeg,
        VisionRateLimiter limiter,
        MakoshSettings settings,
        IVisionClient? vision = null)
    {
        _capture = capture;
        _ocr = ocr;
        _jpeg = jpeg;
        _limiter = limiter;
        _settings = settings;
        _vision = vision;
    }

    public async Task<string> LookAsync(string? mode, CancellationToken cancellationToken = default)
    {
        var image = await _capture.CaptureAsync(cancellationToken);
        var text = await _ocr.RecognizeAsync(image, cancellationToken);
        if (!string.Equals(mode, "vision", StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(text))
            {
                return "OCR экрана (без облака):\n" + Cut(text, 4000);
            }

            return "OCR ничего не разобрал. DeepSeek не принимает картинки — облачное зрение недоступно, только текст с экрана.";
        }

        if (BlocksImages(_settings.BaseUrl) || BlocksImages(_settings.VisionModel))
        {
            var leftover = string.IsNullOrEmpty(text) ? "(пусто)" : text;
            return "DeepSeek chat не умеет смотреть картинки. Локальный OCR:\n" + Cut(leftover, 4000);
        }

        if (!_limiter.TryConsume())
        {
            var leftover = string.IsNullOrEmpty(text) ? "(пусто)" : text;
            return
                $"Лимит облачного зрения исчерпан ({_settings.VisionMaxPerHour}/час). " +
                "Остался OCR:\n" + Cut(leftover, 3000);
        }

        var jpeg = await _jpeg.EncodeAsync(image.FitMaxWidth(_settings.VisionMaxWidth), VisionJpeg.Quality, cancellationToken);
        if (_vision is null || !_settings.HasChatModel)
        {
            return "Нет ключа API для зрения.";
        }

        var prompt = "Кратко опиши, что на экране. OCR рядом, сверься с ним:\n" +
                     (string.IsNullOrEmpty(text) ? "OCR пуст" : Cut(text, 1500));
        var body = await _vision.DescribeAsync(_settings.VisionModel, prompt, jpeg, cancellationToken);
        var left = _limiter.CallsLeft();
        return $"{body}\n\n(облачных кадров осталось в этом часе: {left})";
    }

    public static bool BlocksImages(string? value) =>
        (value ?? "").Contains("deepseek", StringComparison.OrdinalIgnoreCase);

    static string Cut(string text, int max) =>
        text.Length <= max ? text : text[..max];
}
