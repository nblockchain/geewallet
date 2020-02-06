It is assumed that by contributing to this repository in the form of
PullRequests/MergeRequests, you grant the intellectual property of your
contribution under the terms of the MIT licence.
If you don't wish to comply with this policy, you can keep a fork in your
gitlab/github account.

# F# Coding Style

* We use PascalCase for namespaces, types, methods, properties and record
members, but camelCase for parameters, private fields, and local functions
(in unit tests we have the exception of allowing under_score_naming for
fields in cases where it improves readability).
* When writing non-static type members we prefer to use the word `self`.
* We follow the same convention of C# to prefix interfaces with the uppercase
letter 'I'.
* Given that we use the C#ish style of PascalCase for type names (instead of
camelCase), then it only makes sense to try to use the type names which start
with uppercase, instead of the camelCased F# types (e.g. use `Option` and `List`
instead of `option` and `list`). The only exception to this rule is: primitive
types (where we prefer `string` and `int` over `String` and `Int32` unless we're
using a static method of them; and `array` over `Array` because they are actually
different things).
* To not confuse array types with lists, we prefer to use `List.Empty` over `[]`
(where it's possible; e.g. in match cases it's not possible), and `array<Foo>`
over `Foo []`.
* We prefer the generic notation `Foo<Bar>` rather than `Bar Foo`.
* We prefer to not use shadowing practice, even if the F# compiler allows it
(not to confuse shadowing with mutation, which is discouraged too anyway).
* We prefer to write parenthesis only when strictly necessary (e.g. in F# they
are not required for `if` clauses, unlike C#) or for readability purposes (e.g.
when it's not clear what operator would be applied first by the order preference
rules of the language).
* Whenever possible, we prefer to use currified arguments (instead of tuples),
should we need to use F# partial application.
* We avoid to write the keyword `new` for instances of non-IDisposable types.
* When dealing with `Option<Foo>` elements, we consider it's much safer to use
`match` patterns (or the functions `Option.iter` and `Option.exists`) instead
of using the less safe approaches  `x.IsSome && x.Value = ...` or
`x.IsNone || x.Value = ...`, which might break easily when refactoring them.
* In case of doubt, we prefer to expliticly add the accessibility keywords
(`private`, `public`, `internal`...), should the F# language allow it.
* With `if` blocks we prefer to put the `then` keyword in the same line as the
`if`, but use a newline afterwards; and the `else` or `elif` keywords indented
to be aligned with the `if`. Example:

```
if foo.SomeBoolProperty then
    DoSomething()
elif foo.SomeFuncReturingBool() then
    DoOtherThing()
else
    DoYetAnotherThing()
```

Another example:

```
let someVariableToBeAssigned =
    if foo.SomeBoolProperty then
        "someValue"
    elif foo.SomeOtherCondition() then
        "otherValue"
    else
        "elseValue"
```

* A space should be added after the colon (and not before) when denoting a type,
so: `(foo: Foo)`
* When using property initializers, we prefer to use the immutable syntax sugar:
```
let foo = Foo(Bar = bar, Baz = baz)
```
instead of the more verbose (and scary)
```
let foo = Foo()
foo.Bar <- bar
foo.Baz <- baz
```
* When laying out XamarinForms UIs, we prefer to use XAML (if possible) instead
of adding them programmatically with code.
* The `open` keyword should be used to open namespaces if and only if the
element used from it is used more than once in the same file.
* We prefer the short F# syntax to declare exception types (just
`exception Foo of Bar*Baz`) except when constructors need to be used (e.g. for
passing the inner exception to the base class).
* We only use the `mutable` keyword when strictly necessary. Should you need it,
special precautions should be taken to access the element from one exclusive
thread (e.g. by using locks). In order to write immutable algorithms (as opposed
to imperative-style ones), should you need to write recursive functions to
compose them, you have to make sure they are tail-recursive-friendly, to not
cause stack-overflow exceptions.
* When creating Tasks in UI code (Xamarin.Forms), don't run them without some
careful guarding (e.g. we want to fail fast, as in crash the app, if any
exception happens in it); for example, you could use the special function
`FrontendHelpers.DoubleCheckCompletion` to help on this endeavour.
* Don't use abbreviations or very short names on variables, types, methods, etc.
We prefer to be verbose and readable than compact and clever.
* Don't over-comment the code; splitting big chunks of code into smaller
functions with understandable names is better than adding comments that may
become obsolete as the code evolves.
* We prefer the Java way of mapping project names and namespaces with the tree
structure of the code. For example, a module whose full name is Foo.Bar.Baz
should either live in a project called "Foo.Bar" (and be named "Baz" under
the namespace "Foo.Bar"), or: in a project called "Foo", but in a subdirectory
called "Bar" (and be named "Baz" under the namespace "Foo.Bar").
* We prefer records over tuples, especially when being part of other type
structures.
* As a naming convention, variables with `Async<'T>` type should be suffixed
with `Job`, and variables with `Task<'T>` should be suffixed with `Task`.
* When adding NUnit tests, don't use `[<Test>]let Foo` and naked `module Bar`
syntax, but `[<Test>]member __.Foo` and `[<TestFixture>]type Bar()` (note the
parenthesis, as it's an important bit), otherwise the tests might not run in
all platforms.
* When dealing with exceptions in async{} code, we prefer normal try-with
blocks instead of using `Async.Catch`, because the latter incentivizes the
developer to use a type-less style of catching an exception, plus the
discriminated union used for its result is quite unreadable (`Choice1Of2`
and `Choice2Of2` don't give any clue about which one is the successful case
and which one is the exceptional one).


# Workflow best practices

* If you want to contribute a change to this project, you should create a
MergeRequest in GNOME's gitlab (not a PullRequest in github). This repo is:
https://gitlab.gnome.org/World/geewallet
* When contributing a MergeRequest, separate your commits in units of work
(don't mix changes that have different concerns in the same commit). Don't
forget to include all explanations and reasonings in the commit messages,
instead of just leaving them as part of the MergeRequest description.
* Push each commit separately (instead of sending more than 1 commit in a
single push), so that we can have a CI status for each commit in the MR. This
is a best practice because it will make sure that the build is not broken in
between commits (otherwise, future developers may have a hard time when
trying to bisect bugs). If you have already pushed your commits to the remote
in one push, this can be re-done by using the `scripts/gitpush1by1.fsx` script,
or this technique manually: https://stackoverflow.com/a/3230241/544947
* Git commit messages should follow this style:

```
Area/Sub-area: short title of what is changed (50 chars max)

Explanation of **why** (and maybe **how** as well, in case there's a part of
the change that is not self-explanatory). Don't hesitate to be very verbose
here, adding any references you may need, in this way[1], or even @nicknames of
people that helped. Manually crop your lines to not be longer than 80 chars.

Fixes https://gitlab.gnome.org/World/geewallet/issues/45

[1] http://foo.bar/baz
```

As you can see, the example above would be for a commit message that fixes
the issue #45. **Area** usually refers to the project name, but without the need
to include the `GWallet` prefix (for example changing the `GWallet.Backend`
project would mean you only use `Backend` as area). The **Sub-area** may refer
to a folder or module inside the area, but it's not a strict mapping.

Do not use long lines (manually crop them with EOLs because git doesn't do this
automatically).


# Dev Roadmap

Our priority list is [the Kanban view of our issue/task list](https://gitlab.gnome.org/World/geewallet/-/boards).

Some other items that haven't been prioritized include (likely only intelligible if you're already a contributor):
- Switch to use https://github.com/madelson/MedallionShell in Infra.fs (we might want to use paket instead of nuget for this, as it's friendlier to .fsx scripts, see https://cockneycoder.wordpress.com/2017/08/07/getting-started-with-paket-part-1/, or wait for https://github.com/Microsoft/visualfsharp/pull/5850).
- Study the need for ConfigureAwait(false) in the backend (or similar & easier approaches such as https://blogs.msdn.microsoft.com/benwilli/2017/02/09/an-alternative-to-configureawaitfalse-everywhere/ or https://github.com/Fody/ConfigureAwait ).
- Develop a `Maybe<'T>` type that wraps `ValueOption<'T>` type (https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/value-options) (faster to type this way) but doesn't expose the `Value` property (for safety).
- Speed improvements:
  * Frontend.XF: after clicking Next in the WelcomePage, not only start creating the private key, also query balances if privKey creation finishes before the user writes the payment password.
  * (Possibly not good -> ) If using Mode.Fast, check if there's a cached balance first. If there isn't, or the time it was cached was long ago, query the confirmed balance only (like in firstStartup, we may be already doing this in case there's no cached balance) returning `decimal*Async<bool>` (the latter to give info, later, about if there's an imminentIncomingPayment), but if it was checked very recently, just query the unconfirmed one (in this case, compare the unconfirmed balance received with the cached one: if lower, show it; if higher, show the cached one and assume imminentIncomingPayment, i.e. refresh interval being shorter).
  * (Unsure about this one -> ) Query confirmed at the same time as unconfirmed, only look at a single value of those if the one to receive earlier was the unconfirmed one.
  * Frontend.XF: start querying servers in WelcomePage, with dummy addresses, just to gather server stats.
  * First startup: bundle stats for all servers (gathered by the maintainer as a user).
  * Frontend.XF: show balances as soon as the first confirmed balance is retreived, and put an in-progress animated gif in the currency rows that are still being queried (this way you will easily tell as well which currencies have low server availability, which might push the user to turn some of them off in the settings).
  * Backend.Config.DEFAULT_NETWORK_TIMEOUT: see comment above this setting, to couple it with FaultTolerantParalellClient (or create two timeout settings, see 091b151ff4a37ca74a312609f173d5fe589ac623 ).
  * Improve stats.json feeding by 1) collecting new stats at bump.fsx time; 2) disable cancellation in non-FAST mode for this dev-env collection. 
- Use this logo for BTC when lightning support is merged: https://www.reddit.com/r/Bitcoin/comments/dklkyo/released_this_logo_for_public_use_at_lighting/
- Migrate from Nethereum to Nethermind, especially if Light Client Support is implemented: https://gitcoin.co/issue/NethermindEth/nethermind/32/3818 (https://github.com/NethermindEth/nethermind/issues/32)
