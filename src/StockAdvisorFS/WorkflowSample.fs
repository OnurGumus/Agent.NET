module StockAdvisorFS.WorkflowSample

open System.Threading.Tasks
open AgentNet

// ============================================================================
// WORKFLOW SAMPLE
// Demonstrates the AgentNet workflow DSL with parallel execution and
// sequential pipeline for comparing two stocks.
// ============================================================================

// ----------------------------------------------------------------------------
// Domain Types
// ----------------------------------------------------------------------------

type StockSymbols = { Symbol1: string; Symbol2: string }
type StockData = { Symbol: string; Info: string; Volatility: string }
type StockPair = { Stock1: StockData; Stock2: StockData }
type ComparisonReport = { Pair: StockPair; Comparison: string }

// ----------------------------------------------------------------------------
// Workflow Executors
// ----------------------------------------------------------------------------

/// Fetches stock info and volatility for a symbol
let private fetchStockData (symbol: string) : Task<StockData> = task {
    let! info = StockTools.getStockInfo symbol
    let! volatility = StockTools.calculateVolatility symbol
    return { Symbol = symbol; Info = info; Volatility = volatility }
}

/// Fetches data for both stocks in parallel
let private fetchBothStocks (symbols: StockSymbols) : Task<StockPair> = task {
    let! results = Task.WhenAll([|
        fetchStockData symbols.Symbol1
        fetchStockData symbols.Symbol2
    |])
    return { Stock1 = results.[0]; Stock2 = results.[1] }
}

/// Compares two stocks
let private compareStockPair (pair: StockPair) : Task<ComparisonReport> = task {
    let! comparison = StockTools.compareStocks pair.Stock1.Symbol pair.Stock2.Symbol
    return { Pair = pair; Comparison = comparison }
}

/// Generates a formatted report
let private generateReport (report: ComparisonReport) : string =
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

// ----------------------------------------------------------------------------
// Workflow Definition
// ----------------------------------------------------------------------------

/// Multi-stock comparison workflow using the AgentNet workflow DSL
let private stockComparisonWorkflow = workflow {
    start (Executor.fromTask "FetchBothStocks" fetchBothStocks)
    next (Executor.fromTask "CompareStocks" compareStockPair)
    next (Executor.fromFn "GenerateReport" generateReport)
}

// ----------------------------------------------------------------------------
// Public Entry Point
// ----------------------------------------------------------------------------

/// Runs the workflow sample comparing two stocks
let run (symbol1: string) (symbol2: string) : Task<unit> = task {
    printfn $"\nRunning comparison workflow for {symbol1} vs {symbol2}..."
    printfn "Step 1: Fetching stock data in parallel..."

    let input = { Symbol1 = symbol1; Symbol2 = symbol2 }
    let! report = Workflow.run input stockComparisonWorkflow

    printfn "%s" report
}
