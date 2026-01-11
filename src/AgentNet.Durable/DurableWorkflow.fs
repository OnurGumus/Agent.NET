namespace AgentNet.Durable

open System
open AgentNet

/// Durable workflow extensions for WorkflowBuilder.
/// These operations require DurableTask runtime - will fail with runInProcess.
/// Users must `open AgentNet.Durable` to access these extensions.
[<AutoOpen>]
module DurableWorkflowExtensions =

    /// Type witness helper for awaitEvent.
    /// Usage: awaitEvent "ApprovalEvent" eventOf<ApprovalDecision>
    let eventOf<'T> : 'T = Unchecked.defaultof<'T>

    type WorkflowBuilder with
        /// Waits for an external event with the given name and expected type.
        /// The workflow is checkpointed and suspended until the event arrives.
        /// The received event becomes the input for the next step.
        /// Usage: awaitEvent "ApprovalEvent" eventOf<ApprovalDecision>
        /// This operation requires DurableTask runtime - will fail with runInProcess.
        [<CustomOperation("awaitEvent")>]
        member _.AwaitEvent(state: WorkflowState<'input, _>, eventName: string, _witness: 'T) : WorkflowState<'input, 'T> =
            let durableId = $"AwaitEvent_{eventName}_{typeof<'T>.Name}"
            { Steps = state.Steps @ [AwaitEvent(durableId, eventName, typeof<'T>)] }

        /// Delays the workflow for the specified duration.
        /// The workflow is checkpointed and suspended during the delay.
        /// This operation requires DurableTask runtime - will fail with runInProcess.
        [<CustomOperation("delayFor")>]
        member _.DelayFor(state: WorkflowState<'input, 'output>, duration: TimeSpan) : WorkflowState<'input, 'output> =
            let durableId = $"Delay_{int duration.TotalMilliseconds}ms"
            { Steps = state.Steps @ [Delay(durableId, duration)] }


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
            failwith "Workflow contains durable-only operations (awaitEvent, delayFor). Use DurableWorkflow.toOrchestration for durable hosting."

    // TODO: Implement toOrchestration when Microsoft.Agents.AI.DurableTask package is integrated
    // let toOrchestration (workflow: WorkflowDef<'input, 'output>) = ...
    // let registerActivities (workflow: WorkflowDef<'input, 'output>) (app: FunctionsApplicationBuilder) = ...
