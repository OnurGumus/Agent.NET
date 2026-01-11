/// Tests for durable workflow operations (awaitEvent, delay)
module AgentNet.Tests.DurableWorkflowTests

open System
open NUnit.Framework
open Swensen.Unquote
open AgentNet
open AgentNet.Durable

// Domain types for durable workflow tests
type ApprovalDecision = { Approved: bool; Reason: string }

[<Test>]
let ``awaitEvent creates AwaitEvent step in workflow``() =
    // Arrange & Act
    let durableWorkflow: WorkflowDef<string, ApprovalDecision> = workflow {
        step (fun (x: string) -> x.ToUpper() |> Task.fromResult)
        awaitEvent "ApprovalDecision"
    }

    // Assert: Workflow has 2 steps
    durableWorkflow.Steps.Length =! 2

[<Test>]
let ``delay creates Delay step in workflow``() =
    // Arrange & Act
    let durableWorkflow = workflow {
        step (fun (x: int) -> x * 2 |> Task.fromResult)
        delayFor (TimeSpan.FromHours 1.)
    }

    // Assert: Workflow has 2 steps
    durableWorkflow.Steps.Length =! 2

[<Test>]
let ``awaitEvent and delay can be combined``() =
    // Arrange & Act
    let durableWorkflow: WorkflowDef<string, ApprovalDecision> = workflow {
        step (fun (x: string) -> x |> Task.fromResult)
        delayFor (TimeSpan.FromMinutes 5.)
        awaitEvent "HumanReview"
    }

    // Assert: Workflow has 3 steps
    durableWorkflow.Steps.Length =! 3

[<Test>]
let ``runInProcess fails for workflow with awaitEvent``() =
    // Arrange
    let durableWorkflow: WorkflowDef<string, ApprovalDecision> = workflow {
        step (fun (x: string) -> x.ToUpper() |> Task.fromResult)
        awaitEvent "ApprovalDecision"
    }

    // Act & Assert - Exception is thrown during MAF compilation
    let ex = Assert.Throws<Exception>(fun () ->
        (durableWorkflow |> Workflow.runInProcess "test").GetAwaiter().GetResult() |> ignore)
    test <@ ex.Message.Contains("AwaitEvent") @>
    test <@ ex.Message.Contains("durable") || ex.Message.Contains("DurableWorkflow") @>

[<Test>]
let ``runInProcess fails for workflow with delay``() =
    // Arrange
    let durableWorkflow = workflow {
        step (fun (x: int) -> x * 2 |> Task.fromResult)
        delayFor (TimeSpan.FromSeconds 30.)
    }

    // Act & Assert - Exception is thrown during MAF compilation
    let ex = Assert.Throws<Exception>(fun () ->
        (durableWorkflow |> Workflow.runInProcess 5).GetAwaiter().GetResult() |> ignore)
    test <@ ex.Message.Contains("Delay") @>
    test <@ ex.Message.Contains("durable") || ex.Message.Contains("DurableWorkflow") @>

[<Test>]
let ``containsDurableOperations returns true for workflow with awaitEvent``() =
    // Arrange
    let durableWorkflow: WorkflowDef<string, string> = workflow {
        step (fun (x: string) -> x |> Task.fromResult)
        awaitEvent "TestEvent"
    }

    // Act & Assert
    DurableWorkflow.containsDurableOperations durableWorkflow =! true

[<Test>]
let ``containsDurableOperations returns true for workflow with delay``() =
    // Arrange
    let durableWorkflow = workflow {
        step (fun (x: int) -> x |> Task.fromResult)
        delayFor (TimeSpan.FromMinutes 1.)
    }

    // Act & Assert
    DurableWorkflow.containsDurableOperations durableWorkflow =! true

[<Test>]
let ``containsDurableOperations returns false for workflow without durable ops``() =
    // Arrange
    let normalWorkflow = workflow {
        step (fun (x: int) -> x * 2 |> Task.fromResult)
        step (fun (x: int) -> x + 1 |> Task.fromResult)
    }

    // Act & Assert
    DurableWorkflow.containsDurableOperations normalWorkflow =! false

[<Test>]
let ``containsDurableOperations detects durable ops in workflow``() =
    // Arrange
    let durableWorkflow: WorkflowDef<string, string> = workflow {
        step (fun (x: string) -> x |> Task.fromResult)
        awaitEvent "Event"
    }

    // Act & Assert
    DurableWorkflow.containsDurableOperations durableWorkflow =! true

[<Test>]
let ``validateForInProcess throws for durable workflow``() =
    // Arrange
    let durableWorkflow: WorkflowDef<string, string> = workflow {
        step (fun (x: string) -> x |> Task.fromResult)
        awaitEvent "Event"
    }

    // Act & Assert
    let ex = Assert.Throws<Exception>(fun () ->
        DurableWorkflow.validateForInProcess durableWorkflow)
    test <@ ex.Message.Contains("durable") @>

[<Test>]
let ``validateForInProcess succeeds for non-durable workflow``() =
    // Arrange
    let normalWorkflow = workflow {
        step (fun (x: int) -> x * 2 |> Task.fromResult)
    }

    // Act & Assert - should not throw
    DurableWorkflow.validateForInProcess normalWorkflow

[<Test>]
let ``Resilience ops work fine without durable ops via runInProcess``() =
    // Arrange: Workflow with resilience but no durable ops
    let mutable attempts = 0
    let unreliable (x: int) =
        attempts <- attempts + 1
        if attempts < 2 then failwith "Fail"
        x * 2 |> Task.fromResult

    let resilientWorkflow = workflow {
        step unreliable
        retry 3
    }

    // Act
    let result = (resilientWorkflow |> Workflow.runInProcess 5).GetAwaiter().GetResult()

    // Assert
    result =! 10
    DurableWorkflow.containsDurableOperations resilientWorkflow =! false
