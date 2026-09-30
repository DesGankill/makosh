namespace Makosh.Core;

public static class AppLauncher
{
    public static string Open(string name, IAppHost host, AppCatalog? catalog = null)
    {
        catalog ??= AppCatalog.BuiltIn();
        var raw = (name ?? "").Trim();
        var denied = $"Приложения «{name}» нет в каталоге. Добавьте имя в data/apps.json (не путь к exe). Сейчас: {catalog.Summary()}.";

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host.OpenUrl(raw);
            return $"Открыл {raw}";
        }

        if (raw.Contains('\\') || raw.Contains('/') || raw.Contains(':'))
        {
            return denied;
        }

        var entry = catalog.Find(raw);
        if (entry is null)
        {
            return denied;
        }

        if (string.Equals(entry.Kind, "url", StringComparison.OrdinalIgnoreCase) ||
            entry.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            host.OpenUrl(entry.Target);
            return entry.Id == "browser" ? "Открыл браузер" : $"Открыл {entry.Target}";
        }

        var target = entry.Target;
        if (target.Contains('\\') || target.Contains('/'))
        {
            if (!Path.IsPathRooted(target) || !File.Exists(target))
            {
                return $"Приложение «{entry.Id}» прописано в списке, но файл не найден: {target}";
            }

            host.StartProcess(target);
            return $"Запустил {entry.Id}";
        }

        host.StartProcess(target);
        return $"Запустил {target}";
    }
}

public static class PathOpener
{
    public static string Open(string raw, PathGuard paths, IShell shell)
    {
        var path = paths.ResolveSafe(raw);
        if (!Path.Exists(path))
        {
            return $"Не найдено: {path}";
        }

        shell.OpenFileOrFolder(path);
        return $"Открыл {path}";
    }
}
