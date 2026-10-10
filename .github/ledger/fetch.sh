#!/usr/bin/env bash
#
# Fetches every package a ledger names into one folder, the way a deployment holds them.
#
# ledger.yml points it at the ledger on main, and submit.yml at the ledger a submission would
# make. Both then read the folder with tool/Ledger and hold every entry to what came back, so
# a submission is checked by exactly the job that checks the ledger it would become.
#
# Loading a plugin is running its code, so whatever calls this holds a read-only token, no
# secrets, and writes nothing anywhere.
#
#   fetch.sh <submitted.json> <work folder>
#
# The packages land in <work folder>/claimed, and the progress goes to standard error. Exit 0
# fetched all of them. Exit 1 printed which one could not be fetched, on standard output, as a
# line a person can read. Exit 2 is a usage error.

set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "usage: fetch.sh <submitted.json> <work folder>" >&2
    exit 2
fi

ledger=$1
work=$2
feed=https://api.nuget.org/v3-flatcontainer

mkdir -p "$work"
rm -rf "$work/Fetch" "$work/claimed"
mkdir -p "$work/claimed"

# One project referencing exactly the plugin packages the ledger names, published so that the
# assemblies land in one folder. What else lands there does not matter: LoadPluginsFrom sweeps
# a folder and passes over assemblies with no plugin in them, which is why pointing it at an
# application's own output is a reasonable thing to do.
dotnet new classlib -o "$work/Fetch" --framework net10.0 > /dev/null
while read -r id version; do
    echo "::group::${id} ${version}" >&2
    if ! dotnet add "$work/Fetch" package "${id}" --version "${version}" >&2; then
        echo "::endgroup::" >&2
        echo "\`${id}\` ${version} could not be fetched from nuget.org."
        exit 1
    fi
    echo "::endgroup::" >&2
done < <(jq -r '.plugins[] | "\(.id) \(.version)"' "$ledger")

dotnet publish "$work/Fetch" -c Release -o "$work/claimed" >&2

# A rule set package carries no assembly, so `dotnet publish` has nothing to copy out of one.
# It is fetched and unzipped instead, which is the same two operations with the framework's
# part left out — and it lands in the same folder, because the probe reads one folder and a
# deployment holds both kinds in one place too.
#
# The document is renamed to the package it came out of. It is a rule set's identifier that
# has one owner, not its file name, so two packages may both ship `rules.json` and neither is
# wrong — and the identifier inside is exactly what is being checked, so it cannot be what the
# file is named by here.
while read -r id version; do
    echo "::group::${id} ${version}" >&2
    lower=$(tr '[:upper:]' '[:lower:]' <<<"$id")
    if ! curl -sSfL -o "$work/${id}.nupkg" "${feed}/${lower}/${version}/${lower}.${version}.nupkg"; then
        echo "::endgroup::" >&2
        echo "\`${id}\` ${version} could not be fetched from nuget.org."
        exit 1
    fi

    rm -rf "$work/one" && mkdir -p "$work/one"

    # -C matches the folder without regard to case, because the catalogue does and the two
    # have to agree. A package that is indexed daily and cannot be admitted here is worse than
    # either answer would have been on its own.
    #
    # A failure is swallowed, because unzip's is "no matching files" — which is what the count
    # below says, and says better: with a package identifier and a version in it.
    unzip -o -j -C "$work/${id}.nupkg" 'ruleset/*.json' -d "$work/one" > /dev/null || true

    # At least one, and which of them is which is not this script's to decide. A package ships
    # the document named for it and, where that one is built out of parts, those parts too —
    # the way a library's internal types ship in its assembly rather than in packages of their
    # own. What each one declares is read by the probe, and declared.sh holds the set to the
    # rule: one entry point, and every other identifier under it.
    found=$(find "$work/one" -name '*.json' | wc -l)
    if [[ "$found" -eq 0 ]]; then
        echo "::endgroup::" >&2
        echo "\`${id}\` ${version} holds no document in \`ruleset\`."
        exit 1
    fi

    # Numbered rather than named for the identifier, because the identifier is what is being
    # checked and a file named for a claim would be this script believing it.
    n=0
    for document in "$work"/one/*.json; do
        n=$((n + 1))
        mv "$document" "$work/claimed/${id}.${n}.json"
    done
    echo "::endgroup::" >&2
done < <(jq -r '(.ruleSets // [])[] | "\(.id) \(.version)"' "$ledger")
