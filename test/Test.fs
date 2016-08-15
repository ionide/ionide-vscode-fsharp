module Test

open NUnit.Framework
open FsUnit

[<Test>]
let test () =
    42 |> should equal 42