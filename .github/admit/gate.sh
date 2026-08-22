#!/usr/bin/env bash
#
# Decides whether a pull request may merge without a person reading it.
#
# Everything a submission can get wrong about its own claims is already decided by
# ledger.yml, which fetches the packages and re-derives the file. This script decides a
# different question: whether the change is the *shape* a submission has. A pull request that
# adds a row and touches nothing else has nothing left for a person to judge; anything else
# is held, and the person is told which of these it was.
#
# It reads data and never runs any of it. The workflow calling it checks out the base branch
# for this script and for the reserved list, so a pull request cannot edit either to admit
# itself — and rule 2 refuses that pull request anyway.
#
#   gate.sh <base claim.json> <head claim.json> <reserved.json> <changed files>
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
    echo "usage: gate.sh <base claim.json> <head claim.json> <reserved.json> <changed files>" >&2
    exit 2
fi

base=$1
head=$2
reserved=$3
files=$4
base_ref=${BASE_REF:-main}
draft=${DRAFT:-false}

held=()
hold() { held+=("$1"); }

# 1. The pull request itself.
if [[ "$base_ref" != "main" ]]; then
    hold "It does not target \`main\`, so what it would merge into is not the ledger."
fi

if [[ "$draft" == "true" ]]; then
    hold "It is a draft."
fi

# 2. What it touches. An allowlist of one path: the workflows, the tools and the reserved
# list are all things this gate trusts, so a change to any of them is a change to the gate.
changed=$(grep -v '^[[:space:]]*$' "$files" | sort -u)
if [[ "$changed" != "ledger/claim.json" ]]; then
    hold "It changes more than the ledger. Only \`ledger/claim.json\` merges without review:
$(sed 's/^/  - `/;s/$/`/' <<<"$changed")"
fi

if [[ ! -s "$head" ]] || ! jq -e . "$head" >/dev/null 2>&1; then
    hold "\`ledger/claim.json\` is missing or is not valid JSON."
    printf '%s\n\n' "${held[@]}"
    exit 1
fi

# 3. Additions only. The file is generated, sorted and formatted the one way, so an entry
# added in the right place is the only edit that produces no removed line. That makes this
# single comparison the whole of "no existing claim was withdrawn, moved or rewritten" —
# which matters because re-derivation cannot see a deletion: a row that is gone names no
# package to fetch, and the ledger it regenerates matches.
removed=$(diff "$base" "$head" | grep '^<')
if [[ -n "$removed" ]]; then
    hold "It removes or rewrites lines that were already in the ledger. [A claim is permanent](https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md#a-claim-is-permanent):
\`\`\`
$removed
\`\`\`"
fi


# An entry inserted in the wrong place is still a pure insertion, so the comparison above
# passes it. Re-derivation would fail on it — the tool sorts — but the gate should not say a
# pull request merges without review when it does not, and "the ledger is sorted" is one line
# to check.
if ! jq -e '.plugins | map(.id) == (map(.id) | sort)' "$head" >/dev/null 2>&1; then
    hold "The entries are not in identifier order. Paste yours where the tool printed it."
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

# 4. Each added row, against the parts of the policy that are decidable without a person.
while IFS= read -r entry; do
    [[ -z "$entry" ]] && continue
    id=$(jq -r '.id // ""' <<<"$entry")
    admitted=$(jq -r '.admitted // ""' <<<"$entry")
    namespace=$(jq -r '.namespace // ""' <<<"$entry")
    prefix=$(jq -r '.prefix // "null"' <<<"$entry")

    # The identifier and the version are handed to `dotnet add package` by ledger.yml, so
    # they are held to what nuget.org allows before they are handed anywhere.
    if [[ ! "$id" =~ ^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$ ]]; then
        hold "\`$id\` is not a package identifier."
        continue
    fi

    if [[ ! "$admitted" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        hold "\`$id\` writes \`admitted\` as \`$admitted\`, which is not a three-part version."
    fi

    if [[ "$prefix" != "null" ]]; then
        hold "\`$id\` claims the shorthand character \`$prefix\`. [There are fewer than a dozen left](https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md#shorthand-characters), and the default answer is no, so this one is decided by a person."
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
        hold "\`$id\` claims the namespace \`$namespace\`, which is not its vendor segment (\`${vendor,,}\`). [Vendor-qualify](https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md#namespaces), or wait for a person to read this one."
    fi
done < <(jq -c '.[]' <<<"$added" 2>/dev/null)

if [[ ${#held[@]} -gt 0 ]]; then
    printf '%s\n\n' "${held[@]}"
    exit 1
fi

exit 0
