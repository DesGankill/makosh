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

    public Task<string> LookAsync(string? mode, CancellationToken cancellationToken = default) =>
        LookAsync(mode, skipCapture: false, query: null, cancellationToken);

    public Task<string> LookAsync(string? mode, bool skipCapture, CancellationToken cancellationToken = default) =>
        LookAsync(mode, skipCapture, query: null, cancellationToken);

    public async Task<string> LookAsync(string? mode, bool skipCapture, string? query, CancellationToken cancellationToken = default)
    {
        if (skipCapture || IsSkipMode(mode))
        {
            return "Скриншот и облачное зрение не делаю: вы явно попросили этого не делать.";
        }

        var image = await _capture.CaptureAsync(cancellationToken);
        var page = await _ocr.RecognizeAsync(image, cancellationToken);
        var facts = page.FormatFacts(4000);
        var question = string.IsNullOrWhiteSpace(query)
            ? ""
            : "Вопрос пользователя (ответь по данным ниже, не описывай лишнее):\n" + query.Trim() + "\n\n";
        if (!string.Equals(mode, "vision", StringComparison.Ordinal))
        {
            if (!page.IsEmpty)
            {
                var note = page.IsUncertain
                    ? "Это локальный OCR (не зрение). Текст слабый, обрывки. Не утверждай то, чего нет в списке.\n"
                    : "Это локальный OCR (факты, без облака, не картинка). Только список ниже.\n";
                return question + note + facts;
            }

            return question + "OCR ничего уверенно не разобрал. Не выдумываю интерфейс.";
        }

        if (BlocksImages(_settings.BaseUrl) || BlocksImages(_settings.VisionModel))
        {
            return question + "Этот провайдер не принимает картинки. Локальный OCR (факты):\n" + facts;
        }

        if (_vision is null || string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return question + "Нет ключа API для зрения. OCR:\n" + facts;
        }

        if (!_limiter.TryConsume())
        {
            return
                question +
                $"Лимит облачного зрения исчерпан ({_settings.VisionMaxPerHour}/час). " +
                "Остался OCR (факты):\n" + page.FormatFacts(3000);
        }

        var jpeg = await _jpeg.EncodeAsync(image.FitMaxWidth(_settings.VisionMaxWidth), VisionJpeg.Quality, cancellationToken);
        var prompt =
            (string.IsNullOrWhiteSpace(query) ? "" : "Вопрос пользователя: " + query.Trim() + "\n") +
            "Ответь на вопрос. Это кадр плюс OCR-текст (OCR может врать и быть обрывками). " +
            "Обнаружено — только кадр и OCR. Не добавляй меню, чаты, ярлыки и приложения, которых нет на кадре и в OCR. " +
            "Если не уверен — скажи об этом. Не описывай весь экран, если спросили про конкретный объект.\n" +
            "OCR-фрагменты:\n" +
            page.FormatFacts(1500);
        string body;
        try
        {
            body = await _vision.DescribeAsync(_settings.VisionModel, prompt, jpeg, cancellationToken);
        }
        catch (LlmException ex)
        {
            return ex.UserMessage + "\n" + question + "OCR (факты):\n" + facts;
        }

        var left = _limiter.CallsLeft();
        var uncertainty = page.IsUncertain ? "OCR был слабым — визуальное описание может быть неполным.\n" : "";
        return $"{uncertainty}{body}\n\n(облачных кадров осталось в этом часе: {left})";
    }

    public static bool IsSkipMode(string? mode) =>
        mode is "none" or "skip" or "off";

    public static bool BlocksImages(string? value) =>
        (value ?? "").Contains("deepseek", StringComparison.OrdinalIgnoreCase);
}
