/// Tests for parallel fan-out/fan-in workflows
/// Based on the parallel examples from docs/WorkflowDSL-Design.md
module AgentNet.Tests.ParallelWorkflowTests

open NUnit.Framework
open Swensen.Unquote
open AgentNet

// Domain types for parallel tests
type StockData = { Symbol: string; Price: float }

type AnalystReport = { Analyst: string; Rating: string; Score: int }

type Summary = { Reports: AnalystReport list; Consensus: string; AverageScore: float }

type DataPacket = { Id: int; Value: string }

[<Test>]
let ``FanOut executes all executors and FanIn aggregates results``() =
    // Arrange
    let loadData (symbol: string) = { Symbol = symbol; Price = 150.0 } |> toTask
    let technicalAnalyst (data: StockData) = 
        { Analyst = "Technical"; Rating = "Buy"; Score = 8 } |> toTask

    let fundamentalAnalyst (data: StockData) = 
        { Analyst = "Fundamental"; Rating = "Hold"; Score = 6 } |> toTask   

    let sentimentAnalyst (data: StockData) = 
        { Analyst = "Sentiment"; Rating = "Buy"; Score = 7 } |> toTask

    let summarize (reports: AnalystReport list) = //Executor.fromFn "Summarize" (fun (reports: AnalystReport list) ->
        let avgScore = reports |> List.averageBy (fun r -> float r.Score)
        let consensus = if avgScore >= 7.0 then "Buy" else "Hold"
        { Reports = reports; Consensus = consensus; AverageScore = avgScore }
        |> toTask

    let parallelWorkflow = workflow {
        start loadData
        fanOut [ s technicalAnalyst; s fundamentalAnalyst; s sentimentAnalyst ]
        fanIn (summarize)
    }

    // Act
    let result = Workflow.runSync "AAPL" parallelWorkflow

    // Assert
    result.Reports.Length =! 3
    result.Reports |> List.exists (fun r -> r.Analyst = "Technical") =! true
    result.Reports |> List.exists (fun r -> r.Analyst = "Fundamental") =! true
    result.Reports |> List.exists (fun r -> r.Analyst = "Sentiment") =! true
    result.Consensus =! "Buy"
    result.AverageScore =! 7.0

[<Test>]
let ``FanOut with two executors``() =
    // Arrange
    let prepare = Executor.fromFn "Prepare" (fun (x: int) -> x * 2)
    let addTen = Executor.fromFn "AddTen" (fun (x: int) -> x + 10)
    let multiplyThree = Executor.fromFn "MultiplyThree" (fun (x: int) -> x * 3)
    let combine = Executor.fromFn "Combine" (fun (results: int list) -> results |> List.sum)

    let parallelWorkflow = workflow {
        start prepare
        fanOut [ s addTen; s multiplyThree ]
        fanIn combine
    }

    // Act: input 5 -> prepare: 10 -> fanOut: [20, 30] -> combine: 50
    let result = Workflow.runSync 5 parallelWorkflow

    // Assert
    result =! 50

[<Test>]
let ``FanOut preserves order of results``() =
    // Arrange: Create executors that tag their output with index
    let identity = Executor.fromFn "Identity" id

    let tag0 = Executor.fromFn "Tag0" (fun (s: string) -> $"0:{s}")
    let tag1 = Executor.fromFn "Tag1" (fun (s: string) -> $"1:{s}")
    let tag2 = Executor.fromFn "Tag2" (fun (s: string) -> $"2:{s}")

    let join = Executor.fromFn "Join" (fun (results: string list) ->
        String.concat "," results)

    let parallelWorkflow = workflow {
        start identity
        fanOut [ s tag0; s tag1; s tag2 ]
        fanIn join
    }

    // Act
    let result = Workflow.runSync "X" parallelWorkflow

    // Assert: Results should be in executor order
    result =! "0:X,1:X,2:X"

[<Test>]
let ``FanOut followed by additional processing``() =
    // Arrange
    let init = Executor.fromFn "Init" (fun (n: int) -> n)

    let double = Executor.fromFn "Double" (fun (n: int) -> n * 2)
    let triple = Executor.fromFn "Triple" (fun (n: int) -> n * 3)

    let sum = Executor.fromFn "Sum" (fun (nums: int list) -> List.sum nums)

    let format = Executor.fromFn "Format" (fun (total: int) -> $"Total: {total}")

    let parallelWorkflow = workflow {
        start init
        fanOut [ s double; s triple ]
        fanIn sum
        next format
    }

    // Act: 10 -> fanOut: [20, 30] -> sum: 50 -> format: "Total: 50"
    let result = Workflow.runSync 10 parallelWorkflow

    // Assert
    result =! "Total: 50"

[<Test>]
let ``FanOut with custom record types``() =
    // Arrange
    let createPackets = Executor.fromFn "CreatePackets" (fun (prefix: string) ->
        { Id = 1; Value = prefix })

    let processA = Executor.fromFn "ProcessA" (fun (p: DataPacket) ->
        { p with Value = p.Value + "-A" })
    let processB = Executor.fromFn "ProcessB" (fun (p: DataPacket) ->
        { p with Value = p.Value + "-B" })

    let merge = Executor.fromFn "Merge" (fun (packets: DataPacket list) ->
        packets |> List.map (fun p -> p.Value) |> String.concat "|")

    let parallelWorkflow = workflow {
        start createPackets
        fanOut [ s processA; s processB ]
        fanIn merge
    }

    // Act
    let result = Workflow.runSync "DATA" parallelWorkflow

    // Assert
    result =! "DATA-A|DATA-B"
