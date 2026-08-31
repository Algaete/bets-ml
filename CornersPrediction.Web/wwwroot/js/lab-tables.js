(() => {
    const tableSelector = '.g-shell table, .h-shell table, #BotPicksDataScience table';
    const wrapperSelector = '.g-table-scroll, .h-table-scroll, .table-responsive, .lab-table-scroll';
    const collator = new Intl.Collator(document.documentElement.lang || 'es', {
        numeric: true,
        sensitivity: 'base'
    });

    const normalizedText = value => String(value ?? '')
        .replace(/\s+/g, ' ')
        .trim();

    const numericValue = value => {
        const text = normalizedText(value)
            .replace(/\u00a0/g, '')
            .replace(/\s/g, '');
        if (!text || text === '—' || text === '-') return null;

        const match = text.match(/[-+]?\d[\d.,]*/);
        if (!match) return null;

        let token = match[0];
        const comma = token.lastIndexOf(',');
        const dot = token.lastIndexOf('.');
        if (comma >= 0 && dot >= 0) {
            const decimalSeparator = comma > dot ? ',' : '.';
            const thousandsSeparator = decimalSeparator === ',' ? /\./g : /,/g;
            token = token.replace(thousandsSeparator, '');
            if (decimalSeparator === ',') token = token.replace(',', '.');
        } else if (comma >= 0) {
            token = token.replace(',', '.');
        }

        const parsed = Number(token);
        return Number.isFinite(parsed) ? parsed : null;
    };

    const dateValue = value => {
        const text = normalizedText(value);
        const match = text.match(/\d{4}-\d{2}-\d{2}(?:[ T]\d{2}:\d{2}(?::\d{2})?)?/);
        if (!match) return null;
        const parsed = Date.parse(match[0].replace(' ', 'T'));
        return Number.isFinite(parsed) ? parsed : null;
    };

    const inferType = (heading, rows, columnIndex) => {
        const declared = heading.dataset.sortType;
        if (declared) return declared;

        const values = rows
            .map(row => row.cells[columnIndex])
            .filter(Boolean)
            .map(cell => normalizedText(cell.dataset.sortValue ?? cell.textContent))
            .filter(value => value && value !== '—' && value !== '-')
            .slice(0, 20);
        if (values.length === 0) return 'text';
        if (values.every(value => dateValue(value) !== null)) return 'date';
        if (values.every(value => numericValue(value) !== null)) return 'number';
        return 'text';
    };

    const comparableValue = (cell, type) => {
        const raw = cell?.dataset.sortValue ?? cell?.textContent ?? '';
        if (type === 'number') return numericValue(raw);
        if (type === 'date') return dateValue(raw);
        return normalizedText(raw);
    };

    const updateSortIndicators = (table, activeHeading, direction) => {
        table.querySelectorAll('thead th[data-lab-sort-column]').forEach(heading => {
            const active = heading === activeHeading;
            heading.setAttribute('aria-sort', active ? direction : 'none');
            heading.classList.toggle('is-sorted', active);
            const indicator = heading.querySelector('.lab-sort-indicator');
            if (indicator) indicator.textContent = active
                ? direction === 'ascending' ? '▲' : '▼'
                : '↕';
        });
    };

    const sortTable = (table, heading, columnIndex) => {
        const body = table.tBodies[0];
        if (!body) return;

        const allRows = Array.from(body.rows);
        const sortableRows = allRows.filter(row =>
            row.cells[columnIndex]
            && !row.querySelector('td[colspan]')
            && row.dataset.labNoSort !== 'true');
        if (sortableRows.length < 2) return;

        sortableRows.forEach((row, index) => {
            if (!row.dataset.labOriginalIndex) row.dataset.labOriginalIndex = String(index + 1);
        });

        const previousColumn = table.dataset.labSortColumn;
        const previousDirection = table.dataset.labSortDirection;
        const direction = previousColumn === String(columnIndex) && previousDirection === 'ascending'
            ? 'descending'
            : 'ascending';
        const type = inferType(heading, sortableRows, columnIndex);
        const multiplier = direction === 'ascending' ? 1 : -1;

        sortableRows.sort((left, right) => {
            const leftValue = comparableValue(left.cells[columnIndex], type);
            const rightValue = comparableValue(right.cells[columnIndex], type);
            if (leftValue === null || leftValue === '') return rightValue === null || rightValue === '' ? 0 : 1;
            if (rightValue === null || rightValue === '') return -1;

            const comparison = type === 'text'
                ? collator.compare(leftValue, rightValue)
                : leftValue - rightValue;
            if (comparison !== 0) return comparison * multiplier;
            return Number(left.dataset.labOriginalIndex) - Number(right.dataset.labOriginalIndex);
        });

        const trailingRows = allRows.filter(row => !sortableRows.includes(row));
        const fragment = document.createDocumentFragment();
        sortableRows.forEach(row => fragment.appendChild(row));
        trailingRows.forEach(row => fragment.appendChild(row));
        body.appendChild(fragment);

        table.dataset.labSortColumn = String(columnIndex);
        table.dataset.labSortDirection = direction;
        table.dataset.labSortType = type;
        updateSortIndicators(table, heading, direction);
    };

    const enhanceTable = table => {
        if (!(table instanceof HTMLTableElement) || table.dataset.labTableReady === 'true') return;
        const headingRow = table.tHead?.rows[table.tHead.rows.length - 1];
        if (!headingRow) return;

        table.dataset.labTableReady = 'true';
        table.classList.add('lab-sortable-table');

        let wrapper = table.closest(wrapperSelector);
        if (!wrapper) {
            wrapper = document.createElement('div');
            wrapper.className = 'lab-table-scroll';
            wrapper.tabIndex = 0;
            wrapper.setAttribute('aria-label', 'Tabla desplazable y ordenable');
            table.parentNode.insertBefore(wrapper, table);
            wrapper.appendChild(table);
        } else {
            wrapper.classList.add('lab-table-scroll');
            if (!wrapper.hasAttribute('tabindex')) wrapper.tabIndex = 0;
        }

        Array.from(headingRow.cells).forEach((heading, columnIndex) => {
            if (heading.dataset.noSort === 'true' || heading.colSpan > 1) return;
            heading.scope = heading.scope || 'col';
            heading.dataset.labSortColumn = String(columnIndex);
            heading.setAttribute('aria-sort', 'none');

            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'lab-sort-button';
            button.title = 'Ordenar las filas visibles por esta columna';
            while (heading.firstChild) button.appendChild(heading.firstChild);

            const indicator = document.createElement('span');
            indicator.className = 'lab-sort-indicator';
            indicator.setAttribute('aria-hidden', 'true');
            indicator.textContent = '↕';
            button.appendChild(indicator);
            button.addEventListener('click', () => sortTable(table, heading, columnIndex));
            heading.appendChild(button);
        });
    };

    const enhanceWithin = root => {
        if (root instanceof HTMLTableElement && root.matches(tableSelector)) enhanceTable(root);
        root.querySelectorAll?.(tableSelector).forEach(enhanceTable);
    };

    const start = () => {
        enhanceWithin(document);
        const observer = new MutationObserver(mutations => {
            mutations.forEach(mutation => mutation.addedNodes.forEach(node => {
                if (node instanceof Element) enhanceWithin(node);
            }));
        });
        observer.observe(document.body, { childList: true, subtree: true });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
