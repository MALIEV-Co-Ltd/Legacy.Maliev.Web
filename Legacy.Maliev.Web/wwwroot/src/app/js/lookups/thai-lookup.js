(() => {
    'use strict';
    if (window.malievThaiLookupInitialized) return;
    window.malievThaiLookupInitialized = true;
    const live = new Map();
    const initialize = root => {
        if (root.dataset.lookupBound) return;
        root.dataset.lookupBound = 'true';
        const fields = JSON.parse(root.dataset.lookupFields);
        const messages = JSON.parse(root.dataset.messages);
        const field = name => document.getElementById(fields[name]);
        const query = root.querySelector('[data-lookup-query]');
        const list = root.querySelector('[data-lookup-results]');
        const status = root.querySelector('[data-lookup-status]');
        const more = root.querySelector('[data-lookup-more]');
        const preview = root.querySelector('[data-lookup-preview]');
        const detail = root.querySelector('[data-lookup-detail]');
        const replace = root.querySelector('[data-lookup-replace]');
        const unconstrained = root.querySelector('[data-lookup-unconstrained]');
        const company = root.dataset.lookupKind === 'company';
        const language = root.dataset.language;
        let controller, timer, generation = 0, rows = [], active = -1, selected, resolution, cursor;
        let codes = {}, applying = false;
        const say = key => { status.textContent = messages[key] || ''; };
        const editable = element => element && !element.disabled && !element.readOnly;
        const name = item => language === 'th' ? item?.nameTh : item?.nameEn || item?.nameTh;
        const isThai = () => !field('country') || field('country').value === root.dataset.thaiCountryValue;
        const set = (key, value) => {
            const element = field(key);
            if (!editable(element) || value == null) return;
            element.value = value;
            element.dispatchEvent(new Event('input', { bubbles: true }));
            element.dispatchEvent(new Event('change', { bubbles: true }));
        };
        const stop = () => {
            generation++; clearTimeout(timer); controller?.abort();
            root.removeAttribute('aria-busy');
        };
        const close = () => {
            list.replaceChildren(); rows = []; active = -1; cursor = null;
            more.hidden = true; preview.hidden = true;
            if (unconstrained) unconstrained.hidden = true;
            query.setAttribute('aria-expanded', 'false'); query.removeAttribute('aria-activedescendant');
        };
        const constraints = () => ({ ...codes,
            postcode: /^[0-9๐-๙]{5}$/.test(field('postcode')?.value || '') ? field('postcode').value : undefined });
        const label = item => company ? [item.nameTh, item.nameEn, item.taxId].filter(Boolean).join(' · ')
            : [name(item.subdistrict), name(item.district), name(item.province), item.postcode].filter(Boolean).join(' · ');
        const review = item => {
            selected = item; preview.hidden = false; replace.checked = company;
            detail.value = company ? (language === 'th' ? item.nameTh : item.nameEn) || item.nameTh || item.nameEn || ''
                : field('detail')?.value || resolution?.detailText || '';
            if (company) root.querySelector('[data-lookup-tax]').value = item.taxId || '';
            root.querySelector('[data-lookup-provenance]').textContent = company
                ? [item.sourceUrl, item.retrievedAt].filter(Boolean).join(' · ') : '';
            detail.focus();
        };
        const render = items => {
            rows = items; active = -1; list.replaceChildren();
            query.removeAttribute('aria-activedescendant');
            rows.forEach((item, index) => {
                const li = document.createElement('li'); li.setAttribute('role', 'presentation');
                const button = document.createElement('button'); button.type = 'button';
                button.id = list.id + '-' + index; button.setAttribute('role', 'option');
                button.setAttribute('aria-selected', 'false'); button.textContent = label(item);
                button.addEventListener('click', () => review(item)); li.append(button); list.append(li);
            });
            query.setAttribute('aria-expanded', rows.length ? 'true' : 'false');
        };
        const request = async (path, body, append = false) => {
            stop(); const version = generation; controller = new AbortController();
            const token = Array.from(root.closest('form')?.querySelectorAll('input') || [])
                .find(input => input.name === root.dataset.antiforgeryField)?.value;
            if (!token) { say('unavailable'); return; }
            const currentController = controller;
            const timeout = setTimeout(() => currentController.abort(), 10000);
            say('loading'); root.setAttribute('aria-busy', 'true');
            try {
                const response = await fetch('/lookups/' + path, { method: 'POST', credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', [root.dataset.antiforgeryHeader]: token },
                    body: JSON.stringify(body), signal: controller.signal, cache: 'no-store' });
                if (version !== generation) return;
                if (!response.ok) { close(); say(response.status === 422 ? 'unsupported' : response.status === 400 ? 'invalid' : 'unavailable'); return; }
                const data = await response.json();
                if (version !== generation) return;
                if (path.endsWith('/resolve')) {
                    resolution = data;
                    if (data.outcome === 'conflict') {
                        close(); say('conflict');
                        if (unconstrained && Object.values(constraints()).some(Boolean)) unconstrained.hidden = false;
                        return;
                    }
                    render(data.candidates || []); more.hidden = true;
                    say(data.hasMore ? 'narrow' : rows.length ? 'choose' : 'empty');
                } else {
                    resolution = null;
                    if (company && ['unavailable', 'unsupported'].includes(data.outcome)) { close(); say(data.outcome); return; }
                    render(append ? rows.concat(data.items || []) : data.items || []);
                    cursor = data.nextCursor; more.hidden = company || !data.hasMore || !cursor;
                    say(data.hasMore ? 'narrow' : rows.length ? 'choose' : 'empty');
                }
            } catch (error) {
                if (version === generation) { close(); say('unavailable'); }
            } finally {
                clearTimeout(timeout); if (version === generation) root.removeAttribute('aria-busy');
            }
        };
        const search = (append = false) => {
            if (!company && !isThai()) { stop(); close(); say('country'); return; }
            const raw = query.value.trim();
            const postcodeFirst = !company && /^[0-9๐-๙]{5}$/.test(raw);
            const q = postcodeFirst ? '' : raw;
            const filters = { ...constraints(), ...(postcodeFirst ? { postcode: raw } : {}) };
            if (company && q.length < 2 || !company && !q && !Object.values(filters).some(Boolean)) { stop(); close(); say(''); return; }
            preview.hidden = true;
            void request(company ? 'companies/search' : 'addresses/search', company
                ? { q, language, queryType: 'name', limit: 20 }
                : { q, constraints: filters, limit: 20, cursor: append ? cursor : undefined }, append);
        };
        query.addEventListener('input', () => { stop(); close(); timer = setTimeout(() => search(), 300); });
        query.addEventListener('keydown', event => {
            if (event.key === 'Escape') { stop(); close(); return; }
            if (event.key === 'Enter') {
                event.preventDefault();
                if (active >= 0 && rows[active]) review(rows[active]); else search();
                return;
            }
            if (!rows.length) return;
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault(); active = (active + (event.key === 'ArrowDown' ? 1 : -1) + rows.length) % rows.length;
                list.querySelectorAll('[role=option]').forEach((option, index) => option.setAttribute('aria-selected', index === active ? 'true' : 'false'));
                query.setAttribute('aria-activedescendant', list.id + '-' + active);
            }
        });
        more.addEventListener('click', () => search(true));
        root.querySelector('[data-lookup-resolve]')?.addEventListener('click', () => {
            if (!isThai()) { say('country'); return; }
            close(); void request('addresses/resolve', { text: root.querySelector('[data-lookup-paste]').value, constraints: constraints() });
        });
        unconstrained?.addEventListener('click', () => {
            if (!isThai()) { say('country'); return; }
            close(); void request('addresses/resolve', { text: root.querySelector('[data-lookup-paste]').value });
        });
        root.querySelector('[data-lookup-cancel]').addEventListener('click', () => { preview.hidden = true; query.focus(); });
        root.querySelector('[data-lookup-apply]').addEventListener('click', () => {
            if (!selected || !company && !isThai()) return;
            const values = company ? (replace.checked ? { company: detail.value, taxId: root.querySelector('[data-lookup-tax]').value || undefined } : {})
                : { province: name(selected.province), district: name(selected.district), subdistrict: name(selected.subdistrict), postcode: selected.postcode,
                    ...(replace.checked ? { detail: detail.value } : {}) };
            if (Object.entries(values).some(([key, value]) => field(key) && !editable(field(key)) && value != null && field(key).value !== value)) { say('locked'); return; }
            if (Object.entries(values).some(([key, value]) => editable(field(key)) && field(key).maxLength > 0 && value?.length > field(key).maxLength)) { say('invalid'); return; }
            applying = true;
            try {
                Object.entries(values).forEach(([key, value]) => set(key, value));
                if (!company) codes = { provinceCode: selected.province.code, districtCode: selected.district.code, subdistrictCode: selected.subdistrict.code };
            } finally { applying = false; }
            close(); say('applied'); query.focus();
        });
        ['province', 'district', 'subdistrict', 'postcode', 'country'].forEach(key => field(key)?.addEventListener('input', () => {
            if (applying) return;
            stop(); close();
            applying = true;
            try {
                if (key === 'country') codes = {};
                else if (key === 'province') { codes = {}; ['district', 'subdistrict', 'postcode'].forEach(child => set(child, '')); }
                else if (key === 'district') { delete codes.districtCode; delete codes.subdistrictCode; ['subdistrict', 'postcode'].forEach(child => set(child, '')); }
                else if (key === 'subdistrict') { delete codes.subdistrictCode; set('postcode', ''); }
                else if (key === 'postcode') { delete codes.subdistrictCode; }
            } finally { applying = false; }
            if (key !== 'country') { query.value = field(key).value; timer = setTimeout(() => search(), 300); }
        }));
        window.addEventListener('pagehide', stop, { once: true });
        live.set(root, () => { stop(); window.removeEventListener('pagehide', stop); });
    };
    const start = () => {
        document.querySelectorAll('[data-thai-lookup]').forEach(initialize);
        for (const [root, dispose] of live) {
            if (!root.isConnected) { dispose(); live.delete(root); }
        }
    };
    const observer = new MutationObserver(start);
    observer.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('pagehide', () => { observer.disconnect(); live.forEach(dispose => dispose()); live.clear(); }, { once: true });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
