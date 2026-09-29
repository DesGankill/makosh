using Makosh.Core;

namespace Makosh.Tests;

public class DeviceRegistryTests
{
    [Fact]
    public void Empty_registry_lists_only_the_hub()
    {
        var devices = new DeviceRegistry();
        Assert.Equal("Сейчас никто не подключён, кроме этого хаба.", devices.ListText());
    }

    [Fact]
    public void Register_list_and_get()
    {
        var devices = new DeviceRegistry();
        devices.Register(new Device { DeviceId = "d1", Name = "Pixel", Kind = "android" });
        Assert.Equal("- Pixel (android, id=d1)", devices.ListText());
        Assert.Equal("Pixel", devices.Get("d1")?.Name);
    }

    [Fact]
    public void Register_same_id_updates_entry()
    {
        var devices = new DeviceRegistry();
        devices.Register(new Device { DeviceId = "d1", Name = "Old", Kind = "android" });
        devices.Register(new Device { DeviceId = "d1", Name = "New", Kind = "pc" });
        Assert.Equal("New", devices.Get("d1")?.Name);
        Assert.Equal("pc", devices.Get("d1")?.Kind);
        Assert.DoesNotContain("Old", devices.ListText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Find_by_id_name_kind_substring_and_alias()
    {
        var devices = new DeviceRegistry();
        devices.Register(new Device { DeviceId = "abc", Name = "Pixel 8", Kind = "android" });
        Assert.Equal("abc", devices.Find("abc")?.DeviceId);
        Assert.Equal("abc", devices.Find("pixel 8")?.DeviceId);
        Assert.Equal("abc", devices.Find("android")?.DeviceId);
        Assert.Equal("abc", devices.Find("Pixel")?.DeviceId);
        Assert.Equal("abc", devices.Find("телефон")?.DeviceId);
        Assert.Null(devices.Find("пк"));
    }

    [Fact]
    public void Drop_removes_device()
    {
        var devices = new DeviceRegistry();
        devices.Register(new Device { DeviceId = "d1", Name = "A", Kind = "pc" });
        devices.Drop("d1");
        Assert.Null(devices.Get("d1"));
        Assert.Equal("Сейчас никто не подключён, кроме этого хаба.", devices.ListText());
    }

    [Fact]
    public void Drop_old_session_does_not_remove_replaced_device()
    {
        var devices = new DeviceRegistry();
        var first = new Device { DeviceId = "d1", Name = "A", Kind = "pc", SessionId = Guid.NewGuid() };
        var second = new Device { DeviceId = "d1", Name = "B", Kind = "pc", SessionId = Guid.NewGuid() };
        devices.Register(first);
        devices.Register(second);
        Assert.False(devices.DropIfSession("d1", first.SessionId));
        Assert.Equal("B", devices.Get("d1")?.Name);
        Assert.True(devices.DropIfSession("d1", second.SessionId));
        Assert.Null(devices.Get("d1"));
    }
}
