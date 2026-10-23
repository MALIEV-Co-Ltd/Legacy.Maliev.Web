import { _3Dconnexion } from '/src/vendor/@3dconnexion/3dconnexionjs/3dconnexion.module.min.js';

const navigationLibraryUrl = 'https://127.51.68.120:8181/3dconnexion/nlproxy';
const canvas = document.getElementById('model-viewer-canvas');

function exposeSpaceMouseHelp() {
    const toolbar = canvas?.parentElement?.querySelector('.iq-camera-toolbar');
    if (!toolbar || toolbar.querySelector('[data-spacemouse-help]')) {
        return;
    }
    const label = document.documentElement.lang.toLowerCase().startsWith('th')
        ? 'การใช้งาน SpaceMouse'
        : 'SpaceMouse help';
    const link = document.createElement('a');
    link.href = '/help/spacemouse.html';
    link.target = '_blank';
    link.rel = 'noopener';
    link.className = 'iq-tool-btn';
    link.textContent = '?';
    link.title = label;
    link.setAttribute('aria-label', label);
    link.setAttribute('data-spacemouse-help', '');
    toolbar.appendChild(link);
}

async function navigationLibraryIsAvailable() {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 1500);
    try {
        const response = await fetch(navigationLibraryUrl, {
            cache: 'no-store',
            signal: controller.signal,
        });
        return response.ok;
    } catch {
        return false;
    } finally {
        window.clearTimeout(timeout);
    }
}

async function enableSpaceMouseNavigation() {
    exposeSpaceMouseHelp();
    if (!window.viewer || typeof window.viewer.EnableSpaceMouse !== 'function') {
        return;
    }
    canvas?.setAttribute('data-spacemouse-status', 'connecting');
    if (await navigationLibraryIsAvailable()) {
        window.viewer.EnableSpaceMouse(_3Dconnexion);
    } else {
        window.viewer.EnableSpaceMouse(null);
    }
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', enableSpaceMouseNavigation, { once: true });
} else {
    void enableSpaceMouseNavigation();
}

