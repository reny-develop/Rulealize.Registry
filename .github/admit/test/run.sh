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

# ruleset <id> [version] — a rule set submission, which states a package and a version and
# nothing else. There is no third field: the identifier IS the package identifier.
ruleset() {
    jq -nc --arg id "$1" --arg version "${2:-0.1.0}" '{ id: $id, version: $version }'
}

# document <id> [version] — one rule set as the ledger tool reads it out of the document
document() {
    jq -nc --arg id "$1" --arg version "${2:-0.1.0}" \
        '{ id: $id, admitted: $version, requires: [], uses: [], inputs: ["submit"] }'
}

documents() {
    jq -n --argjson ruleSets "[$(printf '%s,' "$@" | sed 's/,$//')]" '{ plugins: [], ruleSets: $ruleSets }'
}

# gate <name> <admit|close|fix> [reason] — the head submitted list on standard input.
# "close" means it is not a submission at all; "fix" is the submitter's own, and neither
# waits on anybody.
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
        1) actual=close ;;
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

# rules <contexts as json> — a branch's active rules, in the shape the API answers with
rules() {
    jq -nc --argjson contexts "$1" \
        '[ { type: "deletion" },
           { type: "pull_request", parameters: { required_approving_review_count: 0 } },
           { type: "required_status_checks",
             parameters: { required_status_checks: ($contexts | map({ context: . })) } } ]'
}

# required <name> <agrees|refuses> <required json> <rules json> [reason]
required() {
    local name=$1 expect=$2 reason=${5:-}
    printf '%s\n' "$3" > "$work/required.json"
    printf '%s\n' "$4" > "$work/rules.json"

    local output status actual
    output=$("$here/../required.sh" "$work/required.json" "$work/rules.json" 2>&1)
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

gate fixes-a-removal fix "removes or rewrites" \
    <<<"$(jq '.plugins |= map(select(.namespace != "bind"))' "$work/base.json")"

gate fixes-a-rewrite fix "removes or rewrites" <<<"$(jq --argjson e "$acme" \
    '.plugins = [$e] + (.plugins | map(if .namespace == "math" then .version = "2.0.0" else . end))' "$work/base.json")"

# A shorthand character is first come like a namespace, outside the reserved set. What it
# costs whoever asks second is writing the operation out, which is what a namespace costs too.
gate admits-a-shorthand admit \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme '"!"')" "$insert" "$work/base.json")"

# Not this one. `|` separates a tuple's components inside its own text, so a plugin holding it
# would swallow the text form of somebody else's value.
gate fixes-a-reserved-prefix fix "reserved prefix" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme '"|"')" "$insert" "$work/base.json")"

gate fixes-a-long-prefix fix "one character or none" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme '"!!"')" "$insert" "$work/base.json")"

FILES=$'ledger/submitted.json\ntool/Ledger/Program.cs' \
    gate closes-another-file close "more than the submitted list" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

FILES='ledger/reserved.json' \
    gate closes-the-reserved-list close "more than the submitted list" \
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

gate fixes-nothing-added fix "adds nothing" < "$work/base.json"

# A second line for a package already in the ledger. It states nothing new — the assembly it
# names agrees with it, so declared.sh has nothing to refuse — and the catalogue would carry
# the plugin twice.
gate fixes-a-duplicate fix "one line per package" \
    <<<"$(jq --argjson e "$(submission Rulealize.Plugin.Binding bind '"@"' 2.0.0)" \
        '.plugins = .plugins + [$e]' "$work/base.json")"

# ── rule sets ──────────────────────────────────────────────────────────────────────
#
# The same gate, pointed at a second kind of entry. Everything about the shape of the pull
# request is the rule it already was; what is new is an entry with two fields instead of four,
# and the reason it has two is that a rule set's identifier is its package identifier — so
# there is no second name here to hold to a format, to an order of its own, or to a reserved
# list.

rules=$(ruleset Acme.Rules.Approval)
holds='.ruleSets = [$e] + .ruleSets'

gate admits-a-rule-set admit <<<"$(jq --argjson e "$rules" "$holds" "$work/base.json")"

# One pull request may add one of each. Neither list is the other's business and the gate
# reads both the same way.
gate admits-both-kinds admit \
    <<<"$(jq --argjson p "$acme" --argjson r "$rules" \
        '.plugins = [$p] + .plugins | .ruleSets = [$r] + .ruleSets' "$work/base.json")"

gate fixes-a-rule-set-removal fix "removes or rewrites" \
    <<<"$(jq '.ruleSets = []' "$work/base.json")"

gate fixes-a-rule-set-out-of-order fix "identifier order" \
    <<<"$(jq --argjson e "$rules" '.ruleSets = .ruleSets + [$e]' "$work/base.json")"

gate fixes-a-rule-set-version fix "three-part version" \
    <<<"$(jq --argjson e "$(ruleset Acme.Rules.Approval 1.0)" "$holds" "$work/base.json")"

gate fixes-a-rule-set-duplicate fix "one line per package" \
    <<<"$(jq --argjson e "$(ruleset Rulealize.RuleSet.Approval 2.0.0)" \
        '.ruleSets = .ruleSets + [$e]' "$work/base.json")"

# The identifier is not a second name, so writing one is not a shorter way of saying the same
# thing — it is stating something this ledger does not record and would never check.
gate fixes-a-stated-identifier fix "states more than a rule set entry holds" \
    <<<"$(jq --argjson e "$(jq -nc '{ id: "Acme.Rules.Approval", version: "1.0.0", ruleSet: "approval" }')" \
        "$holds" "$work/base.json")"

# A package holding an assembly and a document under one identifier. The two entries would be
# checked against each other's artifact, and at least one of them would be withheld daily.
gate fixes-a-straddling-package fix "one or the other" \
    <<<"$(jq --argjson p "$acme" --argjson r "$(ruleset Acme.Deploy.Rules)" \
        '.plugins = [$p] + .plugins | .ruleSets = [$r] + .ruleSets' "$work/base.json")"

gate fixes-broken-json fix "not valid JSON" <<<'{ "plugins": ['

# The file itself, gone. Every claim in the ledger goes with it, so this is not a submission
# with a mistake in it.
gate closes-a-deletion close "leaves no" < /dev/null

BASE_REF=release \
    gate fixes-another-base fix "does not target" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

DRAFT=true \
    gate fixes-a-draft fix "is a draft" \
    <<<"$(jq --argjson e "$acme" "$insert" "$work/base.json")"

# Both at once — a version in the wrong format on a pull request that also changes something
# else. Closing wins: fixing the version would not make this a submission.
FILES=$'ledger/submitted.json\nREADME.md' \
    gate closes-both-at-once close "more than the submitted list" \
    <<<"$(jq --argjson e "$(submission Acme.Deploy.Rules acme null 1.0)" "$insert" "$work/base.json")"

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

declared refuses-a-reserved-prefix refuses "$submitted" \
    "$(ledger "$(derived Acme.Deploy.Rules acme '"|"')")" \
    "reserved shorthand character"

declared refuses-a-reserved-namespace refuses \
    "$(jq -n --argjson p "[$(submission Str.Tools str null)]" '{ plugins: $p }')" \
    "$(ledger "$(derived Str.Tools str null)")" \
    "reserved namespace"

# A rule set states two things and both are checked, which is the whole of it. Nothing here
# has a reserved list to be refused against and nothing has a namespace to move, because the
# identifier the document declares is the package identifier it was fetched by.
held=$(jq -n --argjson r "[$(ruleset Acme.Rules.Approval)]" '{ plugins: [], ruleSets: $r }')

declared agrees-with-its-document agrees "$held" "$(documents "$(document Acme.Rules.Approval)")"

# The document renaming itself. A rule set published under one package and declaring another
# identifier is unresolvable by anybody: `uses` names the identifier, and a fetch by it either
# misses or returns a document that says something else.
declared refuses-another-document-id refuses "$held" \
    "$(documents "$(document approval)")" \
    "was submitted, and no document of that name"

declared refuses-a-second-document refuses "$held" \
    "$(documents "$(document Acme.Rules.Approval)" "$(document Acme.Rules.Shift)")" \
    "neither a rule set anybody submitted nor part of one"

# A package may ship the document named for it and the parts that one is built out of, the way
# a library's internal types ship in its assembly. What keeps those from being an ungoverned
# name space is that each is named under the package: nuget.org allocated that prefix, so two
# packages cannot ship one identifier however many parts either is built from.
declared agrees-with-a-part agrees "$held" \
    "$(documents "$(document Acme.Rules.Approval)" "$(document Acme.Rules.Approval.Step)")"

declared refuses-a-part-named-elsewhere refuses "$held" \
    "$(documents "$(document Acme.Rules.Approval)" "$(document Acme.Rules.Shared)")" \
    "neither a rule set anybody submitted nor part of one"

# A package holding parts and nothing declaring its own name. `uses` names the package, so
# there has to be a document that answers to it.
declared refuses-a-package-with-no-entry refuses "$held" \
    "$(documents "$(document Acme.Rules.Approval.Step)")" \
    "no document of that name came out of the packages"

# The package version and the document's own version, drifted apart. The fetch goes by the
# first and every `uses` constraint is answered by the second, so a package resolvable at
# 0.1.0 would satisfy `^0.2` and nothing would say why.
declared refuses-a-drifted-document refuses "$held" \
    "$(documents "$(document Acme.Rules.Approval 0.2.0)")" \
    "and its document says 0.2.0"

# Neither list is the other's evidence. A ledger holding both kinds is checked against a
# derivation holding both, and an entry of one kind is never answered by the other.
declared agrees-with-both-kinds agrees \
    "$(jq -n --argjson p "[$one]" --argjson r "[$(ruleset Acme.Rules.Approval)]" \
        '{ plugins: $p, ruleSets: $r }')" \
    "$(jq -n --argjson p "[$(derived Acme.Deploy.Rules acme null)]" \
        --argjson r "[$(document Acme.Rules.Approval)]" '{ plugins: $p, ruleSets: $r }')"

echo
echo "required:"

must=$(jq -nc '{ checks: ["build", "cases", "rederive"] }')

required agrees-with-the-branch agrees "$must" "$(rules '["build", "cases", "rederive"]')"

# A branch may require more than a submission is admitted on. This says what has to be there.
required agrees-with-more agrees "$must" "$(rules '["build", "cases", "rederive", "codeql"]')"

required refuses-one-missing refuses "$must" "$(rules '["build", "cases"]')" \
    '`rederive` is not a required status check'

required refuses-no-status-rule refuses "$must" '[{ "type": "deletion" }]' \
    "requires no status check"

required refuses-no-rules refuses "$must" '[]' "requires no status check"

# Anything that is not a list of rules is the branch answering something else, and it is not
# evidence that a check is required.
required refuses-another-answer refuses "$must" '{ "message": "Not Found" }' \
    "did not answer with a list of rules"

# The other end of the same list. A context no job reports under is one auto-merge waits on
# for ever, which is a submission that never merges rather than one that merges too early.
while IFS= read -r check; do
    check=${check%$'\r'}
    [[ -z "$check" ]] && continue
    if grep -qE "^  ${check}:[[:space:]]*$" "$here/../../workflows/"*.yml; then
        printf '  ok    %-26s %s\n' "names-a-job" "$check"
        passed=$((passed + 1))
    else
        printf '  FAIL  %-26s `%s` is a job in no workflow\n' "names-a-job" "$check"
        failed=$((failed + 1))
    fi
done < <(jq -r '.checks[]' "$here/../required.json")

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed of $((passed + failed)) failed."
    exit 1
fi

echo "$passed cases, all as expected."
