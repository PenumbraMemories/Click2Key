using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Click2Key.Models;

namespace Click2Key.Services;

public sealed class PortableSettings
{
    public Key ToggleKey { get; set; } = Key.Tab;
    public bool DarkMode { get; set; }
    public List<BindingRule> Rules { get; set; } = [];
    public List<SwapRule> Swaps { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static string PathName => Path.Combine(AppContext.BaseDirectory, "猫玖-settings.json");

    public static PortableSettings? Load()
    {
        if (!File.Exists(PathName)) return null;
        return JsonSerializer.Deserialize<PortableSettings>(File.ReadAllText(PathName), Options);
    }

    public static void Save(PortableSettings settings)
    {
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, PathName, true);
    }
}
