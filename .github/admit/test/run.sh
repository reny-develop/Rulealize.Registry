#!/usr/bin/env bash
#
# The gate's cases. Every rule in gate.sh has one that trips it, and the admitting case has
# one too, because a gate that never admits anything is the failure nobody notices.
#
# Each held case also states which reason it expects. Without that, a case that holds for the
# wrong reason — a broken fixture, a missing tool — reads as a pass, and the rule it was
# written for is never exercised again.
#
# The ledgers are built here rather than committed as forty files, and both sides of every
# comparison come out of the same jq, so the layout the gate diffs is the layout the tool
# that writes the real ledger produces: one entry per block, sorted, no trailing difference.
#
#   .github/admit/test/run.sh

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "These cases need jq, and every one of them would report a hold without it." >&2
    exit 2
fi

here=$(cd "$(dirname "$0")" && pwd)
gate="$here/../gate.sh"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# jq on Windows writes CRLF, and the gate compares files line by line. Both sides are
# normalised here so that a case fails for the reason it was written for and not for the
# platform it ran on. Git hands the workflow LF either way.
ledger() { tr -d '\r'; }

jq --indent 2 . "$here/base.json" | ledger > "$work/base.json"
printf 'ledger/claim.json\n' > "$work/files.txt"

passed=0
failed=0

# entry <id> <namespace> <prefix as json> [version]
entry() {
    jq -nc --arg id "$1" --arg ns "$2" --argjson prefix "$3" --arg version "${4:-0.1.0}" \
        '{
            id: $id,
            admitted: $version,
            namespace: $ns,
            prefix: $prefix,
            operations: { expression: ["\($ns).one"], effect: [], schema: [] }
        }'
}

# run <name> <admit|hold> [reason the hold must give] — the head ledger on standard input,
# FILES, BASE_REF and DRAFT overridable per case.
run() {
    local name=$1 expect=$2 reason=${3:-} head="$work/head.json" files="$work/files.txt"
    ledger > "$head"

    if [[ -n "${FILES:-}" ]]; then
        files="$work/files.$name.txt"
        printf '%s\n' "$FILES" > "$files"
    fi

    local output status actual
    output=$("$gate" "$work/base.json" "$head" "$here/reserved.json" "$files" 2>&1)
    status=$?

    case $status in
        0) actual=admit ;;
        1) actual=hold ;;
        *) actual="error($status)" ;;
    esac

    if [[ "$actual" != "$expect" ]]; then
        printf '  FAIL  %-24s expected %s, got %s\n' "$name" "$expect" "$actual"
        [[ -n "$output" ]] && sed 's/^/          /' <<<"$output"
        failed=$((failed + 1))
        return
    fi

    if [[ -n "$reason" && "$output" != *"$reason"* ]]; then
        printf '  FAIL  %-24s held, but not for "%s"\n' "$name" "$reason"
        sed 's/^/          /' <<<"$output"
        failed=$((failed + 1))
        return
    fi

    printf '  ok    %-24s %s\n' "$name" "${reason:-$expect}"
    passed=$((passed + 1))
}

acme=$(entry Acme.Deploy.Rules acme null)
insert='.plugins = [$e] + .plugins'

echo "gate:"

run admits-a-submission admit <<<"$(jq --indent 2 --argjson e "$acme" "$insert" "$work/base.json")"

run admits-two admit <<<"$(jq --indent 2 --argjson a "$acme" --argjson z "$(entry Zeta.Rules zeta null)" \
    '.plugins = [$a] + .plugins + [$z]' "$work/base.json")"

run holds-a-removal hold "removes or rewrites" \
    <<<"$(jq --indent 2 '.plugins |= map(select(.namespace != "bind"))' "$work/base.json")"

run holds-a-rewrite hold "removes or rewrites" <<<"$(jq --indent 2 --argjson e "$acme" \
    '.plugins = [$e] + (.plugins | map(if .namespace == "math" then .admitted = "2.0.0" else . end))' "$work/base.json")"

run holds-out-of-order hold "identifier order" \
    <<<"$(jq --indent 2 --argjson e "$(entry Zeta.Rules zeta null)" "$insert" "$work/base.json")"

run holds-a-reserved-name hold "reserved namespace" \
    <<<"$(jq --indent 2 --argjson e "$(entry Str.Tools str null)" "$insert" "$work/base.json")"

run holds-a-shorthand hold "shorthand character" \
    <<<"$(jq --indent 2 --argjson e "$(entry Acme.Deploy.Rules acme '"!"')" "$insert" "$work/base.json")"

run holds-a-general-name hold "vendor segment" \
    <<<"$(jq --indent 2 --argjson e "$(entry Acme.Deploy.Rules deploy null)" "$insert" "$work/base.json")"

run holds-a-bad-identifier hold "not a package identifier" \
    <<<"$(jq --indent 2 --argjson e "$(entry 'Acme Rules; rm -rf /' acme null)" "$insert" "$work/base.json")"

run holds-a-bad-version hold "three-part version" \
    <<<"$(jq --indent 2 --argjson e "$(entry Acme.Deploy.Rules acme null 1.0)" "$insert" "$work/base.json")"

run holds-nothing-added hold "adds no plugin" < "$work/base.json"

run holds-broken-json hold "not valid JSON" <<<'{ "plugins": ['

FILES=$'ledger/claim.json\ntool/Ledger/Program.cs' \
    run holds-another-file hold "changes more than the ledger" \
    <<<"$(jq --indent 2 --argjson e "$acme" "$insert" "$work/base.json")"

FILES='.github/workflows/admit.yml' \
    run holds-the-gate-itself hold "changes more than the ledger" \
    <<<"$(jq --indent 2 --argjson e "$acme" "$insert" "$work/base.json")"

BASE_REF=release \
    run holds-another-base hold "does not target" \
    <<<"$(jq --indent 2 --argjson e "$acme" "$insert" "$work/base.json")"

DRAFT=true \
    run holds-a-draft hold "is a draft" \
    <<<"$(jq --indent 2 --argjson e "$acme" "$insert" "$work/base.json")"

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed of $((passed + failed)) failed."
    exit 1
fi

echo "$passed cases, all as expected."
