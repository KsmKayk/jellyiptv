using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;

namespace JellyIptv.Browse;

/// <summary>The bits of Jellyfin the materializer needs (abstracted for tests).</summary>
public interface IChannelBrowser
{
    Guid ItemId(string externalId, Type type);

    bool Exists(Guid itemId);

    /// <summary>Lists a channel folder through ChannelManager, which creates/updates its children in the database.</summary>
    /// <param name="parentId">Folder item id, or <see cref="Guid.Empty"/> for the channel root.</param>
    Task BrowseAsync(Guid parentId, User? user, CancellationToken cancellationToken);
}

public sealed class JellyfinChannelBrowser(ILibraryManager libraryManager, IChannelManager channelManager) : IChannelBrowser
{
    private readonly Lazy<Guid> _channelId = new(() => IptvIds.ChannelId(libraryManager));

    public Guid ItemId(string externalId, Type type) => IptvIds.ItemId(libraryManager, externalId, type);

    public bool Exists(Guid itemId) => libraryManager.GetItemById(itemId) is not null;

    public async Task BrowseAsync(Guid parentId, User? user, CancellationToken cancellationToken)
    {
        // ChannelManager.GetChannelItemsInternal: ChannelIds[0] selects the channel; empty ParentId = channel root,
        // otherwise the folder item is loaded by id and its ExternalId is passed to IChannel.GetChannelItems.
        var query = new InternalItemsQuery(user)
        {
            ChannelIds = [_channelId.Value],
            ParentId = parentId,
            EnableTotalRecordCount = false,
        };
        await channelManager.GetChannelItemsInternal(query, new Progress<double>(), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Makes sure a catalog movie/series exists as a Jellyfin channel item and returns its id.</summary>
public sealed class IptvItemMaterializer
{
    private const int ExistsRetries = 10;

    private readonly IChannelBrowser _browser;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IptvItemMaterializer(IChannelBrowser browser)
        : this(browser, TimeSpan.FromMilliseconds(300))
    {
    }

    internal IptvItemMaterializer(IChannelBrowser browser, TimeSpan retryDelay)
    {
        _browser = browser;
        _retryDelay = retryDelay;
    }

    public async Task<Guid?> MaterializeAsync(ItemKey key, string categoryId, User? user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            throw new ArgumentException("Categoria não informada.", nameof(categoryId));
        }

        var (root, category, type) = key switch
        {
            { InAll: true } => throw new ArgumentException("Use a chave da categoria.", nameof(key)),
            { Kind: ItemKind.Movie } => (ItemKind.VodRoot, new ItemKey(ItemKind.VodCategory, CategoryId: categoryId), typeof(Movie)),
            { Kind: ItemKind.Series } => (ItemKind.SeriesRoot, new ItemKey(ItemKind.SeriesCategory, CategoryId: categoryId), typeof(Folder)),
            _ => throw new ArgumentException($"'{key.Format()}' não pode ser aberto.", nameof(key)),
        };

        // Channel root -> "vod"/"series" -> category folder -> item. Each folder is a ChannelFolderType.Container => Folder.
        Guid[] chain =
        [
            Guid.Empty,
            _browser.ItemId(new ItemKey(root).Format(), typeof(Folder)),
            _browser.ItemId(category.Format(), typeof(Folder)),
            _browser.ItemId(key.Format(), type),
        ];
        var target = chain[^1];
        if (_browser.Exists(target))
        {
            return target;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await MaterializeLockedAsync(chain, user).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Guid?> MaterializeLockedAsync(Guid[] chain, User? user)
    {
        var target = chain[^1];
        if (_browser.Exists(target))
        {
            return target;
        }

        var start = 0;
        for (var i = chain.Length - 2; i >= 1; i--)
        {
            if (_browser.Exists(chain[i]))
            {
                start = i;
                break;
            }
        }

        for (var i = start; i < chain.Length - 1; i++)
        {
            // Not the request token: an aborted browse would leave the folder half-created behind Jellyfin's 3 h cache.
            await _browser.BrowseAsync(chain[i], user, CancellationToken.None).ConfigureAwait(false);
            if (!await ExistsEventuallyAsync(chain[i + 1]).ConfigureAwait(false))
            {
                return null;
            }
        }

        return target;
    }

    /// <summary>
    /// ChannelManager creates the items after releasing its lock, so a browse that hit its fresh cache
    /// (another request is still creating them) returns before they exist.
    /// </summary>
    private async Task<bool> ExistsEventuallyAsync(Guid itemId)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_browser.Exists(itemId))
            {
                return true;
            }

            if (attempt == ExistsRetries)
            {
                return false;
            }

            await Task.Delay(_retryDelay).ConfigureAwait(false);
        }
    }
}
