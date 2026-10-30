(function () {
    'use strict';
    if (window.__jellyIptvLoaded) {
        return;
    }
    window.__jellyIptvLoaded = true;

    var BUTTON_ID = 'jellyiptv-download';
    var LABELS = {
        movie: 'Baixar filme',
        series: 'Baixar série inteira',
        season: 'Baixar temporada',
        episode: 'Baixar episódio'
    };
    var lastId = null;

    // Channel folders open in #/list?parentId=..., media items in #/details?id=...
    function currentItemId() {
        var hash = window.location.hash || '';
        var q = hash.indexOf('?');
        var params = new URLSearchParams(q >= 0 ? hash.substring(q + 1) : '');
        if (/^#!?\/details/.test(hash)) {
            return params.get('id');
        }
        if (/^#!?\/list/.test(hash)) {
            return params.get('parentId');
        }
        return null;
    }

    function getButton() {
        var button = document.getElementById(BUTTON_ID);
        if (!button) {
            button = document.createElement('button');
            button.id = BUTTON_ID;
            button.type = 'button';
            button.style.cssText = 'position:fixed;right:24px;bottom:24px;z-index:9999;padding:12px 20px;'
                + 'border:none;border-radius:24px;background:#00a4dc;color:#fff;font-size:15px;font-weight:600;'
                + 'box-shadow:0 4px 12px rgba(0,0,0,.4);cursor:pointer;display:none;';
            button.addEventListener('click', onClick);
            document.body.appendChild(button);
        }
        return button;
    }

    function hide() {
        var button = document.getElementById(BUTTON_ID);
        if (button) {
            button.style.display = 'none';
        }
    }

    function toast(message) {
        var el = document.createElement('div');
        el.textContent = message;
        el.style.cssText = 'position:fixed;left:50%;bottom:90px;transform:translateX(-50%);z-index:10000;'
            + 'background:rgba(0,0,0,.85);color:#fff;padding:10px 18px;border-radius:6px;font-size:14px;';
        document.body.appendChild(el);
        setTimeout(function () { el.remove(); }, 4000);
    }

    function check() {
        var id = currentItemId();
        if (id === lastId) {
            return;
        }
        lastId = id;
        if (!id || !window.ApiClient || !/^[0-9a-f]{32}$|^[0-9a-f-]{36}$/i.test(id)) {
            hide();
            return;
        }
        window.ApiClient.getJSON(window.ApiClient.getUrl('JellyIptv/Items/' + id)).then(function (info) {
            if (lastId !== id) {
                return;
            }
            if (!info || !info.Kind || !info.CanDownload) {
                hide();
                return;
            }
            var button = getButton();
            button.textContent = '\u2B07 ' + LABELS[info.Kind];
            button.setAttribute('data-item-id', id);
            button.disabled = false;
            button.style.display = 'block';
        }, hide);
    }

    function onClick(event) {
        var button = event.currentTarget;
        button.disabled = true;
        window.ApiClient.ajax({
            type: 'POST',
            url: window.ApiClient.getUrl('JellyIptv/Download'),
            data: JSON.stringify({ ItemId: button.getAttribute('data-item-id') }),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (result) {
            var skipped = result.Total - result.Added;
            toast(result.Added + ' item(ns) adicionado(s) à fila de downloads'
                + (skipped > 0 ? ' (' + skipped + ' já estavam na fila)' : ''));
        }, function (error) {
            var status = error && error.status;
            toast(status === 403 ? 'Sem permissão para baixar.' : 'Erro ao adicionar à fila de downloads.');
        }).then(function () {
            button.disabled = false;
        });
    }


    // --- IPTV catalog results on Jellyfin's search page (#/search.html?query=...) ---
    // The search page keeps the query in the hash (replaceState, so no hashchange): poll it.
    var SEARCH_ID = 'jellyiptv-search';
    var searchQuery = null;
    var searchSection = null;
    var searchTimer = null;
    var searchSeq = 0;
    var opening = false;
    var openSeq = 0;
    var openTimer = null;
    var OPEN_TIMEOUT_MS = 60000;

    function resetOpening() {
        clearTimeout(openTimer);
        openTimer = null;
        opening = false;
        openSeq++;
    }

    function searchRouteQuery() {
        var hash = window.location.hash || '';
        if (!/^#!?\/search(\.html)?(\?|$)/.test(hash)) {
            return null;
        }
        var q = hash.indexOf('?');
        var params = new URLSearchParams(q >= 0 ? hash.substring(q + 1) : '');
        return (params.get('query') || '').trim();
    }

    function removeSearch() {
        if (searchSection) {
            searchSection.remove();
        }
        searchSection = null;
    }

    function attachSearch(section) {
        var page = document.querySelector('#searchPage:not(.hide)') || document.querySelector('.page:not(.hide)');
        if (!page) {
            return false;
        }
        var fields = page.querySelector('.searchFields');
        if (fields && fields.parentNode) {
            fields.parentNode.insertBefore(section, fields.nextSibling);
        } else {
            page.insertBefore(section, page.firstChild);
        }
        return true;
    }

    function checkSearch() {
        var query = searchRouteQuery();
        if (query === null || query.length < 2) {
            if (searchQuery !== null) {
                clearTimeout(searchTimer);
                searchQuery = null;
                searchSeq++;
                removeSearch();
            }
            if (query === null && opening) {
                resetOpening();
            }
            return;
        }
        if (query === searchQuery) {
            if (searchSection && !searchSection.isConnected) {
                attachSearch(searchSection);
            }
            return;
        }
        // Keep the previous rows on screen until the new results arrive (no flicker).
        searchQuery = query;
        searchSeq++;
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () { runSearch(query); }, 400);
    }

    function runSearch(query) {
        if (!window.ApiClient) {
            return;
        }
        var seq = ++searchSeq;
        window.ApiClient.getJSON(window.ApiClient.getUrl('JellyIptv/Search', { q: query, limit: 24 })).then(function (result) {
            if (seq !== searchSeq) {
                return;
            }
            var movies = (result && result.Movies) || [];
            var series = (result && result.Series) || [];
            if (!movies.length && !series.length) {
                removeSearch();
                return;
            }
            var section = document.createElement('div');
            section.id = SEARCH_ID;
            section.className = 'padded-left padded-right';
            section.style.cssText = 'padding-top:1em;';
            if (movies.length) {
                section.appendChild(searchRow('IPTV \u2014 Filmes', movies));
            }
            if (series.length) {
                section.appendChild(searchRow('IPTV \u2014 S\u00e9ries', series));
            }
            var previous = searchSection;
            searchSection = section;
            if (previous && previous.isConnected) {
                previous.parentNode.replaceChild(section, previous);
            } else {
                if (previous) {
                    previous.remove();
                }
                attachSearch(section);
            }
        }, function () {
            // Keep whatever is on screen; the next query change retries.
        });
    }

    function searchRow(title, items) {
        var wrap = document.createElement('div');
        wrap.style.cssText = 'margin-bottom:1.2em;';
        var heading = document.createElement('h2');
        heading.textContent = title;
        heading.style.cssText = 'font-size:1.2em;font-weight:600;margin:0 0 .5em;';
        var scroller = document.createElement('div');
        scroller.style.cssText = 'display:flex;gap:12px;overflow-x:auto;padding:4px 0 10px;';
        items.forEach(function (item) {
            scroller.appendChild(searchCard(item));
        });
        wrap.appendChild(heading);
        wrap.appendChild(scroller);
        return wrap;
    }

    function searchCard(item) {
        var card = document.createElement('button');
        card.type = 'button';
        card.title = String(item.Name || '');
        card.style.cssText = 'position:relative;flex:0 0 auto;width:140px;padding:0;border:none;background:none;'
            + 'color:inherit;text-align:left;cursor:pointer;font:inherit;';
        var poster = document.createElement('div');
        poster.style.cssText = 'width:140px;height:210px;border-radius:6px;overflow:hidden;background:#333;';
        var url = String(item.ImageUrl || '');
        if (/^https?:\/\//i.test(url)) {
            var img = document.createElement('img');
            img.src = url;
            img.alt = '';
            img.loading = 'lazy';
            img.referrerPolicy = 'no-referrer';
            img.style.cssText = 'width:100%;height:100%;object-fit:cover;display:block;';
            img.onerror = function () { img.remove(); };
            poster.appendChild(img);
        }
        var name = document.createElement('div');
        name.textContent = String(item.Name || '');
        name.style.cssText = 'margin-top:6px;font-size:.9em;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
        card.appendChild(poster);
        card.appendChild(name);
        if (item.Year) {
            var year = document.createElement('div');
            year.textContent = String(item.Year);
            year.style.cssText = 'font-size:.8em;opacity:.7;';
            card.appendChild(year);
        }
        card.addEventListener('click', function () { openSearchItem(item, card); });
        return card;
    }

    function openSearchItem(item, card) {
        if (opening || !window.ApiClient) {
            return;
        }
        opening = true;
        var seq = ++openSeq;
        clearTimeout(openTimer);
        openTimer = setTimeout(function () {
            if (seq === openSeq) {
                resetOpening();
                status.remove();
            }
        }, OPEN_TIMEOUT_MS);
        var status = document.createElement('div');
        status.textContent = 'Abrindo\u2026';
        status.style.cssText = 'position:absolute;left:0;top:0;width:140px;height:210px;border-radius:6px;display:flex;'
            + 'align-items:center;justify-content:center;background:rgba(0,0,0,.65);color:#fff;font-weight:600;';
        card.appendChild(status);
        window.ApiClient.ajax({
            type: 'POST',
            url: window.ApiClient.getUrl('JellyIptv/Search/Open'),
            data: JSON.stringify({ Key: item.Key, CategoryId: item.CategoryId }),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (result) {
            if (seq !== openSeq) {
                return;
            }
            var serverId = encodeURIComponent(window.ApiClient.serverId());
            var id = encodeURIComponent(result.ItemId);
            window.location.hash = result.Route === 'list'
                ? '#/list.html?parentId=' + id + '&serverId=' + serverId
                : '#/details?id=' + id + '&serverId=' + serverId;
        }, function (error) {
            if (seq !== openSeq) {
                return;
            }
            var code = error && error.status;
            toast(code === 404 ? 'Item n\u00e3o encontrado no canal IPTV.' : 'Erro ao abrir o item IPTV.');
        }).then(function () {
            if (seq === openSeq) {
                resetOpening();
            }
            status.remove();
        });
    }

    window.addEventListener('hashchange', check);
    window.addEventListener('hashchange', checkSearch);
    setInterval(check, 1000);
    setInterval(checkSearch, 500);
    check();
    checkSearch();
})();
