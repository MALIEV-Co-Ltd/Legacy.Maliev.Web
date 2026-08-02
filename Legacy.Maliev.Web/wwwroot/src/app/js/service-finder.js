(function () {
    'use strict';

    function initialiseServiceFinder(root) {
        var allSteps = Array.prototype.slice.call(root.querySelectorAll('[data-finder-step]'));
        var progressSteps = Array.prototype.slice.call(root.querySelectorAll('[data-finder-progress-step]'));
        var nextButton = root.querySelector('[data-finder-next]');
        var nextLabel = root.querySelector('[data-finder-next-label]');
        var backButton = root.querySelector('[data-finder-back]');
        var skipButton = root.querySelector('[data-finder-skip]');
        var skipResultsButton = root.querySelector('[data-finder-skip-to-results]');
        var error = root.querySelector('[data-finder-error]');
        var progressLabel = root.querySelector('[data-finder-progress-label]');
        var progressOptionalLabel = root.querySelector('[data-finder-progress-optional]');
        var results = root.querySelector('[data-finder-results]');
        var resultStatus = root.querySelector('[data-finder-result-status]');
        var emptyState = root.querySelector('[data-finder-empty]');
        var noMatchState = root.querySelector('[data-finder-no-match]');
        var pathState = root.querySelector('[data-finder-path]');
        var pathPrefix = root.querySelector('[data-finder-path-prefix]');
        var pathLabel = root.querySelector('[data-finder-path-label]');
        var pathNote = root.querySelector('[data-finder-path-note]');
        var resetButton = root.querySelector('[data-finder-reset]');
        var quotationLink = root.querySelector('[data-finder-quotation-link]');
        var suggestionDescription = root.querySelector('[data-finder-suggestion-description]');
        var cards = Array.prototype.slice.call(root.querySelectorAll('[data-finder-service]'));
        var summaryValues = Array.prototype.slice.call(root.querySelectorAll('[data-finder-summary]'));
        var summaryRows = Array.prototype.slice.call(root.querySelectorAll('[data-finder-summary-row]'));
        var materialGuidance = root.querySelector('[data-finder-material-guidance]');
        var materialGuidanceNote = root.querySelector('[data-finder-material-guidance-note]');
        var materialList = root.querySelector('[data-finder-material-list]');
        var state = {
            currentStep: 0,
            answers: {},
            completed: false,
            started: false,
            recommendedIds: [],
            finderPath: []
        };

        if (!allSteps.length || !nextButton || !nextLabel || !backButton || !error || !progressLabel || !results || !resultStatus || !emptyState || !noMatchState || !resetButton) {
            return;
        }

        var services = [
            { id: 'custom', base: 2 },
            { id: 'cnc', base: 2 },
            { id: 'printing', base: 1 },
            { id: 'scanning', base: 1 },
            { id: 'design', base: 1 },
            { id: 'silicone', base: 1 },
            { id: 'injection', base: 1 }
        ];

        function answer(key) {
            return state.answers[key] || '';
        }

        function hasCoreAnswers() {
            return ['files', 'service', 'material', 'quantity', 'end-use'].every(function (key) {
                return Boolean(answer(key));
            });
        }

        function shouldIncludeOptionalStep(step) {
            var key = step.dataset.finderKey;
            if (!step.hasAttribute('data-finder-optional')) return true;

            var direction = answer('service');
            var workflow = answer('workflow-3d');
            var files = answer('files');
            var endUse = answer('end-use');

            if (key === 'workflow-3d') {
                return direction === 'service-3d';
            }

            if (key === 'cnc-priority' || key === 'cnc-detail' || key === 'cnc-environment') {
                return direction === 'service-machining';
            }

            if (key === 'printing-priority' || key === 'printing-finish') {
                return direction === 'service-3d'
                    && (!workflow || workflow === 'workflow-3d-printing' || workflow === 'workflow-3d-unsure');
            }

            if (key === 'scanning-output' || key === 'scanning-accuracy') {
                return direction === 'service-3d'
                    && (workflow === 'workflow-3d-scanning'
                        || workflow === 'workflow-3d-unsure'
                        || (!workflow && (files === 'files-real-part' || endUse === 'use-replacement')));
            }

            if (key === 'molding-priority' || key === 'molding-detail') {
                return direction === 'service-molding';
            }

            if (key === 'performance') {
                return direction === 'service-unsure'
                    || (direction === 'service-3d' && workflow === 'workflow-3d-design')
                    || (!direction && answer('material') === 'material-unsure');
            }

            if (key === 'environment') {
                return direction === 'service-unsure'
                    || direction === 'service-3d'
                    || direction === 'service-molding'
                    || (direction !== 'service-machining'
                        && (['performance-strength', 'performance-flexibility', 'performance-temperature'].indexOf(answer('performance')) !== -1
                            || endUse === 'use-industrial'));
            }

            return false;
        }

        function clearInactiveOptionalAnswers() {
            allSteps.forEach(function (step) {
                if (step.hasAttribute('data-finder-optional') && !shouldIncludeOptionalStep(step)) {
                    delete state.answers[step.dataset.finderKey];
                }
            });
        }

        function getActiveSteps() {
            clearInactiveOptionalAnswers();
            return allSteps.filter(shouldIncludeOptionalStep);
        }

        function pushFinderEvent(eventName, fields) {
            var event = { event: eventName };

            Object.keys(fields || {}).forEach(function (key) {
                event[key] = fields[key];
            });

            if (window.malievAnalytics && typeof window.malievAnalytics.emit === 'function') {
                window.malievAnalytics.emit(event);
            }
        }

        function ensureFinderStarted() {
            if (state.started) return;

            state.started = true;
            pushFinderEvent('service_finder_started', {});
        }

        function scrollFinderElement(element) {
            if (!element || typeof element.scrollIntoView !== 'function') return;

            var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            element.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' });
        }

        function updateQuotationHandoff() {
            if (!quotationLink) return;

            var baseHref = quotationLink.dataset.finderBaseHref;
            if (!baseHref) {
                baseHref = quotationLink.getAttribute('href') || '/Quotation/Index';
                quotationLink.dataset.finderBaseHref = baseHref;
            }

            if (!state.completed || !hasCoreAnswers()) {
                quotationLink.setAttribute('href', baseHref);
                quotationLink.removeAttribute('data-finder-handoff-ready');
                quotationLink.removeAttribute('data-finder-path');
                return;
            }

            var handoffUrl = new URL(baseHref, window.location.href);
            var answerQueryKeys = {
                files: 'finder_files',
                service: 'finder_service',
                material: 'finder_material',
                quantity: 'finder_quantity',
                'end-use': 'finder_end_use',
                performance: 'finder_performance',
                environment: 'finder_environment'
            };

            Object.keys(answerQueryKeys).slice(0, 5).forEach(function (key) {
                handoffUrl.searchParams.delete(answerQueryKeys[key]);
                handoffUrl.searchParams.set(answerQueryKeys[key], answer(key));
            });

            Object.keys(answerQueryKeys).slice(5).forEach(function (key) {
                handoffUrl.searchParams.delete(answerQueryKeys[key]);
                if (answer(key)) handoffUrl.searchParams.set(answerQueryKeys[key], answer(key));
            });

            handoffUrl.searchParams.delete('finder_recommendations');
            handoffUrl.searchParams.set('finder_recommendations', state.recommendedIds.join(','));
            handoffUrl.searchParams.delete('finder_path');
            handoffUrl.searchParams.set('finder_path', state.finderPath.join(','));
            quotationLink.setAttribute('href', handoffUrl.pathname + handoffUrl.search + handoffUrl.hash);
            quotationLink.setAttribute('data-finder-handoff-ready', 'true');
            quotationLink.dataset.finderPath = state.finderPath.join(',');
        }

        function getOptionLabel(key, value) {
            var step = root.querySelector('[data-finder-key="' + key + '"]');
            var option = step && step.querySelector('[data-finder-answer="' + value + '"]');
            var label = option && option.querySelector('strong');
            return label ? label.textContent.trim() : '—';
        }

        function scoreService(service) {
            var score = service.base;
            var files = answer('files');
            var direction = answer('service');
            var material = answer('material');
            var quantity = answer('quantity');
            var endUse = answer('end-use');
            var performance = answer('performance');
            var environment = answer('environment');
            var workflow = answer('workflow-3d');
            var cncPriority = answer('cnc-priority');
            var cncDetail = answer('cnc-detail');
            var cncEnvironment = answer('cnc-environment');
            var printingPriority = answer('printing-priority');
            var printingFinish = answer('printing-finish');
            var scanningOutput = answer('scanning-output');
            var scanningAccuracy = answer('scanning-accuracy');
            var moldingPriority = answer('molding-priority');
            var moldingDetail = answer('molding-detail');

            if (direction === 'service-machining') {
                score += service.id === 'cnc' ? 8 : (service.id === 'custom' ? 3 : 0);
            } else if (direction === 'service-3d') {
                score += service.id === 'printing' || service.id === 'scanning' ? 7 : (service.id === 'design' ? 5 : 0);
            } else if (direction === 'service-molding') {
                score += service.id === 'silicone' || service.id === 'injection' ? 8 : (service.id === 'custom' ? 3 : 0);
            } else if (direction === 'service-unsure' && service.id === 'custom') {
                score += 3;
            }

            if (workflow === 'workflow-3d-printing') {
                if (service.id === 'printing') score += 8;
                if (service.id === 'design') score += 2;
                if (service.id === 'scanning') score -= 4;
            } else if (workflow === 'workflow-3d-scanning') {
                if (service.id === 'scanning') score += 9;
                if (service.id === 'design') score += 2;
                if (service.id === 'printing') score -= 4;
            } else if (workflow === 'workflow-3d-design') {
                if (service.id === 'design') score += 9;
                if (service.id === 'scanning') score += 2;
                if (service.id === 'printing') score += 1;
            }

            if (files === 'files-3d') {
                if (service.id === 'cnc' || service.id === 'printing' || service.id === 'injection') score += 3;
                if (service.id === 'silicone' || service.id === 'design') score += 2;
            } else if (files === 'files-2d') {
                if (service.id === 'cnc' || service.id === 'design' || service.id === 'custom') score += 3;
                if (service.id === 'printing' || service.id === 'injection') score += 1;
            } else if (files === 'files-image') {
                if (service.id === 'design') score += 7;
                if (service.id === 'printing' || service.id === 'custom') score += 2;
                if (service.id === 'scanning') score -= 2;
            } else if (files === 'files-none') {
                if (service.id === 'design') score += 6;
                if (service.id === 'scanning') score += 4;
                if (service.id === 'custom') score += 3;
            } else if (files === 'files-real-part') {
                if (service.id === 'scanning') score += 7;
                if (service.id === 'custom') score += 3;
                if (service.id === 'design') score += 2;
            }

            if (material === 'material-metal') {
                if (service.id === 'cnc') score += 6;
                if (service.id === 'printing' || service.id === 'custom') score += 2;
                if (service.id === 'injection' || service.id === 'silicone') score -= 2;
            } else if (material === 'material-standard-plastic') {
                if (service.id === 'printing') score += 7;
                if (service.id === 'injection') score += 4;
                if (service.id === 'cnc') score += 1;
            } else if (material === 'material-resin') {
                if (service.id === 'printing') score += 8;
                if (service.id === 'design') score += 2;
                if (service.id === 'cnc' || service.id === 'injection') score -= 2;
            } else if (material === 'material-plastic') {
                if (service.id === 'printing') score += 4;
                if (service.id === 'injection') score += 5;
                if (service.id === 'cnc') score += 3;
                if (service.id === 'custom') score += 2;
            } else if (material === 'material-silicone') {
                score += service.id === 'silicone' ? 9 : (service.id === 'design' || service.id === 'custom' ? 2 : -2);
            } else if (material === 'material-unsure' && service.id === 'custom') {
                score += 2;
            }

            if (quantity === 'quantity-1-10') {
                if (service.id === 'design' || service.id === 'printing' || service.id === 'cnc') score += 3;
                if (service.id === 'silicone') score += 4;
                if (service.id === 'injection') score += 1;
            } else if (quantity === 'quantity-11-100') {
                if (service.id === 'silicone') score += 4;
                if (service.id === 'injection') score += 5;
                if (service.id === 'printing' || service.id === 'cnc') score += 2;
            } else if (quantity === 'quantity-101-1000') {
                if (service.id === 'injection') score += 7;
                if (service.id === 'printing' || service.id === 'cnc') score += 3;
                if (service.id === 'silicone') score += 1;
            } else if (quantity === 'quantity-over-1000') {
                if (service.id === 'injection') score -= 100;
                if (service.id === 'cnc' || service.id === 'printing' || service.id === 'custom') score += 4;
            }

            if (endUse === 'use-prototype') {
                if (service.id === 'printing' || service.id === 'design') score += 4;
                if (service.id === 'cnc' || service.id === 'silicone' || service.id === 'injection') score += 2;
            } else if (endUse === 'use-industrial') {
                if (service.id === 'cnc' || service.id === 'injection') score += 5;
                if (service.id === 'printing' || service.id === 'custom') score += 2;
            } else if (endUse === 'use-replacement') {
                if (service.id === 'scanning') score += 5;
                if (service.id === 'cnc' || service.id === 'custom') score += 4;
                if (service.id === 'design') score += 2;
            } else if (endUse === 'use-consumer') {
                if (service.id === 'silicone') score += 5;
                if (service.id === 'printing' || service.id === 'design') score += 3;
                if (service.id === 'injection') score += 2;
            }

            if (performance === 'performance-strength') {
                if (service.id === 'cnc' || service.id === 'injection') score += 4;
                if (service.id === 'printing' || service.id === 'custom') score += 2;
                if (service.id === 'silicone') score -= 2;
            } else if (performance === 'performance-appearance') {
                if (service.id === 'printing' || service.id === 'design' || service.id === 'silicone') score += 3;
            } else if (performance === 'performance-flexibility') {
                if (service.id === 'silicone' || service.id === 'printing' || service.id === 'injection') score += 4;
                if (service.id === 'cnc') score -= 2;
            } else if (performance === 'performance-temperature') {
                if (service.id === 'cnc' || service.id === 'injection') score += 4;
                if (service.id === 'printing' || service.id === 'custom') score += 2;
            }

            if (environment === 'environment-outdoor' || environment === 'environment-wet') {
                if (service.id === 'printing' || service.id === 'injection' || service.id === 'custom') score += 2;
            } else if (environment === 'environment-heat-chemical') {
                if (service.id === 'cnc' || service.id === 'injection' || service.id === 'printing') score += 3;
                if (service.id === 'silicone') score += 1;
            }

            if (cncPriority) {
                if (service.id === 'cnc') score += 4;
                if (cncPriority === 'cnc-priority-strength' || cncPriority === 'cnc-priority-precision') {
                    if (service.id === 'cnc') score += 2;
                } else if (cncPriority === 'cnc-priority-weight') {
                    if (service.id === 'cnc' || service.id === 'design') score += 1;
                } else if (cncPriority === 'cnc-priority-wear' || cncPriority === 'cnc-priority-corrosion' || cncPriority === 'cnc-priority-chemical') {
                    if (service.id === 'cnc') score += 2;
                }
            }

            if (cncDetail) {
                if (service.id === 'cnc') score += 3;
                if (cncDetail === 'cnc-detail-finish' && (service.id === 'cnc' || service.id === 'custom')) score += 1;
            }

            if (cncEnvironment === 'cnc-environment-outdoor' || cncEnvironment === 'cnc-environment-chemical' || cncEnvironment === 'cnc-environment-clean') {
                if (service.id === 'cnc') score += 3;
            }

            if (printingPriority) {
                if (service.id === 'printing') score += 4;
                if (printingPriority === 'printing-priority-detail' && service.id === 'design') score += 2;
                if (printingPriority === 'printing-priority-strength' || printingPriority === 'printing-priority-heat') {
                    if (service.id === 'custom') score += 1;
                }
            }

            if (printingFinish) {
                if (service.id === 'printing') score += 2;
                if (printingFinish === 'printing-finish-accuracy' && service.id === 'design') score += 1;
            }

            if (scanningOutput) {
                if (service.id === 'scanning') score += 6;
                if (scanningOutput === 'scanning-output-cad' && service.id === 'design') score += 4;
                if (scanningOutput === 'scanning-output-cad' && service.id === 'cnc') score += 2;
                if (scanningOutput === 'scanning-output-deviation' && service.id === 'custom') score += 2;
                if (scanningOutput === 'scanning-output-visual' && service.id === 'design') score += 2;
            }

            if (scanningAccuracy) {
                if (service.id === 'scanning') score += 3;
                if (scanningAccuracy === 'scanning-accuracy-reference' && service.id === 'design') score += 1;
            }

            if (moldingPriority) {
                if (service.id === 'silicone' || service.id === 'injection') score += 3;
                if (moldingPriority === 'molding-priority-flexible' && service.id === 'silicone') score += 5;
                if (moldingPriority === 'molding-priority-rigid' && service.id === 'injection') score += 5;
                if (moldingPriority === 'molding-priority-wear' && (service.id === 'silicone' || service.id === 'injection')) score += 1;
            }

            if (moldingDetail) {
                if (service.id === 'silicone' || service.id === 'injection') score += 2;
                if (moldingDetail === 'molding-detail-consistency' && service.id === 'injection') score += 2;
            }

            return score;
        }

        function addUnique(list, id) {
            if (list.indexOf(id) === -1) list.push(id);
        }

        function recommendationChain() {
            var chain = [];
            var files = answer('files');
            var direction = answer('service');
            var material = answer('material');
            var quantity = answer('quantity');
            var endUse = answer('end-use');
            var workflow = answer('workflow-3d');
            var scanningOutput = answer('scanning-output');

            if (!files && !direction && !material && !quantity && !endUse) {
                addUnique(chain, 'custom');
                addUnique(chain, 'printing');
            } else if (files === 'files-real-part') {
                addUnique(chain, 'scanning');
            } else if (files === 'files-none') {
                if (direction === 'service-machining' || endUse === 'use-replacement') {
                    addUnique(chain, 'scanning');
                } else {
                    addUnique(chain, 'design');
                }
            } else if (files === 'files-image') {
                addUnique(chain, 'design');
            } else if (files === 'files-2d') {
                addUnique(chain, 'design');
            }

            if (direction === 'service-machining') {
                addUnique(chain, 'cnc');
            } else if (direction === 'service-3d') {
                if (workflow === 'workflow-3d-scanning' || scanningOutput) {
                    addUnique(chain, 'scanning');
                    if (scanningOutput === 'scanning-output-cad' || scanningOutput === 'scanning-output-deviation') {
                        addUnique(chain, 'design');
                    }
                } else if (workflow === 'workflow-3d-design') {
                    addUnique(chain, 'design');
                } else if (files === 'files-none' && endUse === 'use-replacement') {
                    addUnique(chain, 'scanning');
                } else {
                    addUnique(chain, 'printing');
                }
            } else if (direction === 'service-molding') {
                addUnique(chain, material === 'material-silicone' ? 'silicone' : 'injection');
            } else if (material === 'material-silicone') {
                addUnique(chain, 'silicone');
            } else if (quantity === 'quantity-101-1000') {
                addUnique(chain, 'injection');
            } else if (material === 'material-metal') {
                addUnique(chain, 'cnc');
            } else {
                addUnique(chain, 'printing');
            }

            if (quantity === 'quantity-over-1000') {
                chain = chain.filter(function (id) { return id !== 'injection'; });
            }

            return chain;
        }

        function getContextualServiceIds() {
            var direction = answer('service');

            if (direction === 'service-machining') {
                return ['custom', 'cnc', 'scanning'];
            }

            if (direction === 'service-3d') {
                var workflow = answer('workflow-3d');
                var scanningOutput = answer('scanning-output');

                if (workflow === 'workflow-3d-scanning' || scanningOutput) {
                    return ['custom', 'scanning', 'design', 'cnc'];
                }

                if (workflow === 'workflow-3d-design') {
                    return ['custom', 'design', 'scanning', 'printing'];
                }

                return ['custom', 'printing', 'scanning', 'design'];
            }

            if (direction === 'service-molding') {
                return ['custom', 'silicone', 'injection', 'scanning', 'design'];
            }

            return null;
        }

        function getOptionContext(key, value) {
            var files = answer('files');
            var direction = answer('service');
            var endUse = answer('end-use');
            var context = { hidden: false, suggested: false };

            if (key === 'service') {
                if (files === 'files-3d' || files === 'files-2d') {
                    context.suggested = value === 'service-3d' || value === 'service-machining';
                } else if (files === 'files-image') {
                    context.suggested = value === 'service-3d';
                } else if (files === 'files-real-part') {
                    context.suggested = value === 'service-3d' || value === 'service-machining';
                } else if (files === 'files-none') {
                    context.suggested = value === 'service-3d' || value === 'service-unsure';
                }
            } else if (key === 'material') {
                if (direction === 'service-3d') {
                    context.hidden = value === 'material-silicone';
                    context.suggested = value === 'material-standard-plastic'
                        || value === 'material-resin'
                        || value === 'material-plastic';
                } else if (direction === 'service-machining') {
                    context.hidden = value === 'material-resin' || value === 'material-silicone';
                    context.suggested = value === 'material-metal' || value === 'material-plastic';
                } else if (direction === 'service-molding') {
                    context.hidden = value === 'material-resin';
                    context.suggested = value === 'material-silicone' || value === 'material-plastic';
                } else if (files === 'files-real-part') {
                    context.suggested = value === 'material-unsure';
                }
            } else if (key === 'quantity') {
                if (direction === 'service-3d') {
                    context.suggested = value === 'quantity-1-10' || value === 'quantity-11-100';
                } else if (direction === 'service-molding') {
                    context.suggested = value === 'quantity-11-100' || value === 'quantity-101-1000';
                }
            } else if (key === 'workflow-3d') {
                if (files === 'files-real-part' || endUse === 'use-replacement') {
                    context.suggested = value === 'workflow-3d-scanning';
                } else if (files === 'files-3d') {
                    context.suggested = value === 'workflow-3d-printing' || value === 'workflow-3d-design';
                } else if (files === 'files-image' || files === 'files-none') {
                    context.suggested = value === 'workflow-3d-design' || value === 'workflow-3d-scanning';
                }
            } else if (key === 'cnc-priority') {
                if (answer('material') === 'material-metal') {
                    context.suggested = value === 'cnc-priority-strength'
                        || value === 'cnc-priority-precision'
                        || value === 'cnc-priority-corrosion';
                } else if (endUse === 'use-industrial' || endUse === 'use-replacement') {
                    context.suggested = value === 'cnc-priority-strength'
                        || value === 'cnc-priority-wear'
                        || value === 'cnc-priority-precision';
                }
            } else if (key === 'cnc-detail') {
                context.suggested = endUse === 'use-industrial'
                    ? value === 'cnc-detail-inspection' || value === 'cnc-detail-threads'
                    : value === 'cnc-detail-finish';
            } else if (key === 'cnc-environment') {
                context.suggested = value === 'cnc-environment-outdoor'
                    || value === 'cnc-environment-chemical';
            } else if (key === 'printing-priority') {
                if (answer('material') === 'material-resin') {
                    context.suggested = value === 'printing-priority-detail';
                } else if (answer('material') === 'material-standard-plastic') {
                    context.suggested = value === 'printing-priority-strength'
                        || value === 'printing-priority-flexible';
                } else if (endUse === 'use-consumer') {
                    context.suggested = value === 'printing-priority-detail';
                }
            } else if (key === 'printing-finish') {
                context.suggested = value === 'printing-finish-smooth'
                    || value === 'printing-finish-accuracy';
            } else if (key === 'scanning-output') {
                context.suggested = files === 'files-real-part' || endUse === 'use-replacement'
                    ? value === 'scanning-output-cad' || value === 'scanning-output-deviation'
                    : value === 'scanning-output-raw' || value === 'scanning-output-visual';
            } else if (key === 'scanning-accuracy') {
                context.suggested = value === 'scanning-accuracy-dimension'
                    || value === 'scanning-accuracy-reference';
            } else if (key === 'molding-priority') {
                if (answer('material') === 'material-silicone') {
                    context.suggested = value === 'molding-priority-flexible';
                } else if (answer('material') === 'material-plastic') {
                    context.suggested = value === 'molding-priority-rigid'
                        || value === 'molding-priority-wear';
                } else if (endUse === 'use-consumer') {
                    context.suggested = value === 'molding-priority-appearance';
                }
            } else if (key === 'molding-detail') {
                context.suggested = value === 'molding-detail-surface'
                    || value === 'molding-detail-consistency';
            }

            return context;
        }

        function refreshOptionContext() {
            var suggestionLabel = root.dataset.finderSuggestionLabel || 'Suggested';

            allSteps.forEach(function (step) {
                var key = step.dataset.finderKey;
                var selectedValue = answer(key);

                step.querySelectorAll('[data-finder-answer]').forEach(function (option) {
                    var value = option.dataset.finderAnswer;
                    var context = getOptionContext(key, value);

                    if (context.hidden && selectedValue === value) {
                        delete state.answers[key];
                        selectedValue = '';
                    }

                    option.hidden = context.hidden;
                    option.setAttribute('aria-hidden', context.hidden ? 'true' : 'false');
                    option.classList.toggle('is-suggested', context.suggested && !context.hidden);

                    if (context.suggested && !context.hidden) {
                        option.setAttribute('data-finder-suggestion', suggestionLabel);
                        option.setAttribute('title', suggestionLabel);
                        if (suggestionDescription) option.setAttribute('aria-describedby', suggestionDescription.id);
                    } else {
                        option.removeAttribute('data-finder-suggestion');
                        option.removeAttribute('title');
                        if (suggestionDescription) option.removeAttribute('aria-describedby');
                    }
                });

                setOptionSelection(step, selectedValue);
            });
        }

        function serviceLabel(id) {
            var card = root.querySelector('[data-finder-service="' + id + '"]');
            var title = card && card.querySelector('.service-index-card-body > strong');
            return title ? title.textContent.trim() : id;
        }

        function setOptionSelection(step, value) {
            var options = Array.prototype.slice.call(step.querySelectorAll('[data-finder-answer]'));
            options.forEach(function (option) {
                var selected = option.getAttribute('data-finder-answer') === value;
                option.classList.toggle('is-selected', selected);
                option.setAttribute('aria-pressed', selected ? 'true' : 'false');
            });
        }

        function renderStep() {
            refreshOptionContext();
            var activeSteps = getActiveSteps();
            state.currentStep = Math.max(0, Math.min(state.currentStep, activeSteps.length - 1));
            var current = state.currentStep;
            var currentStep = activeSteps[current];

            allSteps.forEach(function (step) {
                step.hidden = step !== currentStep;
                step.classList.remove('is-entering');
            });

            if (currentStep) {
                currentStep.getBoundingClientRect();
                currentStep.classList.add('is-entering');
            }

            root.style.setProperty('--finder-progress-count', String(activeSteps.length));
            progressSteps.forEach(function (progressStep) {
                var step = activeSteps.find(function (activeStep) {
                    return activeStep.dataset.finderStep === progressStep.dataset.finderProgressStep;
                });
                var stepIndex = step ? activeSteps.indexOf(step) : -1;
                var isActive = stepIndex === current;
                progressStep.hidden = !step;
                progressStep.classList.toggle('is-active', isActive);
                progressStep.classList.toggle('is-complete', stepIndex !== -1 && (stepIndex < current || root.classList.contains('is-complete')));
                progressStep.setAttribute('aria-current', isActive ? 'step' : 'false');
                if (step) {
                    var number = progressStep.querySelector('span:first-child');
                    if (number) number.textContent = String(stepIndex + 1);
                }
            });

            progressLabel.textContent = root.dataset.progressQuestion + ' ' + (current + 1) + ' ' + root.dataset.progressOf + ' ' + activeSteps.length;
            if (progressOptionalLabel) progressOptionalLabel.hidden = !currentStep || !currentStep.hasAttribute('data-finder-optional');
            root.dataset.currentFinderStep = String(current + 1);
            backButton.disabled = current === 0;
            nextLabel.textContent = current === activeSteps.length - 1 ? nextLabel.dataset.finalLabel : nextLabel.dataset.nextLabel;
            if (skipButton) skipButton.hidden = !currentStep || !currentStep.hasAttribute('data-finder-optional');
            error.hidden = true;
        }

        function updateSummary() {
            summaryValues.forEach(function (value) {
                var key = value.getAttribute('data-finder-summary');
                value.textContent = getOptionLabel(key, answer(key));
            });

            summaryRows.forEach(function (row) {
                var key = row.getAttribute('data-finder-summary-row');
                row.hidden = !answer(key);
            });
        }

        function guidanceReason(key) {
            var reasons = {
                strength: root.dataset.guidanceReasonStrength,
                appearance: root.dataset.guidanceReasonAppearance,
                flexibility: root.dataset.guidanceReasonFlexibility,
                temperature: root.dataset.guidanceReasonTemperature,
                environment: root.dataset.guidanceReasonEnvironment,
                weight: root.dataset.guidanceReasonWeight,
                wear: root.dataset.guidanceReasonWear,
                precision: root.dataset.guidanceReasonPrecision,
                corrosion: root.dataset.guidanceReasonCorrosion,
                finish: root.dataset.guidanceReasonFinish,
                scan: root.dataset.guidanceReasonScan,
                print: root.dataset.guidanceReasonPrint,
                molding: root.dataset.guidanceReasonMolding
            };

            return reasons[key] || root.dataset.guidanceReasonGeneral || '';
        }

        function materialGuidanceCopy(id) {
            var copy = {
                'material-standard-plastic': root.dataset.guidanceStandardPlastic,
                'material-resin': root.dataset.guidanceResin,
                'material-plastic': root.dataset.guidanceEngineeringPlastic,
                'material-metal': root.dataset.guidanceMetal,
                'material-silicone': root.dataset.guidanceSilicone
            };

            return copy[id] || '';
        }

        function materialCanBeRecommended(id) {
            var direction = answer('service');
            if (direction === 'service-3d' && id === 'material-silicone') return false;
            if (direction === 'service-machining' && (id === 'material-silicone' || id === 'material-resin')) return false;
            if (direction === 'service-molding' && id === 'material-resin') return false;
            return true;
        }

        function getMaterialRecommendations() {
            var material = answer('material');
            var performance = answer('performance');
            var environment = answer('environment');
            var direction = answer('service');
            var workflow = answer('workflow-3d');
            var scanningOutput = answer('scanning-output');
            var cncPriority = answer('cnc-priority');
            var cncDetail = answer('cnc-detail');
            var printingPriority = answer('printing-priority');
            var moldingPriority = answer('molding-priority');
            var recommendations = [];

            if (workflow === 'workflow-3d-scanning' || scanningOutput) {
                return recommendations;
            }

            function add(id, reason) {
                if (materialCanBeRecommended(id) && !recommendations.some(function (item) { return item.id === id; })) {
                    recommendations.push({ id: id, reason: reason });
                }
            }

            function requirementReason() {
                if (cncPriority === 'cnc-priority-weight') return 'weight';
                if (cncPriority === 'cnc-priority-wear') return 'wear';
                if (cncPriority === 'cnc-priority-precision') return 'precision';
                if (cncPriority === 'cnc-priority-corrosion') return 'corrosion';
                if (cncPriority === 'cnc-priority-chemical') return 'temperature';
                if (cncPriority === 'cnc-priority-strength') return 'strength';
                if (cncDetail === 'cnc-detail-finish') return 'finish';
                if (printingPriority === 'printing-priority-detail') return 'appearance';
                if (printingPriority === 'printing-priority-flexible') return 'flexibility';
                if (printingPriority === 'printing-priority-heat') return 'temperature';
                if (printingPriority === 'printing-priority-strength') return 'strength';
                if (moldingPriority === 'molding-priority-flexible') return 'flexibility';
                if (moldingPriority === 'molding-priority-heat') return 'temperature';
                if (moldingPriority === 'molding-priority-wear') return 'wear';
                if (moldingPriority === 'molding-priority-appearance') return 'appearance';
                if (performance === 'performance-strength') return 'strength';
                if (performance === 'performance-appearance') return 'appearance';
                if (performance === 'performance-flexibility') return 'flexibility';
                if (performance === 'performance-temperature') return 'temperature';
                if (environment === 'environment-heat-chemical') return 'temperature';
                if (environment === 'environment-outdoor' || environment === 'environment-wet') return 'environment';
                return direction === 'service-machining' ? 'general' : 'environment';
            }

            var explicitReason = requirementReason();

            if (material && material !== 'material-unsure') {
                add(material, explicitReason);
                if (material === 'material-standard-plastic' && (explicitReason === 'strength' || explicitReason === 'temperature' || explicitReason === 'environment' || explicitReason === 'wear')) {
                    add('material-plastic', explicitReason);
                } else if (material === 'material-standard-plastic' && explicitReason === 'appearance') {
                    add('material-resin', 'appearance');
                } else if (material === 'material-plastic' && (explicitReason === 'strength' || explicitReason === 'temperature' || explicitReason === 'precision' || explicitReason === 'wear')) {
                    add('material-metal', explicitReason);
                } else if (material === 'material-resin' && explicitReason === 'appearance') {
                    add('material-standard-plastic', 'appearance');
                } else if (material === 'material-silicone' && explicitReason === 'flexibility') {
                    add('material-standard-plastic', 'flexibility');
                }
            } else if (explicitReason === 'weight' || explicitReason === 'precision' || explicitReason === 'corrosion' || explicitReason === 'wear') {
                add('material-metal', explicitReason);
                add('material-plastic', explicitReason);
            } else if (explicitReason === 'strength') {
                add('material-plastic', 'strength');
                add('material-metal', 'strength');
                add('material-standard-plastic', 'strength');
            } else if (explicitReason === 'appearance') {
                add('material-resin', 'appearance');
                add('material-standard-plastic', 'appearance');
            } else if (explicitReason === 'flexibility') {
                add('material-silicone', 'flexibility');
                add('material-standard-plastic', 'flexibility');
            } else if (explicitReason === 'temperature') {
                add('material-plastic', 'temperature');
                add('material-metal', 'temperature');
            } else if (explicitReason === 'environment') {
                add('material-standard-plastic', 'environment');
                add('material-plastic', 'environment');
            } else {
                if (direction === 'service-machining') {
                    add('material-metal', 'general');
                    add('material-plastic', 'general');
                } else {
                    add('material-standard-plastic', 'environment');
                    add('material-plastic', 'environment');
                }
            }

            return recommendations.slice(0, 3);
        }

        function renderMaterialGuidance() {
            if (!materialGuidance || !materialList) return;

            var recommendations = getMaterialRecommendations();
            materialList.textContent = '';
            recommendations.forEach(function (recommendation) {
                var item = document.createElement('li');
                var icon = document.createElement('i');
                var content = document.createElement('span');
                var label = document.createElement('strong');
                var detail = document.createElement('small');

                icon.className = 'fas fa-check-circle';
                icon.setAttribute('aria-hidden', 'true');
                label.textContent = getOptionLabel('material', recommendation.id);
                detail.textContent = materialGuidanceCopy(recommendation.id) + ' ' + guidanceReason(recommendation.reason);
                content.appendChild(label);
                content.appendChild(detail);
                item.appendChild(icon);
                item.appendChild(content);
                materialList.appendChild(item);
            });

            materialGuidance.hidden = recommendations.length === 0;
            if (materialGuidanceNote) materialGuidanceNote.textContent = materialGuidance.dataset.note || materialGuidanceNote.textContent;
        }

        function showRecommendations() {
            var contextualServiceIds = getContextualServiceIds();
            var candidateServices = services.filter(function (service) {
                return !contextualServiceIds || contextualServiceIds.indexOf(service.id) !== -1;
            });
            var ranked = candidateServices.map(function (service) {
                return { id: service.id, score: scoreService(service) };
            }).sort(function (a, b) {
                return b.score - a.score;
            });
            var maximum = ranked[0].score;
            var rankedMatches = ranked.filter(function (service) {
                return service.score >= 4 && service.score >= maximum - 3;
            });
            var chain = recommendationChain().filter(function (id) {
                return !contextualServiceIds || contextualServiceIds.indexOf(id) !== -1;
            });
            var recommendedIds = [];

            chain.forEach(function (id) {
                var match = ranked.find(function (service) { return service.id === id; });
                if (match && match.score >= 0) addUnique(recommendedIds, id);
            });
            rankedMatches.forEach(function (service) {
                if (recommendedIds.length < 4) addUnique(recommendedIds, service.id);
            });

            var recommended = recommendedIds.slice(0, 4).map(function (id) {
                return { id: id };
            });
            var visibleChain = recommendedIds.filter(function (id) { return chain.indexOf(id) !== -1; });
            var isCompleteFinder = hasCoreAnswers();

            state.recommendedIds = recommended.map(function (service) { return service.id; });
            state.finderPath = visibleChain.length ? visibleChain : state.recommendedIds.slice();
            state.completed = true;
            pushFinderEvent(isCompleteFinder ? 'service_finder_completed' : 'service_finder_results_viewed', {
                recommended_service_ids: state.recommendedIds.join(','),
                finder_path: state.finderPath.join('>'),
                partial: isCompleteFinder ? 'false' : 'true'
            });

            cards.forEach(function (card) {
                var isRecommended = recommended.some(function (service) { return service.id === card.dataset.finderService; });
                var isContextuallyAllowed = !contextualServiceIds || contextualServiceIds.indexOf(card.dataset.finderService) !== -1;
                card.hidden = !isRecommended || !isContextuallyAllowed;
                card.setAttribute('aria-hidden', isRecommended && isContextuallyAllowed ? 'false' : 'true');
            });

            emptyState.hidden = true;
            noMatchState.hidden = recommended.length > 0;
            if (pathState && pathPrefix && pathLabel && pathNote) {
                pathState.hidden = visibleChain.length < 2;
                if (visibleChain.length >= 2) {
                    pathPrefix.textContent = pathState.dataset.pathPrefix;
                    pathLabel.textContent = visibleChain.map(serviceLabel).join(' → ');
                    pathNote.textContent = pathState.dataset.pathNote;
                }
            }
            if (recommended.length > 0) {
                resultStatus.textContent = isCompleteFinder
                    ? recommended.length + ' ' + (recommended.length === 1 ? resultStatus.dataset.matchSingular : resultStatus.dataset.matchPlural)
                    : recommended.length + ' ' + resultStatus.dataset.partial;
            } else {
                resultStatus.textContent = resultStatus.dataset.noMatch;
            }
            updateSummary();
            renderMaterialGuidance();
            updateQuotationHandoff();
            results.classList.add('is-ready');
            scrollFinderElement(results);
        }

        function clearCompletionState() {
            state.completed = false;
            state.recommendedIds = [];
            state.finderPath = [];
            root.classList.remove('is-complete');
            results.classList.remove('is-ready');
            cards.forEach(function (card) {
                card.hidden = true;
                card.setAttribute('aria-hidden', 'true');
            });
            emptyState.hidden = false;
            noMatchState.hidden = true;
            if (pathState) pathState.hidden = true;
            if (materialGuidance) materialGuidance.hidden = true;
            resultStatus.textContent = resultStatus.dataset.initialText;
            updateQuotationHandoff();
        }

        function resetFinder() {
            state.currentStep = 0;
            state.answers = {};
            clearCompletionState();
            allSteps.forEach(function (step) {
                setOptionSelection(step, '');
            });
            updateSummary();
            renderStep();
            scrollFinderElement(root);
        }

        allSteps.forEach(function (step) {
            var key = step.dataset.finderKey;
            step.querySelectorAll('[data-finder-answer]').forEach(function (option) {
                option.addEventListener('click', function () {
                    if (state.completed) clearCompletionState();
                    ensureFinderStarted();
                    state.answers[key] = option.dataset.finderAnswer;
                    setOptionSelection(step, state.answers[key]);
                    refreshOptionContext();
                    updateSummary();
                    error.hidden = true;
                });
            });
        });

        cards.forEach(function (card) {
            var recommendationLink = card.tagName && card.tagName.toLowerCase() === 'a'
                ? card
                : card.querySelector('a');
            if (!recommendationLink) return;

            recommendationLink.addEventListener('click', function () {
                if (!state.completed) return;

                pushFinderEvent('service_finder_recommendation_clicked', {
                    service_id: card.dataset.finderService,
                    finder_path: state.finderPath.join('>')
                });
            });
        });

        function focusCurrentStepOption() {
            var activeSteps = getActiveSteps();
            var currentStep = activeSteps[state.currentStep];
            var option = currentStep && (currentStep.querySelector('.is-selected:not([hidden])') || currentStep.querySelector('[data-finder-answer]:not([hidden])'));
            if (option) option.focus();
        }

        function moveToNextStepOrResults() {
            var activeSteps = getActiveSteps();
            if (state.currentStep >= activeSteps.length - 1) {
                root.classList.add('is-complete');
                showRecommendations();
                return;
            }

            state.currentStep += 1;
            renderStep();
            focusCurrentStepOption();
        }

        function skipCurrentOptionalStep() {
            var activeSteps = getActiveSteps();
            var currentStep = activeSteps[state.currentStep];
            if (!currentStep || !currentStep.hasAttribute('data-finder-optional')) return;

            ensureFinderStarted();
            delete state.answers[currentStep.dataset.finderKey];
            pushFinderEvent('service_finder_step_skipped', {
                step_key: currentStep.dataset.finderKey,
                step_number: state.currentStep + 1
            });
            updateSummary();
            moveToNextStepOrResults();
        }

        nextButton.addEventListener('click', function () {
            var activeSteps = getActiveSteps();
            var currentStep = activeSteps[state.currentStep];
            if (!currentStep) return;
            var currentStepKey = currentStep.dataset.finderKey;
            refreshOptionContext();
            var selectedAnswer = answer(currentStepKey);
            if (!selectedAnswer) {
                if (currentStep.hasAttribute('data-finder-optional')) {
                    skipCurrentOptionalStep();
                    return;
                }

                error.hidden = false;
                var firstOption = currentStep.querySelector('[data-finder-answer]:not([hidden])');
                if (firstOption) firstOption.focus();
                return;
            }

            if (state.currentStep === activeSteps.length - 1 && state.completed) return;

            pushFinderEvent('service_finder_step_completed', {
                step_key: currentStepKey,
                answer_id: selectedAnswer,
                step_number: state.currentStep + 1
            });

            moveToNextStepOrResults();
        });

        if (skipButton) skipButton.addEventListener('click', skipCurrentOptionalStep);

        if (skipResultsButton) {
            skipResultsButton.addEventListener('click', function () {
                ensureFinderStarted();
                root.classList.add('is-complete');
                showRecommendations();
            });
        }

        backButton.addEventListener('click', function () {
            if (state.currentStep === 0) return;
            if (state.completed) clearCompletionState();
            state.currentStep -= 1;
            renderStep();
            focusCurrentStepOption();
        });

        resetButton.addEventListener('click', resetFinder);
        nextLabel.dataset.nextLabel = nextLabel.textContent;
        renderStep();
        updateSummary();
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-service-finder]').forEach(initialiseServiceFinder);
    });
}());
