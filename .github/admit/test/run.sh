#!/usr/bin/env bash
#
# The cases for both halves of admission.
#
# gate.sh decides whether a pull request is the shape a submission has, without loading
# anything. declared.sh decides whether what it states is what its package claims, after
# loading it. Every rule in each has a case that trips it, and both have an agreeing case
# too, because a gate that never admits anything is the failure nobody notices.
#
# Each refused case also states which reason it expects. Without that, a case that holds for the
# wrong reason — a broken fixture, a missing tool — reads as a pass, and the rule it was
# written for is never exercised again.
#
#   .github/admit/test/run.sh

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "These cases need jq, and every one of them would report a refusal without it." >&2
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

# gate <name> <admit|review|fix> [reason] — the head submitted list on standard input.
# "review" is the maintainer's queue; "fix" is the submitter's own, and never reaches anybody
# but them.
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
        1) actual=review ;;
        3) actual=fix ;;
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

# A namespace that is not the vendor segment of its identifier. First come is the rule, and a
# general name costs the plugin that did not get it some verbosity and nothing else — so this
# is nobody's to refuse and nobody's to sit on.
gate admits-a-general-name admit \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules deploy null)" "$insert" "$work/base.json")"

gate reviews-a-removal review "removes or rewrites" \
    <<<"$(jq '.plugins |= map(select(.namespace != "bind"))' "$work/base.json")"

gate reviews-a-rewrite review "removes or rewrites" <<<"$(jq --argjson e "$acme" \
    '.plugins = [$e] + (.plugins | map(if .namespace == "math" then .version = "2.0.0" else . end))' "$work/base.json")"

gate reviews-a-shorthand review "shorthand character" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme '"!"')" "$insert" "$work/base.json")"

FILES=$'ledger/submitted.json\ntool/Ledger/Program.cs' \
    gate reviews-another-file review "more than the submitted list" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

FILES='ledger/reserved.json' \
    gate reviews-the-reserved-list review "more than the submitted list" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

# Everything below is the submitter's own to put right, and reaches nobody else.
gate fixes-out-of-order fix "identifier order" \
    <<<"$(jq --argjson e "$(submission Zeta.Rules zeta null)" "$insert" "$work/base.json")"

gate fixes-a-reserved-name fix "reserved namespace" \
    <<<"$(jq --argjson e "$(submission Str.Tools str null)" "$insert" "$work/base.json")"

gate fixes-a-bad-identifier fix "not a package identifier" \
    <<<"$(jq --argjson e "$(submission 'Acme Rules; rm -rf /' acme null)" "$insert" "$work/base.json")"

gate fixes-a-bad-version fix "three-part version" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme null 1.0)" "$insert" "$work/base.json")"

gate fixes-a-bad-namespace fix "lowercase letters and digits" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules Acme null)" "$insert" "$work/base.json")"

gate fixes-nothing-added fix "adds no plugin" < "$work/base.json"

gate fixes-broken-json fix "not valid JSON" <<<'{ "plugins": ['

BASE_REF=release \
    gate fixes-another-base fix "does not target" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

DRAFT=true \
    gate fixes-a-draft fix "is a draft" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

# A submission that is both — a shorthand character asked for on a pull request that also has
# a version in the wrong format. The maintainer's queue wins, because the part only they can
# answer does not stop being true when something else is wrong as well.
gate reviews-both-at-once review "shorthand character" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme '"!"' 1.0)" "$insert" "$work/base.json")"

echo
echo "declared:"

one=$(submission Acme.Deploy.Rules acme null)
truth=$(ledger "$(derived Acme.Deploy.Rules acme null)")
submitted=$(jq -n --argjson p "[$one]" '{ plugins: $p }')

declared agrees-with-its-package agrees "$submitted" "$truth"

declared refuses-another-manifest-id refuses "$submitted" \
    "$(ledger "$(derived Microsoft.Rules acme null)")" \
    "was submitted, and no plugin of that name"

declared refuses-a-second-plugin refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme null)" "$(derived Acme.Extra.Rules extra null)")" \
    "came out of the packages and was never submitted"

declared refuses-another-namespace refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules deploy null)")" \
    "submits namespace \`acme\`, and the package claims \`deploy\`"

declared refuses-an-undeclared-shorthand refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme '"!"')")" \
    "submits prefix \`null\`, and the package claims \`!\`"

declared refuses-a-drifted-version refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme null 0.2.0)")" \
    "Raise the manifest version and the package version together"

# Not something the tool can write — the runtime prefixes every name it registers — but the
# job that commits the ledger runs no plugin and reads this file on trust, so the shape of a
# thing nobody could have derived is worth refusing anyway.
declared refuses-an-operation-elsewhere refuses "$submitted" \
    "$(jq -c '.plugins[0].operations.expression = ["grid.ray"]' <<<"$truth")" \
    "which is not in its namespace"

declared refuses-a-reserved-namespace refuses \
    "$(jq -n --argjson p "[$(submission Str.Tools str null)]" '{ plugins: $p }')" \
    "$(ledger "$(derived Str.Tools str null)")" \
    "reserved namespace"

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed of $((passed + failed)) failed."
    exit 1
fi

echo "$passed cases, all as expected."
