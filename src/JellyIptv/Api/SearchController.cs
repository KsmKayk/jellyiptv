using System.Security.Claims;
using JellyIptv.Browse;
using JellyIptv.Xtream;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyIptv.Api;

public sealed class SearchOpenRequest
{
    public string Key { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;
}

/// <param name="ItemId">Jellyfin item id ("N" format).</param>
/// <param name="Route">"details" (movie) or "list" (series folder).</param>
public sealed record SearchOpenResponse(string ItemId, string Route)
{
    public static SearchOpenResponse For(ItemKey key, Guid itemId) =>
        new(itemId.ToString("N"), key.Kind == ItemKind.Movie ? "details" : "list");
}

/// <summary>Jellyfin.Api isn't a NuGet package; mirrors Jellyfin.Api.Extensions.ClaimsPrincipalExtensions.GetUserId (10.10.7).</summary>
public static class JellyfinClaims
{
    /// <summary>Jellyfin.Api.Constants.InternalClaimTypes.UserId.</summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    public static Guid GetUserId(ClaimsPrincipal user)
    {
        var value = user.Claims.FirstOrDefault(c => c.Type.Equals(UserIdClaim, StringComparison.OrdinalIgnoreCase))?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}

[ApiController]
[Route("JellyIptv/Search")]
[Authorize]
public class SearchController(
    CatalogSearch search,
    IptvItemMaterializer materializer,
    IUserManager userManager,
    ILogger<SearchController> logger) : ControllerBase
{
    public const string NotAvailableMessage = "Item ainda não disponível no canal. Tente de novo mais tarde (o Jellyfin atualiza o canal a cada 3 h).";

    [HttpGet]
    public async Task<ActionResult<CatalogSearchResult>> Search([FromQuery] string? q, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        try
        {
            return await search.SearchAsync(q, limit ?? CatalogSearch.DefaultLimit, cancellationToken).ConfigureAwait(false);
        }
        catch (XtreamException ex)
        {
            return BadRequest(new ErrorResponse(ex.Message));
        }
    }

    [HttpPost("Open")]
    public async Task<ActionResult<SearchOpenResponse>> Open([FromBody] SearchOpenRequest request, CancellationToken cancellationToken)
    {
        if (!ItemKey.TryParse(request.Key, out var key) || key.InAll || key.Kind is not (ItemKind.Movie or ItemKind.Series)
            || string.IsNullOrWhiteSpace(request.CategoryId))
        {
            return BadRequest(new ErrorResponse("Item inválido."));
        }

        var userId = JellyfinClaims.GetUserId(User);
        var user = userId.Equals(Guid.Empty) ? null : userManager.GetUserById(userId);
        try
        {
            var itemId = await materializer.MaterializeAsync(key, request.CategoryId, user, cancellationToken).ConfigureAwait(false);
            return itemId is { } id
                ? SearchOpenResponse.For(key, id)
                : NotFound(new ErrorResponse(NotAvailableMessage));
        }
        catch (XtreamException ex)
        {
            logger.LogWarning(ex, "JellyIPTV: não foi possível abrir {Key}", request.Key);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse(ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException or ResourceNotFoundException)
        {
            // ChannelManager throws these when a folder of the chain vanished or isn't in its cache/database.
            logger.LogWarning(ex, "JellyIPTV: item {Key} não disponível no canal", request.Key);
            return NotFound(new ErrorResponse(NotAvailableMessage));
        }
    }
}
