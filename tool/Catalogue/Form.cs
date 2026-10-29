// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.RegularExpressions;

/// <summary>An operation's form, read out of its plugin's specification.</summary>
/// <remarks>
/// <para>
/// A form is the JSON that names a node with every value replaced by what may stand there, and
/// where one is written is a convention binding on every plugin — Rulealize.Abstraction's
/// <c>doc/specification-notation.md</c>: a <c>jsonc</c> block under a <b>Form</b> heading or
/// directly under the section that introduces the operation, or a cell in the summary table.
/// Which of the three carries no meaning, because a form names its own <c>op</c>. So this reads
/// all three and identifies a form by what is in it rather than by what stands above it.
/// </para>
/// <para>
/// The operations asked about are the ones the assembly registers, never the ones the
/// document mentions: a specification on <c>main</c> may already describe a release that is
/// not out, and offering a form for a name nothing loads would be the failure this exists to
/// prevent. An operation no form is found for is left out of the answer rather than guessed at,
/// and whoever reads the catalogue links the specification instead.
/// </para>
/// </remarks>
internal static partial class Form
{
    /// <summary>The form of each of <paramref name="operations"/> that <paramref name="specification"/> states.</summary>
    internal static Dictionary<string, string> Read(string specification, IEnumerable<string> operations)
    {
        Document document = Parse(specification);
        Dictionary<string, string> found = new(StringComparer.Ordinal);

        foreach (string op in operations)
        {
            if ((FromTable(document, op) ?? FromSection(document, op) ?? Anywhere(document, op)) is string form)
            {
                found[op] = form;
            }
        }

        return found;
    }

    /// <summary>Where a repository's specification is served as plain text, if it is somewhere that has one.</summary>
    /// <remarks>
    /// The site already links <c>&lt;repository&gt;/blob/main/doc/specification.md</c> for a person;
    /// this is the same file without the page around it. Only GitHub is known to serve one, and
    /// a repository anywhere else is linked rather than read.
    /// </remarks>
    internal static string? Raw(string? repository)
    {
        if (!Uri.TryCreate(repository, UriKind.Absolute, out Uri? url)
            || url.Scheme is not "https"
            || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2 || parts[0].Length is 0 || parts[1].Length is 0)
        {
            return null;
        }

        string repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return $"https://raw.githubusercontent.com/{parts[0]}/{repo}/main/doc/specification.md";
    }

    // ── the three places a form is written ──────────────────────────────────────────

    // A row of a summary table whose first cell is the operation, and a code span in it that
    // names the operation. What follows the span in the same cell is a note on it — "`count`
    // optional", "the cards are static" — and the notation puts such notes in a trailing
    // comment, so that is where it goes.
    static string? FromTable(Document document, string op)
    {
        foreach (string[] row in document.Rows)
        {
            if (row.Length < 2 || row[0].Trim() != $"`{op}`")
            {
                continue;
            }

            foreach (string cell in row.Skip(1))
            {
                Match span = CodeSpan().Match(cell);
                for (; span.Success; span = span.NextMatch())
                {
                    if (Names(span.Groups[1].Value) == op)
                    {
                        string note = cell[(span.Index + span.Length)..].Trim().TrimStart('—', '-', ' ').Trim();
                        return note.Length is 0 ? span.Groups[1].Value : $"{span.Groups[1].Value}   // {note}";
                    }
                }
            }
        }

        return null;
    }

    // A section whose heading names the operation in a code span, and the first jsonc block in
    // it that stands directly under that heading or under a Form heading inside it — not one
    // under "Example" or "The JSON form", which are values rather than forms.
    //
    // Where the heading names several operations and the block holds one form between them —
    // `math.min` / `math.max`, the four orderings of Comparison — that one form is each of
    // theirs with its own name in it. The heading is what says so, and the substitution is
    // made only for a name the heading lists beside the one the form was written with.
    static string? FromSection(Document document, string op)
    {
        for (int h = 0; h < document.Headings.Count; h++)
        {
            Heading heading = document.Headings[h];
            string[] named = [.. CodeSpan().Matches(heading.Text).Select(static m => m.Groups[1].Value)];
            if (!named.Contains(op))
            {
                continue;
            }

            int end = document.Headings.Skip(h + 1).FirstOrDefault(next => next.Level <= heading.Level)?.Line
                ?? int.MaxValue;

            foreach (Block block in document.Blocks.Where(block => block.Line > heading.Line && block.Line < end))
            {
                Heading above = document.Headings.Last(candidate => candidate.Line < block.Line);
                if (!block.Jsonc || (!ReferenceEquals(above, heading) && above.Text.Trim() != "Form"))
                {
                    continue;
                }

                List<string> units = Units(block.Text);
                if (units.FirstOrDefault(unit => Names(unit) == op) is string own)
                {
                    return own;
                }

                if (units is [string shared] && Names(shared) is string sibling && named.Contains(sibling))
                {
                    return OpKey().Replace(shared, $"\"op\": \"{op}\"", 1);
                }

                // The first block that qualifies is the form, or there is none here.
                break;
            }
        }

        return null;
    }

    // Anything else that names the operation and has a placeholder in it: a jsonc block, or a
    // code span in prose. Without the placeholder this would be reading an example, which is a
    // value somebody chose and not what may stand there.
    static string? Anywhere(Document document, string op)
    {
        foreach (Block block in document.Blocks.Where(static block => block.Jsonc))
        {
            if (Units(block.Text).FirstOrDefault(unit => Names(unit) == op && unit.Contains('<')) is string unit)
            {
                return unit;
            }
        }

        foreach (string line in document.Prose)
        {
            Match span = CodeSpan().Match(line);
            for (; span.Success; span = span.NextMatch())
            {
                if (Names(span.Groups[1].Value) == op && span.Groups[1].Value.Contains('<'))
                {
                    return span.Groups[1].Value;
                }
            }
        }

        return null;
    }

    // ── reading the markdown ────────────────────────────────────────────────────────

    static Document Parse(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<Heading> headings = [];
        List<Block> blocks = [];
        List<string[]> rows = [];
        List<string> prose = [];

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                string fence = new('`', trimmed.TakeWhile(static c => c is '`').Count());
                string info = trimmed[fence.Length..].Trim();
                int start = i;
                StringBuilder text = new();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal); i++)
                {
                    text.Append(lines[i]).Append('\n');
                }

                blocks.Add(new Block(start, info is "jsonc" or "json", text.ToString()));
                continue;
            }

            if (HeadingLine().Match(line) is { Success: true } heading)
            {
                headings.Add(new Heading(i, heading.Groups[1].Length, heading.Groups[2].Value));
                continue;
            }

            if (trimmed.StartsWith('|'))
            {
                rows.Add(Cells(trimmed));
            }

            prose.Add(line);
        }

        return new Document(headings, blocks, rows, prose);
    }

    // A table row's cells. A bar inside a code span is part of the span: a form may well write
    // `"generated" | <schema node>` in a cell.
    static string[] Cells(string row)
    {
        List<string> cells = [];
        StringBuilder cell = new();
        bool code = false;

        foreach (char c in row.Trim().Trim('|'))
        {
            if (c is '`')
            {
                code = !code;
            }

            if (c is '|' && !code)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            cell.Append(c);
        }

        cells.Add(cell.ToString());
        return [.. cells];
    }

    // The objects a jsonc block holds at its top level, each as written — on one line or over
    // several, its comments kept, since `// optional` and `// static` are part of what a form
    // says. A block of forms is usually one per line; a block of one form is usually one object
    // over several lines. Braces inside a string or a comment do not count.
    static List<string> Units(string block)
    {
        List<string> units = [];
        int depth = 0;
        int start = -1;
        bool text = false;

        for (int i = 0; i < block.Length; i++)
        {
            char c = block[i];

            if (text)
            {
                if (c is '\\')
                {
                    i++;
                }
                else if (c is '"')
                {
                    text = false;
                }

                continue;
            }

            if (c is '/' && i + 1 < block.Length && block[i + 1] is '/')
            {
                while (i < block.Length && block[i] is not '\n')
                {
                    i++;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    text = true;
                    break;
                case '{':
                    if (depth++ is 0)
                    {
                        start = i;
                    }

                    break;
                case '}' when depth > 0:
                    if (--depth is 0)
                    {
                        // The rest of the line belongs to it when it is a comment.
                        int stop = block.IndexOf('\n', i);
                        string rest = (stop < 0 ? block[(i + 1)..] : block[(i + 1)..stop]).Trim();
                        units.Add(Dedent(block[start..(i + 1)] + (rest.StartsWith("//", StringComparison.Ordinal) ? $"   {rest}" : "")));
                    }

                    break;
            }
        }

        return units;
    }

    // A form that ran over several lines, with the indentation of the block it came out of
    // taken off every line but the first, which began at its brace.
    static string Dedent(string unit)
    {
        string[] lines = unit.Split('\n');
        if (lines.Length is 1)
        {
            return unit.Trim();
        }

        int indent = lines.Skip(1)
            .Where(static line => line.Trim().Length is not 0)
            .Select(static line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        // The closing brace sits at the indentation of the opening one; the lines between are
        // one step in from it, and keep whatever they had beyond that.
        int closing = lines[^1].Length - lines[^1].TrimStart().Length;
        int cut = Math.Min(indent, closing);

        return string.Join('\n', lines.Select((line, n) =>
            n is 0 ? line.TrimEnd() : (line.Length >= cut ? line[cut..] : line.TrimStart()).TrimEnd()));
    }

    // The operation an object names: its own `op`, not one nested inside it.
    static string? Names(string unit)
    {
        int depth = 0;
        bool text = false;

        for (int i = 0; i < unit.Length; i++)
        {
            char c = unit[i];
            if (text)
            {
                if (c is '\\')
                {
                    i++;
                }
                else if (c is '"')
                {
                    text = false;
                }

                continue;
            }

            if (c is '{' or '[')
            {
                depth++;
            }
            else if (c is '}' or ']')
            {
                depth--;
            }
            else if (c is '"')
            {
                if (depth is 1 && OpKey().Match(unit, i) is { Success: true } key && key.Index == i)
                {
                    return key.Groups[1].Value;
                }

                text = true;
            }
        }

        return null;
    }

    [GeneratedRegex("""^(#{2,6})\s+(.*?)\s*#*\s*$""")]
    private static partial Regex HeadingLine();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex("""
        "op"\s*:\s*"([^"]*)"
        """)]
    private static partial Regex OpKey();

    sealed record Heading(int Line, int Level, string Text);

    sealed record Block(int Line, bool Jsonc, string Text);

    sealed record Document(List<Heading> Headings, List<Block> Blocks, List<string[]> Rows, List<string> Prose);
}
