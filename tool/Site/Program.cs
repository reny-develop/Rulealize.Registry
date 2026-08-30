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
Directory.CreateDirectory(Path.Combine(output, "ruleset"));

using JsonDocument index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(catalogue, "index.json")));

// Every page says when the catalogue was last read out of nuget.org, because an index nobody
// is keeping looks exactly like one that is until it says so. It is carried on the class
// rather than passed to Write, which is a static local function and cannot capture it; an
// older catalogue that does not have the field leaves the line off rather than lying about it.
Program.CheckedAt = Text(index.RootElement, "checked");

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

        Declared? claimed = release.TryGetProperty("claimed", out JsonElement said)
            ? new Declared(Text(said, "id"), Text(said, "version"))
            : null;

        releases.Add(new RuleSetRelease(
            release.GetProperty("version").GetString()!,
            requires,
            uses,
            inputs,
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
        releases));

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

foreach (Plugin plugin in plugins)
{
    await Write(Path.Combine(output, "plugin", $"{plugin.Id}.html"), plugin.Id, "..", PluginBody(plugin));
}

// Which identifiers this index can answer for. A `uses` entry naming one that is not here is
// rendered as the text it is, with what that means said once on the page: the document is
// published and the thing it holds is not, so nothing can restore it but its author.
HashSet<string> indexed = [.. ruleSets.Select(static ruleSet => ruleSet.Id)];
HashSet<string> vocabularies = [.. plugins.Select(static plugin => plugin.Id)];

foreach (RuleSet ruleSet in ruleSets)
{
    await Write(
        Path.Combine(output, "ruleset", $"{ruleSet.Id}.html"),
        ruleSet.Id,
        "..",
        RuleSetBody(ruleSet, indexed, vocabularies));
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
            await Write(Path.Combine(output, "op", $"{op}.html"), op, "..", OperationBody(op, kind, plugin));
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

// The catalogue records the second it was read; a page wants the day. Anything that is not a
// timestamp is shown as it is — escaped, like everything else — rather than dropped, because a
// footer that quietly says nothing is how a broken field stays broken.
static string Day(string value) =>
    DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset when)
        ? when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : value;

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
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/publish.md">Publishing</a>
        <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/ledger/submitted.json">Ledger</a>
        <a href="https://github.com/reny-develop/Rulealize">Rulealize</a>
        {(CheckedAt is null ? "" : $"<span class=\"checked\">Last checked {H(Day(CheckedAt))}</span>")}
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
        body.Append($"<h2>{H(release.Version)}");
        if (release.Withheld is not null)
        {
            body.Append(" <span class=\"warn\">not indexed</span>");
        }

        body.Append("</h2>");

        // The one thing on these pages addressed to the plugin's author rather than to
        // somebody reading a rule set. Nothing else tells them: the catalogue is rebuilt
        // without asking anybody, and a release that quietly went missing from an index reads
        // as an index that is behind.
        if (release.Withheld is string withheld)
        {
            body.Append($"<p class=\"withheld\">{Withheld(plugin, release, withheld)}</p>");
            continue;
        }

        body.Append("<p class=\"meta\">");
        if (release.Framework is not null)
        {
            body.Append($"targets <code>{H(release.Framework)}</code>");
        }

        if (release.Abstraction is not null)
        {
            body.Append(
                $"{(release.Framework is null ? "b" : ", b")}uilt against "
                + $"<code>Rulealize.Abstraction {H(release.Abstraction)}</code>");
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

static string RuleSetBody(RuleSet ruleSet, HashSet<string> indexed, HashSet<string> vocabularies)
{
    StringBuilder body = new();
    body.Append($"<h1>{H(ruleSet.Id)}</h1>");

    if (ruleSet.Description is not null)
    {
        body.Append($"<p class=\"lede\">{H(ruleSet.Description)}</p>");
    }

    body.Append("<table class=\"facts\">");
    body.Append(
        $"<tr><th>Latest</th><td>{(ruleSet.Latest is null ? "<span class=\"none\">none indexed</span>" : $"<code>{H(ruleSet.Latest)}</code>")}</td></tr>");
    body.Append($"<tr><th>Admitted at</th><td><code>{H(ruleSet.Admitted)}</code></td></tr>");
    body.Append(
        $"<tr><th>Holds</th><td>{(ruleSet.Indexed is { Uses.Count: > 0 } holding ? $"{holding.Uses.Count}" : "<span class=\"none\">nothing</span>")}</td></tr>");

    if (ruleSet.License is not null)
    {
        body.Append($"<tr><th>Licence</th><td>{H(ruleSet.License)}</td></tr>");
    }

    if (ruleSet.Repository is not null)
    {
        body.Append(Link(ruleSet.Repository) is string source
            ? $"<tr><th>Source</th><td><a href=\"{H(source)}\">{H(source)}</a></td></tr>"
            : $"<tr><th>Source</th><td>{H(ruleSet.Repository)}</td></tr>");
    }

    body.Append(
        $"<tr><th>Package</th><td><a href=\"https://www.nuget.org/packages/{H(ruleSet.Id)}\">nuget.org</a></td></tr>");
    body.Append($"<tr><th>Entry</th><td><a href=\"{H(ruleSet.Id)}.json\">{H(ruleSet.Id)}.json</a></td></tr>");
    body.Append("</table>");

    body.Append("<h2>Hold it</h2>");
    body.Append($$"""
        <pre><code>"uses": [
          { "ruleSet": "{{H(ruleSet.Id)}}", "version": "^{{H(Major(ruleSet.Latest))}}", "as": "{{H(Alias(ruleSet.Id))}}" }
        ]</code></pre>
        """);
    body.Append(
        "<p>The identifier is the package identifier, so nothing has to look it up: "
        + "<a href=\"https://github.com/reny-develop/Rulealize.Cli\"><code>rulealize restore</code></a> "
        + "fetches what <code>uses</code> names the way it fetches what <code>requires</code> names. "
        + "<code>as</code> is the short name the holding document calls it by, and it is what qualifies "
        + "this rule set's inputs inside that one.</p>");

    foreach (RuleSetRelease release in ruleSet.Releases.AsEnumerable().Reverse())
    {
        body.Append($"<h2>{H(release.Version)}");
        if (release.Withheld is not null)
        {
            body.Append(" <span class=\"warn\">not indexed</span>");
        }

        body.Append("</h2>");

        // The one thing on these pages addressed to the rule set's author rather than to
        // somebody about to hold it.
        if (release.Withheld is string withheld)
        {
            body.Append($"<p class=\"withheld\">{WithheldDocument(ruleSet, release, withheld)}</p>");
            continue;
        }

        if (release.Uses.Count is not 0)
        {
            body.Append("<h3>holds</h3><ul class=\"ops\">");
            foreach (Held held in release.Uses)
            {
                string named = $"{H(held.RuleSet)}{(held.Version is null ? "" : $" {H(held.Version)}")}";

                // An identifier this index cannot answer for. Said as text rather than as a
                // link that would 404, and counted below so the page says it once plainly.
                body.Append(indexed.Contains(held.RuleSet)
                    ? $"<li><a href=\"{H(held.RuleSet)}.html\"><code>{named}</code></a> as <code>{H(held.Alias)}</code></li>"
                    : $"<li><code>{named}</code> as <code>{H(held.Alias)}</code> <span class=\"warn\">not indexed</span></li>");
            }

            body.Append("</ul>");

            if (release.Uses.Any(held => !indexed.Contains(held.RuleSet)))
            {
                body.Append(
                    "<p class=\"meta\">A held rule set that is not indexed here is one nothing can fetch. "
                    + "It may be a document its author keeps beside this one, in which case holding it is "
                    + "correct and publishing this was premature — or it may be a name that never resolves.</p>");
            }
        }

        if (release.Requires.Count is not 0)
        {
            body.Append("<h3>requires</h3><ul class=\"ops\">");
            foreach (Needs needs in release.Requires)
            {
                string named = $"{H(needs.Plugin)}{(needs.Version is null ? "" : $" {H(needs.Version)}")}";
                body.Append(vocabularies.Contains(needs.Plugin)
                    ? $"<li><a href=\"../plugin/{H(needs.Plugin)}.html\"><code>{named}</code></a></li>"
                    : $"<li><code>{named}</code></li>");
            }

            body.Append("</ul>");
        }

        if (release.Inputs.Count is not 0)
        {
            // What a composite writes in `held` and in `fires`, which is the one thing
            // somebody has to know about this document before they can hold it.
            body.Append("<h3>inputs</h3><ul class=\"ops\">");
            foreach (string input in release.Inputs)
            {
                body.Append($"<li><code>{H(input)}</code></li>");
            }

            body.Append("</ul>");
        }
    }

    return body.ToString();
}

// Why a release is not in the index. The identifier this entry is filed under is elsewhere on
// the same page, so this is the half of the comparison that is nowhere else.
static string WithheldDocument(RuleSet ruleSet, RuleSetRelease release, string reason)
{
    if (reason is not "claims" || release.Claimed is not Declared claimed)
    {
        return "Nothing could be read out of this release — no <code>ruleset</code> folder, more than one "
            + "document in it, or one the reader refused — so it is not in the index.";
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

static async Task WriteFront(List<Plugin> plugins, List<RuleSet> ruleSets, string output)
{
    StringBuilder body = new();

    body.Append("<h1>The Rulealize index</h1>");
    body.Append("""
        <p class="lede">Which plugin provides an operation, which versions satisfy a rule set's
        <code>requires</code>, which namespaces and shorthand characters are already spoken for,
        and which published rule sets a <code>uses</code> can name.</p>
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
        int operations = plugin.Indexed?.Operations.Sum(static kind => kind.Value.Count) ?? 0;
        int withheld = plugin.Releases.Count(static release => release.Withheld is not null);

        body.Append("<tr>");
        body.Append($"<td><code>{H(plugin.Namespace)}</code></td>");
        body.Append(
            $"<td>{(plugin.Prefix is null ? "<span class=\"none\">—</span>" : $"<code>{H(plugin.Prefix)}</code>")}</td>");
        body.Append($"<td><a href=\"plugin/{H(plugin.Id)}.html\">{H(plugin.Id)}</a></td>");
        body.Append(
            $"<td>{(plugin.Latest is null ? "<span class=\"none\">—</span>" : $"<code>{H(plugin.Latest)}</code>")}");

        // A release that is published and not indexed, said where somebody scanning the table
        // for their own plugin will see it.
        if (withheld is not 0)
        {
            body.Append($" <span class=\"warn\">{withheld} withheld</span>");
        }

        body.Append("</td>");
        body.Append($"<td class=\"n\">{operations}</td>");
        body.Append("</tr>");
    }

    body.Append("</tbody></table>");

    string characters = string.Join(
        ", ",
        plugins.Where(static plugin => plugin.Prefix is not null)
            .OrderBy(static plugin => plugin.Prefix, StringComparer.Ordinal)
            .Select(static plugin => $"<code>{H(plugin.Prefix)}</code> ({H(plugin.Namespace)})"));

    // A catalogue where nobody has reserved one is a sentence with a list in the middle of it
    // and nothing to put there. That none are in use is a true thing to say about it.
    string inUse = characters.Length is 0 ? "<span class=\"none\">none</span>" : characters;

    body.Append($"""
        <h2>Shorthand characters</h2>
        <p>In use: {inUse}. A string beginning with one is handed to that plugin's expander instead of
        being read as text, so the supply is one keystroke wide and cannot be extended — letters, digits
        and anything ordinary data might begin with are unusable. <strong>Under a dozen exist for the
        entire future of the ecosystem</strong>, and none of them is taken: a character is recorded here
        and granted to nobody, so a plugin may reserve one another plugin already reserved. The two load
        together, and a rule set that would otherwise be ambiguous names the vocabulary it meant —
        <code>"$state:board"</code>.</p>
        <p><a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md#shorthand-characters">What
        is refused, and the two things worth knowing first</a>.</p>
        """);

    body.Append("<h2>Published rule sets</h2>");
    body.Append("""
        <p>A rule set's <code>uses</code> names the documents it holds, by the identifier each one
        declares. That identifier is the package identifier, so nothing has to resolve one to the
        other — this table is here to say which documents exist, what each of them holds, and what
        it would cost to hold one, rather than to answer a question a fetcher could not.</p>
        """);

    if (ruleSets.Count is 0)
    {
        // A table with no rows says less than a sentence does, and this is the state the index
        // is in until somebody publishes the first one.
        body.Append("""
            <p class="meta">None yet. A rule set is published as a package whose <code>ruleset</code>
            folder holds one document, and it is submitted the way a plugin is — one line naming the
            package and the version its document was read at.
            <a href="https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/publish.md">What to
            build</a>.</p>
            """);
    }
    else
    {
        body.Append("<table class=\"claims\"><thead><tr>");
        body.Append("<th>Rule set</th><th>Latest</th><th>Holds</th><th>Inputs</th>");
        body.Append("</tr></thead><tbody>");

        foreach (RuleSet ruleSet in ruleSets.OrderBy(static ruleSet => ruleSet.Id, StringComparer.Ordinal))
        {
            int withheld = ruleSet.Releases.Count(static release => release.Withheld is not null);

            body.Append("<tr>");
            body.Append($"<td><a href=\"ruleset/{H(ruleSet.Id)}.html\">{H(ruleSet.Id)}</a></td>");
            body.Append(
                $"<td>{(ruleSet.Latest is null ? "<span class=\"none\">—</span>" : $"<code>{H(ruleSet.Latest)}</code>")}");

            if (withheld is not 0)
            {
                body.Append($" <span class=\"warn\">{withheld} withheld</span>");
            }

            body.Append("</td>");
            body.Append($"<td class=\"n\">{ruleSet.Indexed?.Uses.Count ?? 0}</td>");
            body.Append($"<td class=\"n\">{ruleSet.Indexed?.Inputs.Count ?? 0}</td>");
            body.Append("</tr>");
        }

        body.Append("</tbody></table>");
    }

    body.Append("""
        <h2>The files behind this page</h2>
        <p>Nothing here is written by hand. Each entry is derived from the published package — a plugin
        by loading it and reading back what it registered, a rule set by reading the document it
        distributes — and these are the same files the resolver reads:</p>
        <ul>
          <li><a href="index.json"><code>/index.json</code></a> — every plugin, operation and rule set, in summary</li>
          <li><code>/plugin/&lt;id&gt;.json</code> — one plugin, every released version, every operation of each</li>
          <li><code>/ruleset/&lt;id&gt;.json</code> — one rule set, every released version, what each holds and requires</li>
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
    string? Withheld,
    Declared? Claimed);

internal sealed record RuleSet(
    string Id,
    string Admitted,
    string? Latest,
    string? Description,
    string? Repository,
    string? License,
    List<RuleSetRelease> Releases)
{
    /// <summary>The newest release the catalogue indexed, if it indexed any.</summary>
    internal RuleSetRelease? Indexed => Releases.LastOrDefault(static release => release.Withheld is null);
}

internal static partial class Program
{
    /// <summary>When the catalogue these pages were built from was read, if it says.</summary>
    internal static string? CheckedAt { get; set; }

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
        .warn { color: var(--notice); font-size: .8rem; font-weight: 600; white-space: nowrap; }
        .withheld {
          padding: .5rem .75rem; border-radius: 4px;
          background: var(--notice-bg); color: var(--notice); font-size: .875rem;
        }
        .withheld code { background: none; padding: 0; }
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
        /* Pushed to the end of the row: it is the one thing here that is not a link, and the
           one thing worth reading twice on a page somebody suspects is stale. */
        .checked { margin-left: auto; color: var(--dim); }
        """;
}
