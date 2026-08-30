#!/usr/bin/env bash
#
# Decides whether a pull request may merge without a person reading it, and — when it may not
# — which person that is.
#
# Whether a submission is true is decided elsewhere, by fetching the package and loading it.
# This decides a different question: whether the change is the *shape* a submission has. A
# pull request that adds a line to the submitted list and touches nothing else has nothing
# left for anybody to judge.
#
# The rest split in two, and they are not the same thing:
#
#   - most of it is the submitter's to fix. A version in the wrong format, an entry out of
#     order, a name somebody else may not have. They push a change and this runs again.
#     Nobody else has to be told, and nobody else can help
#   - the other is a pull request that is not a submission. This repository indexes plugins
#     and rule sets and takes nothing else, so that one is closed with a note saying where
#     to raise it
#
# Neither waits on anybody. Telling them apart is the whole reason this prints a verdict
# rather than a yes or a no.
#
# It reads data and never runs any of it. The workflow calling it checks out the base branch
# for this script and for the reserved list, so a pull request cannot edit either to admit
# itself — and the first maintainer rule refuses that pull request anyway.
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
# Exit 0 admits and prints nothing. Exit 1 says this is not a submission and exit 3 says it is
# one the submitter has to put right; both print the reasons as markdown, which the workflow
# posts as the comment. Exit 2 is a usage error.
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
reserved_list=https://github.com/reny-develop/Rulealize.Registry/blob/main/ledger/reserved.json

reasons=()
elsewhere=0

# Something the submitter can put right by pushing again.
fix() { reasons+=("$1"); }

# Not a submission, and this repository takes nothing else.
close() { reasons+=("$1"); elsewhere=1; }

verdict() {
    printf '%s\n\n' "${reasons[@]}"
    [[ $elsewhere -eq 1 ]] && exit 1
    exit 3
}

# 1. The pull request itself.
if [[ "$base_ref" != "main" ]]; then
    fix "It does not target \`main\`, so what it would merge into is not the ledger."
fi

if [[ "$draft" == "true" ]]; then
    fix "It is a draft."
fi

# 2. What it touches. An allowlist of one path: this repository indexes plugins and rule
# sets and takes nothing else through a pull request, and the reserved list, the workflows
# and the tools are all things this gate trusts — a change to any of them is a change to the
# gate.
changed=$(grep -v '^[[:space:]]*$' "$files" | sort -u)
if [[ "$changed" != "ledger/submitted.json" ]]; then
    close "It changes more than the submitted list:
$(sed 's/^/  - `/;s/$/`/' <<<"$changed")"
fi

if [[ ! -s "$head" ]]; then
    close "It leaves no \`ledger/submitted.json\`, and that file is the ledger."
    verdict
fi

if ! jq -e . "$head" >/dev/null 2>&1; then
    fix "\`ledger/submitted.json\` is not valid JSON."
    verdict
fi

# The ledger is two lists — `plugins` and `ruleSets` — and an entry is shaped differently in
# each. What a pull request may do to either is not different, so those rules are read once
# below and the shape of an entry is read per kind after them.
if ! jq -e '(.plugins | type == "array") and ((.ruleSets // []) | type == "array")' "$head" >/dev/null 2>&1; then
    fix "\`ledger/submitted.json\` could not be read as a submitted list."
    verdict
fi

# 3. Additions only. Every entry that was there has to still be there, unchanged, and this
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
    # it, and the catalogue would carry the entry twice.
    duplicated=$(jq -r --arg kind "$kind" \
        '(.[$kind] // []) | map(.id) | group_by(.) | map(select(length > 1) | .[0]) | .[]' "$head" 2>/dev/null)
    if [[ -n "$duplicated" ]]; then
        fix "The ledger holds one line per package, and these are on more than one:
$(sed 's/^/  - `/;s/$/`/' <<<"$duplicated")"
    fi

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

# 4. Each added line, against the parts of the policy that are decidable without a person.
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
