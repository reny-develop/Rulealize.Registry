#!/usr/bin/env bash
#
# Decides whether a submission may be written into the ledger without a person reading it.
#
# Whether a submission is true is decided elsewhere, by fetching the package and loading it.
# This decides a different question: whether the line it adds is one the ledger can hold —
# a well-formed identifier and version, a name nobody else holds and the reserved list does
# not refuse, in the place the order puts it, and nothing already there taken away. Whatever
# it refuses is the submitter's to put right: they edit the submission and it is read again.
# Nobody else has to be told, and nobody else can help.
#
# It reads data and never runs any of it. The workflow calling it builds the head ledger
# itself, from main and the one line the submission asks for, so a submission cannot reach
# this script, the reserved list or any other line of the ledger.
#
# The namespace and the shorthand character are read from what the submission states rather
# than from the package, because finding out what a package really claims means loading it,
# and this runs where a token that can write the ledger is in the environment. A submission
# that states something other than the truth is refused by declared.sh, in the job where no
# token is.
#
#   gate.sh <base submitted.json> <head submitted.json> <reserved.json>
#
# Exit 0 admits and prints nothing. Exit 1 prints the reasons as markdown, which the workflow
# posts as its answer. Exit 2 is a usage error.

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "gate.sh needs jq." >&2
    exit 2
fi

if [[ $# -ne 3 ]]; then
    echo "usage: gate.sh <base submitted.json> <head submitted.json> <reserved.json>" >&2
    exit 2
fi

base=$1
head=$2
reserved=$3

policy=https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/policy.md
reserved_list=https://github.com/reny-develop/Rulealize.Registry/blob/main/ledger/reserved.json

reasons=()

fix() { reasons+=("$1"); }

verdict() {
    printf '%s\n\n' "${reasons[@]}"
    exit 1
}

if [[ ! -s "$head" ]] || ! jq -e . "$head" >/dev/null 2>&1; then
    fix "\`ledger/submitted.json\` is not valid JSON."
    verdict
fi

# The ledger is two lists — `plugins` and `ruleSets` — and an entry is shaped differently in
# each. What a submission may do to either is not different, so those rules are read once
# below and the shape of an entry is read per kind after them.
if ! jq -e '(.plugins | type == "array") and ((.ruleSets // []) | type == "array")' "$head" >/dev/null 2>&1; then
    fix "\`ledger/submitted.json\` could not be read as a submitted list."
    verdict
fi

# Additions only. Every entry that was there has to still be there, unchanged, and this
# compares the entries themselves rather than the lines they are written on — a submission
# that sorts last moves the comma on the line above it, and that is punctuation rather than
# somebody's claim going missing.
#
# It matters because the ledger is what the packages are fetched from: an entry taken out of
# it is a claim taken away from whoever made it, and nothing downstream can notice. The
# ledger that is left agrees with itself perfectly.
added=$(jq -nc '[]')

for kind in plugins ruleSets; do
    removed=$(jq --slurpfile head "$head" --arg kind "$kind" \
        '(.[$kind] // []) - ($head[0][$kind] // [])' "$base" 2>/dev/null)

    if [[ "$(jq 'length' <<<"${removed:-[]}")" -ne 0 ]]; then
        fix "It removes or rewrites entries that were already submitted, and [a claim is permanent](${policy}#a-claim-is-permanent). Put them back:
\`\`\`json
$(jq -r '.[] | tostring' <<<"$removed")
\`\`\`"
    fi

    if ! jq -e --arg kind "$kind" '(.[$kind] // []) | map(.id) == (map(.id) | sort)' "$head" >/dev/null 2>&1; then
        fix "The \`$kind\` entries are not in identifier order."
    fi

    # One line per package. A second line for one already in the ledger states nothing new —
    # it agrees with the same artifact the first one does, so nothing downstream would refuse
    # it, and the catalogue would carry the entry twice. Compared without regard to case,
    # because nuget.org serves one package under every casing of its identifier.
    duplicated=$(jq -r --arg kind "$kind" \
        '(.[$kind] // []) | map(.id) | group_by(ascii_downcase) | map(select(length > 1) | .[0]) | .[]' "$head" 2>/dev/null)
    while IFS= read -r twice; do
        [[ -z "$twice" ]] && continue
        fix "\`$twice\` is already in the ledger, which holds one line per package. A later release needs no new submission — it is read every day."
    done <<<"$duplicated"

    new=$(jq -c --slurpfile base "$base" --arg kind "$kind" \
        '(.[$kind] // []) - ($base[0][$kind] // []) | map(.kind = $kind)' "$head" 2>/dev/null)
    added=$(jq -c --argjson new "${new:-[]}" '. + $new' <<<"$added")
done

# One package is one thing. A package submitted to both lists claims to hold an assembly and a
# document under one identifier, and the two entries would be checked against each other's
# artifact — so the catalogue would withhold at least one of them, daily, for ever.
straddling=$(jq -r '[.plugins[].id] as $p | [(.ruleSets // [])[].id] as $r | $p - ($p - $r) | .[]' "$head" 2>/dev/null)
if [[ -n "$straddling" ]]; then
    fix "These are submitted as both a plugin and a rule set, and a package is one or the other:
$(sed 's/^/  - `/;s/$/`/' <<<"$straddling")"
fi

if [[ "$(jq 'length' <<<"$added")" -eq 0 ]]; then
    fix "It adds nothing."
fi

# Each added line, against the parts of the policy that are decidable without a person.
while IFS= read -r entry; do
    [[ -z "$entry" ]] && continue
    kind=$(jq -r '.kind' <<<"$entry")
    id=$(jq -r '.id // ""' <<<"$entry")
    version=$(jq -r '.version // ""' <<<"$entry")

    # The identifier and the version are what the package is fetched by, so they are held to
    # what nuget.org allows before they are handed anywhere.
    if [[ ! "$id" =~ ^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$ ]]; then
        fix "\`$id\` is not a package identifier."
        continue
    fi

    if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        fix "\`$id\` writes \`version\` as \`$version\`, which is not a three-part version."
    fi

    # A rule set entry states nothing else. Its identifier is the package identifier, which
    # nuget.org allocated, so there is no second name here to hold to a format or to a
    # reserved list — the whole of the rest is read out of the document.
    if [[ "$kind" == "ruleSets" ]]; then
        stated=$(jq -r 'del(.kind) | keys_unsorted - ["id", "version"] | .[]' <<<"$entry")
        if [[ -n "$stated" ]]; then
            fix "\`$id\` states more than a rule set entry holds. It is the package and the version its document was read at, and nothing else is submitted:
$(sed 's/^/  - `/;s/$/`/' <<<"$stated")"
        fi

        continue
    fi

    namespace=$(jq -r '.namespace // ""' <<<"$entry")
    prefix=$(jq -r '.prefix // "null"' <<<"$entry")

    if [[ ! "$namespace" =~ ^[a-z][a-z0-9]*$ ]]; then
        fix "\`$id\` writes \`namespace\` as \`$namespace\`. A namespace is lowercase letters and digits, starting with a letter."
    fi

    # The runtime would refuse the pair once both were loaded, and the probe loads every plugin
    # in the ledger together, so this is caught either way. It is said here as well because
    # two submissions read at the same moment are each checked against a ledger without the
    # other, and this is what is run again against the newest one before either is written.
    holder=$(jq -r --arg id "$id" --arg ns "$namespace" \
        '[.plugins[] | select(.namespace == $ns and .id != $id) | .id] | first // empty' "$head")
    if [[ -n "$holder" ]]; then
        fix "\`$id\` claims the namespace \`$namespace\`, which \`$holder\` already holds. A namespace has exactly one owner."
    fi

    if [[ "$prefix" != "null" && ${#prefix} -ne 1 ]]; then
        fix "\`$id\` writes \`prefix\` as \`$prefix\`. A plugin claims one character or none."
    fi

    for pair in namespace:namespaces prefix:prefixes; do
        name=${pair%%:*}
        field=${pair##*:}
        value=${!name}
        [[ "$name" == prefix && "$value" == "null" ]] && continue

        if jq -e --arg field "$field" --arg value "$value" '.[$field] | index($value)' "$reserved" >/dev/null; then
            fix "\`$id\` claims the [reserved ${name}](${reserved_list}) \`$value\`. Choose another — that list grants nothing to anybody and exists only to refuse."
        fi
    done
done < <(jq -c '.[]' <<<"$added" 2>/dev/null)

if [[ ${#reasons[@]} -gt 0 ]]; then
    verdict
fi

exit 0
