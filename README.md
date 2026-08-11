# Rulealize.Registry

The index of the [Rulealize](https://github.com/reny-develop/Rulealize) ecosystem: which
plugin provides an operation, which plugins a rule set needs, and which namespaces and
shorthand characters are already spoken for.

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

The twelve namespaces taken are `bind`, `branch`, `cmp`, `def`, `grid`, `logic`, `math`,
`rec`, `seq`, `state`, `tuple` and `type`, between them providing 68 operations.

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

## What it will index

| | |
| --- | --- |
| **plugin** | submitted as a package identifier. Everything recorded is derived by loading it |
| **operation** | `grid.ray`, `rec.keys` — derived from the above, never submitted |
| **rule set** | submitted as a repository and a tag, and validated by compiling it |

Rule sets are here on equal footing because Rulealize's claim is that a rule set is data —
it ships, versions and diffs on its own, and the same host binary runs a different set of
rules. That needs somewhere to publish one.

## What it will never be

A package host, or a place that asks a publisher to describe their plugin in a form. A
plugin is a public `IRulealizePlugin` with a parameterless constructor and nothing else
marks one, because the interface is already the contract — so an entry is built by loading
the assembly exactly as an application does, and a submission carries a pointer rather than
a description.

## License

Apache-2.0.
