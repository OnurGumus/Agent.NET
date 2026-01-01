namespace AgentNet

open System

// =============================================================================
// Internal Result Helpers (no external dependencies)
// =============================================================================

module internal ResultHelpers =
    let map f = function Ok x -> Ok (f x) | Error e -> Error e
    let bind f = function Ok x -> f x | Error e -> Error e
    let mapError f = function Ok x -> Ok x | Error e -> Error (f e)

module internal AsyncResultHelpers =
    let map f ar = async {
        let! r = ar
        return ResultHelpers.map f r
    }

    let bind f ar = async {
        let! r = ar
        match r with
        | Ok x -> return! f x
        | Error e -> return Error e
    }


// =============================================================================
// ResultExecutor Type
// =============================================================================

/// An executor that transforms input to Result<output, error> within a workflow
type ResultExecutor<'input, 'output, 'error> = {
    Name: string
    Execute: 'input -> WorkflowContext -> Async<Result<'output, 'error>>
}


// =============================================================================
// ResultExecutor Module
// =============================================================================

/// Module for creating result-aware executors
[<RequireQualifiedAccess>]
module ResultExecutor =

    /// Creates a result executor from a simple function (map semantics - wrapped in Ok)
    let map (name: string) (fn: 'input -> 'output) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fun input _ -> async { return Ok (fn input) }
        }

    /// Creates a result executor from a Result-returning function (bind semantics)
    let bind (name: string) (fn: 'input -> Result<'output, 'error>) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fun input _ -> async { return fn input }
        }

    /// Creates a result executor from an async function (map semantics - wrapped in Ok)
    let mapAsync (name: string) (fn: 'input -> Async<'output>) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fun input _ -> async {
                let! result = fn input
                return Ok result
            }
        }

    /// Creates a result executor from an async Result-returning function (bind semantics)
    let bindAsync (name: string) (fn: 'input -> Async<Result<'output, 'error>>) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fun input _ -> fn input
        }

    /// Creates a result executor with full context access
    let create (name: string) (fn: 'input -> WorkflowContext -> Async<Result<'output, 'error>>) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fn
        }

    /// Creates a result executor from an AgentNet Agent (map semantics - agents don't return Result)
    let fromAgent (name: string) (agent: Agent) : ResultExecutor<string, string, 'error> =
        {
            Name = name
            Execute = fun input _ -> async {
                let! result = agent.Chat input
                return Ok result
            }
        }

    /// Creates a result executor from a regular workflow (wraps output in Ok)
    let fromWorkflow (name: string) (workflow: WorkflowDef<'input, 'output>) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fun input ctx -> async {
                let! result = Workflow.runWithContext input ctx workflow
                return Ok result
            }
        }


// =============================================================================
// ResultWorkflow Step Types
// =============================================================================

/// A step in a result workflow pipeline
type ResultWorkflowStep<'error> =
    | Step of name: string * execute: (obj -> WorkflowContext -> Async<Result<obj, 'error>>)
    | Route of router: (obj -> WorkflowContext -> Async<Result<obj, 'error>>)
    | Parallel of executors: (obj -> WorkflowContext -> Async<Result<obj, 'error>>) list
    | Resilient of settings: ResilienceSettings * inner: ResultWorkflowStep<'error>


/// A result workflow definition that can be executed
type ResultWorkflowDef<'input, 'output, 'error> = {
    /// The steps in the workflow, in order
    Steps: ResultWorkflowStep<'error> list
}


// =============================================================================
// ResultWorkflowInternal Module
// =============================================================================

/// Internal module for result workflow building
module internal ResultWorkflowInternal =

    /// Wraps a typed result executor as an untyped step
    let wrapExecutor<'i, 'o, 'e> (exec: ResultExecutor<'i, 'o, 'e>) : ResultWorkflowStep<'e> =
        Step (exec.Name, fun input ctx -> async {
            let typedInput = input :?> 'i
            let! result = exec.Execute typedInput ctx
            return ResultHelpers.map box result
        })

    /// Wraps a typed router function as an untyped route step
    let wrapRouter<'a, 'b, 'e> (router: 'a -> ResultExecutor<'a, 'b, 'e>) : ResultWorkflowStep<'e> =
        Route (fun input ctx -> async {
            let typedInput = input :?> 'a
            let selectedExecutor = router typedInput
            let! result = selectedExecutor.Execute typedInput ctx
            return ResultHelpers.map box result
        })

    /// Wraps a list of typed executors as untyped parallel functions
    /// All executors run in parallel; if any returns Error, the first Error is returned
    let wrapParallel<'i, 'o, 'e> (executors: ResultExecutor<'i, 'o, 'e> list) : ResultWorkflowStep<'e> =
        let wrappedFns =
            executors
            |> List.map (fun exec ->
                fun (input: obj) (ctx: WorkflowContext) -> async {
                    let typedInput = input :?> 'i
                    let! result = exec.Execute typedInput ctx
                    return ResultHelpers.map box result
                })
        Parallel wrappedFns

    /// Wraps an aggregator executor, handling the obj list -> typed list conversion
    let wrapFanIn<'elem, 'o, 'e> (exec: ResultExecutor<'elem list, 'o, 'e>) : ResultWorkflowStep<'e> =
        Step (exec.Name, fun input ctx -> async {
            // Input is obj list from parallel, convert each element to the expected type
            let objList = input :?> obj list
            let typedList = objList |> List.map (fun o -> o :?> 'elem)
            let! result = exec.Execute typedList ctx
            return ResultHelpers.map box result
        })

    /// Modifies the last step with a resilience update function
    let modifyLastWithResilience (updateSettings: ResilienceSettings -> ResilienceSettings) (steps: ResultWorkflowStep<'e> list) : ResultWorkflowStep<'e> list =
        match List.rev steps with
        | [] -> failwith "No previous step to modify"
        | last :: rest ->
            let newLast =
                match last with
                | Resilient (settings, inner) ->
                    // Already wrapped - update the settings
                    Resilient (updateSettings settings, inner)
                | other ->
                    // Wrap with new resilience settings
                    Resilient (updateSettings ResilienceSettings.defaults, other)
            List.rev (newLast :: rest)


// =============================================================================
// ResultWorkflowBuilder CE
// =============================================================================

/// Builder for the result workflow computation expression
type ResultWorkflowBuilder() =

    member _.Yield(_) : ResultWorkflowStep<'e> list = []

    /// Starts the workflow with a result executor
    [<CustomOperation("start")>]
    member _.Start<'i, 'o, 'e>(steps: ResultWorkflowStep<'e> list, executor: ResultExecutor<'i, 'o, 'e>) : ResultWorkflowStep<'e> list =
        steps @ [ResultWorkflowInternal.wrapExecutor executor]

    /// Adds the next step to the workflow
    [<CustomOperation("next")>]
    member _.Next<'i, 'o, 'e>(steps: ResultWorkflowStep<'e> list, executor: ResultExecutor<'i, 'o, 'e>) : ResultWorkflowStep<'e> list =
        steps @ [ResultWorkflowInternal.wrapExecutor executor]

    /// Routes to different executors based on the previous step's output
    [<CustomOperation("route")>]
    member _.Route<'a, 'b, 'e>(steps: ResultWorkflowStep<'e> list, router: 'a -> ResultExecutor<'a, 'b, 'e>) : ResultWorkflowStep<'e> list =
        steps @ [ResultWorkflowInternal.wrapRouter router]

    /// Runs multiple executors in parallel on the same input (fan-out)
    [<CustomOperation("fanOut")>]
    member _.FanOut<'i, 'o, 'e>(steps: ResultWorkflowStep<'e> list, executors: ResultExecutor<'i, 'o, 'e> list) : ResultWorkflowStep<'e> list =
        steps @ [ResultWorkflowInternal.wrapParallel executors]

    /// Aggregates parallel results into a single output (fan-in)
    [<CustomOperation("fanIn")>]
    member _.FanIn<'elem, 'o, 'e>(steps: ResultWorkflowStep<'e> list, executor: ResultExecutor<'elem list, 'o, 'e>) : ResultWorkflowStep<'e> list =
        steps @ [ResultWorkflowInternal.wrapFanIn executor]

    /// Sets retry count for the previous step
    [<CustomOperation("retry")>]
    member _.Retry<'e>(steps: ResultWorkflowStep<'e> list, count: int) : ResultWorkflowStep<'e> list =
        steps |> ResultWorkflowInternal.modifyLastWithResilience (fun s -> { s with RetryCount = count })

    /// Sets backoff strategy for retries on the previous step
    [<CustomOperation("backoff")>]
    member _.Backoff<'e>(steps: ResultWorkflowStep<'e> list, strategy: BackoffStrategy) : ResultWorkflowStep<'e> list =
        steps |> ResultWorkflowInternal.modifyLastWithResilience (fun s -> { s with Backoff = strategy })

    /// Sets timeout for the previous step
    [<CustomOperation("timeout")>]
    member _.Timeout<'e>(steps: ResultWorkflowStep<'e> list, duration: TimeSpan) : ResultWorkflowStep<'e> list =
        steps |> ResultWorkflowInternal.modifyLastWithResilience (fun s -> { s with Timeout = Some duration })

    /// Sets fallback executor for the previous step (used if all retries fail)
    [<CustomOperation("fallback")>]
    member _.Fallback<'i, 'o, 'e>(steps: ResultWorkflowStep<'e> list, executor: ResultExecutor<'i, 'o, 'e>) : ResultWorkflowStep<'e> list =
        let fallbackFn (input: obj) (ctx: WorkflowContext) : Async<obj> = async {
            let typedInput = unbox<'i> input
            let! result = executor.Execute typedInput ctx
            // Fallback must succeed (it's the last resort), so we extract the Ok value
            // If fallback also returns Error, the exception will propagate
            match result with
            | Ok value -> return box value
            | Error _ -> return failwith "Fallback executor returned an error"
        }
        steps |> ResultWorkflowInternal.modifyLastWithResilience (fun s -> { s with Fallback = Some fallbackFn })

    /// Builds the final result workflow definition
    member _.Run(steps: ResultWorkflowStep<'e> list) : ResultWorkflowDef<'input, 'output, 'e> =
        { Steps = steps }


/// The result workflow computation expression builder instance
[<AutoOpen>]
module ResultWorkflowCE =
    let resultWorkflow = ResultWorkflowBuilder()


// =============================================================================
// ResultWorkflow Execution Module
// =============================================================================

/// Functions for executing result workflows
[<RequireQualifiedAccess>]
module ResultWorkflow =

    /// Calculates delay for a given retry attempt based on backoff strategy
    let private calculateDelay (strategy: BackoffStrategy) (attempt: int) : TimeSpan =
        match strategy with
        | Immediate -> TimeSpan.Zero
        | Linear delay -> delay
        | Exponential (initial, multiplier) ->
            let factor = Math.Pow(multiplier, float (attempt - 1))
            TimeSpan.FromMilliseconds(initial.TotalMilliseconds * factor)

    /// Executes an async operation with timeout
    let private withTimeout (timeout: TimeSpan) (operation: Async<'a>) : Async<'a> =
        async {
            let! child = Async.StartChild(operation, int timeout.TotalMilliseconds)
            return! child
        }

    /// Executes an inner step (non-resilient)
    let rec private executeInnerStep<'e> (step: ResultWorkflowStep<'e>) (input: obj) (ctx: WorkflowContext) : Async<Result<obj, 'e>> =
        async {
            match step with
            | Step (_, execute) ->
                return! execute input ctx

            | Route router ->
                return! router input ctx

            | Parallel executors ->
                let! results =
                    executors
                    |> List.map (fun exec -> exec input ctx)
                    |> Async.Parallel

                // Check for any errors, return first error if found
                let firstError =
                    results
                    |> Array.tryPick (function Error e -> Some e | Ok _ -> None)

                match firstError with
                | Some e -> return Error e
                | None ->
                    let okValues =
                        results
                        |> Array.choose (function Ok v -> Some v | Error _ -> None)
                        |> Array.toList
                    return Ok (box okValues)

            | Resilient (settings, inner) ->
                return! executeWithResilience settings inner input ctx
        }

    /// Executes a step with resilience (retry, timeout, fallback)
    and private executeWithResilience<'e> (settings: ResilienceSettings) (inner: ResultWorkflowStep<'e>) (input: obj) (ctx: WorkflowContext) : Async<Result<obj, 'e>> =
        let rec attemptWithRetry (remainingRetries: int) (attempt: int) : Async<Result<obj, 'e>> =
            async {
                try
                    // Apply timeout if specified
                    let operation = executeInnerStep inner input ctx
                    let timedOperation =
                        match settings.Timeout with
                        | Some t -> withTimeout t operation
                        | None -> operation

                    let! result = timedOperation

                    // For Result workflows, we don't retry on Error (that's expected domain behavior)
                    // We only retry on exceptions (infrastructure failures)
                    return result
                with ex ->
                    if remainingRetries > 0 then
                        // Calculate and apply backoff delay
                        let delay = calculateDelay settings.Backoff attempt
                        if delay > TimeSpan.Zero then
                            do! Async.Sleep (int delay.TotalMilliseconds)

                        // Retry
                        return! attemptWithRetry (remainingRetries - 1) (attempt + 1)
                    else
                        // All retries exhausted - try fallback or rethrow
                        match settings.Fallback with
                        | Some fallbackFn ->
                            let! fallbackResult = fallbackFn input ctx
                            return Ok fallbackResult
                        | None ->
                            return raise ex
            }

        attemptWithRetry settings.RetryCount 1

    /// Runs a result workflow with the given input and context
    let runWithContext<'input, 'output, 'error> (input: 'input) (ctx: WorkflowContext) (workflow: ResultWorkflowDef<'input, 'output, 'error>) : Async<Result<'output, 'error>> =
        let rec executeSteps (steps: ResultWorkflowStep<'error> list) (current: obj) : Async<Result<obj, 'error>> =
            async {
                match steps with
                | [] ->
                    return Ok current
                | step :: remaining ->
                    let! result = executeInnerStep step current ctx
                    match result with
                    | Ok value ->
                        return! executeSteps remaining value
                    | Error e ->
                        return Error e  // Short-circuit on error!
            }

        async {
            let! result = executeSteps workflow.Steps (input :> obj)
            return ResultHelpers.map unbox<'output> result
        }

    /// Runs a result workflow with the given input (creates a new context)
    let run<'input, 'output, 'error> (input: 'input) (workflow: ResultWorkflowDef<'input, 'output, 'error>) : Async<Result<'output, 'error>> =
        let ctx = WorkflowContext.create ()
        runWithContext input ctx workflow

    /// Runs a result workflow synchronously
    let runSync<'input, 'output, 'error> (input: 'input) (workflow: ResultWorkflowDef<'input, 'output, 'error>) : Result<'output, 'error> =
        workflow |> run input |> Async.RunSynchronously

    /// Converts a result workflow to a result executor (enables workflow composition)
    let toExecutor<'input, 'output, 'error> (name: string) (workflow: ResultWorkflowDef<'input, 'output, 'error>) : ResultExecutor<'input, 'output, 'error> =
        {
            Name = name
            Execute = fun input ctx -> runWithContext input ctx workflow
        }
