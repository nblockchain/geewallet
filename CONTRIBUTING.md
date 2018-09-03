It is assumed that by contributing to this repository in the form of
PullRequests/MergeRequests, you grant the intellectual property of your
contribution under the terms of the MIT licence.
If you don't wish to comply with this policy, you can keep a fork in your
gitlab/github account.

Below you can find a guide for our F# coding style:

* We use PascalCase for namespaces, types, methods, properties and record
members, but camelCase for parameters, private fields, and local functions.
* When writing non-static type members we prefer to use the word `self` or
`this`.
* We follow the same convention of C# to prefix interfaces with the letter 'I'.
* Given that we use the C#ish style of PascalCase for type names (instead of
camelCase), then it only makes sense to try to use the type names which start
with uppercase, instead of the camelCased F# types (e.g. use `Option` and `List`
instead of `option` and `list`). The only exception to this rule is: primitive
types (where we prefer `string` and `int` over `String` and `Int32` unless we're
using a static method of them).
* We prefer the generic notation `List<Foo>` rather than `Foo list`.
* We prefer to write parenthesis only when strictly necessary (e.g. in F# they
are not required for `if` clauses, unlike C#) or for readability purposes (e.g.
when it's not clear what operator would be applied first by the order preference
rules of the language).
* Whenever possible, we prefer to use currified arguments (instead of tuples),
should we need to use F# partial application.
* We avoid to write the keyword `new` for instances of non-IDisposable types.
* When dealing with `Option<Foo>` elements, we consider it's much safer to use
`match` patterns instead of using the properties `IsSome`, `IsNone` or `Value`.
* In case of doubt, we prefer to expliticly add the accessibility keywords
(`private`, `public`, `internal`...), should the F# language allow it.
* With `if` blocks we prefer to put the `then` keyword in the same line as the
`if`, and the `else` or `elif` keywords indented to be aligned with the `if`.
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
* If you want to contribute a change to this project, you should create a
MergeRequest in gitlab (not a PullRequest in github). The repo in gitlab is in:
https://gitlab.com/knocte/gwallet
* When contributing a MergeRequest, separate your commits in units of work
(don't mix changes that have different concerns in the same commit). Don't
forget to include all explanations and reasonings in the commit messages,
instead of just leaving them as part of the MergeRequest description.
* Git commit messages should follow this style:
```
Area/Sub-area: short title of what is changed (50 chars max)

Explanation of **why** this is changed. Don't hesitate to be very verbose here,
adding any references you may need, in this way[1], or even @nicknames of people
that helped. Manually crop your lines to not be longer than 80 chararacters.

[1] http://foo.bar/baz
```
**Area** usually refers to the project name, but not including the GWallet
prefix (for example changing the GWallet.Backend project would mean you only use
"Backend" as area). The **Sub-area** may refer to a folder or module inside the
area, but it's not a strict mapping.

Do not use long lines (manually crop them with EOLs because git doesn't do this
automatically).
