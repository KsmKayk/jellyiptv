namespace JellyIptv.Configuration;

public interface IConfigProvider
{
    PluginConfiguration Current { get; }
}

public sealed class PluginConfigProvider : IConfigProvider
{
    public PluginConfiguration Current => Plugin.Instance?.Configuration ?? new PluginConfiguration();
}
