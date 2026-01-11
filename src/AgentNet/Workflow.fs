namespace AgentNet

open System
open System.Threading.Tasks
open AgentNet.Interop

// Type aliases to avoid conflicts between AgentNet and MAF
// NOTE: Don't open Microsoft.Agents.AI.Workflows to avoid Executor<,> conflict
type MAFExecutor = Microsoft.Agents.AI.Workflows.Executor
type MAFWorkflow = Microsoft.Agents.AI.Workflows.Workflow
type MAFWorkflowBuilder = Microsoft.Agents.AI.Workflows.WorkflowBuilder
type MAFInProcessExecution = Microsoft.Agents.AI.Workflows.InProcessExecution
type MAFExecutorCompletedEvent = Microsoft.Agents.AI.Workflows.ExecutorCompletedEvent

/// A step in a workflow pipeline (untyped, internal)
type WorkflowStep =
    | Step of durableId: string * name: string * execute: (obj -> WorkflowContext -> Task<obj>)
    | Route of durableId: string * router: (obj -> WorkflowContext -> Task<obj>)
    | Parallel of branches: (string * (obj -> WorkflowContext -> Task<obj>)) list  // (durableId, executor) pairs


/// A workflow step type that unifies Task functions, Async functions, TypedAgents, Executors, and nested Workflows.
/// This enables clean workflow syntax and mixed-type fanOut operations.
type Step<'i, 'o> =
    | TaskStep of ('i -> Task<'o>)
    | AsyncStep of ('i -> Async<'o>)
    | AgentStep of TypedAgent<'i, 'o>
    | ExecutorStep of Executor<'i, 'o>
    | NestedWorkflow of WorkflowDef<'i, 'o>

/// A workflow definition that can be executed
and WorkflowDef<'input, 'output> = {
    /// Optional name for the workflow (required for MAF compilation)
    Name: string option
    /// The steps in the workflow, in order
    Steps: WorkflowStep list
}


/// SRTP witness type for converting various types to Step.
/// Uses the type class pattern to enable inline resolution at call sites.
type StepConv = StepConv with
    static member inline ToStep(_: StepConv, fn: 'i -> Task<'o>) : Step<'i, 'o> = TaskStep fn
    static member inline ToStep(_: StepConv, fn: 'i -> Async<'o>) : Step<'i, 'o> = AsyncStep fn
    static member inline ToStep(_: StepConv, agent: TypedAgent<'i, 'o>) : Step<'i, 'o> = AgentStep agent
    static member inline ToStep(_: StepConv, exec: Executor<'i, 'o>) : Step<'i, 'o> = ExecutorStep exec
    static member inline ToStep(_: StepConv, step: Step<'i, 'o>) : Step<'i, 'o> = step  // Passthrough
    static member inline ToStep(_: StepConv, wf: WorkflowDef<'i, 'o>) : Step<'i, 'o> = NestedWorkflow wf


/// Internal state carrier that threads type information through the builder
type WorkflowState<'input, 'output> = {
    Steps: WorkflowStep list
}


/// Module for workflow building internals (public for inline SRTP support)
[<AutoOpen>]
module WorkflowInternal =

    /// Executes a single workflow step (used by nested workflows)
    let rec executeStep (step: WorkflowStep) (input: obj) (ctx: WorkflowContext) : Task<obj> =
        task {
            match step with
            | Step (_, _, execute) ->
                return! execute input ctx
            | Route (_, router) ->
                return! router input ctx
            | Parallel branches ->
                let! results =
                    branches
                    |> List.map (fun (_, exec) -> exec input ctx)
                    |> Task.WhenAll
                return (results |> Array.toList) :> obj
        }

    /// Generates a stable durable ID from a Step (always auto-generated, never from Executor.Name)
    let getDurableId<'i, 'o> (step: Step<'i, 'o>) : string =
        match step with
        | TaskStep fn -> DurableId.forStep fn
        | AsyncStep fn -> DurableId.forStep fn
        | AgentStep agent -> DurableId.forAgent agent
        | ExecutorStep exec -> DurableId.forExecutor exec
        | NestedWorkflow _ -> DurableId.forWorkflow<'i, 'o> ()

    /// Gets the display name for a Step (Executor.Name if available, otherwise uses function name)
    let getDisplayName<'i, 'o> (step: Step<'i, 'o>) : string =
        match step with
        | ExecutorStep exec -> exec.Name  // Use explicit display name from Executor
        | TaskStep fn -> DurableId.getDisplayName fn  // Use function name
        | AsyncStep fn -> DurableId.getDisplayName fn  // Use function name
        | AgentStep _ -> "Agent"  // TypedAgent doesn't have a name property
        | NestedWorkflow _ -> "Workflow"  // Nested workflow

    /// Checks if a Step uses a lambda and logs a warning
    let warnIfLambda<'i, 'o> (step: Step<'i, 'o>) (id: string) : unit =
        match step with
        | TaskStep fn -> DurableId.warnIfLambda fn id
        | AsyncStep fn -> DurableId.warnIfLambda fn id
        | _ -> ()

    /// Converts a Step<'i, 'o> to an Executor<'i, 'o>
    /// Note: NestedWorkflow is handled separately in wrapStep
    let stepToExecutor<'i, 'o> (name: string) (step: Step<'i, 'o>) : Executor<'i, 'o> =
        match step with
        | TaskStep fn -> Executor.fromTask name fn
        | AsyncStep fn -> Executor.fromAsync name fn
        | AgentStep agent -> Executor.fromTypedAgent name agent
        | ExecutorStep exec -> { exec with Name = name }
        | NestedWorkflow _ -> failwith "NestedWorkflow should be handled in wrapStep, not stepToExecutor"

    /// Wraps a Step as an untyped WorkflowStep
    let wrapStep<'i, 'o> (durableId: string) (name: string) (step: Step<'i, 'o>) : WorkflowStep =
        match step with
        | NestedWorkflow wf ->
            // Execute all nested workflow steps in sequence
            Step (durableId, name, fun input ctx -> task {
                let mutable current = input
                for nestedStep in wf.Steps do
                    let! result = executeStep nestedStep current ctx
                    current <- result
                return current
            })
        | _ ->
            let exec = stepToExecutor name step
            Step (durableId, exec.Name, fun input ctx -> task {
                let typedInput = input :?> 'i
                let! result = exec.Execute typedInput ctx
                return result :> obj
            })

    /// Wraps a list of Steps as parallel execution (supports mixed types!)
    /// Uses auto-generated durable IDs for each parallel branch
    let wrapStepParallel<'i, 'o> (steps: Step<'i, 'o> list) : WorkflowStep =
        let branches =
            steps
            |> List.mapi (fun i step ->
                let durableId = getDurableId step
                let displayName = getDisplayName step
                let exec = stepToExecutor displayName step
                warnIfLambda step durableId
                let executor = fun (input: obj) (ctx: WorkflowContext) -> task {
                    let typedInput = input :?> 'i
                    let! result = exec.Execute typedInput ctx
                    return result :> obj
                }
                (durableId, executor))
        Parallel branches

    /// Wraps a Step as a fan-in aggregator, handling obj list → typed list conversion
    let wrapStepFanIn<'elem, 'o> (durableId: string) (name: string) (step: Step<'elem list, 'o>) : WorkflowStep =
        let exec = stepToExecutor name step
        Step (durableId, exec.Name, fun input ctx -> task {
            let objList = input :?> obj list
            let typedList = objList |> List.map (fun o -> o :?> 'elem)
            let! result = exec.Execute typedList ctx
            return result :> obj
        })

    /// Wraps a typed executor as an untyped step
    let wrapExecutor<'i, 'o> (durableId: string) (exec: Executor<'i, 'o>) : WorkflowStep =
        Step (durableId, exec.Name, fun input ctx -> task {
            let typedInput = input :?> 'i
            let! result = exec.Execute typedInput ctx
            return result :> obj
        })

    /// Wraps a typed router function as an untyped route step
    /// The router takes input and returns an executor to run on that same input
    let wrapRouter<'a, 'b> (durableId: string) (router: 'a -> Executor<'a, 'b>) : WorkflowStep =
        Route (durableId, fun input ctx -> task {
            let typedInput = input :?> 'a
            let selectedExecutor = router typedInput
            let! result = selectedExecutor.Execute typedInput ctx
            return result :> obj
        })

    /// Wraps a list of typed executors as untyped parallel functions
    let wrapParallel<'i, 'o> (executors: Executor<'i, 'o> list) : WorkflowStep =
        let branches =
            executors
            |> List.map (fun exec ->
                let durableId = DurableId.forExecutor exec
                let executor = fun (input: obj) (ctx: WorkflowContext) -> task {
                    let typedInput = input :?> 'i
                    let! result = exec.Execute typedInput ctx
                    return result :> obj
                }
                (durableId, executor))
        Parallel branches

    /// Wraps an aggregator executor, handling the obj list → typed list conversion
    let wrapFanIn<'elem, 'o> (exec: Executor<'elem list, 'o>) : WorkflowStep =
        let durableId = DurableId.forExecutor exec
        Step (durableId, exec.Name, fun input ctx -> task {
            // Input is obj list from parallel, convert each element to the expected type
            let objList = input :?> obj list
            let typedList = objList |> List.map (fun o -> o :?> 'elem)
            let! result = exec.Execute typedList ctx
            return result :> obj
        })


/// Builder for the workflow computation expression
type WorkflowBuilder() =

    member _.Yield(_) : WorkflowState<'a, 'a> = { Steps = [] }

    // ============ STEP OPERATIONS ============
    // Uses inline SRTP to accept Task fn, Async fn, TypedAgent, Executor, Workflow, or Step directly
    // Durable IDs are auto-generated; display names come from Executor.Name if available

    /// Adds first step - establishes workflow input/output types (uses SRTP for type resolution)
    [<CustomOperation("step")>]
    member inline _.StepFirst(state: WorkflowState<_, _>, x: ^T) : WorkflowState<'i, 'o> =
        let step : Step<'i, 'o> = ((^T or StepConv) : (static member ToStep: StepConv * ^T -> Step<'i, 'o>) (StepConv, x))
        let durableId = WorkflowInternal.getDurableId step
        let displayName = WorkflowInternal.getDisplayName step
        WorkflowInternal.warnIfLambda step durableId
        { Steps = state.Steps @ [WorkflowInternal.wrapStep durableId displayName step] }

    /// Adds subsequent step - threads input type through (uses SRTP for type resolution)
    [<CustomOperation("step")>]
    member inline _.Step(state: WorkflowState<'input, 'middle>, x: ^T) : WorkflowState<'input, 'output> =
        let step : Step<'middle, 'output> = ((^T or StepConv) : (static member ToStep: StepConv * ^T -> Step<'middle, 'output>) (StepConv, x))
        let durableId = WorkflowInternal.getDurableId step
        let displayName = WorkflowInternal.getDisplayName step
        WorkflowInternal.warnIfLambda step durableId
        { Steps = state.Steps @ [WorkflowInternal.wrapStep durableId displayName step] }

    // ============ ROUTING ============

    /// Routes to different executors based on the previous step's output.
    /// Uses SRTP to accept Task fn, Async fn, TypedAgent, Executor, or Step directly.
    /// Durable IDs are auto-generated for each branch.
    [<CustomOperation("route")>]
    member inline _.Route(state: WorkflowState<'input, 'middle>, router: 'middle -> ^T) : WorkflowState<'input, 'output> =
        // Generate a durable ID for the route decision point based on input/output types
        let routeDurableId = DurableId.forRoute router
        let wrappedRouter = fun (input: 'middle) ->
            let result = router input
            let step : Step<'middle, 'output> =
                ((^T or StepConv) : (static member ToStep: StepConv * ^T -> Step<'middle, 'output>) (StepConv, result))
            let displayName = WorkflowInternal.getDisplayName step
            WorkflowInternal.warnIfLambda step (WorkflowInternal.getDurableId step)
            WorkflowInternal.stepToExecutor displayName step
        { Steps = state.Steps @ [WorkflowInternal.wrapRouter routeDurableId wrappedRouter] }

    // ============ FANOUT OPERATIONS ============
    // SRTP overloads for 2-5 arguments - no wrapper needed!
    // For 6+ branches, use: fanOut [+fn1; +fn2; ...]
    // Durable IDs are auto-generated for each parallel branch

    /// Runs 2 steps in parallel (fan-out) - SRTP resolves each argument type
    [<CustomOperation("fanOut")>]
    member inline _.FanOut(state: WorkflowState<'input, 'middle>, x1: ^A, x2: ^B) : WorkflowState<'input, 'o list> =
        let s1 : Step<'middle, 'o> = ((^A or StepConv) : (static member ToStep: StepConv * ^A -> Step<'middle, 'o>) (StepConv, x1))
        let s2 : Step<'middle, 'o> = ((^B or StepConv) : (static member ToStep: StepConv * ^B -> Step<'middle, 'o>) (StepConv, x2))
        { Steps = state.Steps @ [WorkflowInternal.wrapStepParallel [s1; s2]] }

    /// Runs 3 steps in parallel (fan-out) - SRTP resolves each argument type
    [<CustomOperation("fanOut")>]
    member inline _.FanOut(state: WorkflowState<'input, 'middle>, x1: ^A, x2: ^B, x3: ^C) : WorkflowState<'input, 'o list> =
        let s1 : Step<'middle, 'o> = ((^A or StepConv) : (static member ToStep: StepConv * ^A -> Step<'middle, 'o>) (StepConv, x1))
        let s2 : Step<'middle, 'o> = ((^B or StepConv) : (static member ToStep: StepConv * ^B -> Step<'middle, 'o>) (StepConv, x2))
        let s3 : Step<'middle, 'o> = ((^C or StepConv) : (static member ToStep: StepConv * ^C -> Step<'middle, 'o>) (StepConv, x3))
        { Steps = state.Steps @ [WorkflowInternal.wrapStepParallel [s1; s2; s3]] }

    /// Runs 4 steps in parallel (fan-out) - SRTP resolves each argument type
    [<CustomOperation("fanOut")>]
    member inline _.FanOut(state: WorkflowState<'input, 'middle>, x1: ^A, x2: ^B, x3: ^C, x4: ^D) : WorkflowState<'input, 'o list> =
        let s1 : Step<'middle, 'o> = ((^A or StepConv) : (static member ToStep: StepConv * ^A -> Step<'middle, 'o>) (StepConv, x1))
        let s2 : Step<'middle, 'o> = ((^B or StepConv) : (static member ToStep: StepConv * ^B -> Step<'middle, 'o>) (StepConv, x2))
        let s3 : Step<'middle, 'o> = ((^C or StepConv) : (static member ToStep: StepConv * ^C -> Step<'middle, 'o>) (StepConv, x3))
        let s4 : Step<'middle, 'o> = ((^D or StepConv) : (static member ToStep: StepConv * ^D -> Step<'middle, 'o>) (StepConv, x4))
        { Steps = state.Steps @ [WorkflowInternal.wrapStepParallel [s1; s2; s3; s4]] }

    /// Runs 5 steps in parallel (fan-out) - SRTP resolves each argument type
    [<CustomOperation("fanOut")>]
    member inline _.FanOut(state: WorkflowState<'input, 'middle>, x1: ^A, x2: ^B, x3: ^C, x4: ^D, x5: ^E) : WorkflowState<'input, 'o list> =
        let s1 : Step<'middle, 'o> = ((^A or StepConv) : (static member ToStep: StepConv * ^A -> Step<'middle, 'o>) (StepConv, x1))
        let s2 : Step<'middle, 'o> = ((^B or StepConv) : (static member ToStep: StepConv * ^B -> Step<'middle, 'o>) (StepConv, x2))
        let s3 : Step<'middle, 'o> = ((^C or StepConv) : (static member ToStep: StepConv * ^C -> Step<'middle, 'o>) (StepConv, x3))
        let s4 : Step<'middle, 'o> = ((^D or StepConv) : (static member ToStep: StepConv * ^D -> Step<'middle, 'o>) (StepConv, x4))
        let s5 : Step<'middle, 'o> = ((^E or StepConv) : (static member ToStep: StepConv * ^E -> Step<'middle, 'o>) (StepConv, x5))
        { Steps = state.Steps @ [WorkflowInternal.wrapStepParallel [s1; s2; s3; s4; s5]] }

    /// Runs multiple steps in parallel (fan-out) - for 6+ branches, use '+' operator
    [<CustomOperation("fanOut")>]
    member _.FanOut(state: WorkflowState<'input, 'middle>, steps: Step<'middle, 'o> list) : WorkflowState<'input, 'o list> =
        { Steps = state.Steps @ [WorkflowInternal.wrapStepParallel steps] }

    // ============ FANIN OPERATIONS ============
    // Uses inline SRTP to accept Task fn, Async fn, TypedAgent, Executor, or Step directly
    // Durable IDs are auto-generated

    /// Aggregates parallel results with any supported step type (uses SRTP for type resolution)
    [<CustomOperation("fanIn")>]
    member inline _.FanIn(state: WorkflowState<'input, 'elem list>, x: ^T) : WorkflowState<'input, 'output> =
        let step : Step<'elem list, 'output> = ((^T or StepConv) : (static member ToStep: StepConv * ^T -> Step<'elem list, 'output>) (StepConv, x))
        let durableId = WorkflowInternal.getDurableId step
        let displayName = WorkflowInternal.getDisplayName step
        WorkflowInternal.warnIfLambda step durableId
        { Steps = state.Steps @ [WorkflowInternal.wrapStepFanIn durableId displayName step] }

    /// Builds the final workflow definition
    member _.Run(state: WorkflowState<'input, 'output>) : WorkflowDef<'input, 'output> =
        { Name = None; Steps = state.Steps }


module Task = 
    /// An convenient alias for Task.FromResult. Wraps a value in a completed Task.
    let fromResult x = Task.FromResult x

/// The workflow computation expression builder instance
[<AutoOpen>]
module WorkflowCE =
    let workflow = WorkflowBuilder()

    /// Prefix operator to convert any supported type to Step<'i, 'o>.
    /// Supports: Task fn, Async fn, TypedAgent, Executor, or Step passthrough.
    /// Used for fanOut lists with 6+ branches: fanOut [+fn1; +fn2; +fn3; +fn4; +fn5; +fn6]
    let inline (~+) (x: ^T) : Step<'i, 'o> =
        ((^T or StepConv) : (static member ToStep: StepConv * ^T -> Step<'i, 'o>) (StepConv, x))


/// Functions for executing workflows
[<RequireQualifiedAccess>]
module Workflow =

    /// Sets the name of a workflow (used for MAF compilation)
    let withName<'input, 'output> (name: string) (workflow: WorkflowDef<'input, 'output>) : WorkflowDef<'input, 'output> =
        { workflow with Name = Some name }

    // ============ MAF COMPILATION ============

    /// Converts an AgentNet WorkflowStep to a MAF Executor
    let rec private toMAFExecutor (step: WorkflowStep) : MAFExecutor =
        match step with
        | Step (durableId, name, execute) ->
            // Wrap the execute function, creating an AgentNet context for execution
            let fn = Func<obj, Task<obj>>(fun input ->
                let ctx = WorkflowContext.create()
                execute input ctx)
            ExecutorFactory.CreateStep(name, fn)

        | Route (durableId, router) ->
            // Router selects and executes the appropriate branch
            let fn = Func<obj, Task<obj>>(fun input ->
                let ctx = WorkflowContext.create()
                router input ctx)
            ExecutorFactory.CreateStep("Router", fn)

        | Parallel branches ->
            // Create parallel executor from branches
            let branchFns =
                branches
                |> List.map (fun (id, exec) ->
                    Func<obj, Task<obj>>(fun input ->
                        let ctx = WorkflowContext.create()
                        exec input ctx))
                |> ResizeArray
            ExecutorFactory.CreateParallel("Parallel", branchFns)

    /// Compiles a workflow definition to MAF Workflow using WorkflowBuilder.
    /// Returns a Workflow that can be executed with InProcessExecution.RunAsync.
    /// If no name is set, uses "Workflow" as the default name.
    let toMAF<'input, 'output> (workflow: WorkflowDef<'input, 'output>) : MAFWorkflow =
        let name = workflow.Name |> Option.defaultValue "Workflow"
        match workflow.Steps with
        | [] -> failwith "Workflow must have at least one step"
        | firstStep :: restSteps ->
            // Create executors for all steps
            let firstExecutor = toMAFExecutor firstStep
            let restExecutors = restSteps |> List.map toMAFExecutor

            // Build workflow using MAFWorkflowBuilder
            let mutable builder = MAFWorkflowBuilder(firstExecutor).WithName(name)

            // Add edges between consecutive executors
            let mutable prev = firstExecutor
            for exec in restExecutors do
                builder <- builder.AddEdge(prev, exec)
                prev <- exec

            // Mark the last executor as output
            builder <- builder.WithOutputFrom(prev)

            // Build and return the workflow
            builder.Build()

    // ============ MAF IN-PROCESS EXECUTION ============

    /// Runs a workflow via MAF InProcessExecution.
    /// The workflow is compiled to MAF format and executed in-process.
    let runInProcess<'input, 'output> (input: 'input) (workflow: WorkflowDef<'input, 'output>) : Task<'output> =
        task {
            // Compile to MAF workflow
            let mafWorkflow = toMAF workflow

            // Run via InProcessExecution
            let! run = MAFInProcessExecution.RunAsync(mafWorkflow, input :> obj)
            use _ = run

            // Find the last ExecutorCompletedEvent - it should be the workflow output
            let mutable lastResult: obj option = None
            for evt in run.NewEvents do
                match evt with
                | :? MAFExecutorCompletedEvent as completed ->
                    lastResult <- Some completed.Data
                | _ -> ()

            match lastResult with
            | Some data -> return data :?> 'output
            | None -> return failwith "Workflow did not produce output. No ExecutorCompletedEvent found."
        }

    /// Converts a workflow to an executor (enables workflow composition).
    /// Uses MAF InProcessExecution to run the workflow.
    let toExecutor<'input, 'output> (name: string) (workflow: WorkflowDef<'input, 'output>) : Executor<'input, 'output> =
        {
            Name = name
            Execute = fun input _ -> runInProcess input workflow
        }
