#!/usr/bin/env fsharpi

// https://github.com/fsharp/fsharp/issues/740
let f a =
    match a with
    | [] ->
        true
    | _ ->
        true
