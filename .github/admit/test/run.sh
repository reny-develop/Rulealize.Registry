#!/usr/bin/env bash
#
# The cases for both halves of admission.
#
# gate.sh decides whether a pull request is the shape a submission has, without loading
# anything. declared.sh decides whether what it states is what its package claims, after
# loading it. Every rule in each has a case that trips it, and both have an agreeing case
# too, because a gate that never admits anything is the failure nobody notices.
#
# Each held case also states which reason it expects. Without that, a case that holds for the
# wrong reason — a broken fixture, a missing tool — reads as a pass, and the rule it was
# written for is never exercised again.
#
#   .github/admit/test/run.sh

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "These cases need jq, and every one of them would report a hold without it." >&2
    exit 2
fi

here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$here/base.json" "$work/base.json"
printf 'ledger/submitted.json\n' > "$work/files.txt"

passed=0
failed=0

report() {
    local name=$1 expect=$2 reason=$3 actual=$4 output=$5

    if [[ "$actual" != "$expect" ]]; then
        printf '  FAIL  %-26s expected %s, got %s\n' "$name" "$expect" "$actual"
        [[ -n "$output" ]] && sed 's/^/          /' <<<"$output"
        failed=$((failed + 1))
        return
    fi

    if [[ -n "$reason" && "$output" != *"$reason"* ]]; then
        printf '  FAIL  %-26s did not say "%s"\n' "$name" "$reason"
        sed 's/^/          /' <<<"$output"
        failed=$((failed + 1))
        return
    fi

    printf '  ok    %-26s %s\n' "$name" "${reason:-$expect}"
    passed=$((passed + 1))
}

# submission <id> <namespace> <prefix as json> [version]
submission() {
    jq -nc --arg id "$1" --arg ns "$2" --argjson prefix "$3" --arg version "${4:-0.1.0}" \
        '{ id: $id, version: $version, namespace: $ns, prefix: $prefix }'
}

# derived <id> <namespace> <prefix as json> [version] — one plugin as the ledger tool writes it
derived() {
    jq -nc --arg id "$1" --arg ns "$2" --argjson prefix "$3" --arg version "${4:-0.1.0}" \
        '{
            id: $id,
            admitted: $version,
            namespace: $ns,
            prefix: $prefix,
            operations: { expression: ["\($ns).one"], effect: [], schema: [] }
        }'
}

ledger() { jq -n --argjson plugins "[$(printf '%s,' "$@" | sed 's/,$//')]" '{ plugins: $plugins }'; }

# gate <name> <admit|hold> [reason] — the head submitted list on standard input
gate() {
    local name=$1 expect=$2 reason=${3:-} head="$work/head.json" files="$work/files.txt"
    cat > "$head"

    if [[ -n "${FILES:-}" ]]; then
        files="$work/files.${name}.txt"
        printf '%s\n' "$FILES" > "$files"
    fi

    local output status actual
    output=$("$here/../gate.sh" "$work/base.json" "$head" "$here/reserved.json" "$files" 2>&1)
    status=$?
    case $status in
        0) actual=admit ;;
        1) actual=hold ;;
        *) actual="error($status)" ;;
    esac

    report "$name" "$expect" "$reason" "$actual" "$output"
}

# declared <name> <agrees|refuses> <submitted json> <derived json> [reason]
declared() {
    local name=$1 expect=$2 reason=${5:-}
    printf '%s\n' "$3" > "$work/submitted.json"
    printf '%s\n' "$4" > "$work/derived.json"

    local output status actual
    output=$("$here/../declared.sh" "$work/submitted.json" "$work/derived.json" "$here/reserved.json" 2>&1)
    status=$?
    case $status in
        0) actual=agrees ;;
        1) actual=refuses ;;
        *) actual="error($status)" ;;
    esac

    report "$name" "$expect" "$reason" "$actual" "$output"
}

acme=$(submission Acme.Deploy.Rules acme null)
insert='.plugins = [$e] + .plugins'

echo "gate:"

gate admits-a-submission admit <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

gate admits-two admit <<<"$(jq --argjson a "$acme" --argjson z "$(submission Zeta.Rules zeta null)" \
    '.plugins = [$a] + .plugins + [$z]' "$work/base.json")"

# The entry that sorts last moves the comma on the line above it. That is punctuation, and
# holding a submission for it would hold every vendor whose name starts late in the alphabet.
gate admits-a-last-name admit <<<"$(jq --argjson z "$(submission Zeta.Rules zeta null)" \
    '.plugins = .plugins + [$z]' "$work/base.json")"

gate holds-a-removal hold "removes or rewrites" \
    <<<"$(jq '.plugins |= map(select(.namespace != "bind"))' "$work/base.json")"

gate holds-a-rewrite hold "removes or rewrites" <<<"$(jq --argjson e "$acme" \
    '.plugins = [$e] + (.plugins | map(if .namespace == "math" then .version = "2.0.0" else . end))' "$work/base.json")"

gate holds-out-of-order hold "identifier order" \
    <<<"$(jq --argjson e "$(submission Zeta.Rules zeta null)" "$insert" "$work/base.json")"

gate holds-a-reserved-name hold "reserved namespace" \
    <<<"$(jq --argjson e "$(submission Str.Tools str null)" "$insert" "$work/base.json")"

gate holds-a-shorthand hold "shorthand character" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme '"!"')" "$insert" "$work/base.json")"

gate holds-a-general-name hold "vendor segment" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules deploy null)" "$insert" "$work/base.json")"

gate holds-a-bad-identifier hold "not a package identifier" \
    <<<"$(jq --argjson e "$(submission 'Acme Rules; rm -rf /' acme null)" "$insert" "$work/base.json")"

gate holds-a-bad-version hold "three-part version" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme null 1.0)" "$insert" "$work/base.json")"

gate holds-a-bad-namespace hold "lowercase letters and digits" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules Acme null)" "$insert" "$work/base.json")"

gate holds-nothing-added hold "adds no plugin" < "$work/base.json"

gate holds-broken-json hold "not valid JSON" <<<'{ "plugins": ['

FILES=$'ledger/submitted.json\ntool/Ledger/Program.cs' \
    gate holds-another-file hold "changes more than the submitted list" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

FILES='ledger/reserved.json' \
    gate holds-the-reserved-list hold "changes more than the submitted list" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

BASE_REF=release \
    gate holds-another-base hold "does not target" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

DRAFT=true \
    gate holds-a-draft hold "is a draft" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

echo
echo "declared:"

one=$(submission Acme.Deploy.Rules acme null)
truth=$(ledger "$(derived Acme.Deploy.Rules acme null)")
submitted=$(jq -n --argjson p "[$one]" '{ plugins: $p }')

declared agrees-with-its-package agrees "$submitted" "$truth"

declared holds-another-manifest-id refuses "$submitted" \
    "$(ledger "$(derived Microsoft.Rules acme null)")" \
    "was submitted, and no plugin of that name"

declared holds-a-second-plugin refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme null)" "$(derived Acme.Extra.Rules extra null)")" \
    "came out of the packages and was never submitted"

declared holds-another-namespace refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules deploy null)")" \
    "submits namespace \`acme\`, and the package claims \`deploy\`"

declared holds-an-undeclared-shorthand refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme '"!"')")" \
    "submits prefix \`null\`, and the package claims \`!\`"

declared holds-a-drifted-version refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme null 0.2.0)")" \
    "Raise the manifest version and the package version together"

# Not something the tool can write — the runtime prefixes every name it registers — but the
# job that commits the ledger runs no plugin and reads this file on trust, so the shape of a
# thing nobody could have derived is worth refusing anyway.
declared holds-an-operation-elsewhere refuses "$submitted" \
    "$(jq -c '.plugins[0].operations.expression = ["grid.ray"]' <<<"$truth")" \
    "which is not in its namespace"

declared holds-a-reserved-namespace refuses \
    "$(jq -n --argjson p "[$(submission Str.Tools str null)]" '{ plugins: $p }')" \
    "$(ledger "$(derived Str.Tools str null)")" \
    "reserved namespace"

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed of $((passed + failed)) failed."
    exit 1
fi

echo "$passed cases, all as expected."
