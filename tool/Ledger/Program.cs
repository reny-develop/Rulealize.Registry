// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Rulealize;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Plugin;

// Reads a folder and writes down what the things in it claim.
//
//   dotnet run --project tool/Ledger -- <folder> [<output file>]
//
// Two kinds are indexed, and one sweep reads both, because one folder is what a deployment
// holds: the assemblies an application loads and the documents it runs.
//
// A plugin is an assembly, and the validator is an ordinary host. It builds a RuleRuntime,
// points LoadPluginsFrom at a folder, and reports what came back. It knows no plugin by name,
// has no list of the standard twelve, and reads nothing beside the DLLs — so there is no way
// for the ledger to describe a plugin differently from the way an application loading that
// same folder would see it.
//
// A rule set is a document, and reading one runs nothing at all. What it declares about itself
// is `RuleSetIdentity.ReadFrom`, and what it draws on and holds is `PluginRequirement.ReadFrom`
// and `RuleSetRequirement.ReadFrom` — all three Rulealize's, so that nothing about an entry is
// this tool's reading of the format and no constraint is parsed a second time by anything
// downstream. The input names are the exception, and they are the keys of one reserved section.
// That is the whole difference between the two kinds here, and it is why a rule set costs a
// sweep what a plugin costs a process.
//
// Which document answers to an identifier is read out of the document and never off its file
// name, for the reason Rulealize.Cli reads a held rule set that way: a file may be renamed and
// an identifier may not. A `.json` the identity reader refuses is passed over — a publish
// leaves its deps and runtimeconfig in the same folder, and neither is a mistake to find — and
// one it accepts is held to everything else.
//
// Nothing it writes is committed anywhere. What it produces is compared — against the ledger
// somebody wrote, by declared.sh, and against the previous version of the same thing, by
// Catalogue — so two things matter about the output:
//
//   - everything is sorted, so the order a folder scan happened to produce cannot show up as
//     a difference. What a document itself put in order stays in the order it wrote
//   - there is no timestamp, and no version of this tool, in the file. The same folder
//     produces the same bytes, on any run and on any machine
//
// The absence of a prefix is written as null rather than omitted, and all three kinds of
// operation are written even when a plugin registers none of one. A ledger records claims,
// and "claimed no shorthand character" is a claim — one worth being able to see was made.
//
// Operations are grouped by kind rather than listed as objects carrying one, which keeps a
// new operation to a single line of diff. It also gives the one name registered as two kinds
// somewhere to appear twice, which a map from name to kind could not.
//
// "admitted" is the version those claims were read from — a plugin's manifest, a document's
// own `version` — which is why either drifting from the package it was fetched at is caught by
// comparing this against the version the ledger says. Catalogue runs this same tool once per
// release, and there the field means nothing more than what was read.

if (args.Length is 0 or > 2)
{
    Console.Error.WriteLine("usage: Ledger <folder> [<output file>]");
    return 2;
}

string folder = args[0];

RuleRuntime runtime;
try
{
    runtime = new RuleRuntime().LoadPluginsFrom(folder);
}
catch (PluginLoadException failure)
{
    // A folder whose plugins cannot be used together is the thing this ledger exists to
    // prevent, so it is worth reporting as itself rather than as a stack trace.
    Console.Error.WriteLine($"The plugins in '{folder}' cannot be loaded together.");
    Console.Error.WriteLine(failure.Message);
    return 1;
}

List<RuleSetClaim> ruleSets = [];
Dictionary<string, string> declaring = new(StringComparer.Ordinal);

foreach (string path in Directory.EnumerateFiles(folder, "*.json")
    .OrderBy(static path => path, StringComparer.Ordinal))
{
    if (Readable(path) is not string text || Identity(text) is not RuleSetIdentity identity)
    {
        continue;
    }

    // An identifier has one document, the way it has one owner everywhere else. Taking
    // whichever the file system listed first would settle something no document said, and
    // settle it quietly — which is the collision this index exists to move earlier, met here
    // in the one place it can still be met loudly.
    if (declaring.TryGetValue(identity.Id, out string? taken))
    {
        Console.Error.WriteLine($"Two documents in '{folder}' declare '{identity.Id}':");
        Console.Error.WriteLine($"  {taken}");
        Console.Error.WriteLine($"  {path}");
        return 1;
    }

    try
    {
        ruleSets.Add(Read(identity, text));
    }
    catch (Exception failure) when (failure is RuleSetBuildException or FormatException)
    {
        Console.Error.WriteLine($"'{path}' declares '{identity.Id}' and cannot be read as a rule set.");
        Console.Error.WriteLine(failure.Message);
        return 1;
    }

    declaring[identity.Id] = path;
}

if (runtime.Plugins.IsEmpty && ruleSets.Count is 0)
{
    Console.Error.WriteLine($"No plugin and no rule set was found in '{folder}'.");
    return 1;
}

// The runtime prefixes a registered name with the plugin's namespace and does not police the
// rest of it, so an operation can be called anything its author typed. Downstream of this
// file it becomes a file name and a URL — site/op/<name>.html — and a name that is a path
// would write outside the folder it was meant for. Refusing it here keeps it out of the
// ledger, which keeps it out of the catalogue and out of every path built from either.
string[] malformed =
[
    .. runtime.Operations
        .Select(static operation => operation.Op)
        .Where(static op => !IsOperationName(op))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal),
];

if (malformed.Length is not 0)
{
    Console.Error.WriteLine($"These are not operation names: {string.Join(", ", malformed)}");
    return 1;
}

string json = Write(runtime, ruleSets);

if (args.Length is 2)
{
    File.WriteAllText(args[1], json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.Error.WriteLine(
        $"{runtime.Plugins.Length} plugins, {runtime.Operations.Length} operations, "
        + $"{ruleSets.Count} rule sets -> {args[1]}");
}
else
{
    Console.Out.Write(json);
}

return 0;

// ── rule sets ──────────────────────────────────────────────────────────────────────

// Speculative, so anything unreadable is passed over. Whether this file was meant to be a
// rule set is decided by the `id` below and not here, because a folder holds files that are
// not documents at all and refusing those would be refusing a publish for doing its job.
static string? Readable(string path)
{
    try
    {
        return File.ReadAllText(path);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        return null;
    }
}

// What a file in this folder turns out to be, decided by Rulealize rather than here.
// `RuleSetIdentity.ReadFrom` is the reading a fetcher makes the moment a document arrives —
// what did I just get — and it is the same question this asks of a file it did not fetch.
//
// A refusal is passed over rather than reported, because most of what it refuses is not a
// rule set and was never meant to be: the deps and runtimeconfig a publish leaves in the same
// folder are the ordinary case. A document that *was* meant to be one and cannot be read is
// not lost for long — the ledger names an identifier that no document answered to, and
// declared.sh says so.
static RuleSetIdentity? Identity(string text)
{
    try
    {
        return RuleSetIdentity.ReadFrom(text);
    }
    catch (RuleSetBuildException)
    {
        return null;
    }
}

// Everything else a rule set entry holds. `requires` and `uses` are Rulealize's readers, so no
// constraint is parsed twice; the input names are the keys of a section the core reserves, and
// they are the one thing here this tool reads out of the format itself.
static RuleSetClaim Read(RuleSetIdentity identity, string text)
{
    // `RuleSetIdentity` reports the version as the document wrote it and does not judge it,
    // which is right for a reading that has to work on anything that arrived. A ledger needs
    // more: the runtime matches a held rule set on a System.Version, so a document whose
    // version is not one is a document no `uses` entry could ever accept — `Satisfies` would
    // answer false for every constraint, for ever, and never say why.
    if (!Version.TryParse(identity.Version, out Version? version))
    {
        throw new FormatException(
            $"'{identity.Id}' writes version '{identity.Version}', which is not a version a `uses` entry could name.");
    }

    using JsonDocument document = JsonDocument.Parse(text, Lenient);

    string[] inputs = document.RootElement.TryGetProperty("inputs", out JsonElement written)
        && written.ValueKind is JsonValueKind.Object
            ? [.. written.EnumerateObject().Select(static input => input.Name).Order(StringComparer.Ordinal)]
            : [];

    return new RuleSetClaim(
        identity.Id,
        Release(version),
        [.. PluginRequirement.ReadFrom(text)],
        [.. RuleSetRequirement.ReadFrom(text)],
        inputs);
}

// ── output ─────────────────────────────────────────────────────────────────────────

// A namespace, a dot, and a member that starts lowercase and carries only letters and digits:
// grid.ray, seq.elementAt. Every operation in the standard distribution is one, and the
// runtime requires only the namespace it prefixes.
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

static string Write(RuleRuntime runtime, List<RuleSetClaim> ruleSets)
{
    using MemoryStream buffer = new();
    using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
    {
        writer.WriteStartObject();
        writer.WriteString("$schema", "rulealize/registry/ledger/v2");

        writer.WritePropertyName("plugins");
        writer.WriteStartArray();
        foreach (PluginManifest plugin in runtime.Plugins.OrderBy(static plugin => plugin.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", plugin.Id);
            writer.WriteString("admitted", Release(plugin.Version));
            writer.WriteString("namespace", plugin.Namespace);

            if (plugin.ReservedPrefix is char prefix)
            {
                writer.WriteString("prefix", prefix.ToString());
            }
            else
            {
                writer.WriteNull("prefix");
            }

            writer.WritePropertyName("operations");
            writer.WriteStartObject();
            foreach (OperationKind kind in Enum.GetValues<OperationKind>())
            {
                writer.WritePropertyName(kind.ToString().ToLowerInvariant());
                writer.WriteStartArray();
                foreach (OperationDescriptor operation in runtime.Operations
                    .Where(operation => operation.Plugin.Id == plugin.Id && operation.Kind == kind)
                    .OrderBy(static operation => operation.Op, StringComparer.Ordinal))
                {
                    writer.WriteStringValue(operation.Op);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // What a rule set draws on and what it holds, in the order it wrote them. Neither is
        // sorted: a document's own order is as reproducible as sorting would be, and it is the
        // order somebody reading this entry beside the document expects to find.
        writer.WritePropertyName("ruleSets");
        writer.WriteStartArray();
        foreach (RuleSetClaim ruleSet in ruleSets.OrderBy(static ruleSet => ruleSet.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", ruleSet.Id);
            writer.WriteString("admitted", ruleSet.Admitted);

            writer.WritePropertyName("requires");
            writer.WriteStartArray();
            foreach (PluginRequirement required in ruleSet.Requires)
            {
                writer.WriteStartObject();
                writer.WriteString("plugin", required.Plugin);
                WriteConstraint(writer, required.Constraint);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("uses");
            writer.WriteStartArray();
            foreach (RuleSetRequirement held in ruleSet.Uses)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleSet", held.RuleSet);
                WriteConstraint(writer, held.Constraint);

                // Written even where `as` was not, because the alias is what qualifies the
                // component's inputs and a reader of this entry has no document to infer it
                // from. RuleSetRequirement gives the identifier where nothing was written.
                writer.WriteString("as", held.Alias);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("inputs");
            writer.WriteStartArray();
            foreach (string input in ruleSet.Inputs)
            {
                writer.WriteStringValue(input);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    return $"{Encoding.UTF8.GetString(buffer.ToArray())}\n";
}

// An entry naming no version is one any version satisfies, and it is written as null rather
// than left out for the reason a plugin's absent prefix is: it is a thing the document said.
static void WriteConstraint(Utf8JsonWriter writer, string? constraint)
{
    if (constraint is null)
    {
        writer.WriteNull("version");
    }
    else
    {
        writer.WriteString("version", constraint);
    }
}

// A manifest carries a System.Version, whose unset components are -1 and whose ToString
// drops them. A ledger comparing versions against a rule set's `requires` wants all three
// written the same way every time — and a document writing its own version as `1.0` names the
// same release as one writing `1.0.0`, because System.Version is what reads both.
static string Release(Version version) =>
    new Version(version.Major, version.Minor, Math.Max(version.Build, 0)).ToString();

internal static partial class Program
{
    /// <summary>How every rule set document in this ecosystem is read, here and in the runtime.</summary>
    internal static JsonDocumentOptions Lenient => new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

/// <summary>One rule set, as its own document declares it.</summary>
/// <remarks>
/// Nothing here was stated in a submission. An entry names a package and a version, and every
/// one of these is read out of the document that was fetched — which is what makes a rule set
/// entry two fields where a plugin's is four.
/// </remarks>
internal sealed record RuleSetClaim(
    string Id,
    string Admitted,
    PluginRequirement[] Requires,
    RuleSetRequirement[] Uses,
    string[] Inputs);
