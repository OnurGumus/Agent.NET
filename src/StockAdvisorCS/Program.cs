using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using StockAdvisorCS;

await new StockAdvisorApp().RunAsync();

// ============================================================================
// MAIN APPLICATION
// ============================================================================

class StockAdvisorApp
{
    private AIAgent? _agent;
    private readonly Workflow _stockComparisonWorkflow;

    public StockAdvisorApp()
    {
        _stockComparisonWorkflow = BuildWorkflow();
    }

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

    public async Task RunAsync()
    {
        Console.WriteLine("Stock Advisor Agent (C# Edition)");
        Console.WriteLine("================================");

        while (true)
        {
            ShowMenu();
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    if (_agent == null)
                    {
                        Console.WriteLine("\nInitializing agent (requires Azure OpenAI)...");
                        var chatClient = TryCreateChatClient();
                        if (chatClient == null)
                        {
                            Console.WriteLine("Error: AZURE_OPENAI_ENDPOINT environment variable is not set.");
                            Console.WriteLine("Agent chat requires Azure OpenAI configuration.");
                            break;
                        }
                        _agent = CreateStockAdvisor(chatClient);
                    }
                    await RunAgentChatAsync(_agent);
                    break;

                case "2":
                    await RunComparisonWorkflowAsync("AAPL", "MSFT");
                    break;

                case "3":
                    await RunComparisonWorkflowAsync("GOOGL", "AMZN");
                    break;

                case "4":
                    await RunSingleAnalysisAsync("TSLA");
                    break;

                case "5":
                    Console.Write("\nEnter first stock symbol: ");
                    var sym1 = Console.ReadLine()?.Trim().ToUpper() ?? "";
                    Console.Write("Enter second stock symbol: ");
                    var sym2 = Console.ReadLine()?.Trim().ToUpper() ?? "";
                    if (!string.IsNullOrWhiteSpace(sym1) && !string.IsNullOrWhiteSpace(sym2))
                    {
                        await RunComparisonWorkflowAsync(sym1, sym2);
                    }
                    else
                    {
                        Console.WriteLine("Invalid symbols.");
                    }
                    break;

                case "6":
                case "exit":
                    Console.WriteLine("\nGoodbye!");
                    return;

                default:
                    Console.WriteLine("\nInvalid option. Please try again.");
                    break;
            }
        }
    }

    private static void ShowMenu()
    {
        Console.WriteLine();
        Console.WriteLine("Stock Advisor - Demo Scenarios");
        Console.WriteLine("==============================");
        Console.WriteLine("1. Agent Chat (interactive - requires Azure OpenAI)");
        Console.WriteLine("2. Workflow: Compare AAPL vs MSFT");
        Console.WriteLine("3. Workflow: Compare GOOGL vs AMZN");
        Console.WriteLine("4. Single Stock Analysis (TSLA)");
        Console.WriteLine("5. Custom Workflow Comparison");
        Console.WriteLine("6. Exit");
        Console.WriteLine();
        Console.Write("Select option: ");
    }

    private async Task RunComparisonWorkflowAsync(string symbol1, string symbol2)
    {
        Console.WriteLine($"\nRunning MAF workflow for {symbol1} vs {symbol2}...");

        var input = new StockSymbols(symbol1, symbol2);

        await using var run = await InProcessExecution.RunAsync(_stockComparisonWorkflow, input);

        // Process workflow events
        foreach (var evt in run.NewEvents)
        {
            if (evt is ExecutorCompletedEvent completed && completed.ExecutorId == "GenerateReport")
            {
                Console.WriteLine(completed.Data);
            }
        }
    }

    private static async Task RunSingleAnalysisAsync(string symbol)
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

    private static IChatClient? TryCreateChatClient()
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint))
            return null;

        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o";

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new DefaultAzureCredential());

        return azureClient.GetChatClient(deploymentName).AsIChatClient();
    }

    private static AIAgent CreateStockAdvisor(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            name: "StockAdvisor",
            instructions: """
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

                Be concise but thorough in your analysis.
                """,
            tools:
            [
                AIFunctionFactory.Create(StockTools.GetStockInfo),
                AIFunctionFactory.Create(StockTools.GetHistoricalPrices),
                AIFunctionFactory.Create(StockTools.CalculateVolatility),
                AIFunctionFactory.Create(StockTools.CompareStocks)
            ]);
    }

    private static async Task RunAgentChatAsync(AIAgent agent)
    {
        Console.WriteLine("\nAgent Chat Mode - Type 'back' to return to menu\n");

        var thread = agent.GetNewThread();

        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("back", StringComparison.OrdinalIgnoreCase))
                break;

            Console.WriteLine();
            var response = await agent.RunAsync(input, thread);
            Console.WriteLine($"StockAdvisor:\n{response.Text}\n");
        }
    }
}

// ============================================================================
// DOMAIN TYPES FOR WORKFLOW
// ============================================================================

record StockSymbols(string Symbol1, string Symbol2);
record StockData(string Symbol, string Info, string Volatility);
record StockPair(StockData Stock1, StockData Stock2);
record ComparisonReport(StockPair Pair, string Comparison);

// ============================================================================
// CUSTOM EXECUTORS
// ============================================================================

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
