using System.Text.Json;
using JellyIptv.Xtream;

namespace JellyIptv.Tests.TestSupport;

internal static class Fixtures
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static Stream Open(string name) => File.OpenRead(PathOf(name));

    public static T Load<T>(string name) => JsonSerializer.Deserialize<T>(Text(name), XtreamJson.Options)!;
}
