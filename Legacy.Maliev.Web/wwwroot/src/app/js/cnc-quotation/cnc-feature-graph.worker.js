(function (root) {
    'use strict';

    var DEFAULT_METRIC_THREADS = Object.freeze([
        Object.freeze({ designation: 'M3.5 x 0.6', diameterMm: 3.5, pitchMm: 0.6 }),
        Object.freeze({ designation: 'M6 x 1', diameterMm: 6, pitchMm: 1 }),
        Object.freeze({ designation: 'M12 x 1.75', diameterMm: 12, pitchMm: 1.75 }),
        Object.freeze({ designation: 'M14 x 1', diameterMm: 14, pitchMm: 1 })
    ]);

    function values(value) { return Array.isArray(value) ? value : []; }
    function number(value) { return Number.isFinite(Number(value)) ? Number(value) : null; }
    function text(value) { return typeof value === 'string' && value.length ? value : null; }
    function cloneVector(value) {
        return value && Number.isFinite(value.x) && Number.isFinite(value.y) && Number.isFinite(value.z)
            ? { x: value.x, y: value.y, z: value.z } : null;
    }
    function surface(face) { return face && face.surface && typeof face.surface === 'object' ? face.surface : {}; }
    function sortedFaces(faces) { return values(faces).slice().sort(function (left, right) { return String(left.id).localeCompare(String(right.id)); }); }
    function faceIds(faces) { return sortedFaces(faces).map(function (face) { return face.id; }); }

    function feature(kind, faces, additions) {
        return Object.assign({
            id: '',
            bodyId: faces[0] && faces[0].bodyId || '',
            kind: kind,
            primaryFaceIds: faceIds(faces),
            secondaryFeatureIds: [],
            accessAxes: [],
            confidence: 'High',
            evidenceRefs: faceIds(faces)
        }, additions || {});
    }

    function threadEvidence(face) {
        var candidate = face && face.threadEvidence || surface(face).threadEvidence;
        if (!candidate || typeof candidate !== 'object') { return null; }
        var diameter = number(candidate.majorDiameterMm), pitch = number(candidate.pitchMm);
        if (!(diameter > 0) || !(pitch > 0) || typeof candidate.isInternal !== 'boolean') { return null; }
        return {
            groupId: text(candidate.groupId) || text(candidate.id) || 'thread',
            majorDiameterMm: diameter,
            pitchMm: pitch,
            isInternal: candidate.isInternal,
            handedness: candidate.handedness === 'left' ? 'left' : 'right',
            axis: cloneVector(candidate.axis || surface(face).axis),
            centerMm: cloneVector(candidate.centerMm || surface(face).centerMm)
        };
    }

    function normalizeMetricThread(evidence, options) {
        var scale = Math.max(1, number(options && options.modelScaleMm) || 1);
        var diameterTolerance = Math.max(0.15, scale * 0.003);
        var pitchTolerance = Math.max(0.03, scale * 0.001);
        var table = values(options && options.supportedMetricThreads).length
            ? options.supportedMetricThreads : DEFAULT_METRIC_THREADS;
        var matches = table.filter(function (candidate) {
            return candidate && number(candidate.diameterMm) > 0 && number(candidate.pitchMm) > 0
                && Math.abs(Number(candidate.diameterMm) - evidence.majorDiameterMm) <= diameterTolerance
                && Math.abs(Number(candidate.pitchMm) - evidence.pitchMm) <= pitchTolerance;
        }).sort(function (left, right) {
            var leftError = Math.abs(Number(left.diameterMm) - evidence.majorDiameterMm) / diameterTolerance
                + Math.abs(Number(left.pitchMm) - evidence.pitchMm) / pitchTolerance;
            var rightError = Math.abs(Number(right.diameterMm) - evidence.majorDiameterMm) / diameterTolerance
                + Math.abs(Number(right.pitchMm) - evidence.pitchMm) / pitchTolerance;
            return leftError - rightError || String(left.designation).localeCompare(String(right.designation));
        });
        if (matches.length !== 1) { return null; }
        return {
            designation: text(matches[0].designation) || ('M' + Number(matches[0].diameterMm) + ' x ' + Number(matches[0].pitchMm)),
            diameterMm: Number(matches[0].diameterMm),
            pitchMm: Number(matches[0].pitchMm)
        };
    }

    function vertices(face) {
        return values(face && face.loops).flatMap(function (loop) { return values(loop && loop.vertices); })
            .concat(surface(face).kind === 'swept' ? values(face && face.analysisSamples) : [])
            .filter(function (point) { return point && Number.isFinite(point.x) && Number.isFinite(point.y) && Number.isFinite(point.z); });
    }

    function coordinate(point, axisName, center) {
        if (axisName === 'x') { return { axial: point.x, first: point.y - center.y, second: point.z - center.z }; }
        if (axisName === 'y') { return { axial: point.y, first: point.x - center.x, second: point.z - center.z }; }
        return { axial: point.z, first: point.x - center.x, second: point.y - center.y };
    }

    function axisVector(axisName) {
        return { x: axisName === 'x' ? 1 : 0, y: axisName === 'y' ? 1 : 0, z: axisName === 'z' ? 1 : 0 };
    }

    function phaseScore(points, axisName, center, pitchMm, handedness) {
        var samples = points.map(function (point) {
            var local = coordinate(point, axisName, center);
            return { axial: local.axial, angle: Math.atan2(local.second, local.first),
                radius: Math.hypot(local.first, local.second) };
        });
        var average = samples.reduce(function (sum, sample) { return sum + sample.radius; }, 0) / Math.max(1, samples.length);
        var variance = samples.reduce(function (sum, sample) { return sum + Math.pow(sample.radius - average, 2); }, 0) / Math.max(1, samples.length);
        if (!(variance > Math.pow(Math.max(0.03, average * 0.005), 2))) { return null; }
        var bins = Array.from({ length: 36 }, function () { return []; });
        var angularBins = new Set();
        samples.forEach(function (sample) {
            var phase = sample.angle - handedness * Math.PI * 2 * sample.axial / pitchMm;
            phase = ((phase % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
            bins[Math.min(bins.length - 1, Math.floor(phase * bins.length / (Math.PI * 2)))].push(sample.radius);
            var angle = ((sample.angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
            angularBins.add(Math.min(23, Math.floor(angle * 24 / (Math.PI * 2))));
        });
        if (angularBins.size < 18) { return null; }
        var squaredError = 0, count = 0;
        bins.forEach(function (bin) {
            if (!bin.length) { return; }
            var mean = bin.reduce(function (sum, value) { return sum + value; }, 0) / bin.length;
            bin.forEach(function (value) { squaredError += Math.pow(value - mean, 2); count += 1; });
        });
        return count ? Math.sqrt(squaredError / count) / Math.sqrt(variance) : null;
    }

    function faceNormal(face) {
        var supplied = cloneVector(surface(face).normal);
        if (supplied) { return supplied; }
        var points = surface(face).kind === 'swept' && values(face && face.analysisSamples).length >= 3
            ? values(face.analysisSamples) : vertices(face);
        if (points.length < 3) { return null; }
        var first = { x: points[1].x - points[0].x, y: points[1].y - points[0].y, z: points[1].z - points[0].z };
        var second = { x: points[2].x - points[0].x, y: points[2].y - points[0].y, z: points[2].z - points[0].z };
        var normal = { x: first.y * second.z - first.z * second.y,
            y: first.z * second.x - first.x * second.z,
            z: first.x * second.y - first.y * second.x };
        var length = Math.hypot(normal.x, normal.y, normal.z);
        if (!(length > 1e-9)) { return null; }
        var sign = face.orientation === 'reversed' ? -1 : 1;
        return { x: normal.x * sign / length, y: normal.y * sign / length, z: normal.z * sign / length };
    }

    function connectedComponents(faces) {
        var byId = new Map(faces.map(function (face) { return [face.id, face]; }));
        var remaining = new Set(byId.keys()), result = [];
        while (remaining.size) {
            var seed = Array.from(remaining).sort()[0], queue = [seed], component = [];
            remaining.delete(seed);
            while (queue.length) {
                var face = byId.get(queue.shift());
                if (!face) { continue; }
                component.push(face);
                values(face.adjacentFaceIds).slice().sort().forEach(function (id) {
                    if (remaining.has(id) && byId.has(id)) { remaining.delete(id); queue.push(id); }
                });
            }
            result.push(sortedFaces(component));
        }
        return result;
    }

    function boundsCenter(points) {
        var bounds = points.reduce(function (result, point) {
            ['x', 'y', 'z'].forEach(function (axis) {
                result.min[axis] = Math.min(result.min[axis], point[axis]);
                result.max[axis] = Math.max(result.max[axis], point[axis]);
            });
            return result;
        }, { min: { x: Infinity, y: Infinity, z: Infinity }, max: { x: -Infinity, y: -Infinity, z: -Infinity } });
        return { x: (bounds.min.x + bounds.max.x) / 2, y: (bounds.min.y + bounds.max.y) / 2,
            z: (bounds.min.z + bounds.max.z) / 2 };
    }

    function polarity(component, axisName, center) {
        var votes = component.map(function (face) {
            var points = vertices(face), normal = faceNormal(face);
            if (!points.length || !normal) { return 0; }
            var centroid = points.reduce(function (sum, point) {
                return { x: sum.x + point.x / points.length, y: sum.y + point.y / points.length,
                    z: sum.z + point.z / points.length };
            }, { x: 0, y: 0, z: 0 });
            var local = coordinate(centroid, axisName, center);
            var radius = Math.hypot(local.first, local.second);
            if (!(radius > 1e-6)) { return 0; }
            var radial = axisName === 'x' ? { x: 0, y: local.first / radius, z: local.second / radius }
                : axisName === 'y' ? { x: local.first / radius, y: 0, z: local.second / radius }
                    : { x: local.first / radius, y: local.second / radius, z: 0 };
            return normal.x * radial.x + normal.y * radial.y + normal.z * radial.z;
        }).filter(function (vote) { return Math.abs(vote) >= 0.12; });
        if (votes.length < component.length * 0.65) { return null; }
        var positive = votes.filter(function (vote) { return vote > 0; }).length;
        var negative = votes.length - positive;
        if (Math.max(positive, negative) / votes.length < 0.75) { return null; }
        return positive > negative ? 'external' : 'internal';
    }

    function exteriorRadialPolarity(component, bodyFaces, axisName, center, majorRadius) {
        var componentPoints = component.flatMap(vertices), axial = componentPoints.map(function (point) { return coordinate(point, axisName, center).axial; });
        if (!axial.length) { return null; }
        var minimum = Math.min.apply(Math, axial), maximum = Math.max.apply(Math, axial), inset = Math.min((maximum - minimum) * 0.1, 0.25);
        var componentRadii = componentPoints.map(function (point) { var local = coordinate(point, axisName, center); return Math.hypot(local.first, local.second); });
        var bodyRadii = bodyFaces.flatMap(vertices).filter(function (point) { var value = coordinate(point, axisName, center).axial; return value > minimum + inset && value < maximum - inset; })
            .map(function (point) { var local = coordinate(point, axisName, center); return Math.hypot(local.first, local.second); });
        if (!componentRadii.length || !bodyRadii.length) { return null; }
        var componentMaximum = Math.max.apply(Math, componentRadii), bodyMaximum = Math.max.apply(Math, bodyRadii);
        var tolerance = Math.max(0.2, majorRadius * 0.03);
        return Math.abs(componentMaximum - majorRadius) <= tolerance && bodyMaximum <= majorRadius + tolerance ? 'external' : null;
    }

    function threadCenters(bodyFaces, axisName) {
        var centers = [];
        bodyFaces.forEach(function (face) {
            var evidence = threadEvidence(face);
            if (!evidence || !evidence.centerMm || !evidence.axis) { return; }
            var axis = evidence.axis;
            var expected = axisVector(axisName);
            if (Math.abs(axis.x * expected.x + axis.y * expected.y + axis.z * expected.z) < 0.999) { return; }
            centers.push(evidence.centerMm);
        });
        bodyFaces.forEach(function (face) {
            var candidate = surface(face), center = cloneVector(candidate.centerMm), axis = cloneVector(candidate.axis);
            if (!center || !axis || (candidate.kind !== 'cylinder' && candidate.kind !== 'cone')) { return; }
            var expected = axisVector(axisName);
            if (Math.abs(axis.x * expected.x + axis.y * expected.y + axis.z * expected.z) >= 0.999) { centers.push(center); }
        });
        centers.push(boundsCenter(bodyFaces.flatMap(vertices)));
        var unique = new Map();
        centers.forEach(function (center) {
            var key = [center.x, center.y, center.z].map(function (value) { return Math.round(value * 1000); }).join('|');
            unique.set(key, center);
        });
        return Array.from(unique.values()).sort(function (left, right) {
            return [left.x, left.y, left.z].join('|').localeCompare([right.x, right.y, right.z].join('|'));
        });
    }

    function derivedThreadGroups(faces, options) {
        var byBody = new Map(), table = values(options && options.supportedMetricThreads).length
            ? options.supportedMetricThreads : DEFAULT_METRIC_THREADS, groups = [];
        faces.forEach(function (face) {
            if (!byBody.has(face.bodyId)) { byBody.set(face.bodyId, []); }
            byBody.get(face.bodyId).push(face);
        });
        byBody.forEach(function (bodyFaces) {
            ['x', 'y', 'z'].forEach(function (axisName) {
                threadCenters(bodyFaces, axisName).forEach(function (center) {
                    table.forEach(function (metric) {
                        var diameter = number(metric && metric.diameterMm), pitch = number(metric && metric.pitchMm);
                        if (!(diameter > 0) || !(pitch > 0)) { return; }
                        var majorRadius = diameter / 2, radialDepth = Math.max(0.35, pitch * 0.75);
                        var seeds = bodyFaces.filter(function (face) {
                            var points = vertices(face), kind = surface(face).kind;
                            if (points.length < 3 || kind === 'cone' || kind === 'sphere' || kind === 'torus') { return false; }
                            var normal = faceNormal(face);
                            if (kind !== 'swept') {
                                if (!normal) { return false; }
                                var axis = axisVector(axisName);
                                if (Math.abs(normal.x * axis.x + normal.y * axis.y + normal.z * axis.z) > 0.96) { return false; }
                            }
                            var radii = points.map(function (point) {
                                var local = coordinate(point, axisName, center);
                                return Math.hypot(local.first, local.second);
                            });
                            return Math.max.apply(Math, radii) <= majorRadius + 0.16
                                && Math.max.apply(Math, radii) >= majorRadius - 0.16
                                && Math.min.apply(Math, radii) >= majorRadius - radialDepth - 0.16;
                        });
                        var seedIds = new Set(faceIds(seeds));
                        connectedComponents(bodyFaces.filter(function (face) { return surface(face).kind === 'swept'; }))
                            .forEach(function (component) {
                                var seedCount = component.filter(function (face) { return seedIds.has(face.id); }).length;
                                if (seedCount >= 3 && seedCount / component.length >= 0.4) {
                                    component.forEach(function (face) { seedIds.add(face.id); });
                                }
                            });
                        seeds = bodyFaces.filter(function (face) { return seedIds.has(face.id); });
                        connectedComponents(seeds).forEach(function (component) {
                            var points = component.flatMap(vertices);
                            if (component.length < 20 || points.length < 60) { return; }
                            var axial = points.map(function (point) { return coordinate(point, axisName, center).axial; });
                            if (Math.max.apply(Math, axial) - Math.min.apply(Math, axial) < pitch * 2) { return; }
                            var scored = [-1, 1].map(function (handedness) {
                                return { handedness: handedness, score: phaseScore(points, axisName, center, pitch, handedness) };
                            }).filter(function (candidate) { return candidate.score !== null && candidate.score < 0.45; })
                                .sort(function (left, right) { return left.score - right.score; });
                            if (!scored.length || (scored[1] && Math.abs(scored[1].score - scored[0].score) < 0.02)) { return; }
                            var kind = polarity(component, axisName, center)
                                || exteriorRadialPolarity(component, bodyFaces, axisName, center, majorRadius);
                            if (!kind) { return; }
                            groups.push({ score: scored[0].score, faces: component, evidence: {
                                groupId: ['derived', axisName, center.x, center.y, center.z, metric.designation,
                                    faceIds(component)[0]].join('|'),
                                majorDiameterMm: diameter, pitchMm: pitch, isInternal: kind === 'internal',
                                handedness: scored[0].handedness < 0 ? 'left' : 'right', axis: axisVector(axisName),
                                centerMm: center
                            } });
                        });
                    });
                });
            });
        });
        groups.sort(function (left, right) { return left.score - right.score || left.evidence.groupId.localeCompare(right.evidence.groupId); });
        var claimed = new Set(), result = [];
        groups.forEach(function (group) {
            if (group.faces.some(function (face) { return claimed.has(face.id); })) { return; }
            group.faces.forEach(function (face) { claimed.add(face.id); });
            result.push(group);
        });
        return result;
    }

    function recognizeThreads(faces, options) {
        var groups = derivedThreadGroups(faces, options);
        var byId = new Map(faces.map(function (face) { return [face.id, face]; }));
        groups.forEach(function (group) {
            var groupIds = new Set(faceIds(group.faces));
            faces.filter(function (face) { return surface(face).kind === 'cone' && !supportedChamfer(face); })
                .forEach(function (face) {
                    var threadNeighbors = values(face.adjacentFaceIds).filter(function (id) { return groupIds.has(id); }).length;
                    if (threadNeighbors >= 2) { group.faces.push(byId.get(face.id)); group.faces = sortedFaces(group.faces); }
                });
        });
        var claimed = new Set(groups.flatMap(function (group) { return faceIds(group.faces); }));
        var recognized = [], unresolved = [];
        groups.sort(function (left, right) {
            return faceIds(left.faces).join('|').localeCompare(faceIds(right.faces).join('|'));
        }).forEach(function (group) {
            var normalized = normalizeMetricThread(group.evidence, options);
            if (!normalized) {
                unresolved.push(feature('unresolved', group.faces, {
                    required: true,
                    unresolvedReason: 'unresolved_thread_designation',
                    confidence: 'Low'
                }));
                return;
            }
            var threadAxis = group.evidence.axis, threadDepth = threadAxis ? group.faces.reduce(function (span, face) {
                var faceBounds = surface(face).boundsMm;
                if (!faceBounds) { return span; }
                return Math.max(span, Math.abs(threadAxis.x) * (faceBounds.max.x - faceBounds.min.x)
                    + Math.abs(threadAxis.y) * (faceBounds.max.y - faceBounds.min.y)
                    + Math.abs(threadAxis.z) * (faceBounds.max.z - faceBounds.min.z));
            }, 0) : 0;
            recognized.push(feature(group.evidence.isInternal ? 'internal_thread' : 'external_thread', group.faces, {
                accessAxes: group.evidence.axis ? [group.evidence.axis] : [],
                threadDesignation: normalized.designation,
                nominalDiameterMm: normalized.diameterMm,
                measuredMajorDiameterMm: group.evidence.majorDiameterMm,
                pitchMm: normalized.pitchMm,
                measuredPitchMm: group.evidence.pitchMm,
                handedness: group.evidence.handedness,
                dimensions: { nominalDiameterMm: normalized.diameterMm, pitchMm: normalized.pitchMm,
                    depthMm: threadDepth },
                majorDiameterPreparationRequired: !group.evidence.isInternal,
                leadInChamferRequired: false
            }));
        });
        connectedComponents(faces.filter(function (face) { return threadEvidence(face) && !claimed.has(face.id); }))
            .forEach(function (component) {
                unresolved.push(feature('unresolved', component, { required: true,
                    unresolvedReason: 'unresolved_thread_geometry', confidence: 'Low' }));
            });
        return recognized.concat(unresolved);
    }

    function supportedChamfer(face) {
        var halfAngle = number(surface(face).halfAngleRadians);
        return surface(face).kind === 'cone' && halfAngle !== null
            && Math.abs(halfAngle - Math.PI / 4) <= Math.PI / 180;
    }

    function recognizeHoleChains(faces) {
        var byId = new Map(faces.map(function (face) { return [face.id, face]; }));
        var used = new Set(), result = [];
        sortedFaces(faces).forEach(function (face) {
            if (used.has(face.id) || surface(face).kind !== 'cylinder' || face.orientation !== 'reversed') { return; }
            var support = surface(face), axis = cloneVector(support.axis), center = cloneVector(support.centerMm);
            var span = number(support.angularSpanRadians), radius = number(support.radiusMm);
            if (!axis || !center || !(radius > 0) || span === null || Math.abs(span - Math.PI * 2) > 0.0001) { return; }
            var project = function (p) { return (p.x - center.x) * axis.x + (p.y - center.y) * axis.y + (p.z - center.z) * axis.z; };
            var axial = vertices(face).map(project);
            var minimum = Math.min.apply(Math, axial), maximum = Math.max.apply(Math, axial);
            if (!Number.isFinite(minimum) || !Number.isFinite(maximum) || !(maximum - minimum > 0.001)) { return; }
            var periodicSeam = support.closureEvidence === 'brep_periodic_seam';
            var closedRings = values(face.loops).filter(function (loop) {
                var points = values(loop.vertices), projections = points.map(project);
                return loop.closed === true && points.length >= 3 && Math.max.apply(Math, projections) - Math.min.apply(Math, projections) <= 0.001;
            });
            if (!periodicSeam && closedRings.length < 2) { return; }
            var capFaces = values(face.adjacentFaceIds).map(function (id) { return byId.get(id); }).filter(function (adjacent) {
                return adjacent && (surface(adjacent).kind === 'plane' || supportedChamfer(adjacent));
            });
            var capAt = function (limit) { return capFaces.some(function (cap) {
                var normal = faceNormal(cap);
                if (surface(cap).kind === 'plane' && (!normal || Math.abs(normal.x * axis.x + normal.y * axis.y + normal.z * axis.z) < 0.999)) { return false; }
                return vertices(cap).some(function (p) {
                    var projection = project(p), radial = Math.hypot(p.x - center.x - projection * axis.x,
                        p.y - center.y - projection * axis.y, p.z - center.z - projection * axis.z);
                    return Math.abs(projection - limit) <= 0.001 && Math.abs(radial - radius) <= 0.001;
                });
            }); };
            if (!capAt(minimum) || !capAt(maximum)) { return; }
            var chain = [face];
            values(face.adjacentFaceIds).forEach(function (id) {
                var adjacent = byId.get(id);
                if (adjacent && adjacent.orientation === 'reversed' && supportedChamfer(adjacent)) { chain.push(adjacent); }
            });
            chain.forEach(function (entry) { used.add(entry.id); });
            result.push(feature('hole', chain, {
                accessAxes: [axis],
                dimensions: { diameterMm: radius * 2, depthMm: maximum - minimum },
                cylindricalClosure: { angularSpanRadians: span, source: periodicSeam ? 'brep_periodic_seam' : 'closed_boundary_rings',
                    capFaceIds: faceIds(capFaces), minimumAxialMm: minimum, maximumAxialMm: maximum }
            }));
        });
        return result;
    }

    function recognizeChamfers(faces) {
        var byId = new Map(faces.map(function (face) { return [face.id, face]; }));
        return sortedFaces(faces).filter(function (face) {
            if (!supportedChamfer(face) || face.orientation === 'reversed') { return false; }
            var adjacent = values(face.adjacentFaceIds).map(function (id) { return byId.get(id); }).filter(Boolean);
            var touchesSweptThread = adjacent.some(function (item) { return surface(item).kind === 'swept'; });
            return touchesSweptThread || (adjacent.length >= 2 && adjacent.some(function (item) { return surface(item).kind === 'plane'; })
                && adjacent.some(function (item) {
                    return surface(item).kind === 'cylinder' || surface(item).kind === 'revolution'
                        || surface(item).kind === 'swept'
                        || !!threadEvidence(item);
                }));
        }).map(function (face) {
            var plane = values(face.adjacentFaceIds).map(function (id) { return byId.get(id); }).find(function (item) { return item && surface(item).kind === 'plane'; });
            var access = plane ? faceNormal(plane) : cloneVector(surface(face).axis);
            return feature('chamfer', [face], {
                accessAxes: access ? [access] : [],
                dimensions: { includedAngleDegrees: 90 }
            });
        });
    }

    function planeKey(face) {
        var normal = faceNormal(face), points = vertices(face);
        if (!normal || !points.length) { return null; }
        var sign = normal.x < -1e-6 || (Math.abs(normal.x) <= 1e-6 && normal.y < -1e-6)
            || (Math.abs(normal.x) <= 1e-6 && Math.abs(normal.y) <= 1e-6 && normal.z < 0) ? -1 : 1;
        normal = { x: normal.x * sign, y: normal.y * sign, z: normal.z * sign };
        var offset = normal.x * points[0].x + normal.y * points[0].y + normal.z * points[0].z;
        return [face.bodyId, normal.x, normal.y, normal.z, offset].map(function (value) {
            return typeof value === 'number' ? Math.round(value * 1000) : value;
        }).join('|');
    }

    function recognizeSlotsAndPockets(faces) {
        var byBodyPoints = new Map(), byId = new Map(faces.map(function (face) { return [face.id, face]; }));
        faces.forEach(function (face) {
            if (!byBodyPoints.has(face.bodyId)) { byBodyPoints.set(face.bodyId, []); }
            byBodyPoints.get(face.bodyId).push.apply(byBodyPoints.get(face.bodyId), vertices(face));
        });
        var claimed = new Set(), slots = [];
        function dot(a, b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        sortedFaces(faces).forEach(function (floor) {
            if (claimed.has(floor.id) || surface(floor).kind !== 'plane') { return; }
            var normal = faceNormal(floor), points = vertices(floor);
            if (!normal || points.length !== 4) { return; }
            var center = boundsCenter(points), level = dot(normal, points[0]);
            var walls = values(floor.adjacentFaceIds).map(function (id) { return byId.get(id); }).filter(function (wall) {
                var n = faceNormal(wall), wp = vertices(wall);
                if (!wall || claimed.has(wall.id) || wall.bodyId !== floor.bodyId || surface(wall).kind !== 'plane'
                    || !n || !wp.length || Math.abs(dot(n, normal)) > 0.001) { return false; }
                var projections = wp.map(function (p) { return dot(p, normal); });
                return Math.abs(Math.min.apply(Math, projections) - level) <= 0.001
                    && Math.max.apply(Math, projections) > level + 0.001
                    && dot(n, { x: center.x - wp[0].x, y: center.y - wp[0].y, z: center.z - wp[0].z }) > 0.001;
            });
            // This supported template is an open, straight slot. Closed square
            // corners and unproven rounded ends remain review-required.
            if (walls.length !== 2) { return; }
            var firstNormal = faceNormal(walls[0]), secondNormal = faceNormal(walls[1]);
            if (dot(firstNormal, secondNormal) > -0.999) { return; }
            var lateral = { x: normal.y * firstNormal.z - normal.z * firstNormal.y,
                y: normal.z * firstNormal.x - normal.x * firstNormal.z,
                z: normal.x * firstNormal.y - normal.y * firstNormal.x };
            var lengths = points.map(function (p) { return dot(p, lateral); });
            var minLength = Math.min.apply(Math, lengths), maxLength = Math.max.apply(Math, lengths);
            var wallExtents = walls.map(function (wall) {
                var wp = vertices(wall), axial = wp.map(function (p) { return dot(p, normal); }),
                    longitudinal = wp.map(function (p) { return dot(p, lateral); });
                return { top: Math.max.apply(Math, axial), min: Math.min.apply(Math, longitudinal), max: Math.max.apply(Math, longitudinal) };
            });
            if (Math.abs(wallExtents[0].top - wallExtents[1].top) > 0.001
                || wallExtents.some(function (e) { return Math.abs(e.min - minLength) > 0.001 || Math.abs(e.max - maxLength) > 0.001; })) { return; }
            var width = Math.abs(dot(firstNormal, vertices(walls[1])[0]) - dot(firstNormal, vertices(walls[0])[0]));
            var depth = wallExtents[0].top - level, length = maxLength - minLength;
            if (!(width > 0.001) || !(depth > 0.001) || !(length > 0.001)) { return; }
            var obstructed = faces.some(function (candidate) {
                if (candidate.id === floor.id || candidate.bodyId !== floor.bodyId || surface(candidate).kind !== 'plane') { return false; }
                var n = faceNormal(candidate), cp = vertices(candidate);
                if (!n || cp.length < 3 || Math.abs(dot(n, normal)) < 0.999 || dot(cp[0], normal) <= level + 0.001) { return false; }
                var across = cp.map(function (p) { return dot(p, firstNormal); }), along = cp.map(function (p) { return dot(p, lateral); });
                return dot(center, firstNormal) > Math.min.apply(Math, across) + 0.001
                    && dot(center, firstNormal) < Math.max.apply(Math, across) - 0.001
                    && dot(center, lateral) > Math.min.apply(Math, along) + 0.001
                    && dot(center, lateral) < Math.max.apply(Math, along) - 0.001;
            });
            if (obstructed) { return; }
            var owned = [floor].concat(walls);
            owned.forEach(function (face) { claimed.add(face.id); });
            slots.push(feature(length >= width * 2 ? 'slot' : 'pocket', owned, { accessAxes: [normal],
                dimensions: { widthMm: width, depthMm: depth, lengthMm: length },
                cornerEnvelope: { kind: 'open_ended_corridor', maximumDiameterMm: width,
                    floorFaceId: floor.id, wallFaceIds: faceIds(walls), openEndCount: 2 },
                accessEvidence: { source: 'brep_opposing_slot_walls', entryPlaneMm: wallExtents[0].top, floorPlaneMm: level } }));
        });
        return slots.concat(sortedFaces(faces).filter(function (face) {
            if (claimed.has(face.id)) { return false; }
            if (surface(face).kind !== 'plane') { return false; }
            var cylindricalNeighbors = values(face.adjacentFaceIds).map(function (id) { return byId.get(id); }).filter(function (item) { return item && surface(item).kind === 'cylinder'; });
            if (cylindricalNeighbors.length && cylindricalNeighbors.every(function (item) { return item.orientation !== 'reversed'; })) { return false; }
            var normal = faceNormal(face), points = vertices(face), bodyPoints = byBodyPoints.get(face.bodyId) || [];
            if (!normal || points.length < 4 || !bodyPoints.length) { return false; }
            var projection = normal.x * points[0].x + normal.y * points[0].y + normal.z * points[0].z;
            var projections = bodyPoints.map(function (point) { return normal.x * point.x + normal.y * point.y + normal.z * point.z; });
            var tolerance = Math.max(0.001, (Math.max.apply(Math, projections) - Math.min.apply(Math, projections)) * 0.0001);
            return projection > Math.min.apply(Math, projections) + tolerance
                && projection < Math.max.apply(Math, projections) - tolerance;
        }).map(function (face) {
            var normal = faceNormal(face), points = vertices(face);
            var bounds = ['x', 'y', 'z'].map(function (axis) {
                var coordinates = points.map(function (point) { return point[axis]; });
                return { axis: axis, span: Math.max.apply(Math, coordinates) - Math.min.apply(Math, coordinates) };
            }).filter(function (entry) { return Math.abs(normal[entry.axis]) < 0.9; })
                .sort(function (left, right) { return right.span - left.span; });
            var aspect = bounds.length > 1 && bounds[1].span > 0 ? bounds[0].span / bounds[1].span : 1;
            return feature(aspect >= 2 ? 'slot' : 'pocket', [face], {
                accessAxes: [normal], dimensions: { lengthMm: bounds[0] && bounds[0].span || 0,
                    widthMm: bounds[1] && bounds[1].span || 0 }
            });
        }));
    }

    function recognizeDatumsAndProfiles(faces, options, allFaces) {
        var planar = new Map(), result = [];
        sortedFaces(faces).forEach(function (face) {
            if (surface(face).kind === 'cylinder' && face.orientation !== 'reversed') {
                var axis = cloneVector(surface(face).axis), faceBounds = surface(face).boundsMm;
                var depth = axis && faceBounds ? Math.abs(axis.x) * (faceBounds.max.x - faceBounds.min.x)
                    + Math.abs(axis.y) * (faceBounds.max.y - faceBounds.min.y)
                    + Math.abs(axis.z) * (faceBounds.max.z - faceBounds.min.z) : 0;
                result.push(feature('outside_profile', [face], {
                    accessAxes: axis ? [axis] : [],
                    dimensions: { widthMm: number(surface(face).radiusMm) * 2, depthMm: depth }
                }));
                return;
            }
            if (surface(face).kind !== 'plane') { return; }
            var key = planeKey(face);
            if (!key) { return; }
            if (!planar.has(key)) { planar.set(key, []); }
            planar.get(key).push(face);
        });
        var datumEntries = [], extremalDatums = new Map();
        planar.forEach(function (group) {
            var normal = faceNormal(group[0]), points = vertices(group[0]);
            if (!normal || !points.length) { return; }
            var directionKey = [group[0].bodyId, normal.x, normal.y, normal.z].map(function (value) {
                return typeof value === 'number' ? Math.round(value * 1000000) : value;
            }).join('|');
            var projection = normal.x * points[0].x + normal.y * points[0].y + normal.z * points[0].z;
            var entry = { group: group, normal: normal, projection: projection, directionKey: directionKey };
            datumEntries.push(entry);
            var prior = extremalDatums.get(directionKey);
            if (!prior || projection > prior.projection + 0.001) { extremalDatums.set(directionKey, entry); }
        });
        var byId = new Map(values(allFaces || faces).map(function (face) { return [face.id, face]; }));
        var families = new Map();
        datumEntries.forEach(function (entry) {
            if (extremalDatums.get(entry.directionKey) !== entry) { return; }
            var n = entry.normal, familyKey = [entry.group[0].bodyId, Math.abs(n.x), Math.abs(n.y), Math.abs(n.z)].join('|');
            var area = entry.group.reduce(function (sum, face) {
                var areaVector = { x: 0, y: 0, z: 0 };
                values(face.loops).forEach(function (loop) { values(loop.vertices).forEach(function (p, i, points) {
                    var q = points[(i + 1) % points.length];
                    areaVector.x += p.y * q.z - p.z * q.y;
                    areaVector.y += p.z * q.x - p.x * q.z;
                    areaVector.z += p.x * q.y - p.y * q.x;
                }); });
                var planarArea = Math.abs(areaVector.x * n.x + areaVector.y * n.y + areaVector.z * n.z) / 2;
                if (!(planarArea > 0.001)) {
                    var radii = values(face.adjacentFaceIds).map(function (id) { return surface(byId.get(id)); })
                        .filter(function (s) { return s.axis && number(s.radiusMm) > 0
                            && Math.abs(s.axis.x * n.x + s.axis.y * n.y + s.axis.z * n.z) > 0.999; })
                        .map(function (s) { return s.radiusMm; }).sort(function (a, b) { return a - b; });
                    if (radii.length) { planarArea = Math.PI * (Math.pow(radii[radii.length - 1], 2)
                        - (radii.length > 1 ? Math.pow(radii[0], 2) : 0)); }
                }
                return sum + planarArea;
            }, 0);
            if (!families.has(familyKey)) { families.set(familyKey, []); }
            families.get(familyKey).push({ entry: entry, area: area });
        });
        var selectedByBody = new Map();
        Array.from(families.entries()).sort(function (a, b) { return a[0].localeCompare(b[0]); }).forEach(function (item) {
            var pair = item[1];
            if (pair.length !== 2 || pair[0].area <= 0 || pair[1].area <= 0) { return; }
            var a = pair[0].entry.normal, b = pair[1].entry.normal;
            if (a.x * b.x + a.y * b.y + a.z * b.z > -0.999) { return; }
            var bodyId = pair[0].entry.group[0].bodyId, score = Math.min(pair[0].area, pair[1].area);
            var prior = selectedByBody.get(bodyId);
            if (!prior || score > prior.score + 0.001) { selectedByBody.set(bodyId, { pair: pair, score: score }); }
        });
        datumEntries.forEach(function (entry) {
            var selected = selectedByBody.get(entry.group[0].bodyId);
            var required = !!selected && selected.pair.some(function (item) { return item.entry === entry; });
            result.push(feature('datum', entry.group, { accessAxes: [entry.normal], machiningRequired: required,
                facingEvidence: required ? { source: 'brep_primary_datum_selection',
                    faceIds: faceIds(entry.group), accessAxis: entry.normal } : null }));
        });
        return result;
    }

    function recognizeCertifiedFillets(faces) {
        var byId = new Map(faces.map(function (face) { return [face.id, face]; }));
        return sortedFaces(faces).filter(function (face) {
            var faceSurface = surface(face);
            if (faceSurface.kind !== 'torus' || !(number(faceSurface.minorRadiusMm) > 0)) { return false; }
            return values(face.adjacentFaceIds).filter(function (id) { return byId.has(id); }).length >= 2;
        }).map(function (face) {
            return feature('fillet', [face], { dimensions: { radiusMm: number(surface(face).minorRadiusMm) } });
        });
    }

    function build(topology, options) {
        topology = topology || {};
        if (topology.contract !== 'CncCadTopology.v1' || !text(topology.revision)) {
            throw new Error('revision_mismatch: CncFeatureGraph requires CncCadTopology.v1.');
        }
        if (!topology.automaticPlanningEligible) {
            return {
                contract: 'ManufacturingFeatureGraph.v1',
                topologyRevision: topology.revision,
                features: [],
                faceOwners: {},
                machinableFaceIds: [],
                unresolved: [{ reason: 'topology_not_eligible', required: true }]
            };
        }

        var suppliedFaces = values(topology.faces);
        if (suppliedFaces.some(function (face) { return !text(face && face.id); })) {
            return {
                contract: 'ManufacturingFeatureGraph.v1', topologyRevision: topology.revision,
                features: [], faceOwners: {}, machinableFaceIds: [],
                unresolved: [{ reason: 'invalid_topology_face_id', required: true }]
            };
        }
        var allFaces = sortedFaces(suppliedFaces);
        var topologyFaceIds = allFaces.map(function (face) { return face.id; });
        if (new Set(topologyFaceIds).size !== topologyFaceIds.length) {
            return {
                contract: 'ManufacturingFeatureGraph.v1', topologyRevision: topology.revision,
                features: [], faceOwners: {}, machinableFaceIds: [],
                unresolved: [{ reason: 'duplicate_topology_face_id', required: true }]
            };
        }
        var unowned = new Map(allFaces.map(function (face) { return [face.id, face]; }));
        var features = [];
        var recognizers = [recognizeThreads, recognizeHoleChains, recognizeChamfers,
            recognizeSlotsAndPockets, recognizeDatumsAndProfiles, recognizeCertifiedFillets];

        recognizers.forEach(function (recognizer) {
            var claimed = recognizer(Array.from(unowned.values()), options || {}, allFaces);
            claimed.forEach(function (candidate) {
                candidate.primaryFaceIds.forEach(function (faceId) { unowned.delete(faceId); });
                features.push(candidate);
            });
        });
        var faceById = new Map(allFaces.map(function (face) { return [face.id, face]; }));
        var externalThreads = features.filter(function (item) { return item.kind === 'external_thread'; });
        features.filter(function (item) { return item.kind === 'outside_profile'; }).forEach(function (profile) {
            var profileFace = faceById.get(profile.primaryFaceIds[0]), profileSurface = surface(profileFace);
            var profileBounds = profileSurface.boundsMm, profileRadius = number(profileSurface.radiusMm);
            var owner = externalThreads.find(function (thread) {
                var threadAxis = values(thread.accessAxes)[0], threadDiameter = number(thread.dimensions && thread.dimensions.nominalDiameterMm || thread.nominalDiameterMm);
                if (!threadAxis || !profileSurface.axis || !(profileRadius > 0) || !(threadDiameter > 0)
                    || Math.abs(threadAxis.x * profileSurface.axis.x + threadAxis.y * profileSurface.axis.y + threadAxis.z * profileSurface.axis.z) < 0.999999
                    || profileRadius >= threadDiameter / 2 || !profileBounds) { return false; }
                return values(thread.primaryFaceIds).some(function (id) {
                    var threadBounds = surface(faceById.get(id)).boundsMm;
                    return threadBounds && ['x', 'y', 'z'].every(function (name) { return profileBounds.max[name] + 0.001 >= threadBounds.min[name]
                        && threadBounds.max[name] + 0.001 >= profileBounds.min[name]; });
                });
            });
            if (owner) { profile.machiningRequired = false; profile.suppressedByFeatureKind = 'external_thread'; }
        });
        Array.from(unowned.values()).forEach(function (face) {
            var reason = threadEvidence(face) ? 'unresolved_thread_geometry'
                : surface(face).kind === 'cone' ? 'unresolved_conical_feature'
                    : surface(face).kind === 'sphere' || surface(face).kind === 'torus'
                        || surface(face).kind === 'revolution' || surface(face).kind === 'freeform'
                        ? 'uncertified_curved_feature' : 'unclassified_brep_face';
            features.push(feature('unresolved', [face], {
                required: true,
                unresolvedReason: reason,
                confidence: 'Low'
            }));
        });

        features.sort(function (left, right) {
            return left.kind.localeCompare(right.kind) || left.primaryFaceIds.join('|').localeCompare(right.primaryFaceIds.join('|'));
        });
        var faceOwners = {};
        features.forEach(function (item, index) {
            item.id = 'feature-' + String(index + 1).padStart(4, '0') + '-' + item.kind;
            item.primaryFaceIds.forEach(function (faceId) {
                if (faceOwners[faceId]) { throw new Error('duplicate_primary_face_owner: ' + faceId); }
                faceOwners[faceId] = item.id;
            });
        });
        var unresolved = features.filter(function (item) { return item.kind === 'unresolved'; }).map(function (item) {
            return { featureId: item.id, reason: item.unresolvedReason, required: item.required !== false };
        });
        var graph = {
            contract: 'ManufacturingFeatureGraph.v1',
            topologyRevision: topology.revision,
            features: features,
            faceOwners: faceOwners,
            machinableFaceIds: allFaces.map(function (face) { return face.id; }),
            unresolved: unresolved
        };
        var ownerIds = Object.keys(faceOwners).sort();
        var machinableIds = graph.machinableFaceIds.slice().sort();
        if (ownerIds.length !== machinableIds.length || ownerIds.some(function (id, index) { return id !== machinableIds[index]; })) {
            return {
                contract: 'ManufacturingFeatureGraph.v1', topologyRevision: topology.revision,
                features: [], faceOwners: {}, machinableFaceIds: [],
                unresolved: [{ reason: 'feature_ownership_invariant_failed', required: true }]
            };
        }
        if (root.CncPlanContracts && typeof root.CncPlanContracts.validateFeatureGraph === 'function') {
            root.CncPlanContracts.validateFeatureGraph(graph);
        }
        return graph;
    }

    root.CncFeatureGraph = Object.freeze({ build: build, metricThreads: DEFAULT_METRIC_THREADS });
}(typeof self !== 'undefined' ? self : globalThis));
