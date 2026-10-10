/* Microsoft Ads Performance Auditor — progressive enhancement only.
   Every page is fully readable with JavaScript disabled: charts are server-rendered
   SVG, tables arrive sorted, and filtering simply hides rows. */

(function () {
    'use strict';

    /* ------------------------------------------------------------ charts ---
       Crosshair and tooltip for the server-rendered trend charts. Values are
       pre-formatted on the server, so the client never re-implements metric
       formatting and cannot disagree with the tables. */
    function initChart(wrap) {
        var svg = wrap.querySelector('svg');
        var tooltip = wrap.querySelector('.chart__tooltip');
        var zone = wrap.querySelector('.chart__hover-zone');
        if (!svg || !tooltip || !zone) return;

        var points;
        try {
            points = JSON.parse(svg.getAttribute('data-points') || '[]');
        } catch (e) {
            return;
        }
        if (!points.length) return;

        var line = wrap.querySelector('.chart__hover-line');
        var dotCurrent = wrap.querySelector('.chart__hover-dot--current');
        var dotPrevious = wrap.querySelector('.chart__hover-dot--previous');
        var currentName = svg.getAttribute('data-current-label') || 'Current';
        var previousName = svg.getAttribute('data-previous-label') || 'Previous';

        function hide() {
            tooltip.style.opacity = '0';
            if (line) line.style.opacity = '0';
            if (dotCurrent) dotCurrent.style.opacity = '0';
            if (dotPrevious) dotPrevious.style.opacity = '0';
        }

        function nearest(userX) {
            var best = points[0];
            var bestDistance = Math.abs(points[0].x - userX);
            for (var i = 1; i < points.length; i++) {
                var distance = Math.abs(points[i].x - userX);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = points[i];
                }
            }
            return best;
        }

        function show(event) {
            var box = svg.getBoundingClientRect();
            var viewBox = svg.viewBox.baseVal;
            if (!box.width || !viewBox.width) return;

            var scale = viewBox.width / box.width;
            var userX = (event.clientX - box.left) * scale;
            var point = nearest(userX);

            if (line) {
                line.setAttribute('x1', point.x);
                line.setAttribute('x2', point.x);
                line.style.opacity = '1';
            }

            if (dotCurrent) {
                if (point.currentY === null) {
                    dotCurrent.style.opacity = '0';
                } else {
                    dotCurrent.setAttribute('cx', point.x);
                    dotCurrent.setAttribute('cy', point.currentY);
                    dotCurrent.style.opacity = '1';
                }
            }

            if (dotPrevious) {
                if (point.previousY === null || point.previousY === undefined) {
                    dotPrevious.style.opacity = '0';
                } else {
                    dotPrevious.setAttribute('cx', point.x);
                    dotPrevious.setAttribute('cy', point.previousY);
                    dotPrevious.style.opacity = '1';
                }
            }

            var html = '<div class="chart__tooltip-row"><span class="chart__tooltip-key">'
                + '<span class="chart__swatch chart__swatch--current"></span>' + escapeHtml(currentName)
                + '</span><span class="chart__tooltip-val">' + escapeHtml(point.currentLabel) + '</span></div>'
                + '<div class="chart__tooltip-row"><span class="chart__tooltip-key">'
                + escapeHtml(point.currentDate) + '</span></div>';

            if (point.previousLabel) {
                html += '<div class="chart__tooltip-row"><span class="chart__tooltip-key">'
                    + '<span class="chart__swatch chart__swatch--previous"></span>' + escapeHtml(previousName)
                    + '</span><span class="chart__tooltip-val">' + escapeHtml(point.previousLabel) + '</span></div>'
                    + '<div class="chart__tooltip-row"><span class="chart__tooltip-key">'
                    + escapeHtml(point.previousDate || '') + '</span></div>';
            }

            tooltip.innerHTML = html;
            tooltip.style.opacity = '1';

            // Keep the tooltip inside the card.
            var pixelX = point.x / scale;
            var width = tooltip.offsetWidth;
            var left = Math.min(Math.max(pixelX + 14, 4), Math.max(box.width - width - 4, 4));
            tooltip.style.left = left + 'px';
            tooltip.style.top = '6px';
        }

        zone.addEventListener('mousemove', show);
        zone.addEventListener('mouseleave', hide);
        zone.addEventListener('touchmove', function (event) {
            if (event.touches && event.touches.length) show(event.touches[0]);
        }, { passive: true });
        zone.addEventListener('touchend', hide);
    }

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /* ------------------------------------------------------ table sorting ---
       Sorts on the data-sort attribute, which the server renders as a raw
       numeric or lowercase text value, so sorting never depends on the
       formatted display string. */
    function initSortableTable(table) {
        var headers = table.querySelectorAll('th.sortable');

        headers.forEach(function (header, columnIndex) {
            var button = header.querySelector('button');
            if (!button) return;

            button.addEventListener('click', function () {
                var ascending = header.getAttribute('aria-sort') !== 'ascending';

                headers.forEach(function (other) { other.setAttribute('aria-sort', 'none'); });
                header.setAttribute('aria-sort', ascending ? 'ascending' : 'descending');

                var body = table.tBodies[0];
                var index = Array.prototype.indexOf.call(header.parentNode.children, header);

                var rows = Array.prototype.slice.call(body.rows);
                rows.sort(function (a, b) {
                    var left = sortValue(a, index);
                    var right = sortValue(b, index);

                    if (typeof left === 'number' && typeof right === 'number') {
                        return ascending ? left - right : right - left;
                    }

                    return ascending
                        ? String(left).localeCompare(String(right))
                        : String(right).localeCompare(String(left));
                });

                rows.forEach(function (row) { body.appendChild(row); });
            });
        });
    }

    function sortValue(row, index) {
        var cell = row.cells[index];
        if (!cell) return '';

        var raw = cell.getAttribute('data-sort');
        if (raw === null) raw = cell.textContent.trim();

        // Missing values sort last in ascending order rather than as zero.
        if (raw === '') return Number.NEGATIVE_INFINITY;

        var numeric = parseFloat(raw);
        return isNaN(numeric) ? raw.toLowerCase() : numeric;
    }

    /* ---------------------------------------------------- table filtering ---
       A text box and any number of <select> filters, declared on the markup. */
    function initFilters(container) {
        var tableId = container.getAttribute('data-filters-for');
        var table = document.getElementById(tableId);
        if (!table) return;

        var controls = container.querySelectorAll('[data-filter-field]');
        var countTarget = document.querySelector('[data-filter-count="' + tableId + '"]');

        function apply() {
            var body = table.tBodies[0];
            if (!body) return;

            var shown = 0;
            var rows = body.rows;

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var visible = true;

                controls.forEach(function (control) {
                    if (!visible) return;

                    var value = (control.value || '').trim().toLowerCase();
                    if (!value) return;

                    var field = control.getAttribute('data-filter-field');

                    if (field === 'text') {
                        visible = row.textContent.toLowerCase().indexOf(value) !== -1;
                    } else {
                        var actual = (row.getAttribute('data-' + field) || '').toLowerCase();
                        visible = actual === value;
                    }
                });

                row.hidden = !visible;
                if (visible) shown++;
            }

            if (countTarget) {
                countTarget.textContent = shown === rows.length
                    ? 'Showing all ' + rows.length + ' row(s)'
                    : 'Showing ' + shown + ' of ' + rows.length + ' row(s)';
            }
        }

        controls.forEach(function (control) {
            control.addEventListener('input', apply);
            control.addEventListener('change', apply);
        });

        var reset = container.querySelector('[data-filter-reset]');
        if (reset) {
            reset.addEventListener('click', function () {
                controls.forEach(function (control) { control.value = ''; });
                apply();
            });
        }

        apply();
    }

    /* ------------------------------------------------------ loading state ---
       Generating a report can take a while against the live API, so the button
       reports progress instead of looking unresponsive. The rest of the page
       stays usable. */
    function initSubmitProgress(form) {
        form.addEventListener('submit', function () {
            var button = form.querySelector('[data-submit-progress]');
            if (!button) return;

            button.setAttribute('aria-disabled', 'true');
            button.dataset.originalText = button.innerHTML;
            button.innerHTML = '<span aria-hidden="true">⏳</span> Generating report…';

            var status = document.querySelector('[data-submit-status]');
            if (status) {
                status.textContent = 'Retrieving data from ' + (form.dataset.sourceName || 'the data source')
                    + ' and running the selected analyses. This can take a minute for longer periods.';
            }
        });
    }

    /* ---------------------------------------------------------- copy list --- */
    function initCopyButtons() {
        document.querySelectorAll('[data-copy-target]').forEach(function (button) {
            button.addEventListener('click', function () {
                var target = document.getElementById(button.getAttribute('data-copy-target'));
                if (!target) return;

                target.select();

                var done = function (ok) {
                    var original = button.dataset.originalLabel || button.textContent;
                    button.dataset.originalLabel = original;
                    button.textContent = ok ? 'Copied' : 'Press Ctrl+C to copy';
                    setTimeout(function () { button.textContent = original; }, 2200);
                };

                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(target.value).then(function () { done(true); }, function () { done(false); });
                } else {
                    done(false);
                }
            });
        });
    }

    /* -------------------------------------------------- bulk checkbox toggles ---
       For the portfolio scan, where the normal case is "all accounts" and unticking
       a few is the exception. */
    function initBulkToggles() {
        function setAll(containerId, checked) {
            var container = document.getElementById(containerId);
            if (!container) return;
            container.querySelectorAll('input[type="checkbox"]').forEach(function (box) {
                // Never tick a box the configuration has locked; a disabled input would look
                // selected while being silently dropped from the post.
                if (box.disabled) return;
                box.checked = checked;
            });
        }

        document.querySelectorAll('[data-select-all]').forEach(function (button) {
            button.addEventListener('click', function () {
                setAll(button.getAttribute('data-select-all'), true);
            });
        });

        document.querySelectorAll('[data-select-none]').forEach(function (button) {
            button.addEventListener('click', function () {
                setAll(button.getAttribute('data-select-none'), false);
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initBulkToggles();
        document.querySelectorAll('.chart-wrap').forEach(initChart);
        document.querySelectorAll('table.data[data-sortable]').forEach(initSortableTable);
        document.querySelectorAll('[data-filters-for]').forEach(initFilters);
        document.querySelectorAll('form[data-progress]').forEach(initSubmitProgress);
        initCopyButtons();
    });
})();
