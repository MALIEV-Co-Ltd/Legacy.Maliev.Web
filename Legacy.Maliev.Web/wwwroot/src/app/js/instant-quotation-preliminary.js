(function () {
    'use strict';

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function locale(snapshot) {
        return snapshot && snapshot.locale === 'th' ? 'th-TH' : 'en-US';
    }

    function number(snapshot, value) {
        return new Intl.NumberFormat(locale(snapshot), {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }).format(Number(value || 0));
    }

    function measurement(snapshot, value) {
        return number(snapshot, value);
    }

    function money(snapshot, value) {
        return number(snapshot, value) + ' ' + escapeHtml(snapshot.currency || 'THB');
    }

    function text(snapshot, key) {
        return escapeHtml(snapshot.text && snapshot.text[key] ? snapshot.text[key] : key);
    }

    function cssEscape(value) {
        if (window.CSS && typeof window.CSS.escape === 'function') {
            return window.CSS.escape(String(value));
        }

        return String(value).replace(/[^a-zA-Z0-9_-]/g, function (character) {
            return '\\' + character;
        });
    }

    function cropThumbnail(image) {
        if (!image || !image.src || !image.complete || !image.naturalWidth || !image.naturalHeight) {
            return image && image.src ? image.src : '/src/images/3d-canvas-placeholder.svg';
        }

        try {
            var width = image.naturalWidth;
            var height = image.naturalHeight;
            var canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;
            var context = canvas.getContext('2d', { willReadFrequently: true });
            if (!context) {
                return image.src;
            }

            context.drawImage(image, 0, 0, width, height);
            var pixels = context.getImageData(0, 0, width, height).data;
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    var offset = (y * width + x) * 4;
                    var visible = pixels[offset + 3] > 0
                        && (pixels[offset] < 245 || pixels[offset + 1] < 245 || pixels[offset + 2] < 245);
                    if (!visible) {
                        continue;
                    }

                    minX = Math.min(minX, x);
                    minY = Math.min(minY, y);
                    maxX = Math.max(maxX, x);
                    maxY = Math.max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY) {
                return image.src;
            }

            var boundsWidth = maxX - minX + 1;
            var boundsHeight = maxY - minY + 1;
            var paddingX = Math.max(4, Math.round(boundsWidth * 0.12));
            var paddingY = Math.max(4, Math.round(boundsHeight * 0.12));
            var cropX = Math.max(0, minX - paddingX);
            var cropY = Math.max(0, minY - paddingY);
            var cropRight = Math.min(width, maxX + 1 + paddingX);
            var cropBottom = Math.min(height, maxY + 1 + paddingY);
            var cropWidth = cropRight - cropX;
            var cropHeight = cropBottom - cropY;
            var outputWidth = 320;
            var outputHeight = Math.max(1, Math.round(outputWidth * cropHeight / cropWidth));
            var output = document.createElement('canvas');
            output.width = outputWidth;
            output.height = outputHeight;
            var outputContext = output.getContext('2d');
            if (!outputContext) {
                return image.src;
            }

            outputContext.fillStyle = '#fff';
            outputContext.fillRect(0, 0, outputWidth, outputHeight);
            outputContext.drawImage(canvas, cropX, cropY, cropWidth, cropHeight, 0, 0, outputWidth, outputHeight);
            return output.toDataURL('image/png');
        } catch (error) {
            return image.src;
        }
    }

    function thumbnail(part) {
        var selector = '[data-review-thumbnail][data-review-part-id="' + cssEscape(part.partId) + '"]';
        var image = document.querySelector(selector);
        if (!image || !image.src || image.src.indexOf('3d-canvas-placeholder') >= 0) {
            return '/src/images/3d-canvas-placeholder.svg';
        }

        return cropThumbnail(image);
    }

    function detail(label, value, modifier) {
        return '<div class="iq-preliminary-quotation-field' + (modifier ? ' ' + modifier : '') + '"><dt>' + label + '</dt><dd>' + value + '</dd></div>';
    }

    function value(value) {
        return value == null || value === '' ? '—' : String(value);
    }

    function generatedAt(snapshot) {
        if (snapshot.generatedAt) {
            return escapeHtml(snapshot.generatedAt);
        }

        return escapeHtml(new Intl.DateTimeFormat(locale(snapshot), {
            timeZone: 'Asia/Bangkok',
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        }).format(new Date()));
    }

    function dfmMarkup(snapshot, part) {
        if (!part.dfmWarnings || part.dfmWarnings.length === 0) {
            return '<div class="iq-preliminary-quotation-dfm-ok"><span aria-hidden="true">✓</span>'
                + text(snapshot, 'dfmClear') + '</div>';
        }

        return '<ul class="iq-preliminary-quotation-warnings">' + part.dfmWarnings.map(function (warning) {
            return '<li>' + escapeHtml(warning) + '</li>';
        }).join('') + '</ul>';
    }

    function buildDocument(snapshot) {
        var totals = snapshot.totals || {};
        var parts = snapshot.parts || [];
        var partMarkup = parts.map(function (part) {
            return '<article class="iq-preliminary-quotation-part">'
                + '<header class="iq-preliminary-quotation-part-header">'
                + '<span class="iq-preliminary-quotation-part-number">' + escapeHtml(String(part.partNumber).padStart(2, '0')) + '</span>'
                + '<div><h2>' + escapeHtml(part.fileName) + '</h2><p>' + measurement(snapshot, part.dimensionXmm) + ' × ' + measurement(snapshot, part.dimensionYmm) + ' × ' + measurement(snapshot, part.dimensionZmm) + ' mm</p></div>'
                + '</header>'
                + '<div class="iq-preliminary-quotation-part-body">'
                + '<img class="iq-preliminary-quotation-thumbnail" src="' + escapeHtml(thumbnail(part)) + '" alt="' + escapeHtml(part.fileName) + ' preview" loading="lazy">'
                + '<dl class="iq-preliminary-quotation-fields">'
                + detail(text(snapshot, 'material'), escapeHtml(part.material))
                + detail(text(snapshot, 'buildPreference'), escapeHtml(part.buildPreference))
                + detail(text(snapshot, 'color'), escapeHtml(part.color))
                + detail(text(snapshot, 'quantity'), escapeHtml(part.quantity))
                + detail(text(snapshot, 'unitPrice'), money(snapshot, part.unitPrice), 'iq-preliminary-quotation-money')
                + detail(text(snapshot, 'lineSubtotal'), money(snapshot, part.subtotal), 'iq-preliminary-quotation-money')
                + (part.technicalFilamentMinimumApplied
                    ? detail(text(snapshot, 'technicalFilamentMinimum'), '+' + money(snapshot, part.technicalFilamentMinimumAdjustment), 'iq-preliminary-quotation-money')
                    : '')
                + detail(text(snapshot, 'printTime'), measurement(snapshot, part.printTimeMinutes) + ' min')
                + '</dl></div>'
                + '<div class="iq-preliminary-quotation-measurements">'
                + detail(text(snapshot, 'dimensions'), measurement(snapshot, part.dimensionXmm) + ' × ' + measurement(snapshot, part.dimensionYmm) + ' × ' + measurement(snapshot, part.dimensionZmm) + ' mm')
                + detail(text(snapshot, 'volume'), measurement(snapshot, part.volumeCm3) + ' cm³')
                + detail(text(snapshot, 'surfaceArea'), measurement(snapshot, part.surfaceAreaCm2) + ' cm²')
                + detail(text(snapshot, 'minThickness'), measurement(snapshot, part.minThicknessMm) + ' mm')
                + '</div>'
                + '<section class="iq-preliminary-quotation-dfm" aria-label="' + text(snapshot, 'dfmAnalysis') + '"><h3>' + text(snapshot, 'dfmAnalysis') + '</h3>'
                + dfmMarkup(snapshot, part) + '</section></article>';
        }).join('');

        var surcharge = Number(totals.minimumOrderSurcharge || 0) > 0
            ? '<div class="iq-preliminary-quotation-total-row"><span>' + text(snapshot, 'surcharge') + ' (' + text(snapshot, 'minimumOrderMinimum') + ' ' + money(snapshot, totals.minimumOrderPrice) + ')</span><strong>' + money(snapshot, totals.minimumOrderSurcharge) + '</strong></div>'
            : '';
        var styles = [
            '@page { size: A4; margin: 12mm 12mm 16mm; }',
            '* { box-sizing: border-box; }',
            'html { background: #edf2f7; }',
            'body { margin: 0; color: #172033; background: #edf2f7; font-family: Arial, "Noto Sans Thai", sans-serif; font-size: 11px; line-height: 1.45; }',
            '.iq-preliminary-quotation-toolbar { position: sticky; top: 0; z-index: 2; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 16px; color: #fff; background: #142d4c; }',
            '.iq-preliminary-quotation-toolbar-actions { display: flex; gap: 8px; }',
            '.iq-preliminary-quotation-toolbar button { border: 1px solid rgba(255,255,255,.55); border-radius: 5px; padding: 8px 12px; color: #142d4c; background: #fff; font: inherit; font-weight: 600; cursor: pointer; }',
            '.iq-preliminary-quotation-toolbar button.secondary { color: #fff; background: transparent; }',
            '.iq-preliminary-quotation-toolbar-meta { opacity: .82; font-size: 10px; }',
            '.iq-preliminary-quotation-sheet { max-width: 210mm; min-height: 297mm; margin: 20px auto; padding: 13mm 12mm; background: #fff; box-shadow: 0 12px 32px rgba(20,45,76,.16); }',
            '.iq-preliminary-quotation-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 20px; padding-bottom: 14px; border-bottom: 3px solid #0b5ed7; }',
            '.iq-preliminary-quotation-logo { display: block; width: 132px; height: auto; }',
            '.iq-preliminary-quotation-kicker { margin: 3px 0 0; color: #66758a; font-size: 10px; font-weight: 600; letter-spacing: .1em; text-transform: uppercase; }',
            '.iq-preliminary-quotation-title { max-width: 65%; text-align: right; } .iq-preliminary-quotation-title h1 { margin: 0; color: #172033; font-size: 20px; line-height: 1.15; }',
            '.iq-preliminary-quotation-title p { margin: 5px 0 0; color: #a85d00; font-weight: 600; }',
            '.iq-preliminary-quotation-meta { display: flex; justify-content: space-between; gap: 16px; margin: 12px 0 18px; padding: 9px 11px; border: 1px solid #d9e2ec; border-radius: 5px; background: #f7f9fc; color: #4c5d70; }',
            '.iq-preliminary-quotation-section-title { margin: 0 0 9px; color: #172033; font-size: 14px; }',
            '.iq-preliminary-quotation-part { margin: 0 0 13px; padding: 11px; border: 1px solid #d9e2ec; border-radius: 6px; break-inside: avoid; page-break-inside: avoid; }',
            '.iq-preliminary-quotation-part-header { display: flex; align-items: flex-start; gap: 9px; margin-bottom: 9px; }',
            '.iq-preliminary-quotation-part-number { display: inline-flex; flex: 0 0 26px; align-items: center; justify-content: center; height: 26px; border-radius: 50%; color: #fff; background: #0b5ed7; font-size: 10px; font-weight: 600; }',
            '.iq-preliminary-quotation-part-header h2 { margin: 0; font-size: 13px; overflow-wrap: anywhere; } .iq-preliminary-quotation-part-header p { margin: 2px 0 0; color: #66758a; font-family: monospace; font-size: 10px; }',
            '.iq-preliminary-quotation-part-body { display: grid; grid-template-columns: 156px minmax(0, 1fr); gap: 11px; align-items: start; }',
            '.iq-preliminary-quotation-thumbnail { width: 156px; height: 156px; border: 1px solid #d9e2ec; border-radius: 4px; object-fit: contain; background: #fff; }',
            '.iq-preliminary-quotation-fields, .iq-preliminary-quotation-measurements { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 6px 9px; margin: 0; } .iq-preliminary-quotation-fields { grid-template-columns: repeat(3, minmax(0, 1fr)); }',
            '.iq-preliminary-quotation-field { min-width: 0; } .iq-preliminary-quotation-field dt { color: #66758a; font-size: 9px; font-weight: 600; } .iq-preliminary-quotation-field dd { margin: 1px 0 0; color: #172033; font-size: 10px; font-weight: 600; overflow-wrap: anywhere; } .iq-preliminary-quotation-money dd { color: #0b5ed7; font-variant-numeric: tabular-nums; }',
            '.iq-preliminary-quotation-measurements { margin: 10px 0 0 167px; padding-top: 8px; border-top: 1px solid #edf1f5; }',
            '.iq-preliminary-quotation-dfm { margin-top: 10px; padding-top: 8px; border-top: 1px solid #edf1f5; } .iq-preliminary-quotation-dfm h3 { margin: 0 0 5px; color: #66758a; font-size: 10px; text-transform: uppercase; letter-spacing: .05em; }',
            '.iq-preliminary-quotation-warnings { margin: 8px 0 0; padding-left: 17px; color: #9a3d13; font-size: 10px; } .iq-preliminary-quotation-dfm-ok { color: #187a41; background: #effaf3; padding: 5px 6px; border-radius: 4px; font-size: 10px; }',
            '.iq-preliminary-quotation-summary { margin-top: 18px; padding: 12px; border: 2px solid #172033; border-radius: 6px; break-inside: avoid; page-break-inside: avoid; } .iq-preliminary-quotation-summary h2 { margin: 0 0 8px; font-size: 15px; }',
            '.iq-preliminary-quotation-total-row { display: flex; justify-content: space-between; gap: 16px; padding: 5px 0; border-bottom: 1px solid #e4eaf0; } .iq-preliminary-quotation-total-row strong { white-space: nowrap; font-variant-numeric: tabular-nums; }',
            '.iq-preliminary-quotation-grand-total { display: flex; justify-content: space-between; gap: 16px; margin-top: 7px; padding-top: 8px; border-top: 2px solid #172033; color: #0b5ed7; font-size: 15px; font-weight: 600; } .iq-preliminary-quotation-disclaimer { margin: 14px 0 0; color: #66758a; font-size: 9px; }',
            '@media print { html, body { background: #fff; } .iq-preliminary-quotation-toolbar { display: none !important; } .iq-preliminary-quotation-sheet { max-width: none; min-height: auto; margin: 0; padding: 0; box-shadow: none; } }',
            '@media (max-width: 700px) { .iq-preliminary-quotation-sheet { margin: 0; padding: 16px; } .iq-preliminary-quotation-header, .iq-preliminary-quotation-meta { display: block; } .iq-preliminary-quotation-title { max-width: none; margin-top: 10px; text-align: left; } .iq-preliminary-quotation-part-body { grid-template-columns: 112px minmax(0, 1fr); } .iq-preliminary-quotation-thumbnail { width: 112px; height: 112px; } .iq-preliminary-quotation-measurements { margin-left: 0; } .iq-preliminary-quotation-fields, .iq-preliminary-quotation-measurements { grid-template-columns: repeat(2, minmax(0, 1fr)); } }'
        ].join('');

        return '<!doctype html><html lang="' + escapeHtml(snapshot.locale) + '"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>'
            + text(snapshot, 'preliminaryQuotation') + ' | MALIEV</title><style>' + styles + '</style></head><body>'
            + '<div class="iq-preliminary-quotation-toolbar"><div class="iq-preliminary-quotation-toolbar-actions">'
            + '<button type="button" onclick="PrintPreliminaryQuotation();">' + text(snapshot, 'preliminaryQuotationPrint') + '</button>'
            + '<button type="button" onclick="DownloadPreliminaryQuotationPdf();" title="' + text(snapshot, 'preliminaryQuotationDownloadPdfHint') + '">' + text(snapshot, 'preliminaryQuotationDownloadPdf') + '</button>'
            + '<button type="button" class="secondary" onclick="window.close();">' + text(snapshot, 'preliminaryQuotationClose') + '</button>'
            + '</div><span class="iq-preliminary-quotation-toolbar-meta">A4 · ' + text(snapshot, 'preliminaryQuotationNote') + ' · ' + text(snapshot, 'preliminaryQuotationDownloadPdfHint') + '</span></div>'
            + '<main class="iq-preliminary-quotation-sheet"><header class="iq-preliminary-quotation-header"><div><img class="iq-preliminary-quotation-logo" src="'
            + escapeHtml(snapshot.logoDataUri || '/src/images/navbar_logo_black.webp') + '" alt="MALIEV"><p class="iq-preliminary-quotation-kicker">3D printing service</p></div>'
            + '<div class="iq-preliminary-quotation-title"><h1>' + text(snapshot, 'preliminaryQuotation') + '</h1><p>' + text(snapshot, 'reviewOnly') + '</p></div></header>'
            + '<div class="iq-preliminary-quotation-meta"><span><strong>' + text(snapshot, 'preliminaryQuotationGenerated') + ':</strong> ' + generatedAt(snapshot) + '</span><span><strong>' + text(snapshot, 'quantity') + ':</strong> ' + escapeHtml(String(parts.length)) + '</span></div>'
            + '<h2 class="iq-preliminary-quotation-section-title">' + text(snapshot, 'preliminaryQuotationParts') + '</h2>' + partMarkup
            + '<section class="iq-preliminary-quotation-summary"><h2>' + text(snapshot, 'summaryTitle') + '</h2>'
            + '<div class="iq-preliminary-quotation-total-row"><span>' + text(snapshot, 'printingSubtotal') + '</span><strong>' + money(snapshot, totals.subtotal) + '</strong></div>'
            + surcharge
            + '<div class="iq-preliminary-quotation-total-row"><span>' + text(snapshot, 'shipping') + '</span><strong>' + money(snapshot, totals.shipping) + '</strong></div>'
            + '<div class="iq-preliminary-quotation-total-row"><span>' + text(snapshot, 'vat') + '</span><strong>' + money(snapshot, totals.vat) + '</strong></div>'
            + '<div class="iq-preliminary-quotation-total-row"><span>' + text(snapshot, 'leadTime') + '</span><strong>' + escapeHtml(String(totals.leadTimeMinimumDays || 0) + '–' + String(totals.leadTimeMaximumDays || 0)) + ' ' + text(snapshot, 'afterConfirmed') + '</strong></div>'
            + '<div class="iq-preliminary-quotation-grand-total"><span>' + text(snapshot, 'grandTotal') + '</span><strong>' + money(snapshot, totals.total) + '</strong></div></section>'
            + '<p class="iq-preliminary-quotation-disclaimer">' + text(snapshot, 'reviewOnly') + ' ' + text(snapshot, 'reviewIntro') + '</p></main>'
            + '<script>function PrintPreliminaryQuotation(){window.print();}function DownloadPreliminaryQuotationPdf(){var originalTitle=document.title;document.title="MALIEV Preliminary Quotation";window.print();window.setTimeout(function(){document.title=originalTitle;},1000);}</script></body></html>';
    }

    window.malievPreliminaryQuotation = {
        open: function (snapshot) {
            if (!snapshot || !snapshot.parts || !snapshot.parts.length) {
                return;
            }

            var preview = window.open('', '_blank');
            if (!preview) {
                return;
            }

            preview.document.open();
            preview.document.write(buildDocument(snapshot));
            preview.document.close();
            preview.opener = null;
            preview.focus();
            if (window.malievAnalytics && typeof window.malievAnalytics.emit === 'function') {
                window.malievAnalytics.emit({ event: 'preliminary_quotation_opened', file_count: snapshot.parts.length });
            }
        }
    };
}());
