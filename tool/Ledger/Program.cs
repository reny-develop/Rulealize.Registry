// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Rulealize;
using Rulealize.Abstraction.Plugin;

// Reads a folder of plugin assemblies and writes down what they claim.
//
//   dotnet run --project tool/Ledger -- <plugin folder> [<output file>]
//
// The validator is an ordinary host, and this is the whole of what that means.
// It builds a RuleRuntime, points LoadPluginsFrom at a folder, and reports what came back.
// It knows no plugin by name, has no list of the standard twelve, and reads nothing beside
// the DLLs — so there is no way for the ledger to describe a plugin differently from the way
// an application loading that same folder would see it.
//
// Nothing it writes is committed anywhere. What it produces is compared — against the ledger
// somebody wrote, by declared.sh, and against the previous version of the same plugin, by
// Catalogue — so two things matter about the output:
//
//   - everything is sorted, so the order a folder scan happened to produce cannot show up as
//     a difference
//   - there is no timestamp, and no version of this tool, in the file. The same assemblies
//     produce the same bytes, on any run and on any machine
//
// The absence of a prefix is written as null rather than omitted, and all three kinds of
// operation are written even when a plugin registers none of one. A ledger records claims,
// and "claimed no shorthand character" is a claim — one worth being able to see was made.
//
// Operations are grouped by kind rather than listed as objects carrying one, which keeps a
// new operation to a single line of diff. It also gives the one name registered as two kinds
// somewhere to appear twice, which a map from name to kind could not.
//
// "admitted" is the version these claims were read from — the manifest's, which is why a
// manifest and a package that have drifted apart are caught by comparing this against the
// version the ledger says the package was fetched at. Catalogue runs this same tool once per
// release, and there the field means nothing more than what was loaded.

if (args.Length is 0 or > 2)
{
    Console.Error.WriteLine("usage: Ledger <plugin folder> [<output file>]");
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

if (runtime.Plugins.IsEmpty)
{
    Console.Error.WriteLine($"No plugin was found in '{folder}'.");
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

string json = Write(runtime);

if (args.Length is 2)
{
    File.WriteAllText(args[1], json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.Error.WriteLine(
        $"{runtime.Plugins.Length} plugins, {runtime.Operations.Length} operations → {args[1]}");
}
else
{
    Console.Out.Write(json);
}

return 0;

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

static string Write(RuleRuntime runtime)
{
    using MemoryStream buffer = new();
    using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
    {
        writer.WriteStartObject();
        writer.WriteString("$schema", "rulealize/registry/ledger/v1");

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
        writer.WriteEndObject();
    }

    return $"{Encoding.UTF8.GetString(buffer.ToArray())}\n";
}

// A manifest carries a System.Version, whose unset components are -1 and whose ToString
// drops them. A ledger comparing versions against a rule set's `requires` wants all three
// written the same way every time.
static string Release(Version version) =>
    new Version(version.Major, version.Minor, Math.Max(version.Build, 0)).ToString();
