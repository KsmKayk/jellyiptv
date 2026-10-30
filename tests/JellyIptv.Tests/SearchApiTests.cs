using System.Security.Claims;
using Jellyfin.Data.Entities;
using JellyIptv.Api;
using JellyIptv.Browse;
using JellyIptv.Tests.TestSupport;
using JellyIptv.Xtream;
using MediaBrowser.Common.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyIptv.Tests;

public class SearchApiTests
{
    [Fact]
    public void Open_response_routes_movies_to_details_and_series_to_list()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        var movie = SearchOpenResponse.For(new ItemKey(ItemKind.Movie, Id: 1), id);
        Assert.Equal("0f8fad5bd9cb469fa16570867728950e", movie.ItemId);
        Assert.Equal("details", movie.Route);

        Assert.Equal("list", SearchOpenResponse.For(new ItemKey(ItemKind.Series, Id: 1), id).Route);
    }

    [Fact]
    public void Reads_jellyfin_user_id_claim()
    {
        var id = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", id.ToString("N"))], "Custom"));

        Assert.Equal(id, JellyfinClaims.GetUserId(user));
        Assert.Equal(Guid.Empty, JellyfinClaims.GetUserId(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Theory]
    [InlineData("arg", 404)]
    [InlineData("nre", 404)]
    [InlineData("notfound", 404)]
    [InlineData("null", 404)]
    [InlineData("xtream", 503)]
    public async Task Open_maps_failures_to_friendly_errors(string failure, int status)
    {
        var browser = new ThrowingBrowser(failure);
        var controller = new SearchController(new CatalogSearch(new FakeXtreamApi(), new FixedConfig()), new IptvItemMaterializer(browser, TimeSpan.Zero), null!, NullLogger<SearchController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Open(new SearchOpenRequest { Key = "movie:1", CategoryId = "813" }, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(status, objectResult.StatusCode);
        var error = Assert.IsType<ErrorResponse>(objectResult.Value);
        if (status == 404)
        {
            Assert.Equal(SearchController.NotAvailableMessage, error.Message);
        }
    }

    [Fact]
    public async Task Open_rejects_invalid_keys()
    {
        var controller = new SearchController(new CatalogSearch(new FakeXtreamApi(), new FixedConfig()), new IptvItemMaterializer(new ThrowingBrowser("null"), TimeSpan.Zero), null!, NullLogger<SearchController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Open(new SearchOpenRequest { Key = "allmovie:1", CategoryId = "813" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private sealed class ThrowingBrowser(string failure) : IChannelBrowser
    {
        public Guid ItemId(string externalId, Type type) => Guid.NewGuid();

        public bool Exists(Guid itemId) => false;

        public Task BrowseAsync(Guid parentId, User? user, CancellationToken cancellationToken) => failure switch
        {
            "arg" => throw new ArgumentException("bad"),
            "nre" => throw new NullReferenceException(),
            "notfound" => throw new ResourceNotFoundException("gone"),
            "xtream" => throw new XtreamException("down"),
            _ => Task.CompletedTask,
        };
    }
}
