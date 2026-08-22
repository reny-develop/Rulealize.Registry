#!/usr/bin/env bash
#
# Decides whether a pull request may merge without a person reading it.
#
# Whether a submission is true is decided elsewhere, by fetching the packages and loading
# them. This decides a different question: whether the change is the *shape* a submission
# has. A pull request that adds a line to the submitted list and touches nothing else has
# nothing left for a person to judge; anything else is held, and the person is told which of
# these it was.
#
# It reads data and never runs any of it. The workflow calling it checks out the base branch
# for this script and for the reserved list, so a pull request cannot edit either to admit
# itself — and rule 2 refuses that pull request anyway.
#
# The namespace and the shorthand character are read from what the submission states rather
# than from the package, because finding out what a package really claims means loading it,
# and this runs where a token that can merge is in the environment. A submission that states
# something other than the truth is refused by declared.sh, in the job where no token is —
# and a required check that fails is a pull request that never merges, which is the whole of
# what this needs from it.
#
#   gate.sh <base submitted.json> <head submitted.json> <reserved.json> <changed files>
#
# Exit 0 admits and prints nothing. Exit 1 holds and prints the reasons as markdown, which
# the workflow posts as the comment. Exit 2 is a usage error.
#
# BASE_REF and DRAFT come from the pull request; both default to the admitting case, so the
# fixtures only set what they are testing.

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "gate.sh needs jq." >&2
    exit 2
fi

if [[ $# -ne 4 ]]; then
    echo "usage: gate.sh <base submitted.json> <head submitted.json> <reserved.json> <changed files>" >&2
    exit 2
fi

base=$1
head=$2
reserved=$3
files=$4
base_ref=${BASE_REF:-main}
draft=${DRAFT:-false}

policy=https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md

held=()
hold() { held+=("$1"); }

# 1. The pull request itself.
if [[ "$base_ref" != "main" ]]; then
    hold "It does not target \`main\`, so what it would merge into is not the ledger."
fi

if [[ "$draft" == "true" ]]; then
    hold "It is a draft."
fi

# 2. What it touches. An allowlist of one path: the ledger itself is written by CI, and the
# workflows, the tools and the reserved list are all things this gate trusts, so a change to
# any of them is a change to the gate.
changed=$(grep -v '^[[:space:]]*$' "$files" | sort -u)
if [[ "$changed" != "ledger/submitted.json" ]]; then
    hold "It changes more than the submitted list. Only \`ledger/submitted.json\` merges without review:
$(sed 's/^/  - `/;s/$/`/' <<<"$changed")"
fi

if [[ ! -s "$head" ]] || ! jq -e . "$head" >/dev/null 2>&1; then
    hold "\`ledger/submitted.json\` is missing or is not valid JSON."
    printf '%s\n\n' "${held[@]}"
    exit 1
fi

# 3. Additions only. Every entry that was there has to still be there, unchanged, and this
# compares the entries themselves rather than the lines they are written on — a submission
# that sorts last moves the comma on the line above it, and that is punctuation rather than
# somebody's claim going missing.
#
# It matters because the ledger is derived from this file: an entry taken out of it is a
# claim taken away from whoever made it, and re-derivation cannot notice. The ledger it
# rebuilds from what is left agrees with itself perfectly.
removed=$(jq --slurpfile head "$head" '.plugins - $head[0].plugins' "$base" 2>/dev/null)
if [[ -z "$removed" ]]; then
    hold "\`ledger/submitted.json\` could not be read as a submitted list."
    printf '%s\n\n' "${held[@]}"
    exit 1
fi

if [[ "$(jq 'length' <<<"$removed")" -ne 0 ]]; then
    hold "It removes or rewrites entries that were already submitted. [A claim is permanent](${policy}#a-claim-is-permanent):
\`\`\`json
$(jq -r '.[] | tostring' <<<"$removed")
\`\`\`"
fi

if ! jq -e '.plugins | map(.id) == (map(.id) | sort)' "$head" >/dev/null 2>&1; then
    hold "The entries are not in identifier order."
fi

added=$(jq -c --slurpfile base "$base" '.plugins - $base[0].plugins' "$head" 2>/dev/null)
if [[ -z "$added" ]]; then
    hold "The \`plugins\` array could not be read."
    printf '%s\n\n' "${held[@]}"
    exit 1
fi

if [[ "$(jq 'length' <<<"$added")" -eq 0 ]]; then
    hold "It adds no plugin."
fi

# 4. Each added line, against the parts of the policy that are decidable without a person.
while IFS= read -r entry; do
    [[ -z "$entry" ]] && continue
    id=$(jq -r '.id // ""' <<<"$entry")
    version=$(jq -r '.version // ""' <<<"$entry")
    namespace=$(jq -r '.namespace // ""' <<<"$entry")
    prefix=$(jq -r '.prefix // "null"' <<<"$entry")

    # The identifier and the version are handed to `dotnet add package`, so they are held to
    # what nuget.org allows before they are handed anywhere.
    if [[ ! "$id" =~ ^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$ ]]; then
        hold "\`$id\` is not a package identifier."
        continue
    fi

    if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        hold "\`$id\` writes \`version\` as \`$version\`, which is not a three-part version."
    fi

    if [[ ! "$namespace" =~ ^[a-z][a-z0-9]*$ ]]; then
        hold "\`$id\` writes \`namespace\` as \`$namespace\`. A namespace is lowercase letters and digits, starting with a letter."
    fi

    if [[ "$prefix" != "null" ]]; then
        hold "\`$id\` claims the shorthand character \`$prefix\`. [There are fewer than a dozen left](${policy}#shorthand-characters), and the default answer is no, so this one is decided by a person."
    fi

    if jq -e --arg ns "$namespace" '.namespaces | index($ns)' "$reserved" >/dev/null; then
        hold "\`$id\` claims the [reserved namespace](https://github.com/reny-develop/Rulealize.Registry/blob/main/ledger/reserved.json) \`$namespace\`."
    fi

    # Vendor qualification, in the one form a machine can check: the namespace is the vendor
    # segment of the identifier. A namespace that is not that may still be perfectly
    # legitimate — this is the rule that sends it to a person rather than the rule that
    # refuses it, because a general name taken first is the one mistake here nobody can undo.
    vendor=${id%%.*}
    if [[ "$namespace" != "${vendor,,}" ]]; then
        hold "\`$id\` claims the namespace \`$namespace\`, which is not its vendor segment (\`${vendor,,}\`). [Vendor-qualify](${policy}#namespaces), or wait for a person to read this one."
    fi
done < <(jq -c '.[]' <<<"$added" 2>/dev/null)

if [[ ${#held[@]} -gt 0 ]]; then
    printf '%s\n\n' "${held[@]}"
    exit 1
fi

exit 0
