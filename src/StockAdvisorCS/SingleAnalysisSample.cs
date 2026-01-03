namespace StockAdvisorCS;

// ============================================================================
// SINGLE ANALYSIS SAMPLE
// Demonstrates calling async stock tools directly without a workflow.
// ============================================================================

public static class SingleAnalysisSample
{
    /// <summary>
    /// Runs a single stock analysis showing info, volatility, and history.
    /// </summary>
    public static async Task RunAsync(string symbol)
    {
        Console.WriteLine($"\nAnalyzing {symbol}...");

        Console.WriteLine("\nFetching stock info...");
        var info = await StockTools.GetStockInfo(symbol);
        Console.WriteLine(info);

        Console.WriteLine("\nFetching volatility...");
        var volatility = await StockTools.CalculateVolatility(symbol);
        Console.WriteLine(volatility);

        Console.WriteLine("\nFetching historical prices (7 days)...");
        var history = await StockTools.GetHistoricalPrices(symbol, 7);
        Console.WriteLine(history);
    }
}
