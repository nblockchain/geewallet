# Dev Roadmap

Our priority list is [the Kanban view of our issue/task list](https://gitlab.com/nblockchain/geewallet/boards).

Some other items that haven't been prioritized include (likely only intelligible if you're already a contributor):
- Study the need for ConfigureAwait(false) in the backend (or similar & easier approaches such as https://blogs.msdn.microsoft.com/benwilli/2017/02/09/an-alternative-to-configureawaitfalse-everywhere/ or https://github.com/Fody/ConfigureAwait ).
- Develop a `Maybe<'T>` type that wraps `ValueOption<'T>` type (https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/value-options) (faster to type this way) but doesn't expose the `Value` property (for safety).
- Speed improvements:
  * Frontend.XF: after clicking Next in the WelcomePage, not only start creating the private key, also query balances if privKey creation finishes before the user writes the payment password.
  * (Possibly not good -> ) If using Mode.Fast, check if there's a cached balance first. If there isn't, or the time it was cached was long ago, query the confirmed balance only (like in firstStartup, we may be already doing this in case there's no cached balance) returning `decimal*Async<bool>` (the latter to give info, later, about if there's an imminentIncomingPayment), but if it was checked very recently, just query the unconfirmed one (in this case, compare the unconfirmed balance received with the cached one: if lower, show it; if higher, show the cached one and assume imminentIncomingPayment, i.e. refresh interval being shorter).
  * (Unsure about this one -> ) Query confirmed at the same time as unconfirmed, only look at a single value of those if the one to receive earlier was the unconfirmed one.
  * Frontend.XF: start querying servers in WelcomePage, with dummy addresses, just to gather server stats.
  * Frontend.XF: show balances as soon as the first confirmed balance is retrieved, and put an in-progress animated gif in the currency rows that are still being queried (this way you will easily tell as well which currencies have low server availability, which might push the user to turn some of them off in the settings).
  * Backend.Config.DEFAULT_NETWORK_TIMEOUT: see comment above this setting, to couple it with FaultTolerantParalellClient (or create two timeout settings, see 091b151ff4a37ca74a312609f173d5fe589ac623 ).
  * Improve stats.json feeding by 1) collecting new stats at bump.fsx time; 2) disable cancellation in non-FAST mode for this dev-env collection. 
- Use this logo for BTC when lightning support is merged: https://www.reddit.com/r/Bitcoin/comments/dklkyo/released_this_logo_for_public_use_at_lighting/
- Migrate from Nethereum to Nethermind, especially if Light Client Support is implemented: https://gitcoin.co/issue/NethermindEth/nethermind/32/3818 (https://github.com/NethermindEth/nethermind/issues/32)
- Stop using Newtonsoft.Json, in favour of an alternative that doesn't need type converters. Possible options (note: don't suggest to put Chiron in this list because its approach is too manual, so it defeats the point of fleeing from NewtonsoftJson's type converters):
  * https://github.com/Tarmil/FSharp.SystemTextJson
  * https://github.com/realvictorprm/FSharpCompileTimeJson
  * https://github.com/mausch/Fleece
  * https://www.nuget.org/packages/Thoth.Json.Net
  * https://vsapronov.github.io/FSharp.Json/
  * https://github.com/Microsoft/fsharplu/tree/master/FSharpLu.Json
  * https://github.com/stroiman/JsonFSharp
- Paranoid-build mode: instead of binary nuget deps, use either git submodules, or [local nugets](https://github.com/mono/mono-addins/issues/73#issuecomment-389343246) (maybe depending on [this RFE](https://github.com/dotnet/sdk/issues/1151) or any workaround mentioned there, or a config file like it's explained here: https://docs.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior), or [source-only nugets](https://medium.com/@attilah/source-code-only-nuget-packages-8f34a8fb4738).
- Maybe replace JsonRpcSharp with https://www.nuget.org/packages/StreamJsonRpc .
