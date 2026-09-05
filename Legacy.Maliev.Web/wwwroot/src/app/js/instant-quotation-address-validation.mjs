const groups = [
    {
        building: 'BillingBuilding',
        components: ['BillingStreet1', 'BillingStreet2', 'BillingCity', 'BillingProvince', 'BillingPostalCode'],
    },
    {
        building: 'ShippingBuilding',
        components: ['ShippingStreet1', 'ShippingStreet2', 'ShippingCity', 'ShippingProvince', 'ShippingPostalCode'],
        separateShippingOnly: true,
    },
];

export function normalizeAddressForComparison(value) {
    return String(value ?? '')
        .normalize('NFC')
        .toLocaleLowerCase()
        .replace(/[^\p{L}\p{N}]/gu, '');
}

export function buildingContainsAddressComponents(building, addressComponents) {
    const normalizedBuilding = normalizeAddressForComparison(building);
    if (!normalizedBuilding) {
        return false;
    }

    const matches = new Set();
    for (const component of addressComponents ?? []) {
        const normalizedComponent = normalizeAddressForComparison(component);
        if (normalizedComponent.length < 3 || !normalizedBuilding.includes(normalizedComponent)) {
            continue;
        }

        matches.add(normalizedComponent);
        if (matches.size >= 2) {
            return true;
        }
    }

    return false;
}

export function wireInstantQuotationAddressValidation(form) {
    if (!form?.elements) {
        return;
    }

    const message = form.dataset?.addressValidationMessage ?? '';
    const submit = form.querySelector?.('[data-instant-quote-submit]');
    const shipToBilling = form.elements.namedItem('ShipToBillingAddress');

    const validateGroup = (group) => {
        const building = form.elements.namedItem(group.building);
        if (!building?.setCustomValidity) {
            return true;
        }

        const components = group.components.map((name) => form.elements.namedItem(name)?.value ?? '');
        const applies = !group.separateShippingOnly || shipToBilling?.checked !== true;
        const invalid = applies && buildingContainsAddressComponents(building.value, components);
        building.setCustomValidity(invalid ? message : '');
        if (invalid) {
            building.setAttribute?.('aria-invalid', 'true');
        } else {
            building.removeAttribute?.('aria-invalid');
            building.removeAttribute?.('data-address-server-invalid');
        }

        const error = form.querySelector?.(`[data-address-validation-for="${group.building}"]`);
        if (error) {
            error.textContent = invalid ? message : '';
            error.hidden = !invalid;
        }

        return !invalid;
    };

    const updateSubmit = () => {
        if (submit) {
            submit.disabled = !form.checkValidity?.();
        }
    };

    for (const group of groups) {
        const building = form.elements.namedItem(group.building);
        if (building?.dataset?.addressServerInvalid === 'true') {
            building.setCustomValidity(message);
        }

        for (const name of [group.building, ...group.components]) {
            const field = form.elements.namedItem(name);
            for (const eventName of ['input', 'change']) {
                field?.addEventListener?.(eventName, () => {
                    validateGroup(group);
                    updateSubmit();
                });
            }
        }
    }

    shipToBilling?.addEventListener?.('change', () => {
        validateGroup(groups[1]);
        updateSubmit();
    });
    form.addEventListener?.('submit', (event) => {
        const valid = groups.every(validateGroup);
        updateSubmit();
        if (!valid) {
            event.preventDefault?.();
            form.elements.namedItem('BillingBuilding')?.reportValidity?.();
        }
    });
    updateSubmit();
}
