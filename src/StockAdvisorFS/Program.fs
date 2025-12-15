open System
open AgentNet
open Anthropic.SDK
open Microsoft.Extensions.AI
open Microsoft.SemanticKernel
open Microsoft.SemanticKernel.ChatCompletion
open StockAdvisorFS.StockTools

// ============================================================================
// AGENT DEFINITION - Clean, declarative, beautiful!
// Compare this to the C# version in StockAdvisorCS...
// ============================================================================

let stockAdvisor = agent {
    name "StockAdvisor"
    instructions """
        You are a helpful stock analysis assistant. You help users analyze stocks,
        compare investments, and understand market metrics.

        When a user asks about stocks:
        1. Use the available tools to gather relevant data
        2. Analyze the information
        3. Provide clear, actionable insights

        Be concise but thorough in your analysis.
        """

    // Register tools - each is just one line!
    add stockInfoTool
    add historicalTool
    add volatilityTool
    add compareTool
}

// ============================================================================
// MAIN - Wire up Anthropic and run the chat loop
// ============================================================================

[<EntryPoint>]
let main args =
    // Get API key
    let apiKey =
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        |> Option.ofObj
        |> Option.defaultWith (fun () ->
            failwith "ANTHROPIC_API_KEY environment variable is not set")

    // Create chat service
    let anthropicClient = new AnthropicClient(apiKey)
    let chatService =
        ChatClientBuilder(anthropicClient.Messages)
            .UseFunctionInvocation()
            .Build()
            .AsChatCompletionService()

    // Build the agent
    let agent = stockAdvisor.Build(chatService)

    // Chat loop
    printfn "Stock Advisor Agent (F# Edition)"
    printfn "================================="
    printfn "Ask me about stocks! (Type 'exit' to quit)\n"

    let rec loop () =
        printf "You: "
        let input = Console.ReadLine()

        if String.IsNullOrWhiteSpace(input) then
            loop ()
        elif input.Equals("exit", StringComparison.OrdinalIgnoreCase) then
            ()
        else
            printf "\nStockAdvisor: "
            let response = agent.Chat input |> Async.RunSynchronously
            printfn "%s\n" response
            loop ()

    loop ()
    printfn "Goodbye!"
    0
