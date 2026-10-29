// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.RegularExpressions;

/// <summary>The index as one plain-text page, for whoever is writing a rule set rather than browsing.</summary>
/// <remarks>
/// <para>
/// Published as <c>/llms.txt</c>. A rule set is written out of names — <c>type.decimal</c>, not
/// <c>type.number</c> — and a name that is not registered is refused when the document is built.
/// The pages answer that one operation at a time, a link away from the specification that says
/// which keys it takes. Something reading the web rather than clicking through it does not make
/// that trip, so this is every operation of every plugin's newest indexed release, its kind and
/// its form, on one page, beside every published rule set and the line that holds it.
/// </para>
/// <para>
/// Nothing in it is written by hand except the paragraphs that say how to read it. Like the
/// pages it is rebuilt from the catalogue on every run, so it grows with every registration
/// and says when it was last read. A form is the specification's own text, and an operation
/// with none is listed with the specification's address instead of a form somebody guessed.
/// </para>
/// <para>
/// The descriptions are the publishers' and are set on one line after a label saying so, so
/// that none of them can begin a heading of its own; a form is set in a fence longer than any
/// run of backticks inside it, for the same reason.
/// </para>
/// </remarks>
internal static partial class Llms
{
    const string QuickGuide = "https://raw.githubusercontent.com/reny-develop/Rulealize/main/doc/ruleset-quickguide.md";
    const string Notation = "https://raw.githubusercontent.com/reny-develop/Rulealize.Abstraction/main/doc/specification-notation.md";

    /// <summary>The page, from the plugins and rule sets the site was built from.</summary>
    /// <param name="checkedAt">When the catalogue was read, as the pages show it, if it says.</param>
    internal static string Render(List<Plugin> plugins, List<RuleSet> ruleSets, string? checkedAt)
    {
        Plugin[] offering = [.. plugins.Where(static plugin => plugin.Indexed is not null)
            .OrderBy(static plugin => plugin.Id, StringComparer.Ordinal)];
        RuleSet[] holdable = [.. ruleSets.Where(static ruleSet => ruleSet.Indexed is not null)
            .OrderBy(static ruleSet => ruleSet.Id, StringComparer.Ordinal)];
        int operations = offering.Sum(static plugin => plugin.Indexed!.Operations.Sum(static kind => kind.Value.Count));

        StringBuilder page = new();
        page.Append("# Rulealize Registry\n\n");
        page.Append(
            $"> Every operation the {offering.Length} indexed plugins register — {operations} of them — with its kind "
            + $"and the keys it takes, and the {holdable.Length} published rule sets a document can hold. Read out "
            + "of the packages on nuget.org and rebuilt every day"
            + (checkedAt is null ? "." : $"; last checked {checkedAt}.")
            + "\n\n");

        page.Append($"""
            A Rulealize rule set is a JSON document, and nothing in it is built in: every node is an
            operation that a plugin registers under its own namespace, and the document names those
            plugins in `requires`. This is the Registry's index of them — which plugin provides which
            operation, and which rule sets are published — written for whoever is about to write one.
            How a rule set is laid out is Rulealize's quick guide:
            {QuickGuide}

            ## Before writing one

            - Write only operations listed here, with only the keys their form shows. An `op` that is
              not registered, or one whose plugin `requires` does not name, is refused when the rule
              set is built. A name is exact: nothing is looked up from one that is nearly right.
            - Every plugin the document reaches for goes in `requires`, as the line given under it —
              the plugin of each operation used, and the plugin that reserves each shorthand
              character used (`$`, `@`, `#`, …).
            - A form is the JSON that names a node, with every value replaced by what may stand there:
              `<expression>` is any expression node or a JSON literal; `<expression:Number>` has to be
              of that kind (`Boolean`, `Number`, `Text`, `Sequence`, `Record`); a lowercase one such as
              `<expression:coord>` is an opaque value of that plugin's own type; `<state field>` is a
              state field written `"$name"`; `<schema node>`, or `<schema>`, is a schema node;
              `<integer>` and `<boolean>` are literals and never expressions; `"<name>"` and the like
              are literal strings; `…` repeats the element before it. A trailing `// static` means the
              key takes a literal, `// optional` that it may be left out, `// required with "<key>"`
              that it goes with that key, and a key with none of these is required and takes an
              expression. The whole notation:
              {Notation}
            - Each form is read out of the plugin's own specification, on its repository's main
              branch, whenever this page is rebuilt. Where none was found the operation is still
              listed, with the specification to read instead.

            ## Plugins


            """.Replace("\r\n", "\n", StringComparison.Ordinal));

        foreach (Plugin plugin in offering)
        {
            Release latest = plugin.Indexed!;
            string? specification = plugin.Specification
                ?? (Uri.TryCreate(plugin.Repository, UriKind.Absolute, out Uri? source) && source.Scheme is "http" or "https"
                    ? $"{plugin.Repository!.TrimEnd('/')}/blob/main/doc/specification.md"
                    : null);

            page.Append($"### {plugin.Id} {latest.Version}\n\n");
            page.Append($"Namespace `{plugin.Namespace}`. ");
            page.Append(plugin.Prefix is null
                ? "No shorthand character.\n"
                : $"Shorthand character `{OneLine(plugin.Prefix)}`: a string that begins with it is this plugin's shorthand.\n");
            page.Append($"requires: {{ \"plugin\": \"{plugin.Id}\", \"version\": \"^{Program.Major(latest.Version)}\" }}\n");
            page.Append($"Specification: {specification ?? "none known"}\n");

            if (plugin.Description is not null)
            {
                page.Append($"Its package describes it: {OneLine(plugin.Description)}\n");
            }

            page.Append('\n');

            if (latest.Operations.Values.All(static ops => ops.Count is 0))
            {
                page.Append("It registers no operations.\n\n");
                continue;
            }

            foreach ((string kind, List<string> ops) in latest.Operations)
            {
                foreach (string op in ops)
                {
                    page.Append($"#### {op} — {kind}\n\n");
                    if (plugin.Forms.TryGetValue(op, out string? form))
                    {
                        string fence = new('`', Math.Max(3, LongestTicks(form) + 1));
                        page.Append($"{fence}jsonc\n{form.Replace("\r\n", "\n", StringComparison.Ordinal)}\n{fence}\n\n");
                    }
                    else
                    {
                        page.Append($"Form: not found here — read it in the specification: {specification ?? "none known"}\n\n");
                    }
                }
            }
        }

        page.Append("## Published rule sets\n\n");
        if (holdable.Length is 0)
        {
            page.Append("None yet.\n");
            return page.ToString();
        }

        page.Append("""
            A published rule set is held by naming it in a document's `uses`, as the line given under
            it. `as` is the name the holding document calls it by: it may not contain a `.`, and it is
            what qualifies the held rule set's inputs there — `quota.consume`, never the identifier
            written out.


            """.Replace("\r\n", "\n", StringComparison.Ordinal));

        foreach (RuleSet ruleSet in holdable)
        {
            RuleSetRelease latest = ruleSet.Indexed!;
            string alias = Program.Alias(ruleSet.Id);

            page.Append($"### {ruleSet.Id} {latest.Version}\n\n");
            page.Append(
                $"uses: {{ \"ruleSet\": \"{ruleSet.Id}\", \"version\": \"^{Program.Major(latest.Version)}\", \"as\": \"{alias}\" }}\n");

            List<string> offered = ruleSet.Offered.Count is not 0 ? ruleSet.Offered : latest.Inputs;
            if (offered.Count is not 0)
            {
                page.Append($"Inputs, as a holder writes them: {string.Join(", ", offered.Select(input => $"`{alias}.{OneLine(input)}`"))}\n");
            }

            if (latest.Uses.Count is not 0)
            {
                page.Append(
                    $"Holds: {string.Join(", ", latest.Uses.Select(static held => $"{OneLine(held.RuleSet)} {OneLine(held.Version ?? "")}".TrimEnd() + $" as `{OneLine(held.Alias)}`"))}\n");
            }

            if (latest.Requires.Count is not 0)
            {
                page.Append(
                    $"Requires: {string.Join(", ", latest.Requires.Select(static needs => $"{OneLine(needs.Plugin)} {OneLine(needs.Version ?? "")}".TrimEnd()))}\n");
            }

            if (ruleSet.Description is not null)
            {
                page.Append($"Its package describes it: {OneLine(ruleSet.Description)}\n");
            }

            page.Append('\n');
        }

        return page.ToString();
    }

    // A publisher's string, on one line: whatever line breaks it carried are spaces, so it
    // stays inside the line it was put on.
    static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    static int LongestTicks(string text) =>
        Ticks().Matches(text).Select(static run => run.Length).DefaultIfEmpty(0).Max();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("`+")]
    private static partial Regex Ticks();
}
