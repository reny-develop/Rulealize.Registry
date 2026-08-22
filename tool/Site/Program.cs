// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text;
using System.Text.Json;

// Renders the catalogue as pages.
//
//   dotnet run --project tool/Site -- <catalogue folder> <output folder>
//
// The output holds the JSON as well as the HTML, because the static files are the API:
// /index.json and /plugin/<id>.json are what the resolver and anything else reads, and the
// pages are a second view of the same bytes rather than a separate build of the same facts.
// The search on the front page fetches /index.json like any other client, which is the
// cheapest way of finding out whether that file is usable.
//
// There is one page per operation. It holds nothing the plugin's page does not, and it exists
// because `grid.ray` is the thing somebody has in their hand when they arrive — a name out of
// a rule set, or out of an error message, with no way to know which vocabulary it belongs to.
// Answering that is the one question nuget.org structurally cannot.
//
// Every string that came from a package description is escaped on the way in. It is written
// by whoever published the plugin, and the catalogue keeps it escaped in JSON for the same
// reason: none of it is ours.

if (args.Length is not 2)
{
    Console.Error.WriteLine("usage: Site <catalogue folder> <output folder>");
    return 2;
}

string catalogue = args[0];
string output = args[1];

Directory.CreateDirectory(output);
Directory.CreateDirectory(Path.Combine(output, "plugin"));
Directory.CreateDirectory(Path.Combine(output, "op"));

using JsonDocument index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(catalogue, "index.json")));

List<Plugin> plugins = [];
foreach (JsonElement summary in index.RootElement.GetProperty("plugins").EnumerateArray())
{
    string id = summary.GetProperty("id").GetString()!;
    string path = Path.Combine(catalogue, "plugin", $"{id}.json");

    using JsonDocument detail = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    JsonElement root = detail.RootElement;

    List<Release> releases = [];
    foreach (JsonElement release in root.GetProperty("versions").EnumerateArray())
    {
        Dictionary<string, List<string>> operations = [];
        foreach (JsonProperty kind in release.GetProperty("operations").EnumerateObject())
        {
            operations[kind.Name] = [.. kind.Value.EnumerateArray().Select(static op => op.GetString()!)];
        }

        releases.Add(new Release(
            release.GetProperty("version").GetString()!,
            Text(release, "abstraction"),
            Text(release, "framework"),
            operations));
    }

    plugins.Add(new Plugin(
        id,
        root.GetProperty("namespace").GetString()!,
        root.GetProperty("prefix").ValueKind is JsonValueKind.Null ? null : root.GetProperty("prefix").GetString(),
        root.GetProperty("admitted").GetString()!,
        root.GetProperty("latest").GetString()!,
        Text(root, "description"),
        Text(root, "repository"),
        Text(root, "license"),
        releases));

    File.Copy(path, Path.Combine(output, "plugin", $"{id}.json"), overwrite: true);
}

// An operation name becomes a file name and a URL below. The runtime qualifies it with the
// registering plugin's namespace and does not police the rest of it, so nothing upstream of
// here makes `op` a name rather than a path. They are all checked before anything is written,
// because the first thing a name that is a path would do is write outside this folder.
string[] malformed =
[
    .. plugins
        .SelectMany(static plugin => plugin.Releases)
        .SelectMany(static release => release.Operations.Values)
        .SelectMany(static names => names)
        .Where(static op => !IsOperationName(op))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal),
];

if (malformed.Length is not 0)
{
    Console.Error.WriteLine($"These are not operation names: {string.Join(", ", malformed)}");
    return 1;
}

// The other string that becomes a path here. nuget.org allows nothing else in an identifier
// and the ledger is fetched by it, so this is the belt to that brace.
string[] impossible =
[
    .. plugins
        .Select(static plugin => plugin.Id)
        .Where(static id => id.Length is 0 || !id.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        .Order(StringComparer.Ordinal),
];

if (impossible.Length is not 0)
{
    Console.Error.WriteLine($"These are not package identifiers: {string.Join(", ", impossible)}");
    return 1;
}

File.Copy(Path.Combine(catalogue, "index.json"), Path.Combine(output, "index.json"), overwrite: true);

await File.WriteAllTextAsync(Path.Combine(output, "style.css"), Style, new UTF8Encoding(false));
await WriteFront(plugins, output);

foreach (Plugin plugin in plugins)
{
    await Write(Path.Combine(output, "plugin", $"{plugin.Id}.html"), plugin.Id, "..", PluginBody(plugin));
}

// One page per operation, over the latest release of each plugin — a name withdrawn two
// versions ago is not one to offer, and the plugin's own page still records that it existed.
int written = 0;
foreach (Plugin plugin in plugins)
{
    Release latest = plugin.Releases[^1];
    foreach ((string kind, List<string> operations) in latest.Operations)
    {
        foreach (string op in operations)
        {
            await Write(Path.Combine(output, "op", $"{op}.html"), op, "..", OperationBody(op, kind, plugin));
            written++;
        }
    }
}

Console.Error.WriteLine($"{plugins.Count} plugins, {written} operations -> {output}");
return 0;

static string? Text(JsonElement element, string name) =>
    element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String
        ? value.GetString()
        : null;

// Everything that reaches a page goes through this. What comes out of the catalogue is a
// publisher's prose, and a registry that renders it unescaped is a registry that lets one
// publisher write the page every other plugin is read on.
static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

// The one string here that becomes an href rather than text, and it is whatever the publisher
// put in their .nuspec. Escaping stops it closing the attribute; it does not stop
// `javascript:`, which is a scheme and not a character. Anything that is not an absolute
// http(s) URL is rendered as the text it is.
static string? Link(string? url) =>
    Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https" ? url : null;

// A namespace, a dot, and a member that starts lowercase and carries only letters and digits:
// grid.ray, seq.elementAt. Every operation in the standard distribution is one, the runtime
// requires only the namespace it prefixes, and this is what makes the rest of it a name.
static bool IsOperationName(string op)
{
    int dot = op.IndexOf('.');
    if (dot < 1 || dot == op.Length - 1)
    {
        return false;
    }

    if (!char.IsAsciiLetterLower(op[0]) || !char.IsAsciiLetterLower(op[dot + 1]))
    {
        return false;
    }

    for (int i = 1; i < dot; i++)
    {
        if (!char.IsAsciiLetterLower(op[i]) && !char.IsAsciiDigit(op[i]))
        {
            return false;
        }
    }

    for (int i = dot + 2; i < op.Length; i++)
    {
        if (!char.IsAsciiLetterOrDigit(op[i]))
        {
            return false;
        }
    }

    return true;
}

static async Task Write(string path, string title, string root, string body) =>
    await File.WriteAllTextAsync(path, $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <title>{H(title)} — Rulealize Registry</title>
        <link rel="stylesheet" href="{root}/style.css">
        </head>
        <body>
        <header>
        <a class="home" href="{root}/index.html">Rulealize Registry</a>
        <p class="notice">Pre-release. The ledger is authoritative; these pages are generated from it.</p>
        </header>
        <main>
        {body}
        </main>
        <footer>
        <a href="https://github.com/reny-develop/Rulealize.Registry">Repository</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md">Grant policy</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/ledger/submitted.json">Ledger</a>
        <a href="https://github.com/reny-develop/Rulealize">Rulealize</a>
        </footer>
        </body>
        </html>

        """, new UTF8Encoding(false));

static string PluginBody(Plugin plugin)
{
    StringBuilder body = new();
    body.Append($"<h1>{H(plugin.Id)}</h1>");

    if (plugin.Description is not null)
    {
        body.Append($"<p class=\"lede\">{H(plugin.Description)}</p>");
    }

    body.Append("<table class=\"facts\">");
    body.Append($"<tr><th>Namespace</th><td><code>{H(plugin.Namespace)}</code></td></tr>");
    body.Append(
        $"<tr><th>Shorthand</th><td>{(plugin.Prefix is null ? "<span class=\"none\">none</span>" : $"<code>{H(plugin.Prefix)}</code>")}</td></tr>");
    body.Append($"<tr><th>Latest</th><td><code>{H(plugin.Latest)}</code></td></tr>");
    body.Append($"<tr><th>Admitted at</th><td><code>{H(plugin.Admitted)}</code></td></tr>");

    if (plugin.License is not null)
    {
        body.Append($"<tr><th>Licence</th><td>{H(plugin.License)}</td></tr>");
    }

    if (plugin.Repository is not null)
    {
        body.Append(Link(plugin.Repository) is string source
            ? $"<tr><th>Source</th><td><a href=\"{H(source)}\">{H(source)}</a></td></tr>"
            : $"<tr><th>Source</th><td>{H(plugin.Repository)}</td></tr>");
    }

    body.Append(
        $"<tr><th>Package</th><td><a href=\"https://www.nuget.org/packages/{H(plugin.Id)}\">nuget.org</a></td></tr>");
    body.Append($"<tr><th>Entry</th><td><a href=\"{H(plugin.Id)}.json\">{H(plugin.Id)}.json</a></td></tr>");
    body.Append("</table>");

    body.Append("<h2>Add it</h2>");
    body.Append($"<pre><code>dotnet add package {H(plugin.Id)}</code></pre>");
    body.Append(
        "<p>Or name it in a rule set's <code>requires</code> and let "
        + "<a href=\"https://github.com/reny-develop/Rulealize.Cli\"><code>rulealize restore</code></a> "
        + "fetch it with everything else the document asks for.</p>");

    foreach (Release release in plugin.Releases.AsEnumerable().Reverse())
    {
        body.Append($"<h2>{H(release.Version)}</h2>");
        body.Append("<p class=\"meta\">");
        body.Append($"targets <code>{H(release.Framework)}</code>");
        if (release.Abstraction is not null)
        {
            body.Append($", built against <code>Rulealize.Abstraction {H(release.Abstraction)}</code>");
        }

        body.Append("</p>");

        foreach ((string kind, List<string> operations) in release.Operations)
        {
            if (operations.Count is 0)
            {
                continue;
            }

            body.Append($"<h3>{H(kind)}</h3><ul class=\"ops\">");
            foreach (string op in operations)
            {
                body.Append($"<li><a href=\"../op/{H(op)}.html\"><code>{H(op)}</code></a></li>");
            }

            body.Append("</ul>");
        }
    }

    return body.ToString();
}

static string OperationBody(string op, string kind, Plugin plugin)
{
    string[] carrying = [.. plugin.Releases
        .Where(release => release.Operations.TryGetValue(kind, out List<string>? ops) && ops.Contains(op))
        .Select(static release => release.Version)];

    StringBuilder body = new();
    body.Append($"<h1><code>{H(op)}</code></h1>");
    string article = kind is "schema" ? "A" : "An";
    body.Append($"<p class=\"lede\">{article} <strong>{H(kind)}</strong> node, provided by ");
    body.Append($"<a href=\"../plugin/{H(plugin.Id)}.html\">{H(plugin.Id)}</a>.</p>");

    body.Append("<table class=\"facts\">");
    body.Append($"<tr><th>Namespace</th><td><code>{H(plugin.Namespace)}</code></td></tr>");
    body.Append($"<tr><th>Kind</th><td>{H(kind)}</td></tr>");
    body.Append($"<tr><th>In versions</th><td><code>{H(string.Join(", ", carrying))}</code></td></tr>");
    body.Append("</table>");

    body.Append(
        "<p>What it does is in that plugin's specification, which ships with the plugin and "
        + "changes when it releases");

    if (Link(plugin.Repository) is string source)
    {
        body.Append($" — <a href=\"{H(source)}/blob/main/doc/specification.md\">read it</a>");
    }

    body.Append(".</p>");
    return body.ToString();
}

static async Task WriteFront(List<Plugin> plugins, string output)
{
    StringBuilder body = new();

    body.Append("<h1>The Rulealize plugin index</h1>");
    body.Append("""
        <p class="lede">Which plugin provides an operation, which versions satisfy a rule set's
        <code>requires</code>, and which namespaces and shorthand characters are already spoken for.</p>
        """);

    body.Append("""
        <h2>Find an operation</h2>
        <input id="q" type="search" placeholder="grid.ray, seq., state&hellip;" autocomplete="off" spellcheck="false">
        <p id="count" class="meta"></p>
        <ul id="results" class="results"></ul>
        """);

    body.Append("<h2>What is claimed</h2>");
    body.Append("""
        <p>A namespace and a shorthand character have exactly one owner across the whole ecosystem, and
        the runtime refuses two plugins that claim one of either. That check runs when somebody assembles
        a plugin folder — after both were published — so this table is the only place a collision can be
        seen before it costs anything.</p>
        """);

    body.Append("<table class=\"claims\"><thead><tr>");
    body.Append("<th>Namespace</th><th>Shorthand</th><th>Plugin</th><th>Latest</th><th>Operations</th>");
    body.Append("</tr></thead><tbody>");

    foreach (Plugin plugin in plugins.OrderBy(static plugin => plugin.Namespace, StringComparer.Ordinal))
    {
        int operations = plugin.Releases[^1].Operations.Sum(static kind => kind.Value.Count);
        body.Append("<tr>");
        body.Append($"<td><code>{H(plugin.Namespace)}</code></td>");
        body.Append(
            $"<td>{(plugin.Prefix is null ? "<span class=\"none\">—</span>" : $"<code>{H(plugin.Prefix)}</code>")}</td>");
        body.Append($"<td><a href=\"plugin/{H(plugin.Id)}.html\">{H(plugin.Id)}</a></td>");
        body.Append($"<td><code>{H(plugin.Latest)}</code></td>");
        body.Append($"<td class=\"n\">{operations}</td>");
        body.Append("</tr>");
    }

    body.Append("</tbody></table>");

    string spent = string.Join(
        ", ",
        plugins.Where(static plugin => plugin.Prefix is not null)
            .OrderBy(static plugin => plugin.Prefix, StringComparer.Ordinal)
            .Select(static plugin => $"<code>{H(plugin.Prefix)}</code> ({H(plugin.Namespace)})"));

    body.Append($"""
        <h2>Shorthand characters</h2>
        <p>Spent: {spent}. A string beginning with one is handed to that plugin's expander instead of
        being read as text, so the supply is one keystroke wide and cannot be extended — letters, digits
        and anything ordinary data might begin with are unusable. <strong>Under a dozen remain for the
        entire future of the ecosystem</strong>, which is why one is granted by review and the default
        answer is no.</p>
        <p><a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md#shorthand-characters">The
        four things a grant has to clear</a>.</p>
        """);

    body.Append("""
        <h2>The files behind this page</h2>
        <p>Nothing here is written by hand. Each entry is derived by loading the published package and
        reading back what it registered, and these are the same files the resolver reads:</p>
        <ul>
          <li><a href="index.json"><code>/index.json</code></a> — every plugin and operation, in summary</li>
          <li><code>/plugin/&lt;id&gt;.json</code> — one plugin, every released version</li>
        </ul>
        """);

    body.Append("""
        <script>
        const results = document.getElementById('results');
        const count = document.getElementById('count');
        const box = document.getElementById('q');
        let operations = [];

        fetch('index.json').then(r => r.json()).then(data => {
          operations = data.operations;
          count.textContent = operations.length + ' operations indexed.';
        });

        box.addEventListener('input', () => {
          const term = box.value.trim().toLowerCase();
          if (!term) {
            results.replaceChildren();
            count.textContent = operations.length + ' operations indexed.';
            return;
          }
          const hits = operations.filter(o => o.op.toLowerCase().includes(term));
          count.textContent = hits.length + ' of ' + operations.length + ' match.';
          results.replaceChildren(...hits.slice(0, 60).map(o => {
            const li = document.createElement('li');
            const a = document.createElement('a');
            a.href = 'op/' + o.op + '.html';
            a.textContent = o.op;
            const kind = document.createElement('span');
            kind.className = 'kind';
            kind.textContent = o.kind;
            const from = document.createElement('span');
            from.className = 'from';
            from.textContent = o.plugin;
            li.append(a, kind, from);
            return li;
          }));
        });
        </script>
        """);

    await Write(Path.Combine(output, "index.html"), "Rulealize Registry", ".", body.ToString());
}

internal sealed record Release(string Version, string? Abstraction, string? Framework, Dictionary<string, List<string>> Operations);

internal sealed record Plugin(
    string Id,
    string Namespace,
    string? Prefix,
    string Admitted,
    string Latest,
    string? Description,
    string? Repository,
    string? License,
    List<Release> Releases);

internal static partial class Program
{
    // One stylesheet, no framework, no build step. Both themes are defined because a reader
    // arrives in whichever one their system is set to, and a page that only looks right in
    // one of them looks broken in the other.
    public const string Style = """
        :root {
          --bg: #ffffff; --fg: #1a1a1a; --dim: #5c5c5c; --line: #e2e2e2;
          --accent: #0b5fff; --code-bg: #f4f4f5; --notice: #8a6d00; --notice-bg: #fff8e1;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --bg: #14161a; --fg: #e8e8ea; --dim: #a0a0a8; --line: #2b2f36;
            --accent: #7aa2ff; --code-bg: #1e2127; --notice: #e0c060; --notice-bg: #2a2413;
          }
        }
        * { box-sizing: border-box; }
        body {
          margin: 0; background: var(--bg); color: var(--fg);
          font: 16px/1.6 system-ui, -apple-system, "Segoe UI", sans-serif;
        }
        header, main, footer { max-width: 54rem; margin: 0 auto; padding: 0 1.25rem; }
        header { padding-top: 1.5rem; }
        .home { font-weight: 600; text-decoration: none; color: var(--fg); }
        .notice {
          margin: .75rem 0 0; padding: .5rem .75rem; border-radius: 4px;
          background: var(--notice-bg); color: var(--notice); font-size: .875rem;
        }
        main { padding-top: 1rem; padding-bottom: 3rem; }
        h1 { font-size: 1.7rem; margin: 1.5rem 0 .5rem; }
        h2 { font-size: 1.2rem; margin: 2.5rem 0 .75rem; padding-top: .75rem; border-top: 1px solid var(--line); }
        h3 { font-size: .95rem; margin: 1.25rem 0 .5rem; color: var(--dim); font-weight: 600; }
        p { margin: .75rem 0; }
        .lede { font-size: 1.05rem; color: var(--dim); }
        .meta { color: var(--dim); font-size: .875rem; }
        .none { color: var(--dim); }
        a { color: var(--accent); }
        code {
          font-family: ui-monospace, "Cascadia Code", Consolas, monospace; font-size: .9em;
          background: var(--code-bg); padding: .1em .35em; border-radius: 3px;
        }
        pre { background: var(--code-bg); padding: .75rem 1rem; border-radius: 6px; overflow-x: auto; }
        pre code { background: none; padding: 0; }
        table { border-collapse: collapse; width: 100%; margin: 1rem 0; font-size: .95rem; }
        th, td { text-align: left; padding: .45rem .6rem; border-bottom: 1px solid var(--line); vertical-align: top; }
        .facts th { width: 9rem; color: var(--dim); font-weight: 500; }
        .claims thead th { color: var(--dim); font-weight: 600; font-size: .8rem; text-transform: uppercase; }
        .n { text-align: right; }
        input[type=search] {
          width: 100%; padding: .6rem .8rem; font-size: 1rem; color: var(--fg);
          background: var(--bg); border: 1px solid var(--line); border-radius: 6px;
        }
        input[type=search]:focus { outline: 2px solid var(--accent); outline-offset: -1px; }
        ul.ops { list-style: none; padding: 0; margin: 0; display: flex; flex-wrap: wrap; gap: .4rem; }
        ul.results { list-style: none; padding: 0; margin: 0; }
        ul.results li { padding: .4rem 0; border-bottom: 1px solid var(--line); display: flex; gap: .75rem; align-items: baseline; }
        ul.results a { font-family: ui-monospace, Consolas, monospace; }
        .kind, .from { color: var(--dim); font-size: .8rem; }
        .from { margin-left: auto; }
        footer {
          border-top: 1px solid var(--line); padding-top: 1rem; padding-bottom: 2rem;
          font-size: .875rem; display: flex; gap: 1.25rem; flex-wrap: wrap;
        }
        """;
}
