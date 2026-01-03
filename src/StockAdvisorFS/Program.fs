open System
open System.Threading.Tasks
open AgentNet
open Azure.AI.OpenAI
open Azure.Identity
open Microsoft.Extensions.AI
open StockAdvisorFS

// ============================================================================
// DOMAIN TYPES FOR WORKFLOW
// ============================================================================

type StockSymbols = { Symbol1: string; Symbol2: string }
type StockData = { Symbol: string; Info: string; Volatility: string }
type StockPair = { Stock1: StockData; Stock2: StockData }
type ComparisonReport = { Pair: StockPair; Comparison: string }

// ============================================================================
// WORKFLOW EXECUTORS
// ============================================================================

/// Fetches stock info and volatility for a symbol
let fetchStockData (symbol: string) : Task<StockData> = task {
    let! info = StockTools.getStockInfo symbol
    let! volatility = StockTools.calculateVolatility symbol
    return { Symbol = symbol; Info = info; Volatility = volatility }
}

/// Fetches data for both stocks in parallel
let fetchBothStocks (symbols: StockSymbols) : Task<StockPair> = task {
    let! results = Task.WhenAll([|
        fetchStockData symbols.Symbol1
        fetchStockData symbols.Symbol2
    |])
    return { Stock1 = results.[0]; Stock2 = results.[1] }
}

/// Compares two stocks
let compareStockPair (pair: StockPair) : Task<ComparisonReport> = task {
    let! comparison = StockTools.compareStocks pair.Stock1.Symbol pair.Stock2.Symbol
    return { Pair = pair; Comparison = comparison }
}

/// Generates a formatted report
let generateReport (report: ComparisonReport) : string =
    $"""
================================================================================
                        STOCK COMPARISON REPORT
================================================================================

--- {report.Pair.Stock1.Symbol} ---
{report.Pair.Stock1.Info}

{report.Pair.Stock1.Volatility}

--- {report.Pair.Stock2.Symbol} ---
{report.Pair.Stock2.Info}

{report.Pair.Stock2.Volatility}

--- COMPARISON ---
{report.Comparison}
================================================================================
"""

// ============================================================================
// WORKFLOW DEFINITION
// ============================================================================

/// Multi-stock comparison workflow using the AgentNet workflow DSL
let stockComparisonWorkflow = workflow {
    start (Executor.fromTask "FetchBothStocks" fetchBothStocks)
    next (Executor.fromTask "CompareStocks" compareStockPair)
    next (Executor.fromFn "GenerateReport" generateReport)
}

// ============================================================================
// AGENT SETUP
// ============================================================================

let failIfNone msg opt = opt |> Option.defaultWith (fun () -> failwith msg)
let tryGetEnv = Environment.GetEnvironmentVariable >> Option.ofObj

let createChatClient () =
    let endpoint = tryGetEnv "AZURE_OPENAI_ENDPOINT" |> failIfNone "AZURE_OPENAI_ENDPOINT environment variable is not set."
    let deploymentName = tryGetEnv "AZURE_OPENAI_DEPLOYMENT" |> Option.defaultValue "gpt-4o"
    let client = AzureOpenAIClient(Uri(endpoint), DefaultAzureCredential())
    client.GetChatClient(deploymentName).AsIChatClient()

let createStockAdvisor (chatClient: IChatClient) =
    // Create tools using XML doc comments for descriptions
    let stockInfoTool =  Tool.createWithDocs <@ StockTools.getStockInfo @>
    let historicalTool = Tool.createWithDocs <@ StockTools.getHistoricalPrices @>
    let volatilityTool = Tool.createWithDocs <@ StockTools.calculateVolatility @>
    let compareTool =    Tool.createWithDocs <@ StockTools.compareStocks @>

    Agent.create """
        You are a helpful stock analysis assistant. You help users analyze stocks,
        compare investments, and understand market metrics.
        When a user asks about stocks:
        1. Use the available tools to gather relevant data
        2. Analyze the information
        3. Provide clear, actionable insights
        After providing your analysis, also consider broader market context:
        - Market sector trends
        - Economic factors that might affect the stock
        - Risk considerations based on current market conditions
        - A final investment recommendation summary
        Be concise but thorough in your analysis."""
    |> Agent.withName "StockAdvisor"
    |> Agent.withTools [stockInfoTool; historicalTool; volatilityTool; compareTool]
    |> Agent.build chatClient

// ============================================================================
// DEMO SCENARIOS
// ============================================================================

/// Interactive agent chat loop
let runAgentChat (agent: ChatAgent) = task {
    printfn "\nAgent Chat Mode - Type 'back' to return to menu\n"

    let rec loop () = task {
        printf "You: "
        let input = Console.ReadLine()

        if String.IsNullOrWhiteSpace(input) then
            return! loop ()
        elif input.Equals("back", StringComparison.OrdinalIgnoreCase) then
            return ()
        else
            printf "\nStockAdvisor: "
            let! response = agent.Chat(input)
            printfn "%s\n" response
            return! loop ()
    }

    do! loop ()
}

/// Run the multi-stock comparison workflow
let runComparisonWorkflow (symbol1: string) (symbol2: string) = task {
    printfn $"\nRunning comparison workflow for {symbol1} vs {symbol2}..."
    printfn "Step 1: Fetching stock data in parallel..."

    let input = { Symbol1 = symbol1; Symbol2 = symbol2 }
    let! report = Workflow.run input stockComparisonWorkflow

    printfn "%s" report
}

/// Run a single stock analysis
let runSingleAnalysis (symbol: string) = task {
    printfn $"\nAnalyzing {symbol}..."

    printfn "\nFetching stock info..."
    let! info = StockTools.getStockInfo symbol
    printfn "%s" info

    printfn "\nFetching volatility..."
    let! volatility = StockTools.calculateVolatility symbol
    printfn "%s" volatility

    printfn "\nFetching historical prices (7 days)..."
    let! history = StockTools.getHistoricalPrices symbol 7
    printfn "%s" history
}

// ============================================================================
// MAIN MENU
// ============================================================================

let showMenu () =
    printfn ""
    printfn "Stock Advisor - Demo Scenarios"
    printfn "=============================="
    printfn "1. Agent Chat (interactive - requires Azure OpenAI)"
    printfn "2. Workflow: Compare AAPL vs MSFT"
    printfn "3. Workflow: Compare GOOGL vs AMZN"
    printfn "4. Single Stock Analysis (TSLA)"
    printfn "5. Custom Workflow Comparison"
    printfn "6. Exit"
    printfn ""
    printf "Select option: "

let rec mainLoop (agentOpt: ChatAgent option) = task {
    showMenu ()

    match Console.ReadLine() with
    | "1" ->
        match agentOpt with
        | Some agent ->
            do! runAgentChat agent
        | None ->
            printfn "\nInitializing agent (requires Azure OpenAI)..."
            try
                let chatClient = createChatClient ()
                let agent = createStockAdvisor chatClient
                do! runAgentChat agent
                return! mainLoop (Some agent)
            with ex ->
                printfn $"\nError: {ex.Message}"
                printfn "Agent chat requires AZURE_OPENAI_ENDPOINT environment variable."
        return! mainLoop agentOpt

    | "2" ->
        do! runComparisonWorkflow "AAPL" "MSFT"
        return! mainLoop agentOpt

    | "3" ->
        do! runComparisonWorkflow "GOOGL" "AMZN"
        return! mainLoop agentOpt

    | "4" ->
        do! runSingleAnalysis "TSLA"
        return! mainLoop agentOpt

    | "5" ->
        printf "\nEnter first stock symbol: "
        let sym1 = Console.ReadLine().Trim().ToUpper()
        printf "Enter second stock symbol: "
        let sym2 = Console.ReadLine().Trim().ToUpper()
        if not (String.IsNullOrWhiteSpace sym1) && not (String.IsNullOrWhiteSpace sym2) then
            do! runComparisonWorkflow sym1 sym2
        else
            printfn "Invalid symbols."
        return! mainLoop agentOpt

    | "6" | "exit" ->
        printfn "\nGoodbye!"
        return ()

    | _ ->
        printfn "\nInvalid option. Please try again."
        return! mainLoop agentOpt
}

// ============================================================================
// ENTRY POINT
// ============================================================================

printfn "Stock Advisor Agent (F# Edition)"
printfn "================================="

mainLoop None |> fun t -> t.Wait()
