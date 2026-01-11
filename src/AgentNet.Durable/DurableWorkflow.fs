namespace AgentNet.Durable

open System
open AgentNet

/// Functions for compiling and running durable workflows.
/// These require Azure Durable Functions / DurableTask runtime.
[<RequireQualifiedAccess>]
module DurableWorkflow =

    /// Checks if a workflow contains durable-only operations.
    let containsDurableOperations (workflow: WorkflowDef<'input, 'output>) : bool =
        let rec hasDurableOps step =
            match step with
            | AwaitEvent _ -> true
            | Delay _ -> true
            | WithRetry (inner, _) -> hasDurableOps inner
            | WithTimeout (inner, _) -> hasDurableOps inner
            | WithFallback (inner, _, _) -> hasDurableOps inner
            | _ -> false
        workflow.Steps |> List.exists hasDurableOps

    /// Validates that a workflow can run in-process (no durable-only operations).
    /// Throws if durable-only operations are detected.
    let validateForInProcess (workflow: WorkflowDef<'input, 'output>) : unit =
        if containsDurableOperations workflow then
            failwith "Workflow contains durable-only operations (awaitEvent, delay). Use DurableWorkflow.toOrchestration for durable hosting."

    // TODO: Implement toOrchestration when Microsoft.Agents.AI.DurableTask package is integrated
    // let toOrchestration (workflow: WorkflowDef<'input, 'output>) = ...
    // let registerActivities (workflow: WorkflowDef<'input, 'output>) (app: FunctionsApplicationBuilder) = ...
