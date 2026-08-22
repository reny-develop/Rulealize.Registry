# Documentation

| | |
| --- | --- |
| [The grant policy](policy.md) | what may be claimed, on what grounds, and what can never be taken back |

That one is binding. It is what a submission is held to, and it is written to be read by the
person about to open the pull request rather than by whoever maintains this repository.

It lives here rather than in the Rulealize repository for the reason that repository already
applies to plugin specifications: a document belongs with the thing it describes, so that it
can change when that thing does and not on somebody else's release schedule. Which namespaces
are spoken for is not a fact about the runtime, and the runtime is not where it should be
recorded.

The claim ledger the policy governs exists as data — [`ledger/submitted.json`](../ledger/submitted.json),
one line per plugin, every line held to the package it names by [`tool/Ledger`](../tool/Ledger/)
loading it. What that loading finds goes into the catalogue built by
[`tool/Catalogue`](../tool/Catalogue/), which is committed nowhere because it is derived from
nuget.org on every run. [The readme](../README.md) describes both, and why only one of them is
in git.
