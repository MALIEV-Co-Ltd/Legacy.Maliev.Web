(function () {
    'use strict';

    const modelContext = typeof document === 'undefined' ? undefined : document.modelContext;
    if (!modelContext || typeof modelContext.registerTool !== 'function') {
        return;
    }

    const lifecycle = new AbortController();
    const services = Object.freeze({
        'cnc-machining': Object.freeze({ title: 'CNC machining', path: '/services/cnc-machining' }),
        '3d-printing': Object.freeze({ title: '3D printing', path: '/services/3d-printing' }),
        '3d-scanning': Object.freeze({ title: '3D scanning', path: '/services/3d-scanning' }),
        '3d-design': Object.freeze({ title: '3D design', path: '/services/3d-design' }),
        'silicone-casting': Object.freeze({ title: 'Silicone casting', path: '/services/silicone-casting' }),
        'low-volume-injection-molding': Object.freeze({ title: 'Low-volume injection molding', path: '/services/low-volume-injection-molding' }),
        'custom-manufacturing': Object.freeze({ title: 'Custom manufacturing', path: '/services/custom-manufacturing' }),
        'finishing-and-color': Object.freeze({ title: 'Finishing and colour standards', path: '/services/finishing-and-color' }),
    });

    function absoluteUrl(path) {
        return new URL(path, window.location.origin).toString();
    }

    function register(tool) {
        try {
            Promise.resolve(modelContext.registerTool(tool, { signal: lifecycle.signal })).catch(function () {
            });
        } catch (_) {
        }
    }

    register({
        name: 'maliev.list_services',
        title: 'List MALIEV services',
        description: 'List the public manufacturing services offered by MALIEV and their same-origin pages.',
        inputSchema: {
            type: 'object',
            properties: {},
            additionalProperties: false,
        },
        annotations: { readOnlyHint: true, untrustedContentHint: false },
        execute: function () {
            return {
                status: 'ok',
                services: Object.keys(services).map(function (id) {
                    return {
                        id,
                        title: services[id].title,
                        url: absoluteUrl(services[id].path),
                    };
                }),
            };
        },
    });

    register({
        name: 'maliev.open_service',
        title: 'Open a MALIEV service',
        description: 'Navigate the visible page to one allow-listed MALIEV manufacturing service.',
        inputSchema: {
            type: 'object',
            properties: {
                service: {
                    type: 'string',
                    enum: Object.keys(services),
                },
            },
            required: ['service'],
            additionalProperties: false,
        },
        annotations: { readOnlyHint: false, untrustedContentHint: false },
        execute: function (input) {
            const service = input && services[input.service];
            if (!service) {
                throw new Error('Unsupported service.');
            }

            const url = absoluteUrl(service.path);
            window.location.href = url;
            return { status: 'navigating', url };
        },
    });

    register({
        name: 'maliev.start_quotation',
        title: 'Start a MALIEV quotation',
        description: 'Navigate to the existing 3D-printing quotation form without submitting customer data.',
        inputSchema: {
            type: 'object',
            properties: {
                process: {
                    type: 'string',
                    enum: ['3d-printing'],
                },
            },
            required: ['process'],
            additionalProperties: false,
        },
        annotations: { readOnlyHint: false, untrustedContentHint: false },
        execute: function (input) {
            if (!input || input.process !== '3d-printing') {
                throw new Error('Unsupported quotation process.');
            }

            const url = absoluteUrl('/instantquotation/3d-printing');
            window.location.href = url;
            return { status: 'navigating', url };
        },
    });

    const currentPath = window.location.pathname.toLowerCase();
    if (currentPath.startsWith('/instantquotation/') || currentPath.startsWith('/quotation')) {
        register({
            name: 'maliev.review_quotation_form',
            title: 'Review quotation form completeness',
            description: 'Report whether the visible quotation form is complete using field names only, never field contents.',
            inputSchema: {
                type: 'object',
                properties: {},
                additionalProperties: false,
            },
            annotations: { readOnlyHint: true, untrustedContentHint: false },
            execute: function () {
                const form = document.querySelector('#instant-quotation-form');
                if (!form) {
                    return {
                        status: 'unavailable',
                        formPresent: false,
                        valid: false,
                        missingFields: [],
                    };
                }

                const valid = typeof form.checkValidity === 'function' && form.checkValidity();
                const missingFields = Array.prototype.map.call(
                    form.querySelectorAll(':invalid'),
                    function (control) {
                        return control.getAttribute('name') || control.getAttribute('id');
                    })
                    .filter(function (name, index, names) {
                        return Boolean(name) && names.indexOf(name) === index;
                    });

                return {
                    status: valid ? 'complete' : 'incomplete',
                    formPresent: true,
                    valid,
                    missingFields,
                };
            },
        });
    }

    if (typeof window.addEventListener === 'function') {
        window.addEventListener('pagehide', function () {
            lifecycle.abort();
        }, { once: true });
    }
}());
