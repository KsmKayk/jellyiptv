using JellyIptv.Browse;
using JellyIptv.Configuration;
using JellyIptv.Downloads;
using JellyIptv.Xtream;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Api;

[ApiController]
[Route("JellyIptv")]
[Authorize]
public class JellyIptvController(
    IptvItemResolver resolver,
    DownloadPlanner planner,
    DownloadManager downloads,
    IXtreamApi api,
    IConfigProvider config,
    ILogger<JellyIptvController> logger) : ControllerBase
{
    private const string RequiresElevation = "RequiresElevation";

    private bool CanDownload => User.IsInRole("Administrator") || config.Current.AllowNonAdminDownloads;

    [HttpGet("ClientScript")]
    [AllowAnonymous]
    public ActionResult GetClientScript()
    {
        var stream = typeof(JellyIptvController).Assembly.GetManifestResourceStream("JellyIptv.Web.jellyiptv.js");
        return stream is null ? NotFound() : File(stream, "application/javascript; charset=utf-8");
    }

    [HttpGet("Items/{itemId:guid}")]
    public ActionResult<ItemInfoResponse> GetItemInfo(Guid itemId)
    {
        var kind = resolver.Resolve(itemId)?.Kind switch
        {
            ItemKind.Movie => "movie",
            ItemKind.Series => "series",
            ItemKind.Season => "season",
            ItemKind.Episode => "episode",
            _ => null,
        };
        return new ItemInfoResponse(kind, CanDownload);
    }

    [HttpPost("Download")]
    public async Task<ActionResult<DownloadResponse>> Download([FromBody] DownloadRequest request, CancellationToken cancellationToken)
    {
        if (!CanDownload)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("Sem permissão para baixar."));
        }

        var key = resolver.Resolve(request.ItemId);
        if (key is null)
        {
            return NotFound(new ErrorResponse("Item não pertence ao IPTV."));
        }

        try
        {
            var jobs = await planner.PlanAsync(key.Value, cancellationToken).ConfigureAwait(false);
            return new DownloadResponse(downloads.Enqueue(jobs), jobs.Count);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or XtreamException)
        {
            logger.LogWarning(ex, "JellyIPTV: não foi possível enfileirar {Key}", key.Value.Format());
            return BadRequest(new ErrorResponse(ex.Message));
        }
    }

    [HttpGet("Queue")]
    public ActionResult<QueueResponse> GetQueue()
    {
        if (!CanDownload)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("Sem permissão."));
        }

        return new QueueResponse(downloads.IsPaused, downloads.Snapshot().Select(QueueItemDto.From).ToList());
    }

    [HttpDelete("Queue/{id:guid}")]
    public ActionResult Cancel(Guid id)
    {
        if (!CanDownload)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return downloads.Cancel(id) ? NoContent() : NotFound();
    }

    [HttpPost("Queue/{id:guid}/Retry")]
    public ActionResult Retry(Guid id)
    {
        if (!CanDownload)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return downloads.Retry(id) ? NoContent() : NotFound();
    }

    [HttpDelete("Queue/Finished")]
    public ActionResult ClearFinished()
    {
        if (!CanDownload)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        downloads.ClearFinished();
        return NoContent();
    }

    [HttpGet("Categories")]
    [Authorize(Policy = RequiresElevation)]
    public async Task<ActionResult<CategoriesResponse>> GetCategories(CancellationToken cancellationToken)
    {
        try
        {
            var vod = await api.GetVodCategoriesAsync(cancellationToken).ConfigureAwait(false);
            var series = await api.GetSeriesCategoriesAsync(cancellationToken).ConfigureAwait(false);
            var live = await api.GetLiveCategoriesAsync(cancellationToken).ConfigureAwait(false);
            return new CategoriesResponse(
                vod.Select(c => new CategoryDto("vod:" + c.CategoryId, c.CategoryName)).ToList(),
                series.Select(c => new CategoryDto("series:" + c.CategoryId, c.CategoryName)).ToList(),
                live.Select(c => new CategoryDto("live:" + c.CategoryId, c.CategoryName)).ToList());
        }
        catch (XtreamException ex)
        {
            return BadRequest(new ErrorResponse(ex.Message));
        }
    }

    [HttpPost("TestConnection")]
    [Authorize(Policy = RequiresElevation)]
    public async Task<ActionResult<TestConnectionResponse>> TestConnection([FromBody] TestConnectionRequest request, CancellationToken cancellationToken)
    {
        var credentials = XtreamCredentials.From(new PluginConfiguration
        {
            BaseUrl = request.BaseUrl,
            Username = request.Username,
            Password = request.Password,
            UserAgent = request.UserAgent,
        });
        if (!credentials.IsConfigured)
        {
            return new TestConnectionResponse(false, "Preencha URL, usuário e senha.", null, null);
        }

        try
        {
            return TestConnectionResponse.FromUserInfo(await api.GetUserInfoAsync(credentials, cancellationToken).ConfigureAwait(false));
        }
        catch (XtreamException ex)
        {
            return new TestConnectionResponse(false, ex.Message, null, null);
        }
    }
}
