using Microsoft.Agents.AI.Workflows;

namespace StockAdvisorCS;

// ============================================================================
// WORKFLOW SAMPLE
// Demonstrates the MAF WorkflowBuilder with parallel execution and
// sequential pipeline for comparing two stocks.
// ============================================================================

// ----------------------------------------------------------------------------
// Domain Types
// ----------------------------------------------------------------------------

public record StockSymbols(string Symbol1, string Symbol2);
public record StockData(string Symbol, string Info, string Volatility);
public record StockPair(StockData Stock1, StockData Stock2);
public record ComparisonReport(StockPair Pair, string Comparison);

// ----------------------------------------------------------------------------
// Workflow Executors
// ----------------------------------------------------------------------------

internal sealed class FetchBothStocksExecutor() : Executor<StockSymbols, StockPair>("FetchBothStocks")
{
    public override async ValueTask<StockPair> HandleAsync(
        StockSymbols symbols,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("  [FetchBothStocks] Fetching stock data in parallel...");

        var task1 = FetchStockDataAsync(symbols.Symbol1);
        var task2 = FetchStockDataAsync(symbols.Symbol2);
        await Task.WhenAll(task1, task2);

        return new StockPair(task1.Result, task2.Result);
    }

    private static async Task<StockData> FetchStockDataAsync(string symbol)
    {
        var info = await StockTools.GetStockInfo(symbol);
        var volatility = await StockTools.CalculateVolatility(symbol);
        return new StockData(symbol, info, volatility);
    }
}

internal sealed class CompareStocksExecutor() : Executor<StockPair, ComparisonReport>("CompareStocks")
{
    public override async ValueTask<ComparisonReport> HandleAsync(
        StockPair pair,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("  [CompareStocks] Comparing stocks...");
        var comparison = await StockTools.CompareStocks(pair.Stock1.Symbol, pair.Stock2.Symbol);
        return new ComparisonReport(pair, comparison);
    }
}

internal sealed class GenerateReportExecutor() : Executor<ComparisonReport, string>("GenerateReport")
{
    public override ValueTask<string> HandleAsync(
        ComparisonReport report,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("  [GenerateReport] Generating report...");
        var result = $"""

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
""";
        return ValueTask.FromResult(result);
    }
}

// ----------------------------------------------------------------------------
// Workflow Sample
// ----------------------------------------------------------------------------

public static class WorkflowSample
{
    private static readonly Workflow _workflow = BuildWorkflow();

    private static Workflow BuildWorkflow()
    {
        // Create executors
        var fetchBothStocks = new FetchBothStocksExecutor();
        var compareStockPair = new CompareStocksExecutor();
        var reportExecutor = new GenerateReportExecutor();

        // Build the workflow using MAF WorkflowBuilder
        WorkflowBuilder builder = new(fetchBothStocks);
        builder.AddEdge(fetchBothStocks, compareStockPair);
        builder.AddEdge(compareStockPair, reportExecutor);
        builder.WithOutputFrom(reportExecutor);
        return builder.Build();
    }

    /// <summary>
    /// Runs the workflow sample comparing two stocks.
    /// </summary>
    public static async Task RunAsync(string symbol1, string symbol2)
    {
        Console.WriteLine($"\nRunning MAF workflow for {symbol1} vs {symbol2}...");

        var input = new StockSymbols(symbol1, symbol2);

        await using var run = await InProcessExecution.RunAsync(_workflow, input);

        // Process workflow events
        foreach (var evt in run.NewEvents)
        {
            if (evt is ExecutorCompletedEvent completed && completed.ExecutorId == "GenerateReport")
            {
                Console.WriteLine(completed.Data);
            }
        }
    }
}
