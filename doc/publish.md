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

**Exactly one `.json` under `ruleset/`.** A package with none or with two is refused, because
[the identifier is the package](policy.md#rule-set-identifiers) and a package holding two
documents is one where that stopped being true.

### The two strings that have to agree

**`PackageId`, and the `id` your document declares.** They are one string. `uses` names the
identifier, whatever fetches it asks nuget.org for that name, and the document that comes back
has to be the one that was named — the runtime refuses a component whose `id` is not what
`uses` said, so a package where these differ resolves to nothing for anybody.

**`Version`, and the `version` your document declares.** Also one string, and this one fails
more quietly. The fetch goes by the package's version; every `uses` constraint is answered by
the document's. A package published at `1.1.0` whose document still says `1.0.0` downloads
perfectly and satisfies no constraint that named it.

Both are checked on every release, not only at admission — see
[a document that renames itself](policy.md#a-document-that-renames-itself) for what happens to
one that drifts.

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

## What cannot hold it yet

**Nothing fetches a published rule set today.** `rulealize restore` resolves a `uses` against
the documents beside the one it was handed and reports anything else as missing; Studio
resolves against the folder beside the composite. Both go to a folder, and neither goes to the
feed.

So a package published now is admitted, indexed, and consumable only by somebody who downloads
it themselves. That is worth knowing before you spend the name, because
[a claim is permanent](policy.md#a-claim-is-permanent) — and it is also why the index is here
first. An index that arrived after publishing had begun would arrive after the first release
that needed it.
