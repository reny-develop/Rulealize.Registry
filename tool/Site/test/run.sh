#!/usr/bin/env bash
#
# What the site does with a catalogue written by somebody hostile.
#
# Every entry in the catalogue is derived by loading a package, and loading a package is
# running its code. That is [what this registry says it does not police](../../../doc/policy.md),
# so the pages built from it are written as though a publisher will one day put something in
# their assembly and their .nuspec on purpose. These are those two cases.
#
#   tool/Site/test/run.sh

set -uo pipefail

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

failed=0

report() {
    if [[ $1 -eq 0 ]]; then
        printf '  ok    %s\n' "$2"
    else
        printf '  FAIL  %s\n' "$2"
        failed=$((failed + 1))
    fi
}

echo "site:"

# 1. An operation name that is a path. It becomes site/op/<name>.html, so a name carrying ..
# would be written outside the folder — over the front page, in the case below. The generator
# has to refuse the catalogue rather than write any of it.
mkdir -p "$work/name/site"
printf 'the front page\n' > "$work/name/site/index.html"
output=$(cd "$root" && dotnet run --project tool/Site -c Release -- \
    tool/Site/test/hostile-name "$work/name/site/pages" 2>&1)
status=$?

[[ $status -ne 0 ]]
report $? "refuses an operation name that is a path"

grep -q 'not operation names' <<<"$output"
report $? "says which name it refused"

grep -qx 'the front page' "$work/name/site/index.html"
report $? "wrote nothing outside the output folder"

# 2. A .nuspec that is trying to write the page rather than describe a package. The strings
# are the publisher's, so they may say anything; what they may not do is leave the text.
mkdir -p "$work/url"
(cd "$root" && dotnet run --project tool/Site -c Release -- \
    tool/Site/test/hostile-url "$work/url" > /dev/null 2>&1)
report $? "renders a catalogue whose strings are hostile"

hrefs=$(grep -ho 'href="[^"]*"' -r "$work/url" | sort -u)

! grep -qi 'javascript:' <<<"$hrefs"
report $? "does not put a javascript: URL in an href"

# The pages only. The catalogue's JSON carries the publisher's string as their string — it is
# the API, and its readers are the ones who escape it, exactly as this generator does.
! grep -rqF --include='*.html' "<script>alert('description')</script>" "$work/url"
report $? "escapes a description that is markup"

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed failed."
    exit 1
fi

echo "all as expected."
