using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace StockAdvisorCS;

// ============================================================================
// AGENT CHAT SAMPLE
// Demonstrates creating an AI agent with tools and interactive chat.
// ============================================================================

public static class AgentChatSample
{
    private static AIAgent? _cachedAgent;

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

    private static async Task ChatLoopAsync(AIAgent agent)
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

    /// <summary>
    /// Runs the agent chat sample.
    /// </summary>
    public static async Task RunAsync()
    {
        if (_cachedAgent == null)
        {
            Console.WriteLine("\nInitializing agent (requires Azure OpenAI)...");
            var chatClient = TryCreateChatClient();
            if (chatClient == null)
            {
                Console.WriteLine("Error: AZURE_OPENAI_ENDPOINT environment variable is not set.");
                Console.WriteLine("Agent chat requires Azure OpenAI configuration.");
                return;
            }
            _cachedAgent = CreateStockAdvisor(chatClient);
        }

        await ChatLoopAsync(_cachedAgent);
    }
}
