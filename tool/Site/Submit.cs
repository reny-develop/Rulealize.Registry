// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

// The submit page: a form that checks a package against nuget.org and the index before anything
// is sent, and then opens the submission on GitHub with every field filled in.
//
// Everything it checks is checked again after the submission is made — by
// .github/workflows/submit.yml, which reads the issue, builds the ledger it would make and loads
// the package. This page is there so that a mistake is found while the person is still looking
// at it rather than a few minutes later on an issue. It decides nothing, and it is written so
// that the two agree: the same patterns, the same reserved list, the same line.
//
// The page is static and the script is a file beside it, so that tool/Site/test/submit.mjs can
// load the script and run its checks without a browser.
internal static partial class Program
{
    internal const string SubmitPage = """
        <p class="crumbs"><a href="../index.html#submit">Publish</a> <span>/</span> Submit</p>
        <h1 class="title">Submit a package</h1>
        <p class="lede">Claim the names your package uses. What you enter is checked against nuget.org on this
        page, and read again out of the package by the registry after you submit.</p>
        <ol class="stepper" id="stepper">
        <li data-n="1" class="done">Package</li>
        <li data-n="2" class="on">Checks</li>
        <li data-n="3">Submit on GitHub</li>
        </ol>

        <div class="cols" id="fill">
        <div class="main">
        <form class="card form" id="submit" novalidate>
        <div class="field">
        <span class="label-text">What are you submitting?</span>
        <div class="seg-row">
        <div class="seg">
        <label><input type="radio" name="kind" value="Plugin" checked><span>Plugin</span></label>
        <label><input type="radio" name="kind" value="Rule set"><span>Rule set</span></label>
        </div>
        <span class="meta">A rule set needs only its package and version — the document inside says the rest.</span>
        </div>
        </div>
        <div class="field" id="f-package">
        <label for="package">Package identifier</label>
        <input type="text" id="package" autocomplete="off" spellcheck="false" placeholder="Acme.Plugin.Dice">
        <p class="help" data-help="The identifier it is published under on nuget.org.">The identifier it is published under on nuget.org.</p>
        </div>
        <div class="field" id="f-version">
        <label for="version">Version</label>
        <select id="version" disabled><option value="">Enter a package first</option></select>
        <p class="help" data-help="Only versions already published on nuget.org are offered. Your claims are read at this one.">Only versions already published on nuget.org are offered. Your claims are read at this one.</p>
        </div>
        <div class="field plugin-only" id="f-namespace">
        <label for="namespace">Namespace</label>
        <input type="text" id="namespace" autocomplete="off" spellcheck="false" placeholder="dice">
        <p class="help" data-help="Lowercase letters and digits, starting with a letter. Every operation is written under it.">Lowercase letters and digits, starting with a letter. Every operation is written under it.</p>
        </div>
        <div class="field plugin-only" id="f-shorthand">
        <span class="label-text">Shorthand character</span>
        <div class="seg-row">
        <div class="seg">
        <label><input type="radio" name="reserve" value="none" checked><span>None</span></label>
        <label><input type="radio" name="reserve" value="one"><span>Reserve one</span></label>
        </div>
        <input type="text" id="shorthand" maxlength="2" autocomplete="off" aria-label="Shorthand character" hidden>
        </div>
        <p class="help" data-help="Most plugins reserve none. Under a dozen exist, and they are shared rather than owned.">Most plugins reserve none. Under a dozen exist, and they are shared rather than owned.</p>
        </div>
        <div class="form-foot">
        <button class="button" id="review" type="submit" disabled>Review the claim →</button>
        <p class="meta" id="status">Nothing is submitted yet.</p>
        </div>
        </form>
        </div>
        <aside class="side">
        <div class="card">
        <p class="eyebrow">Checked on this page</p>
        <ul class="checks" id="checks"><li class="wait">Enter a package identifier</li></ul>
        <p class="eyebrow">Checked by the registry after you submit</p>
        <ul class="checks" id="later"></ul>
        </div>
        <div class="card soft">
        <h4>Not on nuget.org yet?</h4>
        <p>Publish first — a new version can take about fifteen minutes to be listed.
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/publish.md">How to build a package →</a></p>
        </div>
        </aside>
        </div>

        <div class="cols" id="confirm" hidden>
        <div class="main">
        <section class="card">
        <p class="eyebrow">You are claiming</p>
        <p class="claim" id="claim-name"></p>
        <table class="facts" id="claim-facts"></table>
        <pre><code id="claim-line"></code></pre>
        </section>
        <div class="permanent">
        <h4>A claim is permanent</h4>
        <p id="permanent"></p>
        <label><input type="checkbox" id="understand"> I understand</label>
        </div>
        <h2>What happens next</h2>
        <ol class="next">
        <li><b>GitHub opens your submission, filled in</b>Press the green “Submit new issue” button. That is all — a
        GitHub account is the only thing you need. No fork, no pull request, no file to edit.</li>
        <li><b>The registry reads your package</b>It reads the package the way an application would and compares
        what it says with this page, usually within a few minutes. GitHub notifies you of the result.</li>
        <li><b>Your claim is recorded and the site updates</b>If anything disagrees, the result says what. Edit your
        submission on GitHub and it is read again — nobody has to approve anything.</li>
        </ol>
        <p class="actions">
        <a class="button" id="continue" href="#" aria-disabled="true" target="_blank" rel="noopener">Continue on GitHub ↗</a>
        <a class="button ghost" id="back" href="#">Back to edit</a>
        </p>
        </div>
        <aside class="side">
        <div class="card">
        <p class="eyebrow">Not part of your submission</p>
        <p class="meta">Read out of the package instead, so there is nothing to keep in step:</p>
        <ul class="meta">
        <li>every operation a plugin registers, or every input a rule set offers</li>
        <li>the description, repository and licence</li>
        <li>what it was built against, and what it requires</li>
        <li>every later release — no new submission</li>
        </ul>
        </div>
        </aside>
        </div>

        <noscript><p class="callout">This page checks a package in your browser before it opens the submission, and
        that needs JavaScript. The form it opens is
        <a href="https://github.com/reny-develop/Rulealize.Registry/issues/new?template=submit.yml">here</a>, and
        everything in it is checked again after you send it.</p></noscript>
        <script src="submit.js"></script>
        """;

    internal const string SubmitScript = """
        'use strict';

        // What the submit page checks, as functions of what it was given, so that they can be run
        // without a page. The rules are gate.sh's and declared.sh's, restated for a browser: when
        // one of those changes, this has to change with it, and the cases in
        // tool/Site/test/submit.mjs are where the two are held to each other.
        const Submit = (() => {
          const feed = 'https://api.nuget.org/v3-flatcontainer';
          const repository = 'reny-develop/Rulealize.Registry';
          const idPattern = /^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$/;
          const versionPattern = /^[0-9]+\.[0-9]+\.[0-9]+$/;
          const namespacePattern = /^[a-z][a-z0-9]*$/;

          // What can be said from the index alone: the shape of each name, and whether somebody
          // already holds it. `s` is { kind, id, version, namespace, prefix }, prefix null for none.
          function local(index, s) {
            const plugins = index.plugins ?? [];
            const ruleSets = index.ruleSets ?? [];
            const reserved = index.reserved ?? { namespaces: [], prefixes: [] };
            const out = [];

            if (!s.id) {
              return [{ state: 'wait', title: 'Enter a package identifier' }];
            }

            if (!idPattern.test(s.id)) {
              return [{ state: 'err', field: 'package', title: 'Not a package identifier', detail: 'Letters, digits, dots, hyphens and underscores, as nuget.org allows.' }];
            }

            // nuget.org treats an identifier without regard to case, so a second submission of one
            // package is the same package however it is cased.
            const known = [...plugins.map(p => p.id), ...ruleSets.map(r => r.id)]
              .find(id => id.toLowerCase() === s.id.toLowerCase());
            out.push(known
              ? { state: 'err', field: 'package', title: 'Already in the ledger', detail: known + ' was submitted before. Later releases need no new submission — they are read every day.' }
              : { state: 'ok', title: 'Not already in the ledger' });

            if (s.kind !== 'Plugin') {
              return out;
            }

            if (!s.namespace) {
              out.push({ state: 'wait', title: 'Enter a namespace' });
            } else if (!namespacePattern.test(s.namespace)) {
              out.push({ state: 'err', field: 'namespace', title: 'Not a namespace', detail: 'Lowercase letters and digits, starting with a letter.' });
            } else if ((reserved.namespaces ?? []).includes(s.namespace)) {
              out.push({ state: 'err', field: 'namespace', title: 'Namespace ' + s.namespace + ' is reserved', detail: 'The registry reserves it and grants it to nobody.' });
            } else {
              const holder = plugins.find(p => p.namespace === s.namespace);
              out.push(holder
                ? { state: 'err', field: 'namespace', title: 'Namespace ' + s.namespace + ' is taken', detail: s.namespace + ' is claimed by ' + holder.id + '. A namespace has exactly one owner, permanently.' }
                : { state: 'ok', title: 'Namespace ' + s.namespace + ' is free', detail: 'Not claimed, not reserved.' });
            }

            if (s.prefix === null) {
              out.push({ state: 'ok', title: 'No shorthand character' });
            } else if (!s.prefix) {
              out.push({ state: 'wait', title: 'Enter the shorthand character' });
            } else if ([...s.prefix].length !== 1) {
              out.push({ state: 'err', field: 'shorthand', title: 'One character', detail: 'A plugin claims one shorthand character or none.' });
            } else if ((reserved.prefixes ?? []).includes(s.prefix)) {
              out.push({ state: 'err', field: 'shorthand', title: 'Shorthand ' + s.prefix + ' is refused', detail: 'Ordinary data begins with it, so the registry grants it to nobody.' });
            } else {
              const sharing = plugins.filter(p => p.prefix === s.prefix);
              out.push(sharing.length
                ? { state: 'warn', field: 'shorthand', title: 'Shorthand ' + s.prefix + ' is shared', detail: s.prefix + ' is also reserved by ' + sharing.map(p => p.id).join(', ') + '. That is allowed — a rule set that loads both writes "' + s.prefix + (s.namespace || 'name') + ':…" to say which it means.' }
                : { state: 'ok', title: 'Shorthand ' + s.prefix + ' is free' });
            }

            return out;
          }

          // The released versions nuget.org serves, newest first, or null when it has never heard
          // of the package. A prerelease is not one a ledger line can name.
          async function versions(fetchImpl, id) {
            const answer = await fetchImpl(feed + '/' + id.toLowerCase() + '/index.json');
            if (answer.status === 404) {
              return null;
            }

            if (!answer.ok) {
              throw new Error('nuget.org did not answer');
            }

            const data = await answer.json();
            return (data.versions ?? []).filter(v => versionPattern.test(v)).reverse();
          }

          // A .nupkg is a zip. These two read its directory and one file out of it, which is all a
          // rule set check needs, with the browser's own inflater.
          function entries(bytes) {
            const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
            let end = -1;
            for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 65557); i--) {
              if (view.getUint32(i, true) === 0x06054b50) {
                end = i;
                break;
              }
            }

            if (end < 0) {
              throw new Error('not a zip');
            }

            const count = view.getUint16(end + 10, true);
            let at = view.getUint32(end + 16, true);
            const names = new TextDecoder();
            const out = [];
            for (let n = 0; n < count; n++) {
              if (view.getUint32(at, true) !== 0x02014b50) {
                throw new Error('not a zip directory');
              }

              const nameLength = view.getUint16(at + 28, true);
              out.push({
                name: names.decode(bytes.subarray(at + 46, at + 46 + nameLength)),
                method: view.getUint16(at + 10, true),
                size: view.getUint32(at + 20, true),
                offset: view.getUint32(at + 42, true)
              });
              at += 46 + nameLength + view.getUint16(at + 30, true) + view.getUint16(at + 32, true);
            }

            return out;
          }

          async function read(bytes, entry) {
            const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
            const start = entry.offset + 30 + view.getUint16(entry.offset + 26, true) + view.getUint16(entry.offset + 28, true);
            const data = bytes.subarray(start, start + entry.size);
            if (entry.method === 0) {
              return data;
            }

            if (entry.method !== 8) {
              throw new Error('compressed in a way this cannot read');
            }

            const stream = new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
            return new Uint8Array(await new Response(stream).arrayBuffer());
          }

          // A rule set document carries comments, so it is read the way the runtime reads it:
          // comments and trailing commas outside strings are not part of the value.
          function jsonc(text) {
            let out = '';
            let i = 0;
            let quoted = false;
            text = text.replace(/^﻿/, '');
            while (i < text.length) {
              const c = text[i];
              if (quoted) {
                out += c;
                if (c === '\\') {
                  out += text[i + 1] ?? '';
                  i += 2;
                  continue;
                }

                if (c === '"') {
                  quoted = false;
                }

                i++;
              } else if (c === '"') {
                quoted = true;
                out += c;
                i++;
              } else if (c === '/' && text[i + 1] === '/') {
                while (i < text.length && text[i] !== '\n') {
                  i++;
                }
              } else if (c === '/' && text[i + 1] === '*') {
                const close = text.indexOf('*/', i + 2);
                i = close < 0 ? text.length : close + 2;
              } else if (c === ',' && /^\s*[}\]]/.test(text.slice(i + 1))) {
                i++;
              } else {
                out += c;
                i++;
              }
            }

            return JSON.parse(out);
          }

          // What a rule set package says about itself: no assembly, a ruleset folder, one document
          // in it declaring the package's identifier exactly, at the package's version.
          async function documents(fetchImpl, id, version) {
            const lower = id.toLowerCase();
            const answer = await fetchImpl(feed + '/' + lower + '/' + version + '/' + lower + '.' + version + '.nupkg');
            if (!answer.ok) {
              return [{ state: 'err', field: 'version', title: version + ' could not be downloaded' }];
            }

            const bytes = new Uint8Array(await answer.arrayBuffer());
            const list = entries(bytes);
            const out = [];

            out.push(list.some(e => /^lib\//i.test(e.name))
              ? { state: 'err', field: 'package', title: 'It holds a lib folder', detail: 'A package with one is read as an assembly. A rule set package carries none.' }
              : { state: 'ok', title: 'No lib folder — a document package' });

            const held = list.filter(e => /^ruleset\/[^/]+\.json$/i.test(e.name));
            if (held.length === 0) {
              out.push({ state: 'err', field: 'package', title: 'Nothing in ruleset/', detail: 'A rule set package holds its document in a ruleset folder.' });
              return out;
            }

            const declared = [];
            let entry = null;
            for (const file of held) {
              try {
                const doc = jsonc(new TextDecoder().decode(await read(bytes, file)));
                declared.push(doc.id);
                if (doc.id === id) {
                  entry = { file: file.name, doc };
                }
              } catch {
                declared.push('(unreadable ' + file.name + ')');
              }
            }

            if (!entry) {
              out.push({ state: 'err', field: 'package', title: 'No document declares ' + id, detail: 'They declare ' + declared.join(', ') + '. The identifier is compared exactly, case included.' });
              return out;
            }

            out.push({ state: 'ok', title: entry.file + ' declares ' + id, detail: 'The id matches the package, case included.' });
            out.push(entry.doc.version === version
              ? { state: 'ok', title: 'The document says version ' + version }
              : { state: 'err', field: 'version', title: 'The document says version ' + entry.doc.version, detail: 'The package is ' + version + ', so nothing that names it could resolve to it.' });
            return out;
          }

          // The line the ledger will hold, written the way .github/submit/insert.sh writes it.
          function line(s) {
            const entry = s.kind === 'Plugin'
              ? { id: s.id, version: s.version, namespace: s.namespace, prefix: s.prefix }
              : { id: s.id, version: s.version };
            return '{ ' + Object.entries(entry).map(([k, v]) => JSON.stringify(k) + ': ' + JSON.stringify(v)).join(', ') + ' }';
          }

          // The issue form, with every field filled in. The names are the field ids in
          // .github/ISSUE_TEMPLATE/submit.yml.
          function issueUrl(s) {
            const query = new URLSearchParams({
              template: 'submit.yml',
              title: 'Submit ' + s.id + ' ' + s.version,
              kind: s.kind,
              package: s.id,
              version: s.version
            });
            if (s.kind === 'Plugin') {
              query.set('namespace', s.namespace);
              query.set('shorthand', s.prefix ?? '(none)');
            }

            return 'https://github.com/' + repository + '/issues/new?' + query.toString();
          }

          return { local, versions, entries, read, jsonc, documents, line, issueUrl };
        })();

        if (typeof document !== 'undefined' && document.getElementById('submit')) {
          const $ = id => document.getElementById(id);
          const form = $('submit');
          const order = { err: 0, warn: 1, wait: 2, info: 3, ok: 4 };
          let index = { plugins: [], ruleSets: [] };
          let found = [];
          let asked = '';
          let timer = 0;

          const state = () => ({
            kind: form.elements.kind.value,
            id: $('package').value.trim(),
            version: $('version').value,
            namespace: $('namespace').value.trim(),
            prefix: form.elements.reserve.value === 'one' ? $('shorthand').value.trim() : null
          });

          const item = c => {
            const li = document.createElement('li');
            li.className = c.state;
            li.textContent = c.title;
            if (c.detail) {
              const small = document.createElement('small');
              small.textContent = c.detail;
              li.append(small);
            }
            return li;
          };

          function render() {
            const s = state();
            const plugin = s.kind === 'Plugin';
            document.querySelectorAll('.plugin-only').forEach(el => { el.hidden = !plugin; });
            $('shorthand').hidden = form.elements.reserve.value !== 'one';

            const checks = [...found, ...Submit.local(index, s)];
            $('checks').replaceChildren(...checks.map(item));
            $('later').replaceChildren(item(plugin
              ? { state: 'info', title: 'What the assembly claims', detail: 'Namespace, shorthand and manifest version are read by loading the package — a browser cannot.' }
              : { state: 'info', title: 'The same reading, once more', detail: 'The document is read again where the ledger is written.' }));

            for (const field of ['package', 'version', 'namespace', 'shorthand']) {
              const box = $('f-' + field);
              const help = box.querySelector('.help');
              const worst = checks.filter(c => c.field === field && (c.state === 'err' || c.state === 'warn'))
                .sort((a, b) => order[a.state] - order[b.state])[0];
              box.classList.toggle('err', worst?.state === 'err');
              box.classList.toggle('warn', worst?.state === 'warn');
              help.textContent = worst ? worst.detail ?? worst.title : help.dataset.help;
            }

            const blocked = checks.some(c => c.state === 'err' || c.state === 'wait') || !s.version;
            $('review').disabled = blocked;
            $('status').textContent = blocked
              ? (checks.find(c => c.state === 'err') ? 'Put the marked field right to continue.' : 'Nothing is submitted yet.')
              : 'Nothing is submitted yet.';
          }

          async function lookUp() {
            const s = state();
            const select = $('version');
            found = [];
            if (!s.id || !/^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$/.test(s.id)) {
              select.replaceChildren(new Option('Enter a package first', ''));
              select.disabled = true;
              render();
              return;
            }

            asked = s.id;
            select.replaceChildren(new Option('Looking on nuget.org…', ''));
            select.disabled = true;
            found = [{ state: 'wait', title: 'Looking for ' + s.id + ' on nuget.org' }];
            render();

            try {
              const published = await Submit.versions(fetch, s.id);
              if (asked !== s.id) {
                return;
              }

              if (!published || published.length === 0) {
                found = [{ state: 'err', field: 'package', title: s.id + ' is not on nuget.org', detail: published ? 'It has no released version — a prerelease cannot be submitted.' : 'Publish it first. A new package can take about fifteen minutes to be listed.' }];
                select.replaceChildren(new Option('No released version', ''));
              } else {
                found = [{ state: 'ok', title: s.id + ' is on nuget.org', detail: published.length + ' released version' + (published.length === 1 ? '' : 's') }];
                select.replaceChildren(...published.map(v => new Option(v, v)));
                select.disabled = false;
                await readVersion();
                return;
              }
            } catch {
              found = [{ state: 'err', field: 'package', title: 'nuget.org could not be asked', detail: 'Try again in a moment.' }];
            }

            render();
          }

          async function readVersion() {
            const s = state();
            found = found.filter(c => !c.document);
            if (s.version) {
              found.push({ state: 'ok', title: s.version + ' is published' });
            }

            if (s.kind === 'Rule set' && s.version) {
              found.push({ state: 'wait', title: 'Reading the document in ' + s.version, document: true });
              render();
              try {
                const read = await Submit.documents(fetch, s.id, s.version);
                found = found.filter(c => !c.document).concat(read.map(c => ({ ...c, document: true })));
              } catch {
                found = found.filter(c => !c.document).concat([{ state: 'err', field: 'version', title: 'The package could not be read', document: true }]);
              }
            }

            render();
          }

          function confirm() {
            const s = state();
            $('fill').hidden = true;
            $('confirm').hidden = false;
            const steps = $('stepper').children;
            steps[1].className = 'done';
            steps[2].className = 'on';

            const plugin = s.kind === 'Plugin';
            $('claim-name').textContent = plugin ? s.namespace : s.id;
            const rows = plugin
              ? [['Kind', 'plugin'], ['Package', s.id], ['Version read', s.version], ['Namespace', s.namespace], ['Shorthand', s.prefix ?? 'none']]
              : [['Kind', 'rule set'], ['Package', s.id], ['Version read', s.version]];
            $('claim-facts').replaceChildren(...rows.map(([k, v]) => {
              const tr = document.createElement('tr');
              const th = document.createElement('th');
              th.textContent = k;
              const td = document.createElement('td');
              const code = document.createElement('code');
              code.textContent = v;
              td.append(code);
              tr.append(th, td);
              return tr;
            }));
            $('claim-line').textContent = Submit.line(s);
            $('permanent').textContent = plugin
              ? s.namespace + ' will belong to ' + s.id + ' for good. It cannot be released, transferred or renamed later — check the spelling now.'
              : s.id + ' is recorded for good, and every later release is held to it. Check the spelling now.';
            $('understand').checked = false;
            const go = $('continue');
            go.setAttribute('aria-disabled', 'true');
            go.href = Submit.issueUrl(s);
            window.scrollTo(0, 0);
          }

          fetch('../index.json').then(r => r.json()).then(data => { index = data; render(); }).catch(() => render());

          $('package').addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(lookUp, 400);
            render();
          });
          $('version').addEventListener('change', readVersion);
          form.addEventListener('input', e => { if (e.target.id !== 'package') { render(); } });
          form.addEventListener('change', e => {
            if (e.target.name === 'kind') {
              readVersion();
            } else {
              render();
            }
          });
          form.addEventListener('submit', e => {
            e.preventDefault();
            if (!$('review').disabled) {
              confirm();
            }
          });
          $('understand').addEventListener('change', e => {
            $('continue').setAttribute('aria-disabled', e.target.checked ? 'false' : 'true');
          });
          $('back').addEventListener('click', e => {
            e.preventDefault();
            $('confirm').hidden = true;
            $('fill').hidden = false;
            const steps = $('stepper').children;
            steps[1].className = 'on';
            steps[2].className = '';
          });
          render();
        }
        """;
}
