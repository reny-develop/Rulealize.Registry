// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml.Linq;

// Builds the catalogue the site and the resolver are generated from, and checks every
// published version against what the ledger says was claimed.
//
//   dotnet run --project tool/Catalogue -- <ledger file> <probe> <output folder>
//
// Two kinds are indexed and both are crawled the same way. The ledger is the one file a
// person reads: one line per package, holding what was claimed and the version it was claimed
// at, because a claim is permanent and cannot differ between versions. The catalogue is not
// reviewed by anybody and holds one entry per version, because `requires` and `uses` read
// `^1.0` and what a release offers may grow within a major. That difference is the whole
// reason these are two documents. Operations, inputs, requires and uses are in neither the
// ledger nor a submission — they are read out of what was published, here.
//
// It follows that a new version of anything already in the ledger needs no pull request at
// all: nothing committed changes, and the catalogue picks it up the next time this runs.
// What that would otherwise let through is a package quietly renaming itself between
// versions, so this checks every version against the ledger. One that says something else is
// withheld: it stays in the entry, marked, carrying what it claimed, and nothing is indexed
// off it — not its operations and not its `latest`. The policy that a claim is permanent is
// enforced here, mechanically, while the file a human reads does not move.
//
// For a plugin, what may move is the namespace or the shorthand character. For a rule set it
// is the identifier the document declares and the version it declares, which are the package
// identifier and the package version — a document that renames itself between releases is
// unresolvable by every `uses` that named it, and this is the only party that sees it happen
// before somebody's restore does.
//
// Withheld rather than fatal, because the publisher is the only party who can put it right
// and a run that wrote nothing would tell everybody except them. What does stop this is the
// ledger naming a package nuget.org has no released version of, which is this repository
// pointing at something that is not there.
//
// <probe> is tool/Ledger, built. This tool loads no plugin itself — see the project file for
// why it cannot — and runs that one per version instead, reading the JSON it writes to
// standard output. Pass either the apphost or the .dll; a .dll is run through `dotnet`.
//
// A rule set needs none of that isolation: reading a document runs nothing, and one process
// could hold a hundred of them. It goes through the same probe anyway, so that the registry
// parses the format in exactly one place and that place is Rulealize's own reader.
//
// Prerelease versions are skipped and reported. A rule set's `requires` is written in three
// constraint forms that all parse as System.Version, so no constraint can name a prerelease
// and nothing in the catalogue could ever resolve to one.
//
// There is no published date on anything here. It is not on the package, it would cost a
// second API with its own pagination and compression, and nothing that reads this needs it.

if (args.Length is not 3)
{
    Console.Error.WriteLine("usage: Catalogue <ledger file> <probe> <output folder>");
    return 2;
}

string ledgerPath = args[0];
string probe = args[1];
string outputFolder = args[2];

const string FlatContainer = "https://api.nuget.org/v3-flatcontainer";

using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
string scratch = Directory.CreateTempSubdirectory("rulealize-catalogue").FullName;
List<string> violations = [];

using JsonDocument ledger = JsonDocument.Parse(await File.ReadAllTextAsync(ledgerPath));

List<PluginEntry> catalogued = [];

foreach (JsonElement claim in ledger.RootElement.GetProperty("plugins").EnumerateArray())
{
    string id = claim.GetProperty("id").GetString()!;
    string ns = claim.GetProperty("namespace").GetString()!;
    string? prefix = claim.GetProperty("prefix").ValueKind is JsonValueKind.Null
        ? null
        : claim.GetProperty("prefix").GetString();
    string admitted = claim.GetProperty("version").GetString()!;

    List<string> versions = await ReleasedVersions(id);
    if (versions.Count is 0)
    {
        violations.Add(
            $"{id}: nuget.org serves no released version of it. A package is fetched by the identifier "
            + "the ledger records, so that is the identifier it has to be published under.");
        continue;
    }

    List<VersionEntry> entries = [];
    Package? indexed = null;

    foreach (string version in versions)
    {
        Package package;
        try
        {
            package = await Fetch(id, version);
        }
        catch (Exception failure) when (failure is not (HttpRequestException or TaskCanceledException))
        {
            // Something published that nothing can be read out of: two target frameworks, no
            // lib folder, an assembly the probe refused. It is one publisher's upload and it
            // withholds one version, rather than the index everybody else is in.
            //
            // Not a fetch that failed. nuget.org not answering is this run being unable to
            // look, and the entry it would write is a guess about somebody else's package.
            Console.Error.WriteLine($"{id} {version} could not be read: {failure.Message}");
            entries.Add(VersionEntry.Unreadable(version));
            continue;
        }

        // The claims are the ledger's to state and every version's to honour. One that says
        // something else is withheld rather than indexed — its operations are named in a
        // namespace this plugin does not hold — and the entry records what it said instead,
        // because the publisher is the only party who can put it right and the only one who
        // is not told.
        if (package.Namespace != ns || package.Prefix != prefix)
        {
            Console.Error.WriteLine(
                $"{id} {version} claims namespace '{package.Namespace}' and prefix {Show(package.Prefix)}, "
                + $"where the ledger admitted '{ns}' and {Show(prefix)}. A claim is permanent.");
            entries.Add(VersionEntry.Moved(version, package));
            continue;
        }

        indexed = package;
        entries.Add(new VersionEntry(version, package.Abstraction, package.Framework, package.Operations, null, null));
    }

    // Everything the plugin is described by comes off its newest indexed release. A withheld
    // one describes a plugin the ledger does not hold.
    VersionEntry? newest = entries.LastOrDefault(static entry => entry.Withheld is null);
    int withheld = entries.Count(static entry => entry.Withheld is not null);

    catalogued.Add(new PluginEntry(
        id, ns, prefix, admitted, newest?.Version,
        indexed?.Description, indexed?.Repository, indexed?.License, entries));

    Console.Error.WriteLine(
        $"{id}: {entries.Count} version(s), latest {newest?.Version ?? "none indexed"}"
        + (withheld is 0 ? "" : $", {withheld} withheld"));
}

// The same crawl, over documents. An entry is two fields because a rule set claims one name
// and that name is the package it is published under, so there is nothing here to compare
// against a reserved list and nothing that could be true of one release and not another
// except the two strings the document declares about itself.
List<RuleSetEntry> documented = [];

foreach (JsonElement claim in ledger.RootElement.TryGetProperty("ruleSets", out JsonElement submitted)
    ? submitted.EnumerateArray()
    : [])
{
    string id = claim.GetProperty("id").GetString()!;
    string admitted = claim.GetProperty("version").GetString()!;

    List<string> versions = await ReleasedVersions(id);
    if (versions.Count is 0)
    {
        violations.Add(
            $"{id}: nuget.org serves no released version of it. A package is fetched by the identifier "
            + "the ledger records, so that is the identifier it has to be published under.");
        continue;
    }

    List<RuleSetVersion> entries = [];
    RuleSetPackage? indexed = null;

    foreach (string version in versions)
    {
        RuleSetPackage package;
        try
        {
            package = await FetchRuleSet(id, version);
        }
        catch (Exception failure) when (failure is not (HttpRequestException or TaskCanceledException))
        {
            // A package with no `ruleset` folder, with more than one document in it, or with
            // one the probe would not read. It is one publisher's upload and it withholds one
            // version, rather than the index everybody else is in.
            Console.Error.WriteLine($"{id} {version} could not be read: {failure.Message}");
            entries.Add(RuleSetVersion.Unreadable(version));
            continue;
        }

        // `uses` names an identifier and a fetcher goes to the package of that name, so the
        // two being one string is the whole of what makes a published rule set resolvable.
        // A release where they part company is withheld, and what it said instead is recorded
        // beside it — the publisher is the only party who can put it right.
        if (package.Declared.Id != id || package.Declared.Admitted != Release(version))
        {
            Console.Error.WriteLine(
                $"{id} {version} declares '{package.Declared.Id}' at {package.Declared.Admitted}, "
                + $"where the ledger admitted '{id}'. A claim is permanent.");
            entries.Add(RuleSetVersion.Moved(version, package.Declared));
            continue;
        }

        indexed = package;
        entries.Add(new RuleSetVersion(
            version, package.Declared.Requires, package.Declared.Uses, package.Declared.Inputs, null, null));
    }

    RuleSetVersion? newest = entries.LastOrDefault(static entry => entry.Withheld is null);
    int withheld = entries.Count(static entry => entry.Withheld is not null);

    documented.Add(new RuleSetEntry(
        id, admitted, newest?.Version,
        indexed?.Description, indexed?.Repository, indexed?.License, entries));

    Console.Error.WriteLine(
        $"{id}: {entries.Count} version(s), latest {newest?.Version ?? "none indexed"}"
        + (withheld is 0 ? "" : $", {withheld} withheld"));
}

if (violations.Count is not 0)
{
    Console.Error.WriteLine();
    foreach (string violation in violations)
    {
        Console.Error.WriteLine($"  {violation}");
    }

    return 1;
}

WriteCatalogue(catalogued, documented, outputFolder);
Console.Error.WriteLine($"{catalogued.Count} plugins, {documented.Count} rule sets → {outputFolder}");
return 0;

// ── nuget.org ──────────────────────────────────────────────────────────────────────
// One endpoint, and deliberately only one: the flat container needs no pagination, no
// decompression and no search index, and it carries everything a version entry holds.

async Task<List<string>> ReleasedVersions(string id)
{
    string url = $"{FlatContainer}/{id.ToLowerInvariant()}/index.json";

    // A 404 is nuget.org saying there is no package of that name, which is a thing about the
    // ledger and is answered as one where this returns. Every other answer is this run unable
    // to look, and it stops there rather than reporting an absence it has not established.
    using HttpResponseMessage answer = await http.GetAsync(url);
    if (answer.StatusCode is HttpStatusCode.NotFound)
    {
        return [];
    }

    answer.EnsureSuccessStatusCode();

    using JsonDocument index = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

    List<string> released = [];
    foreach (JsonElement element in index.RootElement.GetProperty("versions").EnumerateArray())
    {
        string version = element.GetString()!;
        if (version.Contains('-', StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{id}: skipping {version}, which no `requires` constraint could name.");
            continue;
        }

        released.Add(version);
    }

    released.Sort(static (left, right) => Version.Parse(left).CompareTo(Version.Parse(right)));
    return released;
}

async Task<Package> Fetch(string id, string version)
{
    string lower = $"{id.ToLowerInvariant()}";
    string url = $"{FlatContainer}/{lower}/{version}/{lower}.{version}.nupkg";

    string folder = Path.Combine(scratch, $"{id}.{version}");
    Directory.CreateDirectory(folder);

    using ZipArchive archive = new(await http.GetStreamAsync(url));

    // The assemblies of one target framework, so that the probe finds one copy of each.
    string framework = OneFramework(id, version, archive);
    foreach (ZipArchiveEntry entry in archive.Entries.Where(entry =>
        entry.FullName.StartsWith($"lib/{framework}/", StringComparison.OrdinalIgnoreCase)
        && entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
    {
        entry.ExtractToFile(Path.Combine(folder, Path.GetFileName(entry.FullName)), overwrite: true);
    }

    ZipArchiveEntry nuspec = archive.Entries.Single(entry =>
        !entry.FullName.Contains('/', StringComparison.Ordinal)
        && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

    using Stream stream = nuspec.Open();
    XElement metadata = XDocument.Load(stream).Root!.Elements()
        .Single(element => element.Name.LocalName is "metadata");

    Claimed claimed = Probe(folder, id);

    return new Package(
        claimed.Namespace,
        claimed.Prefix,
        claimed.Operations,
        framework,
        AbstractionVersion(metadata),
        Value(metadata, "description"),
        Repository(metadata),
        Value(metadata, "license"));
}

// A rule set package is a package with no `lib` folder: what it distributes is a document,
// and a document targets no framework and depends on no assembly. `ruleset/` at the root and
// exactly one `.json` in it, because `uses` resolves an identifier to a package and a package
// holding two documents would put a map back in the middle of that.
async Task<RuleSetPackage> FetchRuleSet(string id, string version)
{
    string lower = id.ToLowerInvariant();
    string url = $"{FlatContainer}/{lower}/{version}/{lower}.{version}.nupkg";

    string folder = Path.Combine(scratch, $"{id}.{version}");
    Directory.CreateDirectory(folder);

    using ZipArchive archive = new(await http.GetStreamAsync(url));

    ZipArchiveEntry[] documents =
    [
        .. archive.Entries.Where(static entry =>
            entry.FullName.StartsWith("ruleset/", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.Count(static character => character is '/') is 1)
    ];

    if (documents.Length is not 1)
    {
        throw new InvalidOperationException(
            $"{id} {version} holds {documents.Length} documents in `ruleset`, and a rule set package holds one.");
    }

    documents[0].ExtractToFile(Path.Combine(folder, Path.GetFileName(documents[0].FullName)), overwrite: true);

    ZipArchiveEntry nuspec = archive.Entries.Single(entry =>
        !entry.FullName.Contains('/', StringComparison.Ordinal)
        && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

    using Stream stream = nuspec.Open();
    XElement metadata = XDocument.Load(stream).Root!.Elements()
        .Single(element => element.Name.LocalName is "metadata");

    return new RuleSetPackage(
        ProbeRuleSet(folder),
        Value(metadata, "description"),
        Repository(metadata),
        Value(metadata, "license"));
}

static string OneFramework(string id, string version, ZipArchive archive)
{
    string[] frameworks = [.. archive.Entries
        .Where(static entry => entry.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
        .Select(static entry => entry.FullName.Split('/'))
        .Where(static parts => parts.Length > 2)
        .Select(static parts => parts[1])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal)];

    return frameworks switch
    {
        [string only] => only,
        [] => throw new InvalidOperationException($"{id} {version} has no lib folder."),

        // Nothing published needs this yet, and guessing which one the ledger's claims came
        // from would be a silent choice. Refusing says which package forced the decision.
        _ => throw new InvalidOperationException(
            $"{id} {version} targets {string.Join(", ", frameworks)}. Multi-targeting is not supported yet.")
    };
}

static string? AbstractionVersion(XElement metadata) =>
    metadata.Descendants()
        .Where(static element => element.Name.LocalName is "dependency")
        .FirstOrDefault(static element =>
            string.Equals((string?)element.Attribute("id"), "Rulealize.Abstraction", StringComparison.OrdinalIgnoreCase))
        ?.Attribute("version")?.Value;

static string? Repository(XElement metadata) =>
    (string?)metadata.Elements().FirstOrDefault(static e => e.Name.LocalName is "repository")?.Attribute("url")
    ?? Value(metadata, "projectUrl");

static string? Value(XElement metadata, string name) =>
    metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();

// ── the probe ──────────────────────────────────────────────────────────────────────

JsonElement Reported(string folder)
{
    bool managed = probe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    ProcessStartInfo start = new()
    {
        FileName = managed ? "dotnet" : probe,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    if (managed)
    {
        start.ArgumentList.Add(probe);
    }

    start.ArgumentList.Add(folder);

    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException($"'{probe}' could not be started.");

    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode is not 0)
    {
        throw new InvalidOperationException($"The probe refused '{folder}'.{Environment.NewLine}{error}");
    }

    using JsonDocument reported = JsonDocument.Parse(output);
    return reported.RootElement.Clone();
}

Claimed Probe(string folder, string id)
{
    JsonElement plugin = Reported(folder).GetProperty("plugins").EnumerateArray()
        .Single(element => element.GetProperty("id").GetString() == id);

    JsonElement prefix = plugin.GetProperty("prefix");
    return new Claimed(
        plugin.GetProperty("namespace").GetString()!,
        prefix.ValueKind is JsonValueKind.Null ? null : prefix.GetString(),
        plugin.GetProperty("operations").Clone());
}

// Not selected by identifier, the way a plugin is. Which identifier the document declares is
// the thing being checked, so asking the probe for one by name would be asking it to confirm
// what it was told. The folder holds one document because the package did.
Document ProbeRuleSet(string folder)
{
    JsonElement[] found = [.. Reported(folder).GetProperty("ruleSets").EnumerateArray()];

    if (found.Length is not 1)
    {
        throw new InvalidOperationException($"The probe read {found.Length} rule sets out of '{folder}'.");
    }

    return new Document(
        found[0].GetProperty("id").GetString()!,
        found[0].GetProperty("admitted").GetString()!,
        found[0].GetProperty("requires").Clone(),
        found[0].GetProperty("uses").Clone(),
        found[0].GetProperty("inputs").Clone());
}

// The feed's version string, written the way the probe writes a document's own. nuget.org
// normalises most of what it serves, and a package published as 1.0.0.0 would otherwise read
// as a document disagreeing with its package about a component neither of them uses.
static string Release(string version) =>
    Version.TryParse(version, out Version? parsed)
        ? new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0)).ToString()
        : version;

// ── output ─────────────────────────────────────────────────────────────────────────

static void WriteCatalogue(List<PluginEntry> plugins, List<RuleSetEntry> ruleSets, string folder)
{
    Directory.CreateDirectory(Path.Combine(folder, "plugin"));
    Directory.CreateDirectory(Path.Combine(folder, "ruleset"));

    foreach (PluginEntry plugin in plugins)
    {
        Write(Path.Combine(folder, "plugin", $"{plugin.Id}.json"), writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "rulealize/registry/plugin/v1");
            writer.WriteString("id", plugin.Id);
            writer.WriteString("namespace", plugin.Namespace);
            WritePrefix(writer, plugin.Prefix);
            writer.WriteString("admitted", plugin.Admitted);
            WriteOptional(writer, "latest", plugin.Latest);
            WriteOptional(writer, "description", plugin.Description);
            WriteOptional(writer, "repository", plugin.Repository);
            WriteOptional(writer, "license", plugin.License);

            writer.WritePropertyName("versions");
            writer.WriteStartArray();
            foreach (VersionEntry version in plugin.Versions)
            {
                writer.WriteStartObject();
                writer.WriteString("version", version.Version);
                WriteOptional(writer, "abstraction", version.Abstraction);
                WriteOptional(writer, "framework", version.Framework);

                // `withheld` is the whole of what a reader has to look at: it is on a version
                // that is not in the index, and it is the only thing that decides that. What
                // that version claimed is beside it, against the plugin's own namespace and
                // prefix in this same file, so a page can say both without being told either.
                if (version.Withheld is string withheld)
                {
                    writer.WriteString("withheld", withheld);

                    if (version.Claimed is Claim claimed)
                    {
                        writer.WritePropertyName("claimed");
                        writer.WriteStartObject();
                        writer.WriteString("namespace", claimed.Namespace);
                        WritePrefix(writer, claimed.Prefix);
                        writer.WriteEndObject();
                    }
                }
                else
                {
                    writer.WritePropertyName("operations");
                    version.Operations!.Value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    // One rule set, every released version, and what each of them draws on and holds. The
    // three lists are what a resolver could not work out without fetching the package — which
    // is the round trip this file exists to save, and it saves it once per version rather
    // than once per document in a graph nobody can see the shape of yet.
    foreach (RuleSetEntry ruleSet in ruleSets)
    {
        Write(Path.Combine(folder, "ruleset", $"{ruleSet.Id}.json"), writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "rulealize/registry/ruleset/v1");
            writer.WriteString("id", ruleSet.Id);
            writer.WriteString("admitted", ruleSet.Admitted);
            WriteOptional(writer, "latest", ruleSet.Latest);
            WriteOptional(writer, "description", ruleSet.Description);
            WriteOptional(writer, "repository", ruleSet.Repository);
            WriteOptional(writer, "license", ruleSet.License);

            writer.WritePropertyName("versions");
            writer.WriteStartArray();
            foreach (RuleSetVersion version in ruleSet.Versions)
            {
                writer.WriteStartObject();
                writer.WriteString("version", version.Version);

                if (version.Withheld is string withheld)
                {
                    writer.WriteString("withheld", withheld);

                    // What the document said about itself instead. The identifier this entry
                    // is filed under is elsewhere in this same file, so a page can show both
                    // without being told either.
                    if (version.Claimed is Document claimed)
                    {
                        writer.WritePropertyName("claimed");
                        writer.WriteStartObject();
                        writer.WriteString("id", claimed.Id);
                        writer.WriteString("version", claimed.Admitted);
                        writer.WriteEndObject();
                    }
                }
                else
                {
                    writer.WritePropertyName("requires");
                    version.Requires!.Value.WriteTo(writer);
                    writer.WritePropertyName("uses");
                    version.Uses!.Value.WriteTo(writer);
                    writer.WritePropertyName("inputs");
                    version.Inputs!.Value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    // One file the whole site searches, because the data set is bounded: a few hundred
    // operations is smaller than the round trip that would fetch them one at a time.
    Write(Path.Combine(folder, "index.json"), writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("$schema", "rulealize/registry/index/v1");

        // When this was last read out of nuget.org. The ledger carries no timestamp because
        // it is compared against a regeneration and a date would be a difference every time;
        // this file is compared against nothing and published, and the one question a reader
        // cannot answer without it is whether they are looking at something still being kept.
        writer.WriteString("checked", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));

        writer.WritePropertyName("plugins");
        writer.WriteStartArray();
        foreach (PluginEntry plugin in plugins)
        {
            writer.WriteStartObject();
            writer.WriteString("id", plugin.Id);
            writer.WriteString("namespace", plugin.Namespace);
            WritePrefix(writer, plugin.Prefix);
            WriteOptional(writer, "latest", plugin.Latest);

            // How many of its releases are not in the index, so that a client listing plugins
            // can say so without fetching every entry to count them.
            writer.WriteNumber("withheld", plugin.Versions.Count(static version => version.Withheld is not null));
            WriteOptional(writer, "description", plugin.Description);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // Every rule set in summary, in the same shape and for the same reason. `holds` is
        // the one fact worth carrying here that a plugin has no analogue of: it is what makes
        // a composite a composite, and a client listing rule sets can say so without fetching
        // every entry to find out.
        writer.WritePropertyName("ruleSets");
        writer.WriteStartArray();
        foreach (RuleSetEntry ruleSet in ruleSets)
        {
            RuleSetVersion? newest = ruleSet.Versions.LastOrDefault(static version => version.Withheld is null);

            writer.WriteStartObject();
            writer.WriteString("id", ruleSet.Id);
            WriteOptional(writer, "latest", ruleSet.Latest);
            writer.WriteNumber("holds", newest?.Uses?.GetArrayLength() ?? 0);
            writer.WriteNumber("withheld", ruleSet.Versions.Count(static version => version.Withheld is not null));
            WriteOptional(writer, "description", ruleSet.Description);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // The operations of each plugin's latest version. A search that offered a name
        // withdrawn two releases ago would be worse than one that did not offer it.
        writer.WritePropertyName("operations");
        writer.WriteStartArray();
        foreach (PluginEntry plugin in plugins)
        {
            if (plugin.Versions.LastOrDefault(static version => version.Withheld is null) is not VersionEntry newest)
            {
                continue;
            }

            foreach (JsonProperty kind in newest.Operations!.Value.EnumerateObject())
            {
                foreach (JsonElement op in kind.Value.EnumerateArray())
                {
                    writer.WriteStartObject();
                    writer.WriteString("op", op.GetString());
                    writer.WriteString("kind", kind.Name);
                    writer.WriteString("plugin", plugin.Id);
                    writer.WriteEndObject();
                }
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    });
}

static void WritePrefix(Utf8JsonWriter writer, string? prefix)
{
    if (prefix is null)
    {
        writer.WriteNull("prefix");
    }
    else
    {
        writer.WriteString("prefix", prefix);
    }
}

static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
{
    if (!string.IsNullOrEmpty(value))
    {
        writer.WriteString(name, value);
    }
}

// A description is whatever the publisher put in their nuspec, so everything written here is
// third-party text that a page will eventually put on screen. The encoder keeps escaping the
// characters that matter if this is ever embedded in HTML — < > & ' " — and stops escaping
// the rest, so a description in a language that is not English survives as itself rather than
// as six times its length in \uXXXX. tool/Ledger needs none of this: nothing reaches its
// output but identifiers, namespaces and operation names.
static void Write(string path, Action<Utf8JsonWriter> body)
{
    JsonWriterOptions options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    using MemoryStream buffer = new();
    using (Utf8JsonWriter writer = new(buffer, options))
    {
        body(writer);
    }

    File.WriteAllText(path, $"{Encoding.UTF8.GetString(buffer.ToArray())}\n");
}

static string Show(string? prefix) => prefix is null ? "no prefix" : $"'{prefix}'";

internal sealed record Claimed(string Namespace, string? Prefix, JsonElement Operations);

internal sealed record Package(
    string Namespace,
    string? Prefix,
    JsonElement Operations,
    string Framework,
    string? Abstraction,
    string? Description,
    string? Repository,
    string? License);

// Operations are there when the version is in the index, and Withheld says so when it is not:
// "claims" for one whose namespace or shorthand character is not the ledger's, with Claimed
// carrying what it said instead, and "unreadable" for one nothing could be read out of.
internal sealed record VersionEntry(
    string Version,
    string? Abstraction,
    string? Framework,
    JsonElement? Operations,
    string? Withheld,
    Claim? Claimed)
{
    internal static VersionEntry Unreadable(string version) =>
        new(version, null, null, null, "unreadable", null);

    internal static VersionEntry Moved(string version, Package package) =>
        new(version, package.Abstraction, package.Framework, null, "claims",
            new Claim(package.Namespace, package.Prefix));
}

internal sealed record Claim(string Namespace, string? Prefix);

internal sealed record PluginEntry(
    string Id,
    string Namespace,
    string? Prefix,
    string Admitted,
    string? Latest,
    string? Description,
    string? Repository,
    string? License,
    List<VersionEntry> Versions);

// What a rule set document declares about itself, and the whole of what a release is held to.
// Two strings: an identifier, which is the package it must be published under, and a version,
// which is what every `uses` constraint naming it is answered by.
internal sealed record Document(
    string Id,
    string Admitted,
    JsonElement Requires,
    JsonElement Uses,
    JsonElement Inputs);

internal sealed record RuleSetPackage(
    Document Declared,
    string? Description,
    string? Repository,
    string? License);

// Requires, uses and inputs are there when the version is in the index, and Withheld says so
// when it is not: "claims" for one whose document declares an identifier or a version that is
// not the package's, with Claimed carrying what it said instead, and "unreadable" for one no
// document could be read out of.
internal sealed record RuleSetVersion(
    string Version,
    JsonElement? Requires,
    JsonElement? Uses,
    JsonElement? Inputs,
    string? Withheld,
    Document? Claimed)
{
    internal static RuleSetVersion Unreadable(string version) =>
        new(version, null, null, null, "unreadable", null);

    internal static RuleSetVersion Moved(string version, Document declared) =>
        new(version, null, null, null, "claims", declared);
}

internal sealed record RuleSetEntry(
    string Id,
    string Admitted,
    string? Latest,
    string? Description,
    string? Repository,
    string? License,
    List<RuleSetVersion> Versions);
