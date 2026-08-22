// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml.Linq;

// Builds the catalogue the site and the resolver are generated from, and checks every
// published version against what the ledger says the plugin claims.
//
//   dotnet run --project tool/Catalogue -- <ledger file> <probe> <output folder>
//
// The ledger is the one file a person reads: one line per plugin, holding the three things a
// plugin claims and the version they were claimed at, because a claim is permanent and cannot
// differ between versions. The catalogue is not reviewed by anybody and holds one entry per
// version, because `requires` reads `^1.0` and an operation may be added within a major. That
// difference is the whole reason these are two documents. Operations are in neither the
// ledger nor a submission — they are read out of the assemblies, here.
//
// It follows that a new version of a plugin already in the ledger needs no pull request at
// all: nothing committed changes, and the catalogue picks it up the next time this runs.
// What that would otherwise let through is a plugin quietly changing its namespace between
// versions, so this checks every version's claims against the ledger and fails on the first
// that disagrees. The policy that a claim is permanent is enforced here, mechanically, while
// the file a human reads does not move.
//
// <probe> is tool/Ledger, built. This tool loads no plugin itself — see the project file for
// why it cannot — and runs that one per version instead, reading the JSON it writes to
// standard output. Pass either the apphost or the .dll; a .dll is run through `dotnet`.
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
        violations.Add($"{id}: the ledger admits it, and nuget.org has no released version of it.");
        continue;
    }

    List<VersionEntry> entries = [];
    Package? latest = null;

    foreach (string version in versions)
    {
        Package package = await Fetch(id, version);
        latest = package;

        // The claims are the ledger's to state and every version's to honour.
        if (package.Namespace != ns || package.Prefix != prefix)
        {
            violations.Add(
                $"{id} {version} claims namespace '{package.Namespace}' and prefix {Show(package.Prefix)}, "
                + $"where the ledger admitted '{ns}' and {Show(prefix)}. A claim is permanent.");
        }

        entries.Add(new VersionEntry(version, package.Abstraction, package.Framework, package.Operations));
    }

    catalogued.Add(new PluginEntry(
        id, ns, prefix, admitted, versions[^1], latest!.Description, latest.Repository, latest.License, entries));

    Console.Error.WriteLine($"{id}: {versions.Count} version(s), latest {versions[^1]}");
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

WriteCatalogue(catalogued, outputFolder);
Console.Error.WriteLine($"{catalogued.Count} plugins → {outputFolder}");
return 0;

// ── nuget.org ──────────────────────────────────────────────────────────────────────
// One endpoint, and deliberately only one: the flat container needs no pagination, no
// decompression and no search index, and it carries everything a version entry holds.

async Task<List<string>> ReleasedVersions(string id)
{
    string url = $"{FlatContainer}/{id.ToLowerInvariant()}/index.json";
    using JsonDocument index = JsonDocument.Parse(await http.GetStringAsync(url));

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

Claimed Probe(string folder, string id)
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
    JsonElement plugin = reported.RootElement.GetProperty("plugins").EnumerateArray()
        .Single(element => element.GetProperty("id").GetString() == id);

    JsonElement prefix = plugin.GetProperty("prefix");
    return new Claimed(
        plugin.GetProperty("namespace").GetString()!,
        prefix.ValueKind is JsonValueKind.Null ? null : prefix.GetString(),
        plugin.GetProperty("operations").Clone());
}

// ── output ─────────────────────────────────────────────────────────────────────────

static void WriteCatalogue(List<PluginEntry> plugins, string folder)
{
    Directory.CreateDirectory(Path.Combine(folder, "plugin"));

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
            writer.WriteString("latest", plugin.Latest);
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
                writer.WriteString("framework", version.Framework);
                writer.WritePropertyName("operations");
                version.Operations.WriteTo(writer);
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
            writer.WriteString("latest", plugin.Latest);
            WriteOptional(writer, "description", plugin.Description);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // The operations of each plugin's latest version. A search that offered a name
        // withdrawn two releases ago would be worse than one that did not offer it.
        writer.WritePropertyName("operations");
        writer.WriteStartArray();
        foreach (PluginEntry plugin in plugins)
        {
            JsonElement operations = plugin.Versions[^1].Operations;
            foreach (JsonProperty kind in operations.EnumerateObject())
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

internal sealed record VersionEntry(string Version, string? Abstraction, string Framework, JsonElement Operations);

internal sealed record PluginEntry(
    string Id,
    string Namespace,
    string? Prefix,
    string Admitted,
    string Latest,
    string? Description,
    string? Repository,
    string? License,
    List<VersionEntry> Versions);
