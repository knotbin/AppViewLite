var hasBlazor = !!window.Blazor;

function applyPageFocus() {
    var focalPost = document.querySelector('.post-focal');
    if (focalPost) focalPost.scrollIntoView();
    else window.scrollTo({ top: 0, left: 0, behavior: 'instant' });

    if (!hasBlazor) { 
        document.querySelector('[autofocus]')?.focus();
    }
    
}

if (hasBlazor) {
    // https://github.com/dotnet/aspnetcore/issues/51338#issuecomment-1766578689
    Blazor.addEventListener('enhancedload', () => {
        applyPageFocus();
    });
}

applyPageFocus();
document.addEventListener('click', event => {
    var url = null;
    var t = event.target;
    while (t) {
        if (t.tagName == 'A' && t.href) {
            url = new URL(t.href);
            break;
        }
        t = t.parentNode;
    }
    if (!url) return;

    if ((document.querySelector('#components-reconnect-modal') || ((url.pathname == '/login' || url.pathname == '/logout') && url.host == window.location.host))) {
        window.location = url;
    } else if (!hasBlazor) { 
        fastNavigateTo(url.href);
        event.preventDefault();
    }

}, true);

/**@type {href: string, dateFetched: number, dom: HTMLElement, title: string}[] */
var recentPages = [];
var currentlyLoadedPage = location.href;
var applyPageId = 0;

async function applyPage(href, preferRefresh, scrollToTop) { 
    console.log('Load: ' + href);
    var token = ++applyPageId;
    if (currentlyLoadedPage == href || preferRefresh) { 
        recentPages = recentPages.filter(x => x.href != href);
    }
    
    var [main, title] = await fetchOrReusePageAsync(href, token);

    if (token != applyPageId) throw 'Superseded navigation.'
    
    var oldMain = document.querySelector('main');
    var page = oldMain.parentElement;
    oldMain.remove();
    page.appendChild(main);
    currentlyLoadedPage = href;
    document.title = title;
    if (scrollToTop) { 
        applyPageFocus();
    }

}

async function fetchOrReusePageAsync(href, token) { 
    
    var p = recentPages.filter(x => x.href == href)[0];
    if (p) {
        return [p.dom, p.title];
    } else { 
        var response = await fetch(href);
        if (response.status != 200) { 
            throw ('HTTP ' + response.status);
        }
        var temp = document.createElement('div');
        temp.innerHTML = await response.text();
        if (token != applyPageId) throw 'Superseded navigation.'
        var dom = temp.querySelector('main');
        var title = temp.querySelector('title').textContent;
        recentPages.push({ href: href, dom: dom, dateFetched: Date.now(), title: title})
        return [dom, title];
    }
}

function fastNavigateTo(href) { 
    if (href.startsWith('/')) href = location.origin + href;
    window.history.pushState(null, null, href);
    applyPage(href, true, true);
}


var currentlyOpenMenu = null;
var currentlyOpenMenuButton = null;
function closeCurrentMenu() { 
    if (!currentlyOpenMenu) return;
    currentlyOpenMenu.classList.remove('menu-visible')
    currentlyOpenMenu = null;
    currentlyOpenMenuButton = null;
}
if (!hasBlazor) {
    window.addEventListener('popstate', e => {
        applyPage(location.href, false);
    });
    
    recentPages.push({
        href: location.href,
        dateFetched: Date.now(),
        dom: document.querySelector('main'),
        title: document.title
    });
    window.addEventListener('scroll', async e => {
        if (document.documentElement.scrollTop <= 0) return;
        var remainingToBottom = document.documentElement.scrollTopMax - document.documentElement.scrollTop;
        if (remainingToBottom >= 500) return;
        var paginationButton = document.querySelector('.pagination-button');
        if (!paginationButton) return;
        if (paginationButton.querySelector('.spinner')) return;
        var oldList = document.querySelector('.main-paginated-list');


        paginationButton.classList.add('spinner-visible')
        paginationButton.insertAdjacentHTML('beforeend', '<div class="spinner"><svg height="100%" viewBox="0 0 32 32" width="100%"><circle cx="16" cy="16" fill="none" r="14" stroke-width="4" style="stroke: rgb(25, 118, 210); opacity: 0.2;"></circle><circle cx="16" cy="16" fill="none" r="14" stroke-width="4" style="stroke: rgb(25, 118, 210); stroke-dasharray: 80px; stroke-dashoffset: 60px;"></circle></svg></div>')
        var nextPage = await fetch(paginationButton.querySelector('a').href);
        if (nextPage.status != 200) throw ('HTTP ' + nextPage.status);
        var temp = document.createElement('div');
        temp.innerHTML = await nextPage.text();


        var newList = temp.querySelector('.main-paginated-list');
        var anyChildren = false;
        if (newList) {
            for (const child of newList.childNodes) {
                child.remove();
                if (child instanceof Element) anyChildren = true;
                oldList.appendChild(child);
            }
        }
        var newPagination = temp.querySelector('.pagination-button');
        if (!newPagination || !anyChildren) paginationButton.remove();
        else paginationButton.replaceWith(newPagination);
    });

    document.addEventListener('click', e => {
        
        var target = e.target;
        var actionButton = target.closest('.post-action-bar-button,[actionkind]');
        if (actionButton) { 
            var actionKind = actionButton.getAttribute('actionkind');
            if (actionKind) {
                closeCurrentMenu();
                console.log(actionKind);

                var postAction = postActions[actionKind];
                if (postAction) {
                    postAction.call(postActions,
                        getAncestorData(actionButton, 'postdid'),
                        getAncestorData(actionButton, 'postrkey'),
                        actionButton.closest('[data-postrkey]')
                    );
                    return;
                }
                
                var userAction = userActions[actionKind];
                if (userAction) {
                    userAction.call(postActions,
                        getAncestorData(actionButton, 'profiledid'),
                        getAncestorData(actionButton, 'followrkey'),
                        getAncestorData(actionButton, 'followsyou'),
                        actionButton.closest('[data-profiledid]')
                    );
                    return;
                }
            } else {
                if (actionButton == currentlyOpenMenuButton) closeCurrentMenu();
                else {
                    var prevMenu = actionButton.previousElementSibling;
                    if (prevMenu && prevMenu.classList.contains('menu')) {
                        closeCurrentMenu();
                        prevMenu.classList.add('menu-visible');

                        currentlyOpenMenuButton = actionButton;
                        currentlyOpenMenu = prevMenu;
                    }
                }
            }
        }
    });
    document.addEventListener('pointerdown', e => {
        var target = e.target;
        if (currentlyOpenMenu) { 
            if (currentlyOpenMenu.contains(target) || currentlyOpenMenuButton.contains(target)) return;
            closeCurrentMenu();
        }
        
        /*
        if (target.previousElementSibling?.classList?.contains('menu')) return;
        for (const menu of document.querySelectorAll('.menu-visible')) {
            if (menu.menuOpenedBy == target) continue;
            if (menu.contains(target)) continue;
            menu.classList.remove('menu-visible')
            menu.menuOpenedBy = null;
        }
        */
    });
}

function getAncestorData(target, name) { 

    while (target) { 
        var d = target.dataset[name];
        if (d) return d;
        target = target.parentElement;
    }
    throw 'Data attribute not found: ' + name
}


class ActionStateToggler { 
    constructor(actorCount, rkey, addRelationship, deleteRelationship, notifyChange) { 
        this.actorCount = actorCount;
        this.rkey = rkey;
        this.addRelationship = addRelationship;
        this.deleteRelationship = deleteRelationship;
        this.busy = false;
        this.haveRelationship = !!rkey;
        this.notifyChange = notifyChange;
    }

    raiseChangeNotification(){ 
        this.notifyChange(this.actorCount, this.haveRelationship);
    }
    async toggleIfNotBusyAsync() {
        if (this.busy) return;
        this.busy = true;
        var prevState = [this.haveRelationship, this.rkey, this.actorCount];
        try {
            this.haveRelationship = !this.haveRelationship;

            if (this.haveRelationship) {
                this.actorCount++;
                this.raiseChangeNotification();
                this.rkey = await this.addRelationship();
            } else { 
                if (this.actorCount > 0) { 
                    this.actorCount--;
                }
                this.raiseChangeNotification();
                await this.deleteRelationship(this.rkey);
            }
            this.raiseChangeNotification();
        } catch (e) {
            [this.haveRelationship, this.rkey, this.actorCount] = prevState;
            this.raiseChangeNotification();
            console.error(e);
        } finally { 
            this.busy = false;
        }
    }
}

function setPostStats(postElement, actorCount, kind, singular, plural) { 
    var stats = postElement.querySelector('.post-stats-' + kind + '-formatted');
    if (!stats) return;
    stats.classList.toggle('display-none', !actorCount);
    var b = stats.firstElementChild;
    var text = stats.lastChild;
    b.textContent = formatEngagementCount(actorCount);
    text.replaceWith(document.createTextNode(' ' + (actorCount == 1 ? singular : plural)));
    var allStats = postElement.querySelector('.post-focal-stats');
    allStats.classList.toggle('display-none', [...allStats.children].every(x => x.classList.contains('display-none')));
}
function setActionStats(postElement, actorCount, kind) { 
    postElement.querySelector('.post-action-bar-button-' + kind + ' span').textContent = actorCount ? formatEngagementCount(actorCount) : '';
}

var postActions = {
    toggleLike: async function (did, rkey, postElement) { 
        postElement.likeToggler ??= new ActionStateToggler(+postElement.dataset.likecount, +postElement.dataset.likerkey, async () => 'aaaaa', async (rkey) => { }, (count, have) => { 
            setPostStats(postElement, count, 'likes', 'like', 'likes');
            setActionStats(postElement, count, 'like');
            postElement.querySelector('.post-action-bar-button-like').classList.toggle('post-action-bar-button-checked', have);
        });
        postElement.likeToggler.toggleIfNotBusyAsync();
    },
    toggleRepost: async function (did, rkey, postElement) { 
        postElement.repostToggler ??= new ActionStateToggler(+postElement.dataset.repostcount, +postElement.dataset.repostrkey, async () => 'aaaaa', async (rkey) => { }, (count, have) => { 
            setPostStats(postElement, count, 'reposts', 'repost', 'reposts');
            setActionStats(postElement, count + +postElement.dataset.quotecount, 'repost');
            postElement.querySelector('.post-action-bar-button-repost').classList.toggle('post-action-bar-button-checked', have);
            postElement.querySelector('.post-toggle-repost-menu-item').textContent = have ? 'Undo repost' : 'Repost'
        });
        postElement.repostToggler.toggleIfNotBusyAsync();
    },
    composeReply: async function (did, rkey) { 
        fastNavigateTo(`/compose?replyDid=${did}&replyRkey=${rkey}`)
    },
    composeQuote: async function (did, rkey) { 
        fastNavigateTo(`/compose?quoteDid=${did}&quoteRkey=${rkey}`)
    },
    viewOnBluesky: async function (did, rkey) { 
        window.open(`https://bsky.app/profile/${did}/post/${rkey}`);
    },
    deletePost: async function (did, rkey, node) { 
    },
}

var userActions = {
    toggleFollow: async function (profiledid, followrkey, followsyou, postElement) { 
        postElement.followToggler ??= new ActionStateToggler(0, followrkey != '-' ? followrkey : null, async () => 'aaaaa', async (rkey) => { }, (count, have) => { 
            var btn = postElement;
            btn.textContent = have ? 'Unfollow' : +followsyou ? 'Follow back' : 'Follow';
            btn.classList.toggle('follow-button-unfollow', have);
        });
        postElement.followToggler.toggleIfNotBusyAsync();
    },
}

function formatTwoSignificantDigits(displayValue) { 
    var r = (Math.floor(displayValue * 10) / 10).toFixed(1);
    if (r.Length > 3)
        r = Math.floor(displayValue);
    return r;

}

function formatEngagementCount(value)
{
    if (value < 1_000)
    {
        // 1..999
        return value + '';
    }
    else if (value < 1_000_000)
    {
        // 1.0K..9.9K
        // 19K..999K
        return formatTwoSignificantDigits(value / 1_000.0) + "K";
    }
    else
    {
        // 1.0M..9.9M
        // 10M..1234567M
        return formatTwoSignificantDigits(value / 1_000_000.0) + "M";
    }

    
}
