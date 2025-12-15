# AgentNet F# Computation Expression Library Spec

## Overview
An F# computation expression library wrapping Microsoft.SemanticKernel to provide a clean, idiomatic way to create AI agents. The goal is to create a "killer app" for F# that even C# developers will want to use.

## Design Philosophy
Inspired by [FSharp.SystemCommandLine](https://github.com/JordanMarr/FSharp.SystemCommandLine):
- **Tools** → Pipeline style (simple, functional)
- **Agent** → Computation expression (declarative, readable)

## Current API (Prototype)

### Tool Definition (Pipeline Style)
```fsharp
/// Gets current stock information
let getStockInfo (symbol: string) : string =
    $"Price for {symbol}: $178.50"

let stockInfoTool =
    Tool.fromFn getStockInfo
    |> Tool.withName "getStockInfo"
    |> Tool.describe "Gets current stock information"
    |> Tool.describeParam "symbol" "The stock ticker symbol"
```

### Agent Definition (CE Style)
```fsharp
let stockAdvisor = agent {
    name "StockAdvisor"
    instructions "You analyze stocks..."

    add stockInfoTool
    add historicalTool
    add volatilityTool
}

// Build and use
let agent = stockAdvisor.Build(chatService)
let! response = agent.Chat "Compare AAPL vs MSFT"
```

## Project Structure
```
src/
├── AgentNet.sln
├── AgentNet/                    # F# library
│   ├── Tool.fs                  # Tool type + pipeline functions
│   ├── Agent.fs                 # AgentConfig + agent CE builder
│   └── SemanticKernel.fs        # SK integration (Tool -> KernelFunction)
├── StockAdvisorCS/              # C# example (before - verbose)
└── StockAdvisorFS/              # F# example (after - clean)
```

## Design Decisions Made
1. **Pipeline for tools, CE for agents** - Follows FSharp.SystemCommandLine pattern
2. **No attributes** - Tool metadata via pipeline functions, not [<KernelFunction>]
3. **Pure functions first** - Define F# functions, then wrap with Tool.fromFn
4. **Single-agent focus** - Get basics right before multi-agent orchestration
5. **Anthropic.SDK for Claude** - Using unofficial SDK with SK integration

## TODO / Future Enhancements
- [ ] Auto-derive tool names via ReflectedDefinition (attempted, needs work)
- [ ] Extract descriptions from XML doc comments (`///`)
- [ ] Better handling of multi-parameter curried functions
- [ ] Streaming response support (IAsyncEnumerable)
- [ ] Multi-agent group chat support
- [ ] Error handling and validation
- [ ] Support for other AI providers (OpenAI, Ollama, Azure)

## Open Questions
- Best way to auto-capture function names without ugly quotation syntax?
- Should we support both `Tool.fromFn` and a `tool { }` CE for flexibility?
- How to handle F# async vs C# Task in tool implementations?

## Dependencies
- Microsoft.SemanticKernel 1.68.0
- Microsoft.SemanticKernel.Agents.Core 1.68.0
- Anthropic.SDK 5.8.0 (for examples)
