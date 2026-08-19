# Rulealize.Registry

The index of the [Rulealize](https://github.com/reny-develop/Rulealize) plugin ecosystem:
which plugin provides an operation, which versions satisfy a rule set's `requires`, and which
namespaces and shorthand characters are already spoken for.

> **Status — early.** What exists is the claim ledger, the tool that derives it, and the job
> that holds a pull request to it. There is no site, and nothing beyond the standard
> distribution is indexed yet. [`doc/design.md`](doc/design.md) says what will be built, in
> what order, and what will not be.

## The ledger

[`ledger/claim.json`](ledger/claim.json) records what each plugin claims — its identifier,
its namespace, its shorthand character, and every operation it registers. **Each of those has
exactly one owner across the whole ecosystem**, and three of them are already spent:

| | Plugin | Namespace |
| --- | --- | --- |
| `@` | `Rulealize.Plugin.Binding` | `bind` |
| `$` | `Rulealize.Plugin.State` | `state` |
| `#` | `Rulealize.Plugin.Definition` | `def` |

The namespaces taken are `bind`, `branch`, `cmp`, `def`, `grid`, `logic`, `math`,
`rec`, `seq`, `state`, `tuple` and `type`.

Nothing in that file is written by hand. [`tool/Ledger`](tool/Ledger/) points a `RuleRuntime`
at a folder of assemblies and writes down what came back, which is the same folder scan a
deployed application performs:

```sh
dotnet run --project tool/Ledger -- <plugin folder> ledger/claim.json
```

It knows no plugin by name and has no list of the standard twelve. Regenerating it against
the same assemblies produces no diff — there is no timestamp in the file and everything in it
is sorted — so a change in the ledger is always a change in what somebody claimed.

[`.github/workflows/ledger.yml`](.github/workflows/ledger.yml) does exactly that on every
pull request touching the ledger: it fetches the packages the file names, re-derives it, and
fails on any difference. A pull request may claim a namespace and an operation list; that job
is what decides whether the claim is true.

## The catalogue

The ledger is what a person reviews. The catalogue is what the site and the resolver are
generated from, and nobody reviews it — it is rebuilt from nuget.org on every run and is not
in this repository.

```sh
dotnet publish tool/Ledger -c Release -o work/probe
dotnet run --project tool/Catalogue -- ledger/claim.json work/probe/Rulealize.Registry.Ledger.dll site
```

| | |
| --- | --- |
| `site/index.json` | every plugin and operation in summary — 10 KB for the standard distribution, which is what makes search a client-side matter |
| `site/plugin/<id>.json` | one plugin, every released version, every operation of each |

The ledger holds one row per plugin because [a claim is permanent](doc/policy.md#a-claim-is-permanent);
the catalogue holds one entry per version because a rule set's `requires` reads `^1.0` and
operations may be added within a major. **So a new version of a plugin already admitted needs
no pull request** — nothing committed changes, and the next scheduled run picks it up.

What that would otherwise let through is a plugin changing its namespace quietly between
releases. [`catalogue.yml`](.github/workflows/catalogue.yml) checks every version's claims
against the ledger and writes nothing at all if one disagrees, so the rule is enforced daily
while the file a person reads does not move.

Nothing hand-written goes into a plugin entry. The description, repository, licence and
abstraction version are in the `.nuspec`; the claims and operations are in the assembly. A
submission is a package identifier.

## Why this is not just a page on nuget.org

A package feed distributes plugins perfectly well, and this repository does not try to
replace it. What a feed cannot model is the part that makes a Rulealize plugin a plugin.

**A plugin claims an identifier, a namespace and — at most — one shorthand character, and
each of those has exactly one owner across the whole ecosystem.** The runtime refuses a
collision when the plugins are loaded together, which is after both were published and after
rule sets naming them are in production. Nobody in the ecosystem sees two plugins that have
never been loaded together, and that is exactly the pair that collides.

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

**It is not up yet.** CI renders it on every run and uploads it, so the generator is
exercised whether or not anything is published; deploying is one repository variable
(`PUBLISH_SITE`) and enabling Pages. Holding the build too would leave the generator as the
one part never run, broken on the day it matters.

## What it indexes

| | |
| --- | --- |
| **plugin** | submitted as a package identifier. Everything recorded is derived by loading it |
| **operation** | `grid.ray`, `rec.keys` — derived from the above, never submitted |

**Rule sets are not indexed here**, though for most of this design's life they were going to
be. There is no scarce name to govern, git offers no immutable enumerable version list to
derive from, and nothing anywhere depends on a rule set — `requires` names plugins and
nothing names a document. Listing one would have supplied a link and no mechanism.
[`doc/design.md` §5](doc/design.md) is the full account.

The resolver still reads a `requires`. The rule set it reads is the user's own file, on their
own disk: **the registry supplies the vocabulary, never the rules.**

## What it will never be

A package host, or a place that asks a publisher to describe their plugin in a form. A
plugin is a public `IRulealizePlugin` with a parameterless constructor and nothing else
marks one, because the interface is already the contract — so an entry is built by loading
the assembly exactly as an application does, and a submission carries a pointer rather than
a description.

## License

Apache-2.0.
