namespace Makosh.Core;

public enum ToolAccess
{
    ReadOnly,
    Safe,
    ConfirmRequired,
    Blocked,
}

public static class ToolPermissions
{
    public static ToolAccess Level(string name) =>
        name switch
        {
            "recall" or "list_devices" or "list_files" => ToolAccess.ReadOnly,
            "look_screen" or "remember" => ToolAccess.Safe,
            "open_app" => ToolAccess.Safe,
            "open_path" or "send_file" or "type_text" or "press_hotkey" => ToolAccess.ConfirmRequired,
            _ => ToolAccess.Safe,
        };
}
