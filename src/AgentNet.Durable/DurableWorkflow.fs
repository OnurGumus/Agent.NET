namespace AgentNet.Durable

open System
open AgentNet

/// Module for durable workflow extensions.
/// These operations only work when the workflow is hosted with DurableTask.
[<RequireQualifiedAccess>]
module DurableWorkflow =

    // Future: awaitEvent<'T>, delay, and other durable-only operations
    // will be added here as WorkflowBuilder extensions.

    /// Placeholder - durable extensions coming soon.
    /// For now, use Workflow.toMAF from the base AgentNet package.
    let placeholder () = ()
