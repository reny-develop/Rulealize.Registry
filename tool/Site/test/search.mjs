// What the search on the front page does, run against the index it reads.
//
//   node tool/Site/test/search.mjs <site folder>
//
// The script is the one part of this site that is a program rather than a page, and it was the
// one part nothing ran. It is extracted from the rendered index.html rather than kept beside
// this file, because a copy would be the thing tested and the page would be the thing served.
//
// The stubs are the smallest that let it run: an element is a bag of properties with a
// `replaceChildren` that records, and `fetch` answers with the catalogue that was built. What
// is asserted is the behaviour a person gets — that a name they arrived holding finds the
// thing that owns it, whichever of the two kinds it turns out to be.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const site = process.argv[2];
if (!site) {
    console.error('usage: node search.mjs <site folder>');
    process.exit(2);
}

const page = readFileSync(join(site, 'index.html'), 'utf8');
const script = page.match(/<script>([\s\S]*?)<\/script>/);
if (!script) {
    console.error('index.html carries no script, and the search is one.');
    process.exit(1);
}

const index = JSON.parse(readFileSync(join(site, 'index.json'), 'utf8'));

// ── the smallest DOM the script needs ──────────────────────────────────────────────

const made = [];
const element = (tag) => ({
    tag,
    className: '',
    textContent: '',
    href: '',
    children: [],
    append(...kids) { this.children.push(...kids); },
    replaceChildren(...kids) { this.children = kids; }
});

const box = element('input');
box.value = '';
let onInput = () => {};
box.addEventListener = (name, handler) => { if (name === 'input') onInput = handler; };

const results = element('ul');
const count = element('p');

globalThis.document = {
    getElementById: (id) => ({ q: box, results, count }[id]),
    createElement: (tag) => { const made_ = element(tag); made.push(made_); return made_; }
};

let settled;
const ready = new Promise((resolve) => { settled = resolve; });
globalThis.fetch = () => Promise.resolve({ json: () => { queueMicrotask(settled); return Promise.resolve(index); } });

new Function(script[1])();
await ready;
await new Promise((resolve) => setTimeout(resolve, 0));

// ── the cases ──────────────────────────────────────────────────────────────────────

let failed = 0;

const search = (term) => {
    box.value = term;
    onInput();
    return results.children.map((li) => ({
        name: li.children[0].textContent,
        href: li.children[0].href,
        kind: li.children[1].textContent,
        from: li.children[2].textContent
    }));
};

const report = (ok, what, detail = '') => {
    if (ok) {
        console.log(`  ok    ${what}`);
    } else {
        console.log(`  FAIL  ${what}${detail ? `\n          ${detail}` : ''}`);
        failed++;
    }
};

console.log('search:');

report(/\d+ operations and \d+ rule sets indexed\./.test(count.textContent),
    'says what it holds before anything is typed', count.textContent);

// The terms come out of the catalogue this was pointed at rather than being written here, so
// that the same cases run against a fixture and against the index as it actually stands. A
// test that knew the names would only ever be true of the day it was written.
const anOperation = index.operations?.[0];
const aRuleSet = index.ruleSets?.find((r) => (r.inputs ?? []).length > 0);

if (!anOperation || !aRuleSet) {
    console.error('this catalogue has no operation, or no rule set with an input, to search for.');
    process.exit(2);
}

// An operation name, which is what this search has always answered for.
const found = search(anOperation.op);
report(found.some((hit) => hit.name === anOperation.op && hit.href === `op/${anOperation.op}.html`),
    'finds an operation and links to its page');
report(found.filter((hit) => hit.name === anOperation.op).every((hit) => hit.kind !== 'rule set'),
    'does not answer an operation query with a rule set');

// An input name, which is the half that is new: it is a name out of somebody else's document
// with no way to know whose, which is the question this whole page exists for.
const input = aRuleSet.inputs[0];
const inputs = search(input).filter((hit) => hit.kind === 'input' && hit.name === input);
report(inputs.length > 0, `finds an input by name (${input})`);
report(inputs.some((hit) => hit.from === aRuleSet.id && hit.href === `ruleset/${aRuleSet.id}.html`),
    'says which rule set the input belongs to, and links to it');

// A rule set by its own identifier, which is what somebody reading the table types. The last
// dotted segment, because that is the part a person remembers out of a package-shaped name.
const shorthand = aRuleSet.id.split('.').pop();
report(search(shorthand).some((hit) => hit.kind === 'rule set' && hit.name === aRuleSet.id),
    `finds a rule set by part of its identifier (${shorthand})`);

// One term, both kinds. `s` is in operation names and input names alike, and the point of one
// box is that a searcher does not have to know which they wanted.
const both = search('s');
report(new Set(both.map((hit) => hit.kind)).size > 1,
    'answers one term with both kinds at once');

const none = search('zzzznotathing');
report(none.length === 0, 'finds nothing that is not there');
report(/^0 of \d+ match\.$/.test(count.textContent), 'counts a miss as a miss', count.textContent);

search('');
report(results.children.length === 0 && /indexed\.$/.test(count.textContent),
    'goes back to the summary when the box is cleared');

// Every href it offers has to be a page that was written, or the search is a list of 404s.
const everything = search('');
box.value = 'e';
onInput();
const missing = results.children
    .map((li) => li.children[0].href)
    .filter((href) => {
        try { readFileSync(join(site, href)); return false; } catch { return true; }
    });
report(missing.length === 0, 'every page it offers exists', missing.slice(0, 3).join(', '));

console.log();
if (failed > 0) {
    console.log(`${failed} failed.`);
    process.exit(1);
}
console.log('all as expected.');
