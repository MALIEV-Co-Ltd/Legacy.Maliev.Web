(function (root, factory) {
    'use strict';
    const search = factory();
    if (typeof module === 'object' && module.exports) module.exports = search;
    if (root.document) search.initialize(root.document);
})(typeof window === 'undefined' ? globalThis : window, function () {
    'use strict';

    function normalizeQuery(value) {
        return String(value ?? '').normalize('NFKC').toLocaleLowerCase().replace(/\s+/gu, ' ').trim();
    }

    function searchSections(sections, query, limit = 8) {
        const normalized = normalizeQuery(query);
        if (!normalized) return { total: 0, items: [] };
        const terms = [...new Set(normalized.split(' '))];
        const matches = [];
        sections.forEach(section => {
            const title = normalizeQuery(section.title);
            const article = normalizeQuery(section.articleTitle);
            const aliases = normalizeQuery(section.aliases);
            const text = String(section.text ?? '').normalize('NFKC').replace(/\s+/gu, ' ').trim();
            const body = normalizeQuery(text);
            if (!terms.every(term => [title, article, aliases, body].some(field => field.includes(term)))) return;
            const score = terms.reduce((sum, term) => sum + (title.includes(term) ? 150 : 0)
                + (aliases.includes(term) ? 80 : 0) + (article.includes(term) ? 40 : 0) + (body.includes(term) ? 1 : 0), 0);
            const positions = terms.map(term => body.indexOf(term)).filter(position => position >= 0);
            const start = Math.max(0, (positions.length ? Math.min(...positions) : 0) - 45);
            const snippet = (start ? '…' : '') + text.slice(start, start + 180) + (start + 180 < text.length ? '…' : '');
            matches.push({ ...section, snippet, score });
        });
        matches.sort((left, right) => right.score - left.score);
        return { total: matches.length, items: matches.slice(0, Math.max(0, limit)) };
    }

    function initialize(document) {
        const container = document.querySelector('[data-knowledge-search]');
        if (!container || container.dataset.searchReady === 'true') return;
        const view = document.defaultView;
        const input = container.querySelector('[data-knowledge-search-input]');
        const clear = container.querySelector('[data-knowledge-search-clear]');
        const results = container.querySelector('[data-knowledge-search-results]');
        const status = container.querySelector('[data-knowledge-search-status]');
        const retry = container.querySelector('[data-knowledge-search-retry]');
        const browse = document.querySelector('[data-knowledge-browse]');
        if (!input || !clear || !results || !status || !retry || !browse) return;
        container.dataset.searchReady = 'true';
        container.hidden = false;

        const language = document.documentElement.lang;
        const urls = new Map();
        browse.querySelectorAll('.workspace-nav-link').forEach(link => {
            const url = new URL(link.href, view.location.href);
            if (url.origin !== view.location.origin || !url.pathname.startsWith('/knowledges/')
                || url.pathname.replace(/\/+$/u, '') === '/knowledges') return;
            url.searchParams.set('culture', language);
            url.hash = '';
            urls.set(url.href, link.dataset.knowledgeSearchTitle || '');
        });
        const loaded = new Map();
        let loading = null;
        let controller = null;
        let debounce = null;
        let revision = 0;
        let suspended = false;

        function extract(article, href, alias) {
            if (!article) throw new Error('Missing knowledge article');
            const articleTitle = article.querySelector('h1')?.textContent.trim() || '';
            if (!articleTitle) throw new Error('Missing article title');
            const introduction = article.cloneNode(true);
            introduction.querySelectorAll('section, .docs-toc, .docs-source, .maliev-actions').forEach(node => node.remove());
            const entries = [{ href, title: articleTitle, articleTitle, aliases: alias, text: introduction.textContent }];
            article.querySelectorAll('section').forEach(section => {
                const heading = section.querySelector(':scope > h2');
                const anchor = section.id || heading?.id;
                if (!heading || !anchor) return;
                const copy = section.cloneNode(true);
                copy.querySelectorAll('.docs-source, .docs-toc, .knowledge-directory-list').forEach(node => node.remove());
                entries.push({ href: href + '#' + encodeURIComponent(anchor), title: heading.textContent.trim(),
                    articleTitle, aliases: alias, text: copy.textContent });
            });
            return entries;
        }

        async function loadIndex() {
            if (loading) return loading;
            controller = new view.AbortController();
            const activeController = controller;
            const timeout = view.setTimeout(() => activeController.abort(), 30000);
            let next = 0;
            const pending = [...urls].filter(([href]) => !loaded.has(href));
            loading = (async () => {
                try {
                    await Promise.all(Array.from({ length: Math.min(3, pending.length) }, async () => {
                        while (next < pending.length) {
                            if (activeController.signal.aborted || suspended || !container.isConnected) throw new Error('Index cancelled');
                            const [href, alias] = pending[next++];
                            const currentPath = view.location.pathname.replace(/\/$/u, '').toLowerCase();
                            if (new URL(href).pathname.replace(/\/$/u, '').toLowerCase() === currentPath) {
                                loaded.set(href, extract(document.querySelector('.knowledge-document'), href, alias));
                                continue;
                            }
                            const response = await view.fetch(href, { credentials: 'same-origin',
                                headers: { Accept: 'text/html' }, redirect: 'error', signal: activeController.signal });
                            if (!response.ok) throw new Error('Article unavailable');
                            // Template content stays inert: indexing must not run scripts or load article images.
                            const template = document.createElement('template');
                            const html = await response.text();
                            if (activeController.signal.aborted || suspended || !container.isConnected) throw new Error('Index cancelled');
                            template.innerHTML = html;
                            loaded.set(href, extract(template.content.querySelector('.knowledge-document'), href, alias));
                        }
                    }));
                    return [...loaded.values()].flat();
                } catch (error) {
                    activeController.abort();
                    throw error;
                } finally {
                    view.clearTimeout(timeout);
                    if (controller === activeController) {
                        loading = null;
                        controller = null;
                    }
                }
            })();
            return loading;
        }

        function reset() {
            browse.hidden = false;
            results.hidden = true;
            results.replaceChildren();
            retry.hidden = true;
            clear.hidden = !input.value;
            status.hidden = !input.value;
            status.textContent = input.value ? container.dataset.searchShort : '';
        }

        async function run() {
            if (suspended || !container.isConnected) return;
            const activeRevision = revision;
            if (normalizeQuery(input.value).length < 2) { reset(); return; }
            browse.hidden = true;
            clear.hidden = false;
            retry.hidden = true;
            results.hidden = true;
            status.hidden = false;
            status.textContent = container.dataset.searchLoading;
            try {
                const sections = await loadIndex();
                if (suspended || !container.isConnected || activeRevision !== revision) return;
                if (normalizeQuery(input.value).length < 2) { reset(); return; }
                const matches = searchSections(sections, input.value);
                results.replaceChildren();
                matches.items.forEach(match => {
                    const item = document.createElement('li');
                    const link = document.createElement('a');
                    link.href = match.href;
                    const title = document.createElement('strong');
                    title.textContent = match.title;
                    link.append(title);
                    if (match.articleTitle !== match.title) {
                        const article = document.createElement('span');
                        article.className = 'knowledge-search-article';
                        article.textContent = match.articleTitle;
                        link.append(article);
                    }
                    const excerpt = document.createElement('p');
                    excerpt.textContent = match.snippet;
                    link.append(excerpt);
                    item.append(link);
                    results.append(item);
                });
                status.textContent = matches.total > matches.items.length
                    ? container.dataset.searchLimited.replace('{0}', matches.items.length).replace('{1}', matches.total)
                    : matches.total ? container.dataset.searchCount.replace('{0}', matches.total) : container.dataset.searchEmpty;
                results.hidden = matches.items.length === 0;
            } catch (_) {
                if (suspended || !container.isConnected || activeRevision !== revision) return;
                if (normalizeQuery(input.value).length < 2) { reset(); return; }
                status.textContent = container.dataset.searchError;
                retry.hidden = false;
            }
        }

        input.addEventListener('input', () => {
            revision++;
            view.clearTimeout(debounce);
            if (normalizeQuery(input.value).length < 2) reset();
            else debounce = view.setTimeout(run, 160);
        });
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                results.querySelector('a')?.focus();
            }
        });
        clear.addEventListener('click', () => { revision++; input.value = ''; view.clearTimeout(debounce); reset(); input.focus(); });
        retry.addEventListener('click', run);
        // BFCache keeps this document and its listeners alive. Abort the old generation,
        // prevent detached/stale completions, then resume only from the restored query.
        view.addEventListener('pagehide', () => {
            suspended = true;
            revision++;
            view.clearTimeout(debounce);
            controller?.abort();
            controller = null;
            loading = null;
        });
        view.addEventListener('pageshow', () => {
            if (!suspended) return;
            suspended = false;
            reset();
            if (normalizeQuery(input.value).length >= 2) debounce = view.setTimeout(run, 160);
        });
        reset();
    }

    return { normalizeQuery, searchSections, initialize };
});
