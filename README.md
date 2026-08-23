# Rulealize.Registry

The index of the [Rulealize](https://github.com/reny-develop/Rulealize) plugin ecosystem:
which plugin provides an operation, which versions satisfy a rule set's `requires`, which
namespaces are already spoken for, and which shorthand characters are in use by whom.

> **Status — early.** What exists is the claim ledger, the tools that check it and build the
> catalogue from it, the jobs that hold a pull request to both, and the site — which is
> [up](https://reny-develop.github.io/Rulealize.Registry/), labelled pre-release. Nothing
> beyond the standard distribution is indexed yet.

## The ledger

[`ledger/submitted.json`](ledger/submitted.json) records what each plugin claims — its
identifier, its namespace and its shorthand character. **The first two have exactly one owner
across the whole ecosystem.** The third has none: a character is recorded here and granted to
nobody, and three are in use so far:

| | Plugin | Namespace |
| --- | --- | --- |
| `@` | `Rulealize.Plugin.Binding` | `bind` |
| `$` | `Rulealize.Plugin.State` | `state` |
| `#` | `Rulealize.Plugin.Definition` | `def` |

The namespaces taken are `bind`, `branch`, `cmp`, `def`, `graph`, `grid`, `logic`, `math`,
`rec`, `seq`, `state`, `tuple` and `type`.

An entry is one line and four fields — the package, the version its claims were read at, the
namespace and the shorthand character. **The operations are not in it.** Nobody submits a list
of what their plugin registers, so nobody keeps one in step with a release and nobody can get
one wrong; they are read out of the assembly by [`tool/Ledger`](tool/Ledger/), which points a
`RuleRuntime` at a folder and writes down what came back — the same folder scan a deployed
application performs. Run it against your own plugin and it prints what it found:

```sh
dotnet run --project tool/Ledger -- <plugin folder>
```

It knows no plugin by name and has no list of the standard twelve.

**Nor is the rest of an entry believed.**
[`.github/workflows/ledger.yml`](.github/workflows/ledger.yml) fetches every package the
ledger names, loads it, and refuses anything that says one thing where its assembly says
another — a namespace, a shorthand character, a version, or a package published under a name
its manifest does not declare. A pull request may state a claim; that job is what decides
whether it is true.

Which is why **a submission that adds one line and touches nothing else merges without anybody
reading it**. [`admit.yml`](.github/workflows/admit.yml) checks that it is that and no more —
the rules are in [`.github/admit/gate.sh`](.github/admit/gate.sh), one case each in
[`.github/admit/test/`](.github/admit/test/) — and then waits for the checks. What it will not
admit waits on nobody either: a submission with something wrong in it says so on the pull
request and is the submitter's to push again, and a pull request that is not a submission is
closed, because **this repository indexes plugins and takes nothing else that way** — anything
about the tools or the site belongs in an issue.
[The grant policy](doc/policy.md#what-happens-to-your-pull-request) says which is which.

## The catalogue

The ledger is what a person reviews. The catalogue is what the site and the resolver are
generated from, and nobody reviews it — it is rebuilt from nuget.org on every run and is not
in this repository.

```sh
dotnet publish tool/Ledger -c Release -o work/probe
dotnet run --project tool/Catalogue -- ledger/submitted.json work/probe/Rulealize.Registry.Ledger.dll site
```

| | |
| --- | --- |
| `site/index.json` | every plugin and operation in summary — 10 KB for the standard distribution, which is what makes search a client-side matter |
| `site/plugin/<id>.json` | one plugin, every released version, every operation of each |

The ledger holds one line per plugin because [a claim is permanent](doc/policy.md#a-claim-is-permanent);
the catalogue holds one entry per version because a rule set's `requires` reads `^1.0` and
operations may be added within a major. **So a new version of a plugin already admitted needs
no pull request** — nothing committed changes, and the next scheduled run picks it up.

What that would otherwise let through is a plugin changing its namespace quietly between
releases. [`catalogue.yml`](.github/workflows/catalogue.yml) checks every version's claims
against the ledger, and a version that disagrees is **withheld**: it stays in the entry and on
the plugin's page, marked, carrying what it claimed, and nothing is indexed off it — not its
operations, and not the plugin's `latest`. So the rule is enforced daily, the file a person
reads does not move, and the one party who can put it right is the one it is said to.

Nothing hand-written goes into a catalogue entry. The description, repository, licence and
abstraction version are in the `.nuspec`; the claims and operations are in the assembly. A
submission is a package identifier, the version to read it at, and the two names it claims —
and every one of those four is checked against the package before it counts.

The abstraction version is worth recording because it is a whole class of failure a user
otherwise meets as a `ReflectionTypeLoadException`, which the runtime can only report as
"most likely built against a different version of `Rulealize.Abstraction`". Prose is linked
rather than copied, for the reason a specification lives with its own plugin: it describes
one version of one vocabulary and has to change when that plugin releases, so a copy here
would ship on this repository's schedule and would eventually be wrong.

## Why this is not just a page on nuget.org

A package feed distributes plugins perfectly well, and this repository does not try to
replace it. What a feed cannot model is the part that makes a Rulealize plugin a plugin.

**A plugin claims a namespace, and it has exactly one owner across the whole ecosystem.** The
runtime refuses a collision when the plugins are loaded together, which is after both were
published and after rule sets naming them are in production. Nobody in the ecosystem sees two
plugins that have never been loaded together, and that is exactly the pair that collides.

An index is the only party that does. That ledger is this repository's first job and the
only part of it that cannot be added later.

## The site

[`tool/Site`](tool/Site/) renders the catalogue as pages: the claim table, a plugin page per
entry, and a page per operation, because `grid.ray` is what somebody arrives holding and
knowing which vocabulary owns it is the question nuget.org structurally cannot answer.

```sh
dotnet run --project tool/Site -- catalogue site
```

It reads `/index.json` and `/plugin/<id>.json` and nothing else — no reference to Rulealize,
none to the catalogue's code — so it is the first consumer of the published API rather than a
second path to the same facts. The search on the front page fetches `/index.json` like any
other client would.

**It is up**: <https://reny-develop.github.io/Rulealize.Registry/>. It carries `noindex` and
says pre-release in its header, because what it indexes is still the standard distribution
and nothing else.

Every page says in its footer when the catalogue was last read out of nuget.org, and
`/index.json` carries the same stamp to the second. An index nobody is keeping looks exactly
like one that is — the daily run could stop and every page would go on being served, correct
about a world that had moved. The date is what says otherwise. The same run leaves one line
per check on the [`checks`](../../tree/checks) branch, which is the record that outlives the
ninety days a workflow log is kept for.

CI renders it on every run and uploads it whether or not that run will publish, so the
generator is exercised either way and only the deploy step reads `PUBLISH_SITE`. Holding the
build too would have left the generator as the one part never run, broken on the day it
mattered.

## What it indexes

| | |
| --- | --- |
| **plugin** | submitted as a package identifier, a version, and the two names it claims. Nothing stated is believed until the package says the same |
| **operation** | `grid.ray`, `rec.keys` — read out of the assembly, never submitted |

**Rule sets are not indexed here**, though for most of this repository's design they were
going to be. Three findings took the case apart. There is **no scarce name to govern**, so
there is nothing only an index can do. **git offers no immutable, enumerable version list**
to derive an entry from — a tag can be moved, so an entry would have to pin a commit and
every version would need a pull request, which is exactly the property that makes a new
plugin version cost nothing here. And **nothing anywhere depends on a rule set**: `requires`
names plugins, and nothing names a document, so listing one would have supplied a link and
no mechanism.

The resolver still reads a `requires`. The rule set it reads is the user's own file, on their
own disk: **the registry supplies the vocabulary, never the rules.**

## What it will never be

A package host, or a place that asks a publisher to describe their plugin in a form. A
plugin is a public `IRulealizePlugin` with a parameterless constructor and nothing else
marks one, because the interface is already the contract — so an entry is built by loading
the assembly exactly as an application does, and a submission carries a pointer rather than
a description.

## License

Apache-2.0 covers what this repository is made of — the tools under [`tool/`](tool/), the
jobs and scripts under [`.github/`](.github/), and the prose.

The ledger is not that. [`ledger/`](ledger/) is a record of who claimed which identifier,
namespace and shorthand character, and it is dedicated to the public domain under
[CC0-1.0](ledger/LICENSE-CC0), as is the catalogue built from it — mirror it, embed it in a
resolver, no attribution asked.

**An indexed plugin is licensed by whoever published it.** An entry is a pointer to a package
and holds nothing of the package itself, so nothing here states the terms of what it points
at.
