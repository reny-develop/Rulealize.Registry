#!/usr/bin/env bash
#
# Holds a submission to what its package actually claims.
#
# A plugin submission states four things — the package, the version, the namespace and the
# shorthand character — and three of those are claims about an assembly nobody has loaded yet.
# A rule set submission states two, and one of them is a claim about a document nobody has
# read. This is what loads the one, reads the other, and compares. It is the reason a
# submission is a handful of fields rather than a pasted ledger row: the operations, the
# inputs, the requires and the uses are never stated, and the parts that are stated are not
# believed.
#
# The two kinds cost different things to check, and the difference is worth naming. A plugin's
# claims are read by running its code. A rule set's are read by parsing JSON — nothing is
# executed, which makes it the one thing indexed here that this repository does not have to
# say "loading is running" about.
#
# It runs where the plugin's code runs, which is the job with no token and no secrets. The
# gate that can merge never sees a package, and never runs one.
#
#   declared.sh <submitted.json> <derived claim.json> <reserved.json>
#
# Exit 0 agrees. Exit 1 prints what disagreed. Exit 2 is a usage error.

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "declared.sh needs jq." >&2
    exit 2
fi

if [[ $# -ne 3 ]]; then
    echo "usage: declared.sh <submitted.json> <derived claim.json> <reserved.json>" >&2
    exit 2
fi

submitted=$1
derived=$2
reserved=$3

wrong=()
note() { wrong+=("$1"); }

for file in "$submitted" "$derived" "$reserved"; do
    if [[ ! -s "$file" ]] || ! jq -e . "$file" >/dev/null 2>&1; then
        echo "'${file}' is missing or is not valid JSON." >&2
        exit 2
    fi
done

# The two sets of identifiers. A package whose manifest names something else lands here as a
# pair of differences — one identifier submitted and never derived, one derived and never
# submitted — and that is exactly the case that would otherwise put a name nobody owns on
# nuget.org into the ledger. It also means one package holds one plugin under its own name,
# which is what a rule set's `requires` already assumes when the resolver goes looking for it.
missing=$(jq -r --slurpfile derived "$derived" \
    '[.plugins[].id] - [$derived[0].plugins[].id] | .[]' "$submitted")
extra=$(jq -r --slurpfile submitted "$submitted" \
    '[.plugins[].id] - [$submitted[0].plugins[].id] | .[]' "$derived")

while IFS= read -r id; do
    [[ -z "$id" ]] && continue
    note "\`${id}\` was submitted, and no plugin of that name came out of the packages. A plugin is published under the identifier its manifest declares."
done <<<"$missing"

while IFS= read -r id; do
    [[ -z "$id" ]] && continue
    note "\`${id}\` came out of the packages and was never submitted. Its identifier is not one anybody claimed here."
done <<<"$extra"

# What was stated about each plugin, against what it says about itself.
while IFS= read -r entry; do
    [[ -z "$entry" ]] && continue
    id=$(jq -r '.id' <<<"$entry")
    actual=$(jq -c --arg id "$id" '.plugins[] | select(.id == $id)' "$derived")
    [[ -z "$actual" ]] && continue

    for field in namespace prefix; do
        said=$(jq -r --arg f "$field" '.[$f] // "null"' <<<"$entry")
        is=$(jq -r --arg f "$field" '.[$f] // "null"' <<<"$actual")
        if [[ "$said" != "$is" ]]; then
            note "\`${id}\` submits ${field} \`${said}\`, and the package claims \`${is}\`."
        fi
    done

    said=$(jq -r '.version' <<<"$entry")
    is=$(jq -r '.admitted' <<<"$actual")
    if [[ "$said" != "$is" ]]; then
        note "\`${id}\` was fetched at ${said}, and its manifest says ${is}. Raise the manifest version and the package version together."
    fi
done < <(jq -c '.plugins[]' "$submitted")

# Every operation belongs to the namespace of the plugin that registered it. The runtime
# guarantees that by construction — it prefixes the name itself — so this says nothing about a
# ledger the tool has just written. It says something about a ledger that reached the job
# which commits it: that job runs no plugin code, and this is how it satisfies itself that
# what it is about to record is bounded by what was submitted.
while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    note "$line"
done < <(jq -r '.plugins[] as $plugin
    | $plugin.operations[][]
    | select(startswith("\($plugin.namespace).") | not)
    | "`\($plugin.id)` lists the operation `\(.)`, which is not in its namespace."' "$derived")

# Reserved names are refused against what the packages claim rather than only against what was
# written down, so neither a namespace nor a shorthand character can be taken by declaring
# something else and being believed.
while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    note "$line"
done < <(jq -r --slurpfile reserved "$reserved" '
    .plugins[]
    | . as $plugin
    | [ (select($plugin.namespace as $n | $reserved[0].namespaces | index($n))
          | "`\($plugin.id)` claims the reserved namespace `\($plugin.namespace)`."),
        (select($plugin.prefix != null and ($plugin.prefix as $p | $reserved[0].prefixes | index($p)))
          | "`\($plugin.id)` claims the reserved shorthand character `\($plugin.prefix)`.") ]
    | .[]' "$derived")

# ── rule sets ──────────────────────────────────────────────────────────────────────
#
# The same two comparisons and no others, because a rule set submission states no third thing.
# A document declares one identifier and one version; the package was fetched by an identifier
# at a version; and the whole of what is checked is that those are the same two strings.
#
# There is no reserved list for either. The identifier IS the package identifier, which
# nuget.org allocated and no two publishers can hold — so unlike a namespace there is nothing
# here that could be taken by declaring it, and nothing to refuse in advance.
missing=$(jq -r --slurpfile derived "$derived" \
    '[(.ruleSets // [])[].id] - [($derived[0].ruleSets // [])[].id] | .[]' "$submitted")
extra=$(jq -r --slurpfile submitted "$submitted" \
    '[(.ruleSets // [])[].id] - [($submitted[0].ruleSets // [])[].id] | .[]' "$derived")

while IFS= read -r id; do
    [[ -z "$id" ]] && continue
    note "\`${id}\` was submitted, and no document of that name came out of the packages. A rule set is published under the identifier its document declares, and one package holds one document."
done <<<"$missing"

while IFS= read -r id; do
    [[ -z "$id" ]] && continue
    note "\`${id}\` came out of the packages and was never submitted. Its identifier is not one anybody claimed here."
done <<<"$extra"

while IFS= read -r entry; do
    [[ -z "$entry" ]] && continue
    id=$(jq -r '.id' <<<"$entry")
    actual=$(jq -c --arg id "$id" '(.ruleSets // [])[] | select(.id == $id)' "$derived")
    [[ -z "$actual" ]] && continue

    said=$(jq -r '.version' <<<"$entry")
    is=$(jq -r '.admitted' <<<"$actual")
    if [[ "$said" != "$is" ]]; then
        note "\`${id}\` was fetched at ${said}, and its document says ${is}. A \`uses\` constraint is answered by the version in the document, so raise that and the package version together."
    fi
done < <(jq -c '(.ruleSets // [])[]' "$submitted")

if [[ ${#wrong[@]} -gt 0 ]]; then
    printf '%s\n' "${wrong[@]}"
    exit 1
fi

exit 0
