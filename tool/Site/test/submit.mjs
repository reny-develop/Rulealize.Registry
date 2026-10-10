// What the submit page checks, run against the script the site serves.
//
//   node tool/Site/test/submit.mjs <site folder>
//
// The script is loaded out of the rendered site rather than kept beside this file, for the
// reason search.mjs gives: a copy would be the thing tested and the page would be the thing
// served. With no `document`, the page half of it does nothing and only `Submit` is left, which
// is the half that decides anything.
//
// The cases hold it to the scripts a submission goes through after it is made — the patterns
// of gate.sh, the line insert.sh writes, the field ids of the issue form — so that the page
// never says yes to something the registry will refuse, or no to something it would take.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { deflateRawSync } from 'node:zlib';

const site = process.argv[2];
if (!site) {
    console.error('usage: node submit.mjs <site folder>');
    process.exit(2);
}

const source = readFileSync(join(site, 'submit', 'submit.js'), 'utf8');
const Submit = new Function(`${source}\nreturn Submit;`)();
const page = readFileSync(join(site, 'submit', 'index.html'), 'utf8');

let failed = 0;
const report = (ok, what, detail = '') => {
    if (ok) {
        console.log(`  ok    ${what}`);
    } else {
        console.log(`  FAIL  ${what}${detail ? `\n          ${detail}` : ''}`);
        failed++;
    }
};

const index = {
    plugins: [
        { id: 'Rulealize.Plugin.Grid', namespace: 'grid', prefix: null },
        { id: 'Rulealize.Plugin.State', namespace: 'state', prefix: '$' }
    ],
    ruleSets: [{ id: 'Rulealize.RuleSet.Quota' }],
    reserved: { namespaces: ['str'], prefixes: ['|'] }
};

const plugin = (over = {}) => ({ kind: 'Plugin', id: 'Acme.Plugin.Dice', version: '1.0.0', namespace: 'dice', prefix: null, ...over });
const states = (checks) => checks.map(c => c.state);
const said = (checks, state, text) => checks.some(c => c.state === state && (c.title + ' ' + (c.detail ?? '')).includes(text));

console.log('submit:');

// ── what the index alone can say ────────────────────────────────────────────────────

report(states(Submit.local(index, plugin())).every(s => s === 'ok'), 'agrees with a new plugin claiming a free namespace');
report(said(Submit.local(index, plugin({ namespace: 'grid' })), 'err', 'claimed by Rulealize.Plugin.Grid'), 'refuses a namespace somebody holds, and says who');
report(said(Submit.local(index, plugin({ namespace: 'str' })), 'err', 'reserved'), 'refuses a reserved namespace');
report(said(Submit.local(index, plugin({ namespace: 'Dice' })), 'err', 'Lowercase'), 'refuses a namespace that is not lowercase');
report(said(Submit.local(index, plugin({ id: 'rulealize.plugin.grid' })), 'err', 'Already in the ledger'), 'refuses a package already submitted, however it is cased');
report(said(Submit.local(index, plugin({ id: 'Acme Rules; rm' })), 'err', 'Not a package identifier'), 'refuses what is not a package identifier');
report(said(Submit.local(index, plugin({ prefix: '$' })), 'warn', 'also reserved by Rulealize.Plugin.State'), 'warns of a shared shorthand character without refusing it');
report(said(Submit.local(index, plugin({ prefix: '|' })), 'err', 'refused'), 'refuses a reserved shorthand character');
report(said(Submit.local(index, plugin({ prefix: '!!' })), 'err', 'one shorthand character or none'), 'refuses two characters');
report(states(Submit.local(index, plugin({ prefix: '' }))).includes('wait'), 'waits for a character it was told is coming');
report(states(Submit.local(index, { kind: 'Rule set', id: 'Acme.Rules.Approval', version: '1.0.0', namespace: 'x', prefix: '|' })).every(s => s === 'ok'),
    'asks nothing of a rule set beyond its package');
report(states(Submit.local(index, plugin({ id: '' }))).join() === 'wait', 'waits for a package before saying anything');

// ── what nuget.org says ─────────────────────────────────────────────────────────────

const answer = (body, status = 200) => Promise.resolve({
    status,
    ok: status >= 200 && status < 300,
    json: () => Promise.resolve(body),
    arrayBuffer: () => Promise.resolve(body)
});

const listed = await Submit.versions(() => answer({ versions: ['0.9.0', '1.0.0-beta', '1.0.0', '1.1.0'] }), 'Acme.Plugin.Dice');
report(JSON.stringify(listed) === JSON.stringify(['1.1.0', '1.0.0', '0.9.0']), 'offers released versions only, newest first', JSON.stringify(listed));
report(await Submit.versions(() => answer({}, 404), 'Acme.Plugin.Dice') === null, 'says when nuget.org has never heard of a package');

// A package built here, the way `dotnet pack` would: a zip with a ruleset folder, deflated.
function zip(files) {
    const locals = [];
    const centrals = [];
    let offset = 0;
    for (const [name, text] of Object.entries(files)) {
        const nameBytes = Buffer.from(name);
        const data = deflateRawSync(Buffer.from(text));
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(8, 8);
        local.writeUInt32LE(data.length, 18);
        local.writeUInt32LE(Buffer.byteLength(text), 22);
        local.writeUInt16LE(nameBytes.length, 26);
        const central = Buffer.alloc(46);
        central.writeUInt32LE(0x02014b50, 0);
        central.writeUInt16LE(8, 10);
        central.writeUInt32LE(data.length, 20);
        central.writeUInt32LE(Buffer.byteLength(text), 24);
        central.writeUInt16LE(nameBytes.length, 28);
        central.writeUInt32LE(offset, 42);
        locals.push(local, nameBytes, data);
        centrals.push(central, nameBytes);
        offset += 30 + nameBytes.length + data.length;
    }
    const directory = Buffer.concat(centrals);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(Object.keys(files).length, 8);
    end.writeUInt16LE(Object.keys(files).length, 10);
    end.writeUInt32LE(directory.length, 12);
    end.writeUInt32LE(offset, 16);
    const bytes = Buffer.concat([...locals, directory, end]);
    return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length);
}

const doc = (id, version) => `{
  "$schema": "rulealize/ruleset/v1",
  // A document carries comments, and the reader keeps strings that look like them.
  "id": "${id}",
  "version": "${version}",
  "note": "http://not-a-comment",
  "inputs": { "raise": {}, },
}`;

const packageOf = (files) => () => answer(zip(files));

const good = await Submit.documents(packageOf({ 'ruleset/approval.json': doc('Acme.Rules.Approval', '1.0.0') }), 'Acme.Rules.Approval', '1.0.0');
report(states(good).every(s => s === 'ok'), 'agrees with a document package declaring its own identifier and version', JSON.stringify(good));

const parts = await Submit.documents(packageOf({
    'ruleset/line.json': doc('Acme.Rules.Approval.Line', '1.0.0'),
    'ruleset/approval.json': doc('Acme.Rules.Approval', '1.0.0')
}), 'Acme.Rules.Approval', '1.0.0');
report(states(parts).every(s => s === 'ok'), 'finds the entry point among its parts');

report(said(await Submit.documents(packageOf({ 'ruleset/a.json': doc('acme.rules.approval', '1.0.0') }), 'Acme.Rules.Approval', '1.0.0'), 'err', 'No document declares'),
    'refuses an identifier that differs only in case');
report(said(await Submit.documents(packageOf({ 'ruleset/a.json': doc('Acme.Rules.Approval', '0.9.0') }), 'Acme.Rules.Approval', '1.0.0'), 'err', 'says version 0.9.0'),
    'refuses a document whose version is not the package\'s');
report(said(await Submit.documents(packageOf({ 'lib/net10.0/A.dll': 'MZ', 'ruleset/a.json': doc('Acme.Rules.Approval', '1.0.0') }), 'Acme.Rules.Approval', '1.0.0'), 'err', 'lib folder'),
    'refuses a package with an assembly in it');
report(said(await Submit.documents(packageOf({ 'readme.md': '#' }), 'Acme.Rules.Approval', '1.0.0'), 'err', 'Nothing in ruleset/'),
    'refuses a package with no document');

// ── what it hands on ────────────────────────────────────────────────────────────────

// The line insert.sh writes, character for character, for both kinds.
report(Submit.line(plugin()) === '{ "id": "Acme.Plugin.Dice", "version": "1.0.0", "namespace": "dice", "prefix": null }',
    'writes the plugin line the ledger will hold', Submit.line(plugin()));
report(Submit.line({ kind: 'Rule set', id: 'Acme.Rules.Approval', version: '1.0.0' }) === '{ "id": "Acme.Rules.Approval", "version": "1.0.0" }',
    'writes the rule set line the ledger will hold');

const url = new URL(Submit.issueUrl(plugin()));
const form = readFileSync(new URL('../../../.github/ISSUE_TEMPLATE/submit.yml', import.meta.url), 'utf8');
const ids = [...form.matchAll(/^\s+id: (\S+)$/gm)].map(m => m[1]);
report(url.searchParams.get('template') === 'submit.yml', 'opens the submission form');
report([...url.searchParams.keys()].filter(k => !['template', 'title'].includes(k)).every(k => ids.includes(k)),
    'fills only fields the form has', `${[...url.searchParams.keys()]} vs ${ids}`);
report(url.searchParams.get('shorthand') === '(none)' && url.searchParams.get('kind') === 'Plugin', 'says no shorthand the way the form is read');
report(!new URL(Submit.issueUrl({ kind: 'Rule set', id: 'Acme.Rules.Approval', version: '1.0.0' })).searchParams.has('namespace'),
    'leaves a rule set\'s namespace empty');

report(page.includes('<script src="submit.js"></script>'), 'the page loads the script beside it');

console.log();
if (failed > 0) {
    console.log(`${failed} failed.`);
    process.exit(1);
}
console.log('all as expected.');
