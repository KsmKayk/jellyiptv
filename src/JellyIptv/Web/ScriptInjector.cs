using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Web;

/// <summary>Registers index.html transformation with the File Transformation plugin (via reflection, as it documents).</summary>
public sealed class ScriptInjector(ILogger<ScriptInjector> logger) : IHostedService
{
    private static readonly Guid TransformationId = Guid.Parse("8a0f7c52-3d1e-4b9a-9f6e-2c7d5b1a4e90");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var assembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);
            var register = assembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")?.GetMethod("RegisterTransformation");
            if (register is null)
            {
                logger.LogWarning("JellyIPTV: plugin File Transformation não encontrado; os botões de download não aparecerão no jellyfin-web.");
                return Task.CompletedTask;
            }

            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["id"] = TransformationId.ToString(),
                ["fileNamePattern"] = "index.html$",
                ["callbackAssembly"] = typeof(IndexHtmlTransformer).Assembly.FullName!,
                ["callbackClass"] = typeof(IndexHtmlTransformer).FullName!,
                ["callbackMethod"] = nameof(IndexHtmlTransformer.Transform),
            });

            // The parameter is Newtonsoft's JObject from File Transformation's load context: build it with its own Parse.
            var payloadType = register.GetParameters()[0].ParameterType;
            var payload = payloadType.GetMethod("Parse", [typeof(string)])!.Invoke(null, [json]);
            register.Invoke(null, [payload]);
            logger.LogInformation("JellyIPTV: script de download registrado no File Transformation.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JellyIPTV: falha ao registrar no File Transformation.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class TransformPayload
{
    public string? Contents { get; set; }
}

public static class IndexHtmlTransformer
{
    public const string ScriptTag = "<script defer src=\"../JellyIptv/ClientScript\"></script>";

    public static string Transform(TransformPayload payload)
    {
        var html = payload.Contents ?? string.Empty;
        if (html.Contains(ScriptTag, StringComparison.Ordinal))
        {
            return html;
        }

        var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? html + ScriptTag : html.Insert(index, ScriptTag);
    }
}
