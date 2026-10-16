import assert from 'node:assert/strict';
import test from 'node:test';
import { normalizeOptionalQuotationFields as normalize, wireOptionalQuotationFields as wire } from '../wwwroot/src/app/js/instant-quotation-optional-fields.mjs';
import { wireInstantQuotationAddressValidation } from '../wwwroot/src/app/js/instant-quotation-address-validation.mjs';
class Field extends EventTarget {
    constructor(value, options = {}) { super(); Object.assign(this, { value, disabled: false, readOnly: false }, options); }
}
function create(tax = '', branch = 'branch', code = 'invalid') {
    const fields = { Company: new Field('--'), TaxNumber: new Field(tax), TaxBranch: new Field(branch), TaxBranchCode: new Field(code) };
    const branchWrapper = {}, codeWrapper = {};
    const form = new EventTarget();
    form.elements = { namedItem: name => fields[name] };
    form.querySelector = selector => ({ '#instant-quote-tax-branch': fields.TaxBranch, '[data-instant-tax-branch-field]': branchWrapper, '[data-instant-tax-branch-code-field]': codeWrapper })[selector];
    return { fields, form, branchWrapper, codeWrapper, submit: new EventTarget() };
}
test('absent tax clears optional malformed branch code and excludes irrelevant constraints', () => {
    const x=create();normalize(x.form);
    assert.equal(x.fields.TaxBranchCode.value,'');assert.equal(x.fields.TaxBranchCode.disabled,true);
    assert.equal(x.branchWrapper.hidden,true);assert.equal(x.codeWrapper.hidden,true);
});
test('dash-only tax is normalized before dependent visibility', () => {
    const x=create(' — ');normalize(x.form);assert.equal(x.fields.TaxNumber.value,'');assert.equal(x.branchWrapper.hidden,true);
});
test('present tax with branch preserves invalid code for browser validation', () => {
    const x=create('0105559123456');normalize(x.form);assert.equal(x.fields.TaxBranchCode.value,'invalid');assert.equal(x.fields.TaxBranchCode.disabled,false);assert.equal(x.codeWrapper.hidden,false);
});
test('head office hides and clears unlocked branch code', () => {
    const x=create('0105559123456','head-office');normalize(x.form);assert.equal(x.branchWrapper.hidden,false);assert.equal(x.codeWrapper.hidden,true);assert.equal(x.fields.TaxBranchCode.value,'');
});
test('tax input restores fields disabled only by optional visibility', () => {
    const x=create();wire(x.form,x.submit);x.fields.TaxNumber.value='0105559123456';x.fields.TaxNumber.dispatchEvent(new Event('input'));assert.equal(x.fields.TaxBranch.disabled,false);assert.equal(x.fields.TaxBranchCode.disabled,false);
});
test('external submit normalizes before later validation listener', () => {
    const x=create('0105559123456');wire(x.form,x.submit);x.fields.TaxNumber.value='--';
    let observed;x.submit.addEventListener('click',()=>{observed=x.fields.TaxNumber.value;});x.submit.dispatchEvent(new Event('click'));assert.equal(observed,'');assert.equal(x.codeWrapper.hidden,true);
});
test('normalization emits input so existing address validation can refresh', () => {
    const x=create('—');let changes=0;x.fields.TaxNumber.addEventListener('input',()=>changes++);normalize(x.form);assert.equal(changes,1);
});
test('readonly authoritative values survive normalization and hiding', () => {
    const x=create('—');x.fields.Company.readOnly=true;x.fields.TaxNumber.readOnly=true;x.fields.TaxBranchCode.readOnly=true;normalize(x.form);assert.equal(x.fields.Company.value,'--');assert.equal(x.fields.TaxNumber.value,'—');assert.equal(x.fields.TaxBranchCode.value,'invalid');
});
test('locked select plus same-name hidden mirror uses actual select', () => {
    const x=create('0105559123456','branch','00001');x.fields.TaxBranch.disabled=true;
    const original=x.form.elements.namedItem;x.form.elements.namedItem=name=>name==='TaxBranch'?{value:''}:original(name);
    normalize(x.form);assert.equal(x.fields.TaxBranch.disabled,true);assert.equal(x.codeWrapper.hidden,false);assert.equal(x.fields.TaxBranchCode.value,'00001');
});
test('real hyphenated company and thirteen digit tax are unchanged', () => {
    const x=create('0105559123456');x.fields.Company.value='A-B Manufacturing';normalize(x.form);assert.equal(x.fields.Company.value,'A-B Manufacturing');assert.equal(x.fields.TaxNumber.value,'0105559123456');
});

test('optional normalization refreshes actual address-validation submit state', () => {
    const x=create('0105559123456','branch','00001');
    x.form.dataset={};
    x.form.checkValidity=()=>x.fields.TaxNumber.value==='' || /^\d{13}$/.test(x.fields.TaxNumber.value);
    const dispatch=x.fields.TaxNumber.dispatchEvent.bind(x.fields.TaxNumber);
    x.fields.TaxNumber.dispatchEvent=event=>{
        const result=dispatch(event);
        if(event.bubbles)x.form.dispatchEvent(new Event(event.type));
        return result;
    };
    wire(x.form,x.submit);
    wireInstantQuotationAddressValidation(x.form,x.submit);
    x.fields.TaxNumber.value='--';x.fields.TaxNumber.dispatchEvent(new Event('input',{bubbles:true}));
    assert.equal(x.submit.disabled,true);
    x.fields.TaxNumber.dispatchEvent(new Event('blur'));
    assert.equal(x.fields.TaxNumber.value,'');assert.equal(x.submit.disabled,false);
});
