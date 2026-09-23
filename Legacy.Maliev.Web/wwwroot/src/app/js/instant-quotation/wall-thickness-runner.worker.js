// Keep the normal-ray engine off the UI thread. No pricing or upload decision uses this result.
importScripts('/src/app/js/instant-quotation/wall-thickness.worker.js?v=2');

self.onmessage = function ({ data }) {
    try {
        var evidence = self.MalievWallThickness.analyze(data.meshes);
        var transfer = [
            evidence.sampleThicknessMm.buffer,
            evidence.sampleAreaMm2.buffer,
            evidence.oppositeSurfacePatches.buffer
        ];
        evidence.fields.forEach(function (field) { transfer.push(field.buffer); });
        self.postMessage({ evidence: evidence }, transfer);
    } catch (_) {
        self.postMessage({ error: 'Thickness analysis unavailable.' });
    }
};
