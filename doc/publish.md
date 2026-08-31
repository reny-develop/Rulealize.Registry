# Publishing to this index

What to build, so that a submission has something true to say. [The grant policy](policy.md)
is what a submission is held to; this is how to produce the thing it is held against.

Neither kind is hosted here. nuget.org distributes both, and this index records what they
claimed and reads back what they actually say.

## A plugin

An ordinary .NET library, packed the ordinary way. `dotnet pack` on the project that
implements `IRulealizePlugin` is the whole of it, and the two rules that are not ordinary are
both in the policy:

- [publish under the identifier your manifest declares](policy.md#plugin-identifiers) — a
  `requires` names a `PluginManifest.Id`, and that is the name anything fetching it asks for
- your manifest's version and your package's version are one version, because
  [the ledger records the release your claims were read at](policy.md#a-plugin) and CI asks
  nuget.org for exactly that string

Everything else about the package is NuGet's business and nothing here has an opinion about it.

## A rule set

A rule set is a document, and a package that distributes one carries no assembly at all:

```
Acme.Rules.Approval.1.0.0.nupkg
└── ruleset/
    └── approval.json      ← "id": "Acme.Rules.Approval"
```

There is nothing to compile, so the project below compiles nothing.

### The project

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <EnableDefaultItems>false</EnableDefaultItems>

    <PackageId>Acme.Rules.Approval</PackageId>
    <Version>1.0.0</Version>
    <Authors>Acme</Authors>
    <Description>A request that has to be raised and granted.</Description>
  </PropertyGroup>

  <ItemGroup>
    <None Include="ruleset/*.json" Pack="true" PackagePath="ruleset/" />
  </ItemGroup>

</Project>
```

Four of those lines are what make it a rule set package rather than a library.

| | |
| --- | --- |
| `IncludeBuildOutput` | `false`, so no `lib` folder is written. A package with one is read here as an assembly |
| `SuppressDependenciesWhenPacking` | `true`. A document depends on no assembly — its `requires` names vocabularies, which is a different thing and not NuGet's to resolve. Without this, `pack` warns `NU5128` about a target framework it can find nothing under |
| `EnableDefaultItems` | `false`. There are no sources to glob, and saying so is cheaper than explaining an empty assembly to somebody later |
| `None … Pack="true"` | the document, and where it sits in the package |

`TargetFramework` is required by the SDK and reaches nothing: with the dependency group
suppressed, no framework appears anywhere in the package that is built.

**At least one `.json` under `ruleset/`, and one of them declares the package's identifier.**
A package with none is refused. A rule set built out of parts ships them alongside — see
[a package may ship more than one document](#a-package-may-ship-more-than-one-document).

### The two strings that have to agree

**`PackageId`, and the `id` your document declares.** They are one string. `uses` names the
identifier, whatever fetches it asks nuget.org for that name, and the document that comes back
has to be the one that was named — the runtime refuses a component whose `id` is not what
`uses` said, so a package where these differ resolves to nothing for anybody.

**One string down to the case.** nuget.org does not care — a package identifier is one name to
it however it is cased — but the runtime compares a `uses` entry against the document supplied
for it exactly, so `Acme.Rules.Approval` and `acme.rules.approval` are two rule sets. A
document whose `id` is your package with the casing changed still downloads — the feed
lowercases the name either way — and then satisfies nobody's `uses`. It is withheld here for
disagreeing with the line you submitted. Copy the identifier from one place to the other
rather than typing it twice.

**`Version`, and the `version` your document declares.** Also one string, and this one fails
more quietly. The fetch goes by the package's version; every `uses` constraint is answered by
the document's. A package published at `1.1.0` whose document still says `1.0.0` downloads
perfectly and satisfies no constraint that named it.

Both are checked on every release, not only at admission — see
[a document that renames itself](policy.md#a-document-that-renames-itself) for what happens to
one that drifts.

### If your rule set holds others

Three things are separate here, and .NET separates the same three:

| | .NET | a rule set |
| --- | --- | --- |
| the reference | `<PackageReference>` in the project file | **`uses` in the document** |
| the artifact | in `~/.nuget/packages`, never in your repository or your package | **fetched into `component/`**, likewise |
| the declaration | `<dependency>` in your nuspec | **nothing** |

**The reference is in the document, and only there.** `uses` names the identifier and the
version, the runtime reads it, and a project file that repeated it would be a second place for
the same fact to be written and a second place to get it wrong.

**The artifact is fetched and belongs to nobody here.** `rulealize restore` from 0.9.0 writes
what a `uses` names into `component/`, which is the tool's the way `plugin/` is. Add both to
your ignore file. Before 0.9.0 it wrote next to the document instead, which for a repository
laid out like this one is the folder being packed — so if you are on an older tool, check what
your package actually contains.

**Your package declares no dependency, and that is deliberate.** NuGet would resolve `uses`
differently from Rulealize: a `^1.0` here means *the lowest published version that satisfies
it*, chosen so a document restores to the same folder next year, and NuGet's nearest
equivalent floats upward. Declaring one would put two resolvers on one graph, and the one that
fetched would not be the one that compiles. So `SuppressDependenciesWhenPacking` is set, and
not merely to quiet a warning.

Naming the file rather than globbing it is cheap insurance on top:

```xml
<None Include="ruleset/roster.json" Pack="true" PackagePath="ruleset/" />
```

### A package may ship more than one document

A rule set built out of parts ships them with it, the way a library's internal types ship in
its assembly rather than in packages of their own:

```
Acme.Rules.Ordering.1.0.0.nupkg
└── ruleset/
    ├── ordering.json        ← "id": "Acme.Rules.Ordering"
    ├── line.json            ← "id": "Acme.Rules.Ordering.Line"
    └── payment.json         ← "id": "Acme.Rules.Ordering.Payment"
```

**Exactly one declares the package's own identifier.** That is the one a `uses` naming this
package gets, and a package where none of them does is refused: `uses` names a package, so
something in it has to answer to that name.

**Every other identifier is named under it.** `Acme.Rules.Ordering.Line`, not
`Acme.Rules.Line` and not `OrderLine`. That prefix is the whole reason this is allowed — the
identifier of a published rule set is one nuget.org allocated, so anything under it is
allocated too, and two packages cannot ship one identifier however many parts either is built
from. Without the rule the parts would be an ungoverned name space, and the collision this
registry exists to prevent would come back through the side door.

A part is not separately nameable from outside: a `uses` naming
`Acme.Rules.Ordering.Line` on its own looks for a package of that name and finds none. That is
the right failure — it is an internal document, and reaching for one says so loudly.

### The file inside may be called anything

`approval.json`, `rules.json`, the name of the process it describes. **Which document answers
to an identifier is read out of the document**, never off its file name — a file gets renamed
and an identifier does not. It is the rule
[Rulealize.Cli](https://github.com/reny-develop/Rulealize.Cli) already resolves a held rule set
by, and the rule this index reads a package by.

### Publish, then submit

```sh
dotnet pack -c Release
dotnet nuget push bin/Release/Acme.Rules.Approval.1.0.0.nupkg --source nuget.org --api-key <key>
```

Then [one line in the ledger](policy.md#a-rule-set), naming the package and the version its
document was read at. CI fetches that package, reads the document inside it, and refuses the
pull request if either string disagrees. Nothing about your `requires`, your `uses` or your
inputs is submitted — they are read.

### After that, a release costs nothing

A new version of a rule set already in the ledger needs **no pull request**. Nothing committed
changes; the next scheduled run finds the release, reads it, and adds it to the entry.

What that same run also does is check it. A release whose document declares a different
identifier, or a version that is not the package's, is **withheld** — it appears on the rule
set's page, marked, carrying what it declared, and `latest` stays at the newest release that
still agrees. Nothing resolves to a withheld release, which is the point: the alternative is
that somebody's restore does.

## What can hold it

[`rulealize restore`](https://github.com/reny-develop/Rulealize.Cli), from 0.8.0, fetches what
a `uses` names — through the whole graph, into the folder the holding document resolves its
components from:

```
$ rulealize restore roster.json
  Acme.Rules.Approval@1.0.0 -> Acme.Rules.Approval.json
1 rule set -> .
holding 1 rule set:
  Acme.Rules.Approval@1.0.0 ('Acme.Rules.Approval.json')
  …
7 plugins -> plugin
'roster.json' compiles against it.
```

The plugins are the union of what every document in the graph requires, which is why there are
more of them than the composite's own `requires` names.

It never writes over a document already in that folder, so a component its author keeps beside
the composite wins over anything published under the same identifier.

**Not everything does.** A host that resolves components out of a folder and never fetches is
a supported arrangement and a common one — a deployment has its documents already. What such a
host needs is somebody to have put them there, which is what the command above is for.

Before you spend the name, the thing worth knowing is that
[a claim is permanent](policy.md#a-claim-is-permanent). Nothing else about publishing is.
