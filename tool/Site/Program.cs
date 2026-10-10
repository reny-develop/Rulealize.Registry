// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

// Renders the catalogue as pages.
//
//   dotnet run --project tool/Site -- <catalogue folder> <output folder>
//
// The output holds the JSON as well as the HTML, and the pages are a second view of the same
// bytes rather than a separate build of the same facts. The search on the front page fetches
// /index.json, which is the cheapest way of finding out whether that file is usable. Where
// the JSON is served is not promised to anybody outside this repository.
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
Directory.CreateDirectory(Path.Combine(output, "ruleset"));
Directory.CreateDirectory(Path.Combine(output, "submit"));

using JsonDocument index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(catalogue, "index.json")));

// Every page says when the catalogue was last read out of nuget.org, because an index nobody
// is keeping looks exactly like one that is until it says so. It is carried on the class
// rather than passed to Write, which is a static local function and cannot capture it; an
// older catalogue that does not have the field leaves the line off rather than lying about it.
Program.CheckedAt = Text(index.RootElement, "checked");

// What the registry refuses to grant, for the front page to show beside what is claimed. An
// older catalogue without the list shows neither row rather than an empty one.
if (index.RootElement.TryGetProperty("reserved", out JsonElement refused))
{
    Program.ReservedNamespaces = [.. Entries(refused, "namespaces").Select(static name => name.GetString()!)];
    Program.ReservedPrefixes = [.. Entries(refused, "prefixes").Select(static name => name.GetString()!)];
}

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
        // A release the catalogue withheld has none, because its operations are named in a
        // namespace the plugin does not hold.
        Dictionary<string, List<string>> operations = [];
        if (release.TryGetProperty("operations", out JsonElement registered))
        {
            foreach (JsonProperty kind in registered.EnumerateObject())
            {
                operations[kind.Name] = [.. kind.Value.EnumerateArray().Select(static op => op.GetString()!)];
            }
        }

        Claimed? claimed = release.TryGetProperty("claimed", out JsonElement said)
            ? new Claimed(Text(said, "namespace"), Text(said, "prefix"))
            : null;

        releases.Add(new Release(
            release.GetProperty("version").GetString()!,
            Text(release, "abstraction"),
            Text(release, "framework"),
            operations,
            Text(release, "withheld"),
            claimed));
    }

    plugins.Add(new Plugin(
        id,
        root.GetProperty("namespace").GetString()!,
        root.GetProperty("prefix").ValueKind is JsonValueKind.Null ? null : root.GetProperty("prefix").GetString(),
        root.GetProperty("admitted").GetString()!,
        Text(root, "latest"),
        Text(root, "description"),
        Text(root, "repository"),
        Text(root, "license"),
        releases));

    File.Copy(path, Path.Combine(output, "plugin", $"{id}.json"), overwrite: true);
}

List<RuleSet> ruleSets = [];
foreach (JsonElement summary in index.RootElement.TryGetProperty("ruleSets", out JsonElement listed)
    ? listed.EnumerateArray()
    : [])
{
    string id = summary.GetProperty("id").GetString()!;
    string path = Path.Combine(catalogue, "ruleset", $"{id}.json");

    using JsonDocument detail = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    JsonElement root = detail.RootElement;

    List<RuleSetRelease> releases = [];
    foreach (JsonElement release in root.GetProperty("versions").EnumerateArray())
    {
        // A release the catalogue withheld has none of the three, because what it declared
        // about itself is not what the ledger admits and nothing is indexed off it.
        List<Needs> requires = [.. Entries(release, "requires").Select(static entry =>
            new Needs(entry.GetProperty("plugin").GetString()!, Text(entry, "version")))];

        List<Held> uses = [.. Entries(release, "uses").Select(static entry =>
            new Held(
                entry.GetProperty("ruleSet").GetString()!,
                Text(entry, "version"),
                Text(entry, "as") ?? entry.GetProperty("ruleSet").GetString()!))];

        List<string> inputs = [.. Entries(release, "inputs").Select(static input => input.GetString()!)];

        // Documents that shipped in the same package as this one. A `uses` naming one of them
        // resolves without anything being fetched, which is the one case where holding
        // something this index has no entry for is right rather than broken.
        List<string> parts = [.. Entries(release, "parts").Select(static part => part.GetString()!)];

        Declared? claimed = release.TryGetProperty("claimed", out JsonElement said)
            ? new Declared(Text(said, "id"), Text(said, "version"))
            : null;

        releases.Add(new RuleSetRelease(
            release.GetProperty("version").GetString()!,
            requires,
            uses,
            inputs,
            parts,
            Text(release, "withheld"),
            claimed));
    }

    ruleSets.Add(new RuleSet(
        id,
        root.GetProperty("admitted").GetString()!,
        Text(root, "latest"),
        Text(root, "description"),
        Text(root, "repository"),
        Text(root, "license"),
        releases,
        [.. Entries(summary, "inputs").Select(static input => input.GetString()!)]));

    File.Copy(path, Path.Combine(output, "ruleset", $"{id}.json"), overwrite: true);
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

// The other string that becomes a path here, for both kinds. nuget.org allows nothing else in
// an identifier and both ledgers are fetched by it, so this is the belt to that brace — and
// for a rule set it is the whole of the check, because its identifier IS its package
// identifier and there is no second name to hold to anything.
string[] impossible =
[
    .. plugins.Select(static plugin => plugin.Id)
        .Concat(ruleSets.Select(static ruleSet => ruleSet.Id))
        .Where(static id => id.Length is 0 || !id.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal),
];

if (impossible.Length is not 0)
{
    Console.Error.WriteLine($"These are not package identifiers: {string.Join(", ", impossible)}");
    return 1;
}

File.Copy(Path.Combine(catalogue, "index.json"), Path.Combine(output, "index.json"), overwrite: true);

await File.WriteAllTextAsync(Path.Combine(output, "style.css"), Style, new UTF8Encoding(false));
await WriteFront(plugins, ruleSets, output);
await Write(Path.Combine(output, "submit", "index.html"), "Submit a package", "..", SubmitPage, "Submit");
await File.WriteAllTextAsync(Path.Combine(output, "submit", "submit.js"), SubmitScript, new UTF8Encoding(false));

foreach (Plugin plugin in plugins)
{
    await Write(Path.Combine(output, "plugin", $"{plugin.Id}.html"), plugin.Id, "..", PluginBody(plugin), "Plugins");
}

// Which identifiers this index can answer for. A `uses` entry naming one that is not here is
// rendered as the text it is, with what that means said once on the page: the document is
// published and the thing it holds is not, so nothing can restore it but its author.
HashSet<string> indexed = [.. ruleSets.Select(static ruleSet => ruleSet.Id)];
Dictionary<string, string> vocabularies = plugins.ToDictionary(static plugin => plugin.Id, static plugin => plugin.Namespace, StringComparer.Ordinal);

foreach (RuleSet ruleSet in ruleSets)
{
    await Write(
        Path.Combine(output, "ruleset", $"{ruleSet.Id}.html"),
        ruleSet.Id,
        "..",
        RuleSetBody(ruleSet, indexed, vocabularies),
        "Rule sets");
}

// One page per operation, over the latest indexed release of each plugin — a name withdrawn
// two versions ago is not one to offer, and the plugin's own page still records that it
// existed. A plugin whose every release was withheld offers none at all.
int written = 0;
foreach (Plugin plugin in plugins)
{
    if (plugin.Indexed is not Release latest)
    {
        continue;
    }

    foreach ((string kind, List<string> operations) in latest.Operations)
    {
        foreach (string op in operations)
        {
            await Write(Path.Combine(output, "op", $"{op}.html"), op, "..", OperationBody(op, kind, plugin), "Plugins");
            written++;
        }
    }
}

Console.Error.WriteLine($"{plugins.Count} plugins, {written} operations, {ruleSets.Count} rule sets -> {output}");
return 0;

static string? Text(JsonElement element, string name) =>
    element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String
        ? value.GetString()
        : null;

// A withheld release carries none of the three lists, and an older catalogue may carry none
// of them at all. Both read as empty rather than as a missing key nothing handles.
static IEnumerable<JsonElement> Entries(JsonElement element, string name) =>
    element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.Array
        ? value.EnumerateArray()
        : [];

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

// The frame every page is written into: a header with the way to every list and to the search,
// and a footer that says when the catalogue was last read. `active` names the header link for
// the kind of page this is, so a reader can see where they are.
static async Task Write(string path, string title, string root, string body, string active = "") =>
    await File.WriteAllTextAsync(path, $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{H(title)} — Rulealize Registry</title>
        <link rel="stylesheet" href="{root}/style.css">
        </head>
        <body>
        <header class="top">
        <div class="wrap bar">
        <a class="brand" href="{root}/index.html"><span class="mark" aria-hidden="true">R</span><b>Rulealize</b> <span>Registry</span></a>
        <nav class="links">
        {NavLink(root, "index.html#namespaces", "Plugins", active)}
        {NavLink(root, "index.html#rule-sets", "Rule sets", active)}
        {NavLink(root, "index.html#shorthand", "Shorthand", active)}
        {NavLink(root, "submit/index.html", "Submit", active)}
        </nav>
        <a class="find" href="{root}/index.html#q">Find a name <kbd>/</kbd></a>
        </div>
        </header>
        <main class="wrap">
        {body}
        </main>
        <footer class="foot">
        <div class="wrap">
        <div class="foot-cols">
        <div class="foot-brand">
        <a class="brand" href="{root}/index.html"><span class="mark" aria-hidden="true">R</span><b>Rulealize</b> <span>Registry</span></a>
        <p>The index of the Rulealize ecosystem. The ledger is authoritative; every page is generated from it.</p>
        </div>
        <div><h4>Registry</h4>
        <a href="{root}/index.html#namespaces">Plugins</a>
        <a href="{root}/index.html#rule-sets">Rule sets</a>
        <a href="{root}/index.html#shorthand">Shorthand</a>
        </div>
        <div><h4>Publish</h4>
        <a href="{root}/submit/index.html">Submit a package</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/publish.md">Publishing guide</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md">Grant policy</a>
        </div>
        <div><h4>Ecosystem</h4>
        <a href="https://github.com/reny-develop/Rulealize">Rulealize</a>
        <a href="https://github.com/reny-develop/Rulealize.Cli">Rulealize.Cli</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry">Repository</a>
        </div>
        </div>
        <div class="foot-base">
        <span>Tools Apache-2.0 · Ledger and catalogue CC0-1.0</span>
        {(CheckedAt is null ? "" : $"<span class=\"checked\"><i class=\"live\" aria-hidden=\"true\"></i>Last checked {H(Stamp(CheckedAt))}</span>")}
        </div>
        </div>
        </footer>
        </body>
        </html>

        """, new UTF8Encoding(false));

static string NavLink(string root, string href, string label, string active) =>
    $"<a href=\"{root}/{href}\"{(label == active ? " class=\"on\" aria-current=\"page\"" : "")}>{label}</a>";

// The catalogue records the second it was read, and a page wants the minute: a reader
// wondering whether this is still kept wants to see that it was read today, and when. Anything
// that is not a timestamp is shown as it is — escaped, like everything else — rather than
// dropped, because a footer that quietly says nothing is how a broken field stays broken.
static string Stamp(string value) =>
    DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset when)
        ? when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
        : value;

// A label above a value, the way the facts down the side of a page are set.
static string Eyebrow(string text) => $"<p class=\"eyebrow\">{text}</p>";

static string PluginBody(Plugin plugin)
{
    StringBuilder body = new();
    body.Append($"<p class=\"crumbs\"><a href=\"../index.html#namespaces\">Plugins</a> <span>/</span> {H(plugin.Namespace)}</p>");
    body.Append($"<p class=\"pill ns\"><code>{H(plugin.Namespace)}</code></p>");
    body.Append($"<h1 class=\"title\">{H(plugin.Id)}</h1>");

    if (plugin.Description is not null)
    {
        body.Append($"<p class=\"lede\">{H(plugin.Description)}</p>");
    }

    Release? indexed = plugin.Indexed;
    body.Append("<ul class=\"badges\">");
    if (plugin.Latest is not null)
    {
        body.Append($"<li>Latest <code>{H(plugin.Latest)}</code></li>");
    }

    if (plugin.License is not null)
    {
        body.Append($"<li>Licence <code>{H(plugin.License)}</code></li>");
    }

    if (indexed?.Framework is string framework)
    {
        body.Append($"<li>Target <code>{H(framework)}</code></li>");
    }

    if (indexed?.Abstraction is string abstraction)
    {
        body.Append($"<li>Abstraction <code>{H(abstraction)}</code></li>");
    }

    body.Append("</ul>");

    body.Append("<div class=\"cols\"><div class=\"main\">");

    body.Append("<h2>Add it</h2>");
    body.Append($"<pre class=\"dark\"><code>dotnet add package {H(plugin.Id)}</code></pre>");
    body.Append(
        "<p>Or name it in a rule set's <code>requires</code> and let "
        + "<a href=\"https://github.com/reny-develop/Rulealize.Cli\"><code>rulealize restore</code></a> "
        + "fetch it with everything else the document asks for.</p>");

    // What the newest indexed release registers, by kind. A name it added since the indexed
    // release before it is marked, because "what is new" is the question somebody updating a
    // pin arrives with.
    if (indexed is not null)
    {
        Release? before = plugin.Releases
            .TakeWhile(release => !ReferenceEquals(release, indexed))
            .LastOrDefault(static release => release.Withheld is null);
        HashSet<string> earlier = [.. before?.Operations.Values.SelectMany(static ops => ops) ?? []];

        body.Append($"<h2>Operations <span class=\"meta\">in <code>{H(indexed.Version)}</code></span></h2>");
        foreach ((string kind, List<string> operations) in indexed.Operations)
        {
            if (operations.Count is 0)
            {
                continue;
            }

            body.Append($"<section class=\"card kinds\"><h3>{H(kind)} <span class=\"n\">{operations.Count}</span></h3><ul class=\"ops\">");
            foreach (string op in operations)
            {
                string fresh = before is not null && !earlier.Contains(op) ? " <span class=\"new\">new</span>" : "";
                body.Append($"<li><a href=\"../op/{H(op)}.html\"><code>{H(op)}</code></a>{fresh}</li>");
            }

            body.Append("</ul></section>");
        }
    }

    body.Append("<h2>Releases</h2><ol class=\"releases\">");
    Release? previous = null;
    List<string> lines = [];
    foreach (Release release in plugin.Releases)
    {
        StringBuilder line = new();
        line.Append($"<li class=\"{(release.Withheld is null ? "" : "held")}\"><h3 class=\"ver\"><code>{H(release.Version)}</code>");
        if (release.Withheld is not null)
        {
            line.Append(" <span class=\"warn\">not indexed</span>");
        }
        else if (ReferenceEquals(release, indexed))
        {
            line.Append(" <span class=\"tag\">latest</span>");
        }

        if (release.Version == plugin.Admitted)
        {
            line.Append(" <span class=\"tag quiet\">admitted</span>");
        }

        line.Append("</h3>");

        // The one thing on these pages addressed to the plugin's author rather than to
        // somebody reading a rule set. Nothing else tells them: the catalogue is rebuilt
        // without asking anybody, and a release that quietly went missing from an index reads
        // as an index that is behind.
        if (release.Withheld is string withheld)
        {
            line.Append($"<p class=\"withheld\">{Withheld(plugin, release, withheld)}</p>");
        }
        else
        {
            line.Append("<p class=\"meta\">");
            if (release.Framework is not null)
            {
                line.Append($"targets <code>{H(release.Framework)}</code>");
            }

            if (release.Abstraction is not null)
            {
                line.Append(
                    $"{(release.Framework is null ? "b" : ", b")}uilt against "
                    + $"<code>Rulealize.Abstraction {H(release.Abstraction)}</code>");
            }

            line.Append("</p>");

            if (previous is not null)
            {
                HashSet<string> had = [.. previous.Operations.Values.SelectMany(static ops => ops)];
                string[] added = [.. release.Operations.Values.SelectMany(static ops => ops).Where(op => !had.Contains(op))];
                if (added.Length is not 0)
                {
                    line.Append("<p class=\"added\">+ " + string.Join(" ", added.Select(static op => $"<code>{H(op)}</code>")) + "</p>");
                }
            }

            previous = release;
        }

        line.Append("</li>");
        lines.Add(line.ToString());
    }

    // Newest first, which is the order somebody choosing a version reads in.
    lines.Reverse();
    body.Append(string.Concat(lines));
    body.Append("</ol>");

    body.Append("</div><aside class=\"side\">");
    body.Append("<div class=\"card\">");
    body.Append(Eyebrow("Entry"));
    body.Append("<table class=\"facts\">");
    body.Append($"<tr><th>Namespace</th><td><code>{H(plugin.Namespace)}</code></td></tr>");
    body.Append(
        $"<tr><th>Shorthand</th><td>{(plugin.Prefix is null ? "<span class=\"none\">none</span>" : $"<code>{H(plugin.Prefix)}</code>")}</td></tr>");
    body.Append(
        $"<tr><th>Latest</th><td>{(plugin.Latest is null ? "<span class=\"none\">none indexed</span>" : $"<code>{H(plugin.Latest)}</code>")}</td></tr>");
    body.Append($"<tr><th>Admitted at</th><td><code>{H(plugin.Admitted)}</code></td></tr>");

    if (plugin.License is not null)
    {
        body.Append($"<tr><th>Licence</th><td>{H(plugin.License)}</td></tr>");
    }

    if (plugin.Repository is not null)
    {
        body.Append(Link(plugin.Repository) is string source
            ? $"<tr><th>Source</th><td><a href=\"{H(source)}\">{H(Host(source))}</a></td></tr>"
            : $"<tr><th>Source</th><td>{H(plugin.Repository)}</td></tr>");
    }

    body.Append(
        $"<tr><th>Package</th><td><a href=\"https://www.nuget.org/packages/{H(plugin.Id)}\">nuget.org</a></td></tr>");
    body.Append($"<tr><th>Entry</th><td><a href=\"{H(plugin.Id)}.json\">{H(plugin.Id)}.json</a></td></tr>");
    body.Append("</table>");

    if (Link(plugin.Repository) is string repository)
    {
        body.Append($"<a class=\"button\" href=\"{H(repository)}/blob/main/doc/specification.md\">Read the specification →</a>");
    }

    body.Append("</div>");
    body.Append(
        $"<div class=\"card soft\"><h4>One owner, permanently</h4><p>The namespace <code>{H(plugin.Namespace)}</code> "
        + "belongs to this plugin across the whole ecosystem. A release claiming any other is withheld, not indexed.</p></div>");
    body.Append("</aside></div>");

    return body.ToString();
}

// A source link is shown as where it goes rather than as the whole URL, which is long and says
// less. The href is the URL, so nothing is hidden from anybody who hovers it.
static string Host(string url) =>
    Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? $"{parsed.Host} ↗" : url;

// Why a release is not in the index. What the ledger holds is elsewhere on the same page, so
// this is the half of the comparison that is nowhere else: what the release claimed instead.
static string Withheld(Plugin plugin, Release release, string reason)
{
    if (reason is not "claims" || release.Claimed is not Claimed claimed)
    {
        return "Nothing could be read out of this release — two target frameworks, no "
            + "<code>lib</code> folder, or an assembly the loader refused — so it is not in the index.";
    }

    List<string> moved = [];
    if (claimed.Namespace != plugin.Namespace)
    {
        moved.Add(
            $"claims the namespace <code>{H(claimed.Namespace)}</code>, where the ledger admits "
            + $"<code>{H(plugin.Namespace)}</code>");
    }

    if (claimed.Prefix != plugin.Prefix)
    {
        moved.Add($"claims {Shorthand(claimed.Prefix)}, where the ledger admits {Shorthand(plugin.Prefix)}");
    }

    if (moved.Count is 0)
    {
        moved.Add("claims something other than what the ledger admits");
    }

    return $"This release {string.Join(", and ", moved)}. A claim is permanent, so it is not in the "
        + "index: its operations are not offered here, and nothing that reads this resolves to it.";

    static string Shorthand(string? prefix) =>
        prefix is null ? "no shorthand character" : $"the shorthand character <code>{H(prefix)}</code>";
}

static string RuleSetBody(RuleSet ruleSet, HashSet<string> indexed, Dictionary<string, string> vocabularies)
{
    RuleSetRelease? newest = ruleSet.Indexed;
    bool composite = newest is { Uses.Count: > 0 };

    StringBuilder body = new();
    body.Append($"<p class=\"crumbs\"><a href=\"../index.html#rule-sets\">Rule sets</a> <span>/</span> {H(Last(ruleSet.Id))}</p>");
    body.Append($"<p class=\"pill ok\">rule set{(composite ? " · composite" : "")}</p>");
    body.Append($"<h1 class=\"title\">{H(ruleSet.Id)}</h1>");

    if (ruleSet.Description is not null)
    {
        body.Append($"<p class=\"lede\">{H(ruleSet.Description)}</p>");
    }

    // What the newest indexed release is made of, drawn: this document, what it holds and under
    // which names, and the vocabularies it requires of its own. The lists below say the same per
    // release; this is the one picture of the whole somebody deciding to hold it wants first.
    if (newest is not null && (newest.Uses.Count is not 0 || newest.Requires.Count is not 0))
    {
        body.Append($"<section class=\"diagram\">{Eyebrow($"What <code>{H(newest.Version)}</code> is made of")}<div class=\"graph\">");
        body.Append($"<div class=\"node self\"><span>this document</span><b>{H(Last(ruleSet.Id))}</b><small>{H(newest.Version)}</small></div>");

        if (newest.Uses.Count is not 0)
        {
            body.Append("<div class=\"held\"><span class=\"edge\">uses</span>");
            foreach (Held held in newest.Uses)
            {
                string top = $"as {H(held.Alias)}{(held.Version is null ? "" : $" · {H(held.Version)}")}";
                string inner = $"<span>{top}</span><b>{H(Last(held.RuleSet))}</b><small>{H(held.RuleSet)}</small>";
                body.Append(indexed.Contains(held.RuleSet)
                    ? $"<a class=\"node\" href=\"{H(held.RuleSet)}.html\">{inner}</a>"
                    : $"<div class=\"node\">{inner}</div>");
            }

            body.Append("</div>");
        }

        if (newest.Requires.Count is not 0)
        {
            body.Append("<div class=\"needs\"><span class=\"edge\">requires — this document's own</span>");
            foreach (Needs needs in newest.Requires)
            {
                string ns = vocabularies.TryGetValue(needs.Plugin, out string? name) ? name : "";
                string inner = $"<code>{H(ns)}</code><span>{H(needs.Plugin)}</span><small>{H(needs.Version)}</small>";
                body.Append(vocabularies.ContainsKey(needs.Plugin)
                    ? $"<a class=\"vocab\" href=\"../plugin/{H(needs.Plugin)}.html\">{inner}</a>"
                    : $"<div class=\"vocab\">{inner}</div>");
            }

            body.Append("</div>");
        }

        body.Append("</div></section>");
    }

    body.Append("<div class=\"cols\"><div class=\"main\">");

    body.Append("<h2>Hold it</h2>");
    body.Append("<p>A document that holds this one writes, in its <code>uses</code>:</p>");
    body.Append($$"""
        <pre class="dark"><code>"uses": [
          { "ruleSet": "{{H(ruleSet.Id)}}",
            "version": "^{{H(Major(ruleSet.Latest))}}", "as": "{{H(Alias(ruleSet.Id))}}" }
        ]</code></pre>
        """);
    body.Append(
        "<p class=\"callout\"><code>as</code> <strong>is not optional here.</strong> The identifier is the "
        + "package identifier, so nothing has to look it up; <code>as</code> is the short name the holding "
        + "document calls it by, and it is what qualifies this rule set's inputs inside that one. An alias "
        + "defaults to the identifier and may not contain a <code>.</code>, so leaving it out is refused with "
        + "a message about a key you did not write.</p>");

    // Said here because this page is where somebody decides to hold it, and the next thing
    // they want is the one command that makes the decision act.
    body.Append(
        "<p class=\"meta\">Getting what a <code>uses</code> names is the feed's job rather than this "
        + "index's. <a href=\"https://github.com/reny-develop/Rulealize.Cli\"><code>rulealize "
        + "restore</code></a> does it from 0.8.0 — the whole graph, into the folder the holding "
        + "document resolves its components from — and takes the lowest version satisfying every "
        + "constraint that named it, which is the rule a <code>requires</code> is resolved by.</p>");

    // What a composite writes in `held` and in `fires`, which is the one thing somebody has to
    // know about this document before they can hold it. A qualified input comes from the held
    // rule set its alias names; an unqualified one is this document's own.
    // What it offers is the catalogue's summary rather than the document's own `inputs`: a
    // composite's own section is often empty, and what a holder writes is its components'
    // inputs under the names it calls them by.
    List<string> offered = ruleSet.Offered.Count is not 0 ? ruleSet.Offered : newest?.Inputs ?? [];
    if (newest is not null && offered.Count is not 0)
    {
        Dictionary<string, Held> byAlias = newest.Uses
            .GroupBy(static held => held.Alias, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        body.Append("<h2>Inputs a composite could constrain</h2>");
        body.Append("<table class=\"inputs\"><thead><tr><th>Input</th><th>Comes from</th></tr></thead><tbody>");
        foreach (string input in offered)
        {
            int dot = input.IndexOf('.');
            string from = dot > 0 && byAlias.TryGetValue(input[..dot], out Held? held)
                ? (indexed.Contains(held.RuleSet)
                    ? $"<a href=\"{H(held.RuleSet)}.html\">{H(held.RuleSet)}</a> · as <code>{H(held.Alias)}</code>"
                    : $"{H(held.RuleSet)} · as <code>{H(held.Alias)}</code>")
                : "this document";
            body.Append($"<tr><td><code>{H(input)}</code></td><td>{from}</td></tr>");
        }

        body.Append("</tbody></table>");
    }

    body.Append("<h2>Releases</h2><ol class=\"releases\">");
    foreach (RuleSetRelease release in ruleSet.Releases.AsEnumerable().Reverse())
    {
        body.Append($"<li class=\"{(release.Withheld is null ? "" : "held")}\"><h3 class=\"ver\"><code>{H(release.Version)}</code>");
        if (release.Withheld is not null)
        {
            body.Append(" <span class=\"warn\">not indexed</span>");
        }
        else if (ReferenceEquals(release, newest))
        {
            body.Append(" <span class=\"tag\">latest</span>");
        }

        body.Append("</h3>");

        // The one thing on these pages addressed to the rule set's author rather than to
        // somebody about to hold it.
        if (release.Withheld is string withheld)
        {
            body.Append($"<p class=\"withheld\">{WithheldDocument(ruleSet, release, withheld)}</p></li>");
            continue;
        }

        if (release.Uses.Count is not 0)
        {
            body.Append("<h4>holds</h4><ul class=\"ops\">");
            foreach (Held held in release.Uses)
            {
                string named = $"{H(held.RuleSet)}{(held.Version is null ? "" : $" {H(held.Version)}")}";

                // Three answers, and the middle one is the one worth having. Indexed: a link.
                // Shipped in this same package: no link, because it has no page — and no
                // warning either, because it is here. Neither: said as text, and counted below.
                body.Append(indexed.Contains(held.RuleSet)
                    ? $"<li><a href=\"{H(held.RuleSet)}.html\"><code>{named}</code></a> as <code>{H(held.Alias)}</code></li>"
                    : release.Parts.Contains(held.RuleSet, StringComparer.Ordinal)
                        ? $"<li><code>{named}</code> as <code>{H(held.Alias)}</code> <span class=\"meta\">ships with it</span></li>"
                        : $"<li><code>{named}</code> as <code>{H(held.Alias)}</code> <span class=\"warn\">not indexed</span></li>");
            }

            body.Append("</ul>");

            if (release.Uses.Any(held => !indexed.Contains(held.RuleSet)
                && !release.Parts.Contains(held.RuleSet, StringComparer.Ordinal)))
            {
                body.Append(
                    "<p class=\"meta\">A held rule set that is not indexed here is one nothing can fetch. "
                    + "It may be a document its author keeps beside this one, in which case holding it is "
                    + "correct and publishing this was premature — or it may be a name that never resolves.</p>");
            }
        }

        if (release.Requires.Count is not 0)
        {
            body.Append("<h4>requires</h4><ul class=\"ops\">");
            foreach (Needs needs in release.Requires)
            {
                string named = $"{H(needs.Plugin)}{(needs.Version is null ? "" : $" {H(needs.Version)}")}";
                body.Append(vocabularies.ContainsKey(needs.Plugin)
                    ? $"<li><a href=\"../plugin/{H(needs.Plugin)}.html\"><code>{named}</code></a></li>"
                    : $"<li><code>{named}</code></li>");
            }

            body.Append("</ul>");
        }

        if (release.Inputs.Count is not 0)
        {
            body.Append("<h4>inputs</h4><ul class=\"ops\">");
            foreach (string input in release.Inputs)
            {
                body.Append($"<li><code>{H(input)}</code></li>");
            }

            body.Append("</ul>");
        }

        body.Append("</li>");
    }

    body.Append("</ol>");

    body.Append("</div><aside class=\"side\"><div class=\"card\">");
    body.Append(Eyebrow("Entry"));
    body.Append("<table class=\"facts\">");
    body.Append(
        $"<tr><th>Latest</th><td>{(ruleSet.Latest is null ? "<span class=\"none\">none indexed</span>" : $"<code>{H(ruleSet.Latest)}</code>")}</td></tr>");
    body.Append($"<tr><th>Admitted at</th><td><code>{H(ruleSet.Admitted)}</code></td></tr>");
    body.Append(
        $"<tr><th>Holds</th><td>{(newest is { Uses.Count: > 0 } holding ? $"{holding.Uses.Count}" : "<span class=\"none\">nothing</span>")}</td></tr>");
    body.Append(
        $"<tr><th>Requires</th><td>{(newest is { Requires.Count: > 0 } needing ? $"{needing.Requires.Count}" : "<span class=\"none\">nothing</span>")}</td></tr>");

    if (ruleSet.License is not null)
    {
        body.Append($"<tr><th>Licence</th><td>{H(ruleSet.License)}</td></tr>");
    }

    if (ruleSet.Repository is not null)
    {
        body.Append(Link(ruleSet.Repository) is string source
            ? $"<tr><th>Source</th><td><a href=\"{H(source)}\">{H(Host(source))}</a></td></tr>"
            : $"<tr><th>Source</th><td>{H(ruleSet.Repository)}</td></tr>");
    }

    body.Append(
        $"<tr><th>Package</th><td><a href=\"https://www.nuget.org/packages/{H(ruleSet.Id)}\">nuget.org</a></td></tr>");
    body.Append($"<tr><th>Entry</th><td><a href=\"{H(ruleSet.Id)}.json\">{H(ruleSet.Id)}.json</a></td></tr>");
    body.Append("</table></div></aside></div>");

    return body.ToString();
}

// The part of a package-shaped identifier a person remembers: Rulealize.RuleSet.Quota is Quota.
static string Last(string id) =>
    id.Split('.').LastOrDefault(static part => part.Length is not 0) ?? id;

// Why a release is not in the index. The identifier this entry is filed under is elsewhere on
// the same page, so this is the half of the comparison that is nowhere else.
static string WithheldDocument(RuleSet ruleSet, RuleSetRelease release, string reason)
{
    if (reason is not "claims" || release.Claimed is not Declared claimed)
    {
        return "Nothing could be read out of this release — no <code>ruleset</code> folder, nothing in "
            + "it declaring the package's own identifier, or a document the reader refused — so it is not "
            + "in the index.";
    }

    List<string> moved = [];
    if (claimed.Id != ruleSet.Id)
    {
        moved.Add(
            $"declares the identifier <code>{H(claimed.Id)}</code>, where the ledger admits "
            + $"<code>{H(ruleSet.Id)}</code>");
    }

    if (claimed.Version is string version && version != release.Version)
    {
        moved.Add($"declares version <code>{H(version)}</code>, where the package is <code>{H(release.Version)}</code>");
    }

    if (moved.Count is 0)
    {
        moved.Add("declares something other than what the ledger admits");
    }

    return $"This release {string.Join(", and ", moved)}. A <code>uses</code> entry names an identifier and "
        + "is answered by the version in the document, so nothing that named this could resolve to it — "
        + "and a claim is permanent, so it is not in the index.";
}

// What a `uses` example writes. The latest indexed release's major, because that is what a
// document holding this today would pin, and 1 where nothing is indexed to read one off.
static string Major(string? latest) =>
    Version.TryParse(latest, out Version? parsed) ? $"{parsed.Major}.{Math.Max(parsed.Minor, 0)}" : "1.0";

// The short name a holder would reach for. `as` may not contain a dot, so a package-shaped
// identifier cannot be its own alias — which is the whole reason `as` is not optional in
// practice, and why the identifier being long costs a document one word.
static string Alias(string id) =>
    id.Split('.').LastOrDefault(static part => part.Length is not 0)?.ToLowerInvariant() ?? id;

// The releases of a plugin that register an operation, as a range when they are a run.
static string[] Carrying(string op, string kind, Plugin plugin) =>
[
    .. plugin.Releases
        .Where(release => release.Operations.TryGetValue(kind, out List<string>? ops) && ops.Contains(op))
        .Select(static release => release.Version),
];

static string OperationBody(string op, string kind, Plugin plugin)
{
    string[] carrying = Carrying(op, kind, plugin);

    StringBuilder body = new();
    body.Append($"<p class=\"crumbs\"><a href=\"../index.html#namespaces\">Plugins</a> <span>/</span> <a href=\"../plugin/{H(plugin.Id)}.html\">{H(plugin.Namespace)}</a> <span>/</span> {H(op)}</p>");
    body.Append($"<h1 class=\"title\"><code>{H(op)}</code></h1>");
    string article = kind is "schema" ? "A" : "An";
    body.Append($"<p class=\"lede\">{article} <strong>{H(kind)}</strong> node, provided by ");
    body.Append($"<a href=\"../plugin/{H(plugin.Id)}.html\">{H(plugin.Id)}</a>.</p>");

    body.Append("<div class=\"cols\"><div class=\"main\">");
    body.Append(
        "<p>What it does is in that plugin's specification, which ships with the plugin and "
        + "changes when it releases");

    if (Link(plugin.Repository) is string source)
    {
        body.Append($" — <a href=\"{H(source)}/blob/main/doc/specification.md\">read it</a>");
    }

    body.Append(".</p>");
    body.Append("<h2>Add it</h2>");
    body.Append($"<pre class=\"dark\"><code>dotnet add package {H(plugin.Id)}</code></pre>");
    body.Append("</div><aside class=\"side\"><div class=\"card\">");
    body.Append(Eyebrow("The index answers"));
    body.Append("<table class=\"facts\">");
    body.Append($"<tr><th>Kind</th><td>{H(kind)}</td></tr>");
    body.Append($"<tr><th>Plugin</th><td><a href=\"../plugin/{H(plugin.Id)}.html\">{H(plugin.Id)}</a></td></tr>");
    body.Append($"<tr><th>Namespace</th><td><code>{H(plugin.Namespace)}</code></td></tr>");
    body.Append($"<tr><th>In versions</th><td><code>{H(string.Join(", ", carrying))}</code></td></tr>");
    body.Append(
        $"<tr><th>Shorthand</th><td>{(plugin.Prefix is null ? "<span class=\"none\">none</span>" : $"<code>{H(plugin.Prefix)}</code>")}</td></tr>");
    body.Append("</table></div></aside></div>");
    return body.ToString();
}

static async Task WriteFront(List<Plugin> plugins, List<RuleSet> ruleSets, string output)
{
    StringBuilder body = new();

    int operationCount = plugins.Sum(static plugin => plugin.Indexed?.Operations.Sum(static kind => kind.Value.Count) ?? 0);
    int withheldCount = plugins.Sum(static plugin => plugin.Releases.Count(static release => release.Withheld is not null))
        + ruleSets.Sum(static ruleSet => ruleSet.Releases.Count(static release => release.Withheld is not null));

    // The example the front page answers before anybody has typed anything: an operation of the
    // plugin that registers the most, because that is the vocabulary somebody is likeliest to be
    // holding a name out of. Read off the catalogue rather than named here.
    Plugin? widest = plugins
        .Where(static plugin => plugin.Indexed is not null)
        .OrderByDescending(static plugin => plugin.Indexed!.Operations.Sum(static kind => kind.Value.Count))
        .ThenBy(static plugin => plugin.Namespace, StringComparer.Ordinal)
        .FirstOrDefault();
    (string Op, string Kind)? example = widest?.Indexed!.Operations
        .SelectMany(static kind => kind.Value.Select(op => (Op: op, Kind: kind.Key)))
        .FirstOrDefault();

    // The names offered under the search box: that operation, an input of a composite, a
    // shorthand character in use, and a rule set by the part of its name a person remembers.
    List<string> tries = [];
    if (example is (string exampleOp, _))
    {
        tries.Add(exampleOp);
    }

    if (ruleSets.SelectMany(static ruleSet => ruleSet.Offered).FirstOrDefault(static input => input.Contains('.')) is string qualified)
    {
        tries.Add(qualified);
    }

    if (plugins.FirstOrDefault(static plugin => plugin.Prefix is not null) is Plugin prefixed)
    {
        tries.Add(prefixed.Namespace);
    }

    if (ruleSets.FirstOrDefault() is RuleSet firstRuleSet)
    {
        tries.Add(Last(firstRuleSet.Id));
    }

    body.Append("<section class=\"hero\"><div class=\"hero-text\">");
    body.Append("<p class=\"pill ok\">Open for submissions</p>");
    body.Append("<h1>Every name in Rulealize, and who owns it.</h1>");
    body.Append("""
        <p class="lede">Which plugin provides an operation, which namespaces and shorthand characters
        are spoken for, and which published rule sets a <code>uses</code> can name — read out of the
        packages themselves, and checked again every day.</p>
        <div class="search"><input id="q" type="search" placeholder="grid.ray, seq., grant, quota&hellip;" autocomplete="off" spellcheck="false" aria-label="Find a name"><kbd>/</kbd></div>
        <p id="count" class="meta"></p>
        <ul id="results" class="results"></ul>
        """);

    if (tries.Count is not 0)
    {
        body.Append("<p class=\"try\">Try " + string.Concat(tries.Select(static term =>
            $"<button type=\"button\" data-q=\"{H(term)}\">{H(term)}</button>")) + "</p>");
    }

    body.Append("</div>");

    if (widest is not null && example is (string op, string kind))
    {
        string[] carrying = Carrying(op, kind, widest);
        string range = carrying.Length switch
        {
            0 => "",
            1 => carrying[0],
            _ => $"{carrying[0]} – {carrying[^1]}",
        };

        body.Append("<aside class=\"answer\">");
        body.Append(Eyebrow("You arrive holding"));
        body.Append($"<p class=\"held-name\"><a href=\"op/{H(op)}.html\"><code>{H(op)}</code></a></p>");
        body.Append(Eyebrow("The index answers"));
        body.Append("<table class=\"facts\">");
        body.Append($"<tr><th>Kind</th><td><code>{H(kind)}</code></td></tr>");
        body.Append($"<tr><th>Plugin</th><td><a href=\"plugin/{H(widest.Id)}.html\"><code>{H(widest.Id)}</code></a></td></tr>");
        body.Append($"<tr><th>Namespace</th><td><code>{H(widest.Namespace)}</code></td></tr>");
        body.Append($"<tr><th>In versions</th><td><code>{H(range)}</code></td></tr>");
        body.Append(
            $"<tr><th>Shorthand</th><td>{(widest.Prefix is null ? "<span class=\"none\">none</span>" : $"<code>{H(widest.Prefix)}</code>")}</td></tr>");
        body.Append("</table>");
        body.Append($"<pre class=\"dark\"><code>$ dotnet add package {H(widest.Id)}</code></pre>");
        body.Append("</aside>");
    }

    body.Append("</section>");

    body.Append("<section class=\"stats\">");
    body.Append($"<div><b>{plugins.Count}</b><span>plugins admitted</span></div>");
    body.Append($"<div><b>{operationCount}</b><span>operations indexed</span></div>");
    body.Append($"<div><b>{ruleSets.Count}</b><span>rule sets published</span></div>");
    if (CheckedAt is not null)
    {
        body.Append($"<div><b class=\"stamp\"><i class=\"live\" aria-hidden=\"true\"></i>{H(Stamp(CheckedAt))}</b><span>catalogue last read from nuget.org</span></div>");
    }

    body.Append("</section>");

    body.Append("<section id=\"namespaces\" class=\"section\">");
    body.Append(Eyebrow("Namespaces"));
    body.Append($"<h2>{plugins.Count} namespaces, one owner each</h2>");
    body.Append("""
        <p class="sub">A namespace has exactly one owner across the whole ecosystem, and the runtime
        refuses two plugins that claim one. That check runs when somebody assembles a plugin folder —
        after both were published — so this is the only place a collision can be seen before it costs
        anything.</p>
        """);

    body.Append("<ul class=\"grid ns-grid\">");
    foreach (Plugin plugin in plugins.OrderBy(static plugin => plugin.Namespace, StringComparer.Ordinal))
    {
        int operations = plugin.Indexed?.Operations.Sum(static kind => kind.Value.Count) ?? 0;
        int withheld = plugin.Releases.Count(static release => release.Withheld is not null);

        body.Append($"<li><a class=\"card ns-card\" href=\"plugin/{H(plugin.Id)}.html\">");
        body.Append($"<span class=\"ns\"><code>{H(plugin.Namespace)}</code>");
        if (plugin.Prefix is not null)
        {
            body.Append($"<kbd class=\"cap\" title=\"shorthand character\">{H(plugin.Prefix)}</kbd>");
        }

        body.Append("</span>");
        body.Append($"<span class=\"id\">{H(plugin.Id)}</span>");
        body.Append("<span class=\"foot\">");
        body.Append(plugin.Latest is null ? "<span class=\"none\">—</span>" : $"<code>{H(plugin.Latest)}</code>");

        // A release that is published and not indexed, said where somebody scanning the list
        // for their own plugin will see it.
        if (withheld is not 0)
        {
            body.Append($" <span class=\"warn\">{withheld} withheld</span>");
        }

        body.Append($"<span class=\"n\">{operations} operations</span></span></a></li>");
    }

    body.Append("</ul>");

    if (ReservedNamespaces.Count is not 0)
    {
        body.Append("<p class=\"reserved\">Reserved by the registry, not grantable: "
            + string.Concat(ReservedNamespaces.Select(static name => $"<code>{H(name)}</code>")) + "</p>");
    }

    body.Append("</section>");

    string characters = string.Join(
        ", ",
        plugins.Where(static plugin => plugin.Prefix is not null)
            .OrderBy(static plugin => plugin.Prefix, StringComparer.Ordinal)
            .Select(static plugin => $"<code>{H(plugin.Prefix)}</code> ({H(plugin.Namespace)})"));

    // A catalogue where nobody has reserved one is a sentence with a list in the middle of it
    // and nothing to put there. That none are in use is a true thing to say about it.
    string inUse = characters.Length is 0 ? "<span class=\"none\">none</span>" : characters;

    body.Append("<section id=\"shorthand\" class=\"band\"><div class=\"band-text\">");
    body.Append(Eyebrow("Shorthand"));
    body.Append("<h2>One keystroke wide, and recorded rather than owned</h2>");
    body.Append($"""
        <p>In use: {inUse}. A string beginning with one is handed to that plugin's expander instead of
        being read as text, so the supply is one keystroke wide and cannot be extended — letters, digits
        and anything ordinary data might begin with are unusable. <strong>Under a dozen exist for the
        entire future of the ecosystem</strong>, and none of them is taken: a character is recorded here
        and granted to nobody, so a plugin may reserve one another plugin already reserved. The two load
        together, and a rule set that would otherwise be ambiguous names the vocabulary it meant —
        <code>"$state:board"</code>.</p>
        <p><a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md#shorthand-characters">What
        is refused, and the two things worth knowing first →</a></p>
        """);
    body.Append("</div><div class=\"keys\">");

    Plugin[] reserving = [.. plugins.Where(static plugin => plugin.Prefix is not null)
        .OrderBy(static plugin => plugin.Prefix, StringComparer.Ordinal)];
    if (reserving.Length is not 0)
    {
        body.Append(Eyebrow("In use"));
        body.Append("<ul class=\"caps\">" + string.Concat(reserving.Select(static plugin =>
            $"<li><a href=\"plugin/{H(plugin.Id)}.html\"><kbd>{H(plugin.Prefix)}</kbd><code>{H(plugin.Namespace)}</code></a></li>")) + "</ul>");
    }

    if (ReservedPrefixes.Count is not 0)
    {
        body.Append(Eyebrow("Refused — ordinary data begins with these"));
        body.Append("<ul class=\"caps refused\">" + string.Concat(ReservedPrefixes.Select(static prefix =>
            $"<li><kbd>{H(prefix)}</kbd></li>")) + "</ul>");
    }

    body.Append("</div></section>");

    body.Append("<section id=\"rule-sets\" class=\"section\">");
    body.Append(Eyebrow("Rule sets"));
    body.Append("<h2>Published rule sets a <code>uses</code> can name</h2>");
    body.Append("""
        <p class="sub">A rule set's <code>uses</code> names the documents it holds, by the identifier
        each one declares. That identifier is the package identifier, so nothing has to resolve one to
        the other — these are the documents that exist, what each of them holds, and what it would cost
        to hold one.</p>
        """);

    if (ruleSets.Count is 0)
    {
        // A list with nothing in it says less than a sentence does, and this is the state the
        // index is in until somebody publishes the first one.
        body.Append("""
            <p class="meta">None yet. A rule set is published as a package whose <code>ruleset</code>
            folder holds the document declaring the package's identifier, and any parts it is built out
            of beside it. It is submitted the way a plugin is — the package and the version its document
            was read at. <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/publish.md">What to
            build</a>.</p>
            """);
    }
    else
    {
        body.Append("<ul class=\"grid rs-grid\">");
        foreach (RuleSet ruleSet in ruleSets.OrderBy(static ruleSet => ruleSet.Id, StringComparer.Ordinal))
        {
            int withheld = ruleSet.Releases.Count(static release => release.Withheld is not null);
            int holds = ruleSet.Indexed?.Uses.Count ?? 0;
            List<string> inputs = ruleSet.Offered.Count is not 0 ? ruleSet.Offered : ruleSet.Indexed?.Inputs ?? [];
            int dot = ruleSet.Id.LastIndexOf('.');

            body.Append($"<li><a class=\"card rs-card\" href=\"ruleset/{H(ruleSet.Id)}.html\">");
            body.Append($"<span class=\"vendor\">{H(dot > 0 ? ruleSet.Id[..(dot + 1)] : "")}</span><b>{H(Last(ruleSet.Id))}</b>");
            if (ruleSet.Description is not null)
            {
                body.Append($"<span class=\"desc\">{H(ruleSet.Description)}</span>");
            }

            if (inputs.Count is not 0)
            {
                body.Append("<span class=\"label\">Inputs</span><span class=\"chips\">");
                body.Append(string.Concat(inputs.Take(3).Select(static input => $"<code>{H(input)}</code>")));
                if (inputs.Count > 3)
                {
                    body.Append($"<code class=\"more\">+{inputs.Count - 3}</code>");
                }

                body.Append("</span>");
            }

            body.Append("<span class=\"foot\">");
            body.Append(ruleSet.Latest is null ? "<span class=\"none\">—</span>" : $"<code>{H(ruleSet.Latest)}</code>");
            if (withheld is not 0)
            {
                body.Append($" <span class=\"warn\">{withheld} withheld</span>");
            }

            if (holds is not 0)
            {
                body.Append($"<span class=\"tag\">holds {holds}</span>");
            }

            body.Append("</span></a></li>");
        }

        body.Append("</ul>");
    }

    body.Append("</section>");

    body.Append("<section id=\"submit\" class=\"section\">");
    body.Append(Eyebrow("How a name gets in"));
    body.Append("<h2>One form, and nothing in it is believed</h2>");
    body.Append("""
        <p class="sub">A submission names a package and a version — and for a plugin, the two names it
        claims. Everything else is read out of the package, and nobody has to approve it.</p>
        <ol class="grid steps">
        <li class="card"><span class="n">01</span><b>Fill in the form</b><span class="desc">Package,
        version, and for a plugin its namespace and shorthand — checked against nuget.org as you
        type.</span><pre class="dark"><code>package    Acme.Plugin.Dice
        version    1.0.0
        namespace  dice</code></pre></li>
        <li class="card"><span class="n">02</span><b>Send it from GitHub</b><span class="desc">The form
        opens your submission on GitHub, filled in; one click sends it. The registry loads the package
        and refuses anything it contradicts.</span><pre class="dark"><code><i class="good">✓</i> namespace  dice
        <i class="good">✓</i> prefix     none
        <i class="good">✓</i> version    1.0.0</code></pre></li>
        <li class="card"><span class="n">03</span><b>Every release, indexed daily</b><span class="desc">New
        versions need no new submission. One that renames itself is withheld and marked — never
        silently indexed.</span><pre class="dark"><code>1.1.0  indexed
        1.2.0  <i class="bad">withheld · claims dice2</i></code></pre></li>
        </ol>
        <p class="actions"><a class="button" href="submit/index.html">Submit a package →</a>
        <a class="button ghost" href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/publish.md">How to build a package</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md">Read the grant policy</a></p>
        """);
    body.Append("</section>");

    if (CheckedAt is not null)
    {
        body.Append("<section class=\"band night\"><div class=\"band-text\">");
        body.Append(Eyebrow("Kept, not just served"));
        body.Append("<h2>Every page says when it was last true</h2>");
        body.Append("""
            <p>The catalogue is rebuilt from nuget.org every day. An index nobody keeps looks exactly
            like one that is kept — so the time of the last read is on every page, and a release that
            renamed itself is shown, marked, rather than quietly dropped.</p>
            """);
        body.Append("</div><div class=\"run\">");
        body.Append(Eyebrow("Last run"));
        body.Append($"<p class=\"when\">{H(Stamp(CheckedAt))}</p><ul>");
        body.Append($"<li><i class=\"good\">✓</i> {plugins.Count} plugins read back from their assemblies</li>");
        body.Append($"<li><i class=\"good\">✓</i> {ruleSets.Count} rule sets read back from their documents</li>");
        body.Append("<li><i class=\"good\">✓</i> every release checked against the ledger</li>");
        body.Append($"<li><i>·</i> {withheldCount} releases withheld</li>");
        body.Append("</ul></div></section>");
    }

    body.Append("""
        <script>
        const results = document.getElementById('results');
        const count = document.getElementById('count');
        const box = document.getElementById('q');

        // One flat list of {name, kind, from, href}, because a searcher does not know which of
        // the two kinds the name in their hand belongs to — that is the question they came
        // with. Matching is on the name alone and never on a description: a search that
        // sometimes hits prose is one nobody can predict, and predictable is most of what a
        // search over a few hundred short names has to be.
        let names = [];
        let summary = '';

        fetch('index.json').then(r => r.json()).then(data => {
          const operations = data.operations ?? [];
          const ruleSets = data.ruleSets ?? [];

          names = [
            ...operations.map(o => (
              { name: o.op, kind: o.kind, from: o.plugin, href: 'op/' + o.op + '.html' })),
            ...ruleSets.flatMap(r => [
              { name: r.id, kind: 'rule set', from: '', href: 'ruleset/' + r.id + '.html' },
              ...(r.inputs ?? []).map(i => (
                { name: i, kind: 'input', from: r.id, href: 'ruleset/' + r.id + '.html' }))
            ])
          ];

          summary = operations.length + ' operations and ' + ruleSets.length + ' rule sets indexed.';
          count.textContent = summary;
        });

        const answer = () => {
          const term = box.value.trim().toLowerCase();
          if (!term) {
            results.replaceChildren();
            count.textContent = summary;
            return;
          }
          const hits = names.filter(n => n.name.toLowerCase().includes(term));
          count.textContent = hits.length + ' of ' + names.length + ' match.';
          results.replaceChildren(...hits.slice(0, 60).map(n => {
            const li = document.createElement('li');
            const a = document.createElement('a');
            a.href = n.href;
            a.textContent = n.name;
            const kind = document.createElement('span');
            kind.className = 'kind';
            kind.textContent = n.kind;
            const from = document.createElement('span');
            from.className = 'from';
            from.textContent = n.from;
            li.append(a, kind, from);
            return li;
          }));
        };

        box.addEventListener('input', answer);

        // The names under the box fill it in, and `/` anywhere on the page puts the cursor in
        // it, which is what the same key does on every page that links here.
        if (document.querySelectorAll) {
          document.querySelectorAll('[data-q]').forEach(b => b.addEventListener('click', () => {
            box.value = b.dataset.q;
            answer();
            box.focus();
          }));
          document.addEventListener('keydown', e => {
            if (e.key === '/' && document.activeElement !== box) {
              e.preventDefault();
              box.focus();
            }
          });
          if (location.hash === '#q') {
            box.focus();
          }
        }
        </script>
        """);

    await Write(Path.Combine(output, "index.html"), "Rulealize Registry", ".", body.ToString());
}

// Withheld is what the catalogue said about a release it did not index, and Claimed is what
// that release claimed instead. Both are absent from a release that is in the index.
internal sealed record Release(
    string Version,
    string? Abstraction,
    string? Framework,
    Dictionary<string, List<string>> Operations,
    string? Withheld,
    Claimed? Claimed);

internal sealed record Claimed(string? Namespace, string? Prefix);

internal sealed record Plugin(
    string Id,
    string Namespace,
    string? Prefix,
    string Admitted,
    string? Latest,
    string? Description,
    string? Repository,
    string? License,
    List<Release> Releases)
{
    /// <summary>The newest release the catalogue indexed, if it indexed any.</summary>
    internal Release? Indexed => Releases.LastOrDefault(static release => release.Withheld is null);
}

/// <summary>One entry of a rule set's <c>requires</c>: a vocabulary, and which versions will do.</summary>
internal sealed record Needs(string Plugin, string? Version);

/// <summary>One entry of a rule set's <c>uses</c>: a document it holds, and the name it calls it by.</summary>
internal sealed record Held(string RuleSet, string? Version, string Alias);

/// <summary>What a withheld release's document declared about itself instead.</summary>
internal sealed record Declared(string? Id, string? Version);

internal sealed record RuleSetRelease(
    string Version,
    List<Needs> Requires,
    List<Held> Uses,
    List<string> Inputs,
    List<string> Parts,
    string? Withheld,
    Declared? Claimed);

internal sealed record RuleSet(
    string Id,
    string Admitted,
    string? Latest,
    string? Description,
    string? Repository,
    string? License,
    List<RuleSetRelease> Releases,
    List<string> Offered)
{
    /// <summary>The newest release the catalogue indexed, if it indexed any.</summary>
    internal RuleSetRelease? Indexed => Releases.LastOrDefault(static release => release.Withheld is null);
}

internal static partial class Program
{
    /// <summary>When the catalogue these pages were built from was read, if it says.</summary>
    internal static string? CheckedAt { get; set; }

    /// <summary>The namespaces nothing may claim, as the catalogue publishes them.</summary>
    internal static List<string> ReservedNamespaces { get; set; } = [];

    /// <summary>The shorthand characters nothing may claim, as the catalogue publishes them.</summary>
    internal static List<string> ReservedPrefixes { get; set; } = [];

    // One stylesheet, no framework, no build step, and no font fetched from anywhere: the faces
    // are named and fall back to the system's own. Both themes are defined because a reader
    // arrives in whichever one their system is set to, and a page that only looks right in one
    // of them looks broken in the other. Monospace is kept for one thing — a name somebody
    // could type into a rule set verbatim — so that it means something when it appears.
    public const string Style = """
        :root {
          --paper: #fafaf7; --surface: #ffffff; --ink: #15171c; --dim: #5d626c; --faint: #8b909a;
          --line: #e5e4de; --code: #f1f0eb; --accent: #3355e0; --accent-soft: #ecf0fd; --accent-ink: #1f3bb3;
          --ok: #1e7a4e; --ok-soft: #e6f4ec; --warn: #9a6200; --warn-soft: #fff3d6; --err: #c2362f; --err-soft: #fdecea;
          --night: #0f1115; --night-surface: #171a20; --night-line: #272b33; --night-ink: #e9eaee; --night-dim: #a2a7b2;
          --good: #5fd39a; --bad: #f2c14e;
          --sans: Inter, system-ui, -apple-system, "Segoe UI", sans-serif;
          --mono: "JetBrains Mono", ui-monospace, "Cascadia Code", Consolas, monospace;
          --shadow: 0 4px 16px rgb(21 23 28 / .06);
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --paper: #0f1115; --surface: #171a20; --ink: #e9eaee; --dim: #a2a7b2; --faint: #6e7380;
            --line: #272b33; --code: #20242c; --accent: #8ea6ff; --accent-soft: #1c2440; --accent-ink: #b9c8ff;
            --ok: #5fd39a; --ok-soft: #13291f; --warn: #f2c14e; --warn-soft: #2a2413; --err: #ff8a80; --err-soft: #2d1716;
            --night: #0a0c10; --night-surface: #13161b; --night-line: #23272e;
            --shadow: none;
          }
        }
        * { box-sizing: border-box; }
        html { scroll-padding-top: 1rem; }
        body { margin: 0; background: var(--paper); color: var(--ink); font: 16px/1.6 var(--sans); overflow-x: clip; }
        .wrap { max-width: 72rem; margin: 0 auto; padding: 0 1.5rem; }
        a { color: var(--accent); text-decoration: none; }
        a:hover { text-decoration: underline; }
        code, kbd, pre { font-family: var(--mono); }
        code { font-size: .9em; background: var(--code); padding: .1em .35em; border-radius: 4px; }
        a code { color: var(--accent-ink); }
        pre { background: var(--code); padding: .9rem 1.1rem; border-radius: 10px; overflow-x: auto; font-size: .875rem; line-height: 1.7; }
        pre code { background: none; padding: 0; color: inherit; }
        pre.dark { background: #15171c; color: #e9eaee; border: 1px solid transparent; }
        @media (prefers-color-scheme: dark) { pre.dark { background: #0a0c10; border-color: var(--line); } }
        pre .good, .run .good { color: var(--good); font-style: normal; }
        pre .bad { color: var(--bad); font-style: normal; }
        kbd { font-size: .8rem; color: var(--dim); background: var(--paper); border: 1px solid var(--line); border-radius: 6px; padding: .05rem .45rem; }
        h1, h2, h3, h4 { line-height: 1.2; margin: 0; }
        h2 { font-size: 1.4rem; margin: 2.5rem 0 1rem; }
        h3 { font-size: 1rem; }
        p { margin: .75rem 0; }
        .lede { font-size: 1.15rem; color: var(--dim); max-width: 46rem; }
        .meta, .none { color: var(--dim); }
        .meta { font-size: .875rem; }
        .eyebrow { margin: 0 0 .5rem; font-size: .72rem; font-weight: 600; letter-spacing: .1em; text-transform: uppercase; color: var(--faint); }
        .section .eyebrow, .band .band-text > .eyebrow { color: var(--accent); }
        .warn { color: var(--warn); font-size: .8rem; font-weight: 600; white-space: nowrap; }
        .tag { font-size: .75rem; font-weight: 600; color: var(--accent-ink); background: var(--accent-soft); border-radius: 999px; padding: .1rem .55rem; white-space: nowrap; }
        .tag.quiet { color: var(--dim); background: var(--code); }
        .new { font-size: .7rem; font-weight: 600; color: var(--ok); }
        .pill { display: inline-block; margin: 0 0 .9rem; padding: .3rem .8rem; border-radius: 999px; font-size: .85rem; font-weight: 600; }
        .pill.ok { color: var(--ok); background: var(--ok-soft); }
        .pill.ok::before { content: "● "; }
        .pill.ns { padding: 0; background: none; }
        .pill.ns code { font-size: 1rem; font-weight: 600; color: var(--accent-ink); background: var(--accent-soft); padding: .3rem .75rem; border-radius: 8px; }
        .button { display: inline-block; padding: .7rem 1.2rem; border-radius: 9px; background: var(--ink); color: var(--paper); font-weight: 600; }
        .button:hover { text-decoration: none; opacity: .9; }
        .button.ghost { background: var(--surface); color: var(--ink); border: 1px solid var(--line); }
        .button[aria-disabled="true"], .button:disabled { background: var(--line); color: var(--faint); pointer-events: none; }
        button.button { border: 0; font: inherit; font-weight: 600; cursor: pointer; }
        .card { display: block; background: var(--surface); border: 1px solid var(--line); border-radius: 14px; padding: 1.25rem 1.4rem; color: inherit; }
        a.card:hover { text-decoration: none; border-color: var(--accent); }
        .card.soft { background: var(--accent-soft); border-color: transparent; color: var(--accent-ink); }
        .card.soft h4 { margin-bottom: .4rem; }
        .card.soft p { margin: 0; font-size: .9rem; }

        /* header and footer */
        .top { border-bottom: 1px solid var(--line); }
        .bar { display: flex; align-items: center; gap: 2rem; min-height: 4.5rem; }
        .brand { display: inline-flex; align-items: center; gap: .55rem; color: var(--ink); font-size: 1.05rem; white-space: nowrap; }
        .brand:hover { text-decoration: none; }
        .brand span:not(.mark) { color: var(--dim); }
        .mark { display: inline-grid; place-items: center; width: 2rem; height: 2rem; border-radius: 8px; background: var(--ink); color: var(--paper); font: 700 1.05rem var(--mono); position: relative; }
        .mark::after { content: ""; position: absolute; right: 5px; bottom: 5px; width: 5px; height: 5px; border-radius: 2px; background: var(--accent); }
        .links { display: flex; gap: 1.75rem; margin-left: auto; }
        .links a { color: var(--dim); font-weight: 500; }
        .links a.on { color: var(--ink); font-weight: 600; }
        .find { display: flex; align-items: center; gap: 1.5rem; padding: .4rem .5rem .4rem .8rem; border: 1px solid var(--line); border-radius: 8px; background: var(--surface); color: var(--faint); font-size: .9rem; }
        .find:hover { text-decoration: none; border-color: var(--accent); }
        main.wrap { padding-top: 2.5rem; padding-bottom: 4rem; }
        .foot { border-top: 1px solid var(--line); padding: 3rem 0 2rem; font-size: .9rem; }
        .foot-cols { display: grid; grid-template-columns: 2fr 1fr 1fr 1fr; gap: 2rem; }
        .foot-cols p { color: var(--dim); max-width: 22rem; }
        .foot-cols h4 { font-size: .85rem; margin-bottom: .6rem; }
        .foot-cols div > a:not(.brand) { display: block; color: var(--dim); margin: .35rem 0; }
        .foot-base { display: flex; flex-wrap: wrap; gap: 1rem; justify-content: space-between; margin-top: 2rem; padding-top: 1rem; border-top: 1px solid var(--line); color: var(--faint); }
        /* The one thing in the footer that is not a link, and the one thing worth reading twice
           on a page somebody suspects is stale. */
        .checked { color: var(--dim); }
        .live { display: inline-block; width: .55rem; height: .55rem; margin-right: .5rem; border-radius: 50%; background: var(--ok); vertical-align: .05em; }

        /* front page */
        .hero { display: grid; grid-template-columns: minmax(0, 1fr) 26rem; gap: 4rem; align-items: start; padding-top: 1.5rem; }
        .hero h1 { font-size: 3.25rem; letter-spacing: -.02em; line-height: 1.07; margin: 0 0 1.25rem; }
        .search { position: relative; margin-top: 1.75rem; }
        .search input { width: 100%; padding: 1.1rem 3.5rem 1.1rem 1.2rem; font: 1.05rem var(--mono); color: var(--ink); background: var(--surface); border: 1px solid var(--line); border-radius: 12px; box-shadow: var(--shadow); }
        .search input:focus { outline: 2px solid var(--accent); outline-offset: -1px; }
        .search kbd { position: absolute; right: 1rem; top: 50%; transform: translateY(-50%); }
        .results { list-style: none; padding: 0; margin: .25rem 0 0; background: var(--surface); border-radius: 12px; }
        .results:not(:empty) { border: 1px solid var(--line); padding: .4rem; box-shadow: var(--shadow); }
        .results li { display: flex; gap: .75rem; align-items: baseline; padding: .5rem .7rem; border-radius: 8px; }
        .results li:hover { background: var(--accent-soft); }
        .results a { font-family: var(--mono); font-weight: 600; }
        .kind { font-size: .75rem; color: var(--dim); background: var(--code); border-radius: 5px; padding: .05rem .45rem; }
        .from { margin-left: auto; font-size: .8rem; color: var(--faint); }
        .try { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; color: var(--faint); font-size: .9rem; }
        .try button { font: .8rem var(--mono); color: var(--ink); background: var(--code); border: 0; border-radius: 999px; padding: .25rem .7rem; cursor: pointer; }
        .answer { background: var(--surface); border: 1px solid var(--line); border-radius: 16px; padding: 1.75rem; box-shadow: var(--shadow); }
        .held-name { margin: 0 0 1.25rem; padding-bottom: 1.25rem; border-bottom: 1px solid var(--line); }
        .held-name code { font-size: 2rem; font-weight: 600; background: none; padding: 0; color: var(--ink); }
        .answer pre { margin: 1.25rem 0 0; font-size: .8rem; }
        .stats { display: grid; grid-template-columns: repeat(4, 1fr); margin: 3.5rem 0 0; background: var(--surface); border: 1px solid var(--line); border-radius: 14px; }
        .stats div { padding: 1.4rem 1.75rem; }
        .stats div + div { border-left: 1px solid var(--line); }
        .stats b { display: block; font-size: 2.1rem; line-height: 1.2; }
        .stats b.stamp { font-size: 1.15rem; padding: .55rem 0 .35rem; }
        .stats span { color: var(--dim); font-size: .9rem; }
        .section { margin-top: 5rem; }
        .section h2, .band h2 { font-size: 2.1rem; margin: 0 0 .9rem; letter-spacing: -.01em; }
        .section h2 code { color: var(--accent); background: none; padding: 0; }
        .sub { color: var(--dim); max-width: 42rem; font-size: 1.05rem; }
        .grid { list-style: none; padding: 0; margin: 2rem 0 0; display: grid; gap: 1rem; }
        .ns-grid { grid-template-columns: repeat(4, 1fr); }
        .ns-card { display: flex; flex-direction: column; gap: .2rem; height: 100%; }
        .ns-card .ns { display: flex; justify-content: space-between; align-items: center; }
        .ns-card .ns code { font-size: 1.3rem; font-weight: 600; background: none; padding: 0; color: var(--ink); }
        .cap { font-size: .95rem; font-weight: 700; color: var(--accent-ink); background: var(--accent-soft); border: 0; padding: .1rem .55rem; }
        .ns-card .id { color: var(--dim); font-size: .82rem; overflow-wrap: anywhere; }
        .card .foot { display: flex; align-items: center; gap: .5rem; margin-top: auto; padding-top: .7rem; border-top: 1px solid var(--line); font-size: .8rem; }
        .card .foot code { background: none; padding: 0; color: var(--ink); }
        .card .foot .n, .card .foot .tag { margin-left: auto; }
        .card .foot .n { color: var(--dim); }
        .reserved { margin-top: 1.25rem; color: var(--dim); font-size: .9rem; display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; }
        .reserved code { background: none; border: 1px dashed var(--faint); color: var(--dim); }
        /* Edge to edge, with its content kept on the page's own column. */
        .band { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 4rem; margin: 5rem calc(50% - 50vw) 0; padding: 4.5rem max(1.5rem, calc(50vw - 34.5rem)); background: var(--surface); border-block: 1px solid var(--line); }
        .band p { color: var(--dim); }
        .caps { list-style: none; padding: 0; margin: 0 0 2rem; display: flex; flex-wrap: wrap; gap: 1rem; }
        .caps a { display: flex; flex-direction: column; align-items: center; gap: .3rem; width: 7.5rem; padding: 1rem 0 .9rem; border-radius: 14px; background: var(--accent-soft); box-shadow: inset 0 -6px 0 rgb(51 85 224 / .15); }
        .caps a:hover { text-decoration: none; }
        .caps a kbd { font-size: 2.4rem; font-weight: 600; color: var(--accent-ink); background: none; border: 0; padding: 0; line-height: 1.1; }
        .caps a code { background: none; color: var(--accent-ink); font-size: .8rem; }
        .caps.refused { gap: .75rem; }
        .caps.refused kbd { display: grid; place-items: center; width: 4.25rem; height: 3rem; font-size: 1.2rem; color: var(--faint); background: var(--paper); border-radius: 10px; }
        .rs-grid { grid-template-columns: repeat(3, 1fr); }
        .rs-card { display: flex; flex-direction: column; gap: .3rem; height: 100%; }
        .rs-card .vendor { font: .8rem var(--mono); color: var(--faint); }
        .rs-card b { font-size: 1.35rem; margin-top: -.2rem; }
        .rs-card .desc { color: var(--dim); font-size: .92rem; }
        .label { margin-top: .6rem; font-size: .68rem; font-weight: 600; letter-spacing: .1em; text-transform: uppercase; color: var(--faint); }
        .chips { display: flex; flex-wrap: wrap; gap: .35rem; margin-bottom: .6rem; }
        .chips code { font-size: .78rem; color: var(--ink); }
        .chips code.more { background: none; border: 1px solid var(--line); color: var(--dim); }
        .steps { grid-template-columns: repeat(3, 1fr); counter-reset: none; }
        .steps li { display: flex; flex-direction: column; gap: .4rem; }
        .steps .n { font: 600 .85rem var(--mono); color: var(--accent); }
        .steps b { font-size: 1.15rem; }
        .steps .desc { color: var(--dim); font-size: .92rem; }
        .steps pre { margin: auto 0 0; font-size: .78rem; }
        .actions { display: flex; flex-wrap: wrap; gap: .75rem 1.25rem; align-items: center; margin-top: 2rem; }
        .night { background: var(--night); border-color: var(--night); margin-bottom: -4rem; }
        .night h2 { color: var(--night-ink); }
        .night p { color: var(--night-dim); }
        .night .band-text > .eyebrow { color: #8ea6ff; }
        .run { background: var(--night-surface); border: 1px solid var(--night-line); border-radius: 12px; padding: 1.5rem; color: var(--night-ink); }
        .run .when { font: 600 1.4rem var(--mono); color: var(--night-ink); margin: 0 0 1rem; padding-bottom: 1rem; border-bottom: 1px solid var(--night-line); }
        .run ul { list-style: none; margin: 0; padding: 0; }
        .run li { padding: .35rem 0; }
        .run i { display: inline-block; width: 1.4rem; font-style: normal; font-family: var(--mono); color: var(--night-dim); }

        /* entry pages */
        .crumbs { color: var(--faint); font-size: .9rem; margin: 0 0 1.5rem; }
        .crumbs a { color: var(--faint); }
        .crumbs span { margin: 0 .4rem; }
        .title { font-size: 2.6rem; letter-spacing: -.01em; overflow-wrap: anywhere; margin-bottom: .75rem; }
        .title code { background: none; padding: 0; }
        .badges { list-style: none; padding: 0; margin: 1rem 0 0; display: flex; flex-wrap: wrap; gap: .5rem; }
        .badges li { font-size: .85rem; color: var(--dim); background: var(--surface); border: 1px solid var(--line); border-radius: 6px; padding: .25rem .65rem; }
        .badges code { background: none; padding: 0 0 0 .3rem; color: var(--ink); }
        .cols { display: grid; grid-template-columns: minmax(0, 1fr) 21rem; gap: 3.75rem; margin-top: 2.5rem; padding-top: .5rem; border-top: 1px solid var(--line); }
        .side { display: flex; flex-direction: column; gap: 1rem; }
        .side .button { display: block; text-align: center; margin-top: 1.25rem; }
        table { border-collapse: collapse; width: 100%; font-size: .92rem; }
        th, td { text-align: left; padding: .55rem 0; border-bottom: 1px solid var(--line); vertical-align: top; }
        tr:last-child th, tr:last-child td { border-bottom: 0; }
        .facts th { width: 7rem; color: var(--dim); font-weight: 400; }
        .facts td { overflow-wrap: anywhere; }
        .inputs { background: var(--surface); border: 1px solid var(--line); border-radius: 12px; border-collapse: separate; border-spacing: 0; }
        .inputs th, .inputs td { padding: .7rem 1.1rem; }
        .inputs thead th { font-size: .7rem; letter-spacing: .1em; text-transform: uppercase; color: var(--faint); font-weight: 600; }
        .inputs td:first-child code { color: var(--accent-ink); }
        .kinds { margin: 0 0 1rem; }
        .kinds h3 { margin-bottom: .9rem; }
        .kinds h3 .n { color: var(--faint); font: 400 .85rem var(--mono); margin-left: .35rem; }
        ul.ops { list-style: none; padding: 0; margin: 0; display: flex; flex-wrap: wrap; gap: .45rem; }
        ul.ops code { font-size: .85rem; padding: .25rem .6rem; border-radius: 6px; }
        .releases { list-style: none; padding: 0; margin: 0; background: var(--surface); border: 1px solid var(--line); border-radius: 12px; }
        .releases > li { padding: 1rem 1.4rem; }
        .releases > li + li { border-top: 1px solid var(--line); }
        .releases .ver { display: flex; align-items: center; gap: .6rem; font-size: 1rem; }
        .releases .ver code { background: none; padding: 0; font-weight: 600; }
        .releases h4 { margin: .9rem 0 .5rem; font-size: .8rem; color: var(--dim); font-weight: 600; }
        .releases .meta, .added { margin: .35rem 0 0; }
        .added code { background: none; padding: 0 .3rem 0 0; color: var(--ok); font-size: .82rem; }
        .withheld { padding: .75rem 1rem; border-radius: 8px; background: var(--warn-soft); color: var(--warn); font-size: .9rem; }
        .withheld code { background: none; padding: 0; color: inherit; }
        .callout { padding: .9rem 1.1rem; border-radius: 10px; background: var(--warn-soft); color: var(--warn); font-size: .92rem; }
        .callout code { background: none; padding: 0; color: inherit; font-weight: 600; }
        .diagram { margin-top: 2rem; background: var(--surface); border: 1px solid var(--line); border-radius: 16px; padding: 1.5rem 1.75rem 1.75rem; }
        .graph { display: grid; grid-template-columns: 15rem minmax(0, 1fr) minmax(0, 1.1fr); gap: 2.5rem; align-items: center; }
        .graph .node { display: flex; flex-direction: column; padding: .9rem 1.1rem; border-radius: 12px; background: var(--accent-soft); color: var(--accent-ink); border: 1px solid transparent; }
        .graph a.node:hover { text-decoration: none; border-color: var(--accent); }
        .graph .node span { font: .78rem var(--mono); }
        .graph .node b { font-size: 1.1rem; color: var(--ink); }
        .graph .node small { color: var(--dim); font-size: .8rem; overflow-wrap: anywhere; }
        .graph .self { background: var(--ink); color: var(--faint); }
        .graph .self b { color: var(--paper); }
        .graph .self small { color: var(--faint); }
        .held, .needs { display: flex; flex-direction: column; gap: .75rem; }
        .needs { border-left: 1px solid var(--line); padding-left: 2.5rem; }
        .edge { font: .75rem var(--mono); color: var(--faint); }
        .vocab { display: grid; grid-template-columns: 4rem minmax(0, 1fr) auto; gap: .6rem; align-items: baseline; padding: .6rem .9rem; border: 1px solid var(--line); border-radius: 10px; color: var(--dim); font-size: .85rem; }
        .vocab:hover { text-decoration: none; border-color: var(--accent); }
        .vocab code { background: none; padding: 0; font-weight: 600; color: var(--ink); }
        .vocab span { overflow-wrap: anywhere; }
        .vocab small { font-family: var(--mono); color: var(--faint); }

        /* submit */
        .stepper { list-style: none; padding: 0; margin: 2rem 0 0; display: flex; flex-wrap: wrap; gap: .75rem 2.5rem; }
        .stepper li { display: flex; align-items: center; gap: .6rem; color: var(--faint); font-weight: 500; font-size: .92rem; }
        .stepper li::before { content: attr(data-n); display: grid; place-items: center; width: 1.75rem; height: 1.75rem; border-radius: 50%; border: 1px solid var(--line); background: var(--surface); font-size: .8rem; font-weight: 700; }
        .stepper li.on { color: var(--ink); font-weight: 700; }
        .stepper li.on::before { background: var(--ink); color: var(--paper); border-color: var(--ink); }
        .stepper li.done { color: var(--dim); }
        .stepper li.done::before { content: "✓"; background: var(--ok); color: #fff; border-color: var(--ok); }
        .form { padding: 2rem; }
        .field { margin: 0 0 1.6rem; }
        .field > label, .field > span.label-text { display: block; font-weight: 600; font-size: .92rem; margin-bottom: .45rem; }
        .field input[type=text], .field select { width: 100%; padding: .75rem .9rem; font: .95rem var(--mono); color: var(--ink); background: var(--surface); border: 1px solid var(--line); border-radius: 8px; }
        .field input:focus, .field select:focus { outline: 2px solid var(--accent); outline-offset: -1px; }
        .field .help { font-size: .82rem; color: var(--dim); margin: .4rem 0 0; }
        .field.err input, .field.err select { border-color: var(--err); }
        .field.err .help { color: var(--err); }
        .field.warn .help { color: var(--warn); }
        .seg { display: inline-flex; padding: 4px; border-radius: 10px; background: var(--code); }
        .seg label { padding: .45rem 1.6rem; border-radius: 7px; font-size: .92rem; font-weight: 500; color: var(--dim); cursor: pointer; }
        .seg input { position: absolute; opacity: 0; pointer-events: none; }
        .seg input:checked + span { color: var(--ink); font-weight: 600; }
        .seg label:has(input:checked) { background: var(--surface); box-shadow: 0 1px 3px rgb(21 23 28 / .1); }
        .seg-row { display: flex; flex-wrap: wrap; gap: .75rem; align-items: center; }
        .seg-row input[type=text] { width: 3.5rem; text-align: center; font-size: 1.1rem; }
        .form-foot { display: flex; flex-wrap: wrap; gap: 1rem; align-items: center; margin: 1.5rem -2rem -2rem; padding: 1.25rem 2rem; border-top: 1px solid var(--line); }
        .form-foot .meta { margin: 0; }
        .checks { list-style: none; padding: 0; margin: 0 0 1rem; }
        .checks li { position: relative; padding: 0 0 .9rem 2.1rem; font-size: .9rem; font-weight: 600; }
        .checks li small { display: block; font-weight: 400; color: var(--dim); font-size: .82rem; }
        .checks li::before { position: absolute; left: 0; top: .05rem; display: grid; place-items: center; width: 1.35rem; height: 1.35rem; border-radius: 50%; font-size: .72rem; font-weight: 700; }
        .checks .ok::before { content: "✓"; color: var(--ok); background: var(--ok-soft); }
        .checks .err::before { content: "✕"; color: var(--err); background: var(--err-soft); }
        .checks .err small { color: var(--err); }
        .checks .warn::before { content: "!"; color: var(--warn); background: var(--warn-soft); }
        .checks .info::before { content: "→"; color: var(--accent-ink); background: var(--accent-soft); }
        .checks .wait { color: var(--faint); }
        .checks .wait::before { content: "…"; color: var(--faint); background: var(--code); }
        .claim { font: 600 2.4rem var(--mono); margin: 0 0 1rem; }
        .permanent { margin-top: 1.25rem; padding: 1.1rem 1.4rem; border-radius: 12px; background: var(--warn-soft); color: var(--warn); }
        .permanent h4 { margin-bottom: .3rem; }
        .permanent label { display: flex; gap: .5rem; align-items: center; margin-top: .75rem; color: var(--ink); font-weight: 600; }
        .next { list-style: none; padding: 0; margin: 1rem 0 2rem; counter-reset: next; }
        .next li { position: relative; padding: 0 0 1.1rem 2.75rem; counter-increment: next; color: var(--dim); font-size: .92rem; }
        .next li b { display: block; color: var(--ink); font-size: 1rem; }
        .next li::before { content: counter(next); position: absolute; left: 0; top: 0; display: grid; place-items: center; width: 1.75rem; height: 1.75rem; border-radius: 50%; background: var(--accent-soft); color: var(--accent-ink); font-size: .8rem; font-weight: 700; }
        [hidden] { display: none !important; }

        @media (max-width: 960px) {
          .hero, .band, .cols, .graph { grid-template-columns: minmax(0, 1fr); gap: 2rem; }
          .needs { border-left: 0; padding-left: 0; }
          .ns-grid, .rs-grid, .steps { grid-template-columns: repeat(2, 1fr); }
          .stats { grid-template-columns: repeat(2, 1fr); }
          .stats div:nth-child(3) { border-left: 0; }
          .stats div:nth-child(n + 3) { border-top: 1px solid var(--line); }
          .foot-cols { grid-template-columns: repeat(3, 1fr); }
          .foot-brand { grid-column: 1 / -1; }
        }
        @media (max-width: 640px) {
          .wrap { padding: 0 1rem; }
          main.wrap { padding-top: 1.75rem; padding-bottom: 3rem; }
          .bar { flex-wrap: wrap; gap: .5rem 1rem; padding-block: .75rem; }
          .links { order: 3; width: 100%; margin: 0; gap: 1.25rem; overflow-x: auto; }
          .find { margin-left: auto; gap: .75rem; }
          .hero h1 { font-size: 2.15rem; }
          .title { font-size: 1.9rem; }
          .section h2, .band h2 { font-size: 1.6rem; }
          .ns-grid, .rs-grid, .steps, .foot-cols { grid-template-columns: minmax(0, 1fr); }
          .band { padding: 3rem 1rem; }
          .night { margin-bottom: -3rem; }
          .form { padding: 1.25rem; }
          .form-foot { margin: 1.25rem -1.25rem -1.25rem; padding: 1rem 1.25rem; }
          .seg label { padding: .45rem 1rem; }
        }
        """;
}
