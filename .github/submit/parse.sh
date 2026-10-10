#!/usr/bin/env bash
#
# Reads a submission out of the issue it was made in.
#
# The issue is opened from the site's submit page, through the form in
# .github/ISSUE_TEMPLATE/submit.yml, so its body is that form's answers: one `### ` heading per
# field and the answer under it. This turns them into the one line the ledger would hold and
# says which list it goes in. Nothing here decides whether the line is acceptable — gate.sh
# does that, against the ledger it would be added to — beyond what has to be true for there
# to be a line at all.
#
# The body is whatever the person who opened the issue wrote, so it is read as text, handed to
# jq as a string, and never put anywhere a shell would read it.
#
#   parse.sh <issue body file>
#
# Exit 0 prints { "list": "plugins" | "ruleSets", "entry": { ... } }. Exit 1 prints the reasons
# as markdown, which the workflow posts as its answer. Exit 2 is a usage error.

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "parse.sh needs jq." >&2
    exit 2
fi

if [[ $# -ne 1 || ! -f "$1" ]]; then
    echo "usage: parse.sh <issue body file>" >&2
    exit 2
fi

jq -R -s -r '
    # One answer per heading. GitHub writes "_No response_" for a field left empty, and a
    # person may wrap a value in backticks; neither is part of the value.
    def answers:
        gsub("\r"; "")
        | split("\n")
        | reduce .[] as $line ({ key: null, out: {} };
            if ($line | test("^### ")) then .key = ($line | sub("^### +"; "") | gsub("\\s+$"; ""))
            elif .key == null then .
            else .out[.key] += [$line]
            end)
        | .out
        | map_values(map(gsub("^\\s+|\\s+$"; "")) | map(select(length > 0)) | join(" "))
        | map_values(if . == "_No response_" then "" else . end)
        | map_values(if test("^`[^`]*`$") then .[1:-1] else . end);

    def none: . == "" or ascii_downcase == "none" or . == "(none)";

    answers as $a
    | ($a["Kind"] // "") as $kind
    | ($a["Package identifier"] // "") as $id
    | ($a["Version"] // "") as $version
    | ($a["Namespace"] // "") as $namespace
    | ($a["Shorthand character"] // "") as $prefix
    | [
        (if ($kind | IN("Plugin", "Rule set")) | not then
            "Say whether this is a **Plugin** or a **Rule set**." else empty end),
        (if $id == "" then "The **package identifier** is empty." else empty end),
        (if $version == "" then "The **version** is empty." else empty end),
        (if $kind == "Plugin" and $namespace == "" then "A plugin claims a **namespace**, and it is empty." else empty end),
        (if $kind == "Rule set" and (($namespace | none | not) or ($prefix | none | not)) then
            "A rule set claims no namespace and no shorthand character — its identifier is the package identifier, and the rest is read out of the document. Leave both empty." else empty end)
      ] as $wrong
    | if ($wrong | length) > 0 then
        { reasons: $wrong }
      elif $kind == "Plugin" then
        { list: "plugins",
          entry: { id: $id, version: $version, namespace: $namespace,
                   prefix: (if $prefix | none then null else $prefix end) } }
      else
        { list: "ruleSets", entry: { id: $id, version: $version } }
      end
    | if has("reasons") then .reasons | map(. + "\n") | join("\n") | halt_error(1)
      else tojson end
' "$1" 2>&1
