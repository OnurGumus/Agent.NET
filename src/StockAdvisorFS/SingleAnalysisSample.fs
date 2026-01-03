module StockAdvisorFS.SingleAnalysisSample

open System.Threading.Tasks

// ============================================================================
// SINGLE ANALYSIS SAMPLE
// Demonstrates calling async stock tools directly without a workflow.
// ============================================================================

/// Runs a single stock analysis showing info, volatility, and history
let run (symbol: string) : Task<unit> = task {
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
