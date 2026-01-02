module AgentNet.Tests.ToolTests

open NUnit.Framework
open Swensen.Unquote
open AgentNet

// Test functions
let greet (name: string) : string =
    $"Hello, {name}!"

let add (x: int) (y: int) : int =
    x + y

let formatPrice (symbol: string) (price: decimal) (currency: string) : string =
    $"{symbol}: {price} {currency}"

[<Test>]
let ``Tool.create extracts function name from quotation`` () =
    let tool = Tool.create <@ greet @>
    tool.Name =! "greet"

[<Test>]
let ``Tool.create extracts MethodInfo from quotation`` () =
    let tool = Tool.create <@ greet @>
    tool.MethodInfo.Name =! "greet"

[<Test>]
let ``Tool.create works with curried functions`` () =
    let tool = Tool.create <@ add @>
    tool.Name =! "add"
    tool.MethodInfo.GetParameters().Length =! 2

[<Test>]
let ``Tool.create works with three parameter functions`` () =
    let tool = Tool.create <@ formatPrice @>
    tool.Name =! "formatPrice"
    tool.MethodInfo.GetParameters().Length =! 3

[<Test>]
let ``MethodInfo parameter names are preserved`` () =
    let tool = Tool.create <@ formatPrice @>
    let paramNames = tool.MethodInfo.GetParameters() |> Array.map (fun p -> p.Name)
    paramNames =! [| "symbol"; "price"; "currency" |]

[<Test>]
let ``Tool.describe sets description`` () =
    let tool =
        Tool.create <@ greet @>
        |> Tool.describe "Greets a person by name"
    tool.Description =! "Greets a person by name"

[<Test>]
let ``Tool.create sets empty description by default`` () =
    let tool = Tool.create <@ greet @>
    tool.Description =! ""
