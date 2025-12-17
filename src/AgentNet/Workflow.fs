namespace AgentNet

open System

/// Context passed to executors during workflow execution
type WorkflowContext = {
    /// Unique identifier for this workflow run
    RunId: Guid
    /// Shared state dictionary for passing data between executors
    State: Map<string, obj>
}

module WorkflowContext =
    /// Creates a new empty workflow context
    let create () = {
        RunId = Guid.NewGuid()
        State = Map.empty
    }

    /// Gets a typed value from the context state
    let tryGet<'T> (key: string) (ctx: WorkflowContext) : 'T option =
        ctx.State
        |> Map.tryFind key
        |> Option.bind (fun v ->
            match v with
            | :? 'T as typed -> Some typed
            | _ -> None)

    /// Sets a value in the context state
    let set (key: string) (value: obj) (ctx: WorkflowContext) : WorkflowContext =
        { ctx with State = ctx.State |> Map.add key value }


/// An executor that transforms input to output within a workflow
type Executor<'input, 'output> = {
    Name: string
    Execute: 'input -> WorkflowContext -> Async<'output>
}

/// Module for creating executors
[<RequireQualifiedAccess>]
module Executor =

    /// Creates an executor from a simple function
    let fromFn (name: string) (fn: 'input -> 'output) : Executor<'input, 'output> =
        {
            Name = name
            Execute = fun input _ -> async { return fn input }
        }

    /// Creates an executor from an async function
    let fromAsync (name: string) (fn: 'input -> Async<'output>) : Executor<'input, 'output> =
        {
            Name = name
            Execute = fun input _ -> fn input
        }

    /// Creates an executor from a function that takes context
    let create (name: string) (fn: 'input -> WorkflowContext -> Async<'output>) : Executor<'input, 'output> =
        {
            Name = name
            Execute = fn
        }

    /// Creates an executor from an AgentNet Agent
    let fromAgent (name: string) (agent: Agent) : Executor<string, string> =
        {
            Name = name
            Execute = fun input _ -> agent.Chat input
        }


/// A step in a workflow pipeline
type WorkflowStep =
    | Step of name: string * execute: (obj -> WorkflowContext -> Async<obj>)
    | Route of router: (obj -> WorkflowContext -> Async<obj>)
    | Parallel of executors: (obj -> WorkflowContext -> Async<obj>) list


/// A workflow definition that can be executed
type WorkflowDef<'input, 'output> = {
    /// The steps in the workflow, in order
    Steps: WorkflowStep list
}


/// Internal module for workflow building
module internal WorkflowInternal =

    /// Wraps a typed executor as an untyped step
    let wrapExecutor<'i, 'o> (exec: Executor<'i, 'o>) : WorkflowStep =
        Step (exec.Name, fun input ctx -> async {
            let typedInput = input :?> 'i
            let! result = exec.Execute typedInput ctx
            return result :> obj
        })

    /// Wraps a typed router function as an untyped route step
    /// The router takes input and returns an executor to run on that same input
    let wrapRouter<'a, 'b> (router: 'a -> Executor<'a, 'b>) : WorkflowStep =
        Route (fun input ctx -> async {
            let typedInput = input :?> 'a
            let selectedExecutor = router typedInput
            let! result = selectedExecutor.Execute typedInput ctx
            return result :> obj
        })

    /// Wraps a list of typed executors as untyped parallel functions
    let wrapParallel<'i, 'o> (executors: Executor<'i, 'o> list) : WorkflowStep =
        let wrappedFns =
            executors
            |> List.map (fun exec ->
                fun (input: obj) (ctx: WorkflowContext) -> async {
                    let typedInput = input :?> 'i
                    let! result = exec.Execute typedInput ctx
                    return result :> obj
                })
        Parallel wrappedFns

    /// Wraps an aggregator executor, handling the obj list → typed list conversion
    let wrapGather<'elem, 'o> (exec: Executor<'elem list, 'o>) : WorkflowStep =
        Step (exec.Name, fun input ctx -> async {
            // Input is obj list from parallel, convert each element to the expected type
            let objList = input :?> obj list
            let typedList = objList |> List.map (fun o -> o :?> 'elem)
            let! result = exec.Execute typedList ctx
            return result :> obj
        })


/// Builder for the workflow computation expression
type WorkflowBuilder() =

    member _.Yield(_) : WorkflowStep list = []

    /// Starts the workflow with an executor
    [<CustomOperation("start")>]
    member _.Start<'i, 'o>(steps: WorkflowStep list, executor: Executor<'i, 'o>) : WorkflowStep list =
        steps @ [WorkflowInternal.wrapExecutor executor]

    /// Adds the next step to the workflow
    [<CustomOperation("next")>]
    member _.Next<'i, 'o>(steps: WorkflowStep list, executor: Executor<'i, 'o>) : WorkflowStep list =
        steps @ [WorkflowInternal.wrapExecutor executor]

    /// Routes to different executors based on the previous step's output
    /// Use with pattern matching: route (function | CaseA -> exec1 | CaseB -> exec2)
    [<CustomOperation("route")>]
    member _.Route<'a, 'b>(steps: WorkflowStep list, router: 'a -> Executor<'a, 'b>) : WorkflowStep list =
        steps @ [WorkflowInternal.wrapRouter router]

    /// Runs multiple executors in parallel on the same input (fan-out)
    /// Output becomes a list of all results
    [<CustomOperation("scatter")>]
    member _.Scatter<'i, 'o>(steps: WorkflowStep list, executors: Executor<'i, 'o> list) : WorkflowStep list =
        steps @ [WorkflowInternal.wrapParallel executors]

    /// Aggregates parallel results into a single output (fan-in)
    /// Converts the obj list from scatter into a typed list for the executor
    [<CustomOperation("gather")>]
    member _.Gather<'elem, 'o>(steps: WorkflowStep list, executor: Executor<'elem list, 'o>) : WorkflowStep list =
        steps @ [WorkflowInternal.wrapGather executor]

    /// Builds the final workflow definition
    member _.Run(steps: WorkflowStep list) : WorkflowDef<'input, 'output> =
        { Steps = steps }


/// The workflow computation expression builder instance
[<AutoOpen>]
module WorkflowCE =
    let workflow = WorkflowBuilder()


/// Functions for executing workflows
[<RequireQualifiedAccess>]
module Workflow =

    /// Runs a workflow with the given input
    let run<'input, 'output> (input: 'input) (workflow: WorkflowDef<'input, 'output>) : Async<'output> =
        async {
            let ctx = WorkflowContext.create ()
            let mutable current: obj = input :> obj

            for step in workflow.Steps do
                match step with
                | Step (_, execute) ->
                    let! result = execute current ctx
                    current <- result
                | Route router ->
                    let! result = router current ctx
                    current <- result
                | Parallel executors ->
                    // Run all executors concurrently with the same input
                    let! results =
                        executors
                        |> List.map (fun exec -> exec current ctx)
                        |> Async.Parallel
                    // Output is a list of all results
                    current <- (results |> Array.toList) :> obj

            return current :?> 'output
        }

    /// Runs a workflow synchronously
    let runSync<'input, 'output> (input: 'input) (workflow: WorkflowDef<'input, 'output>) : 'output =
        workflow |> run input |> Async.RunSynchronously
