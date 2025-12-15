namespace AgentNet

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.SemanticKernel
open Microsoft.SemanticKernel.Agents
open Microsoft.SemanticKernel.ChatCompletion

/// Integration with Microsoft SemanticKernel
[<RequireQualifiedAccess>]
module SK =

    /// Creates KernelParameterMetadata for a tool's parameters
    let private createParamMetadata (tool: Tool) : KernelParameterMetadata seq =
        tool.Parameters
        |> Seq.map (fun p ->
            KernelParameterMetadata(p.Name, Description = p.Description, ParameterType = p.Type))

    /// Converts an AgentNet Tool to a SemanticKernel KernelFunction
    let private toolToKernelFunction (tool: Tool) : KernelFunction =
        // Create a delegate that matches the expected signature
        let invokeFunc = Func<KernelArguments, string>(fun args ->
            let argValues =
                tool.Parameters
                |> List.map (fun p ->
                    match args.TryGetValue(p.Name) with
                    | true, value -> value
                    | false, _ -> null)
                |> List.toArray
            let result = tool.Invoke(argValues)
            result :?> string)

        KernelFunctionFactory.CreateFromMethod(
            method = (invokeFunc :> Delegate),
            functionName = tool.Name,
            description = tool.Description,
            parameters = createParamMetadata tool)

    /// Converts an AgentNet Tool to a KernelPlugin
    let private toolToPlugin (tool: Tool) : KernelPlugin =
        let functions = [| toolToKernelFunction tool |]
        KernelPluginFactory.CreateFromFunctions(tool.Name, tool.Description, functions)

    /// Creates a SemanticKernel ChatCompletionAgent from an AgentNet Agent config
    let createAgent (chatService: IChatCompletionService) (config: AgentConfig) : ChatCompletionAgent =

        // Build the kernel
        let builder = Kernel.CreateBuilder()
        builder.Services.AddSingleton<IChatCompletionService>(chatService) |> ignore
        let kernel = builder.Build()

        // Add tools as plugins
        for tool in config.Tools do
            let plugin = toolToPlugin tool
            kernel.Plugins.Add(plugin)

        // Create the agent
        ChatCompletionAgent(
            Name = (config.Name |> Option.defaultValue "Agent"),
            Instructions = config.Instructions,
            Kernel = kernel,
            Arguments = KernelArguments(
                PromptExecutionSettings(
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                )
            )
        )

    /// Builds a fully functional AgentNet.Agent from config and chat service
    let build (chatService: IChatCompletionService) (config: AgentConfig) : AgentNet.Agent =
        let skAgent = createAgent chatService config
        let thread = ChatHistoryAgentThread()

        {
            Config = config
            Chat = fun message -> async {
                let mutable result = ""
                let asyncEnum = skAgent.InvokeAsync(message, thread)
                let enumerator = asyncEnum.GetAsyncEnumerator()

                let rec loop () = async {
                    let! hasMore = enumerator.MoveNextAsync().AsTask() |> Async.AwaitTask
                    if hasMore then
                        result <- result + (enumerator.Current.Message.Content |> Option.ofObj |> Option.defaultValue "")
                        return! loop ()
                }

                do! loop ()
                return result
            }
        }

/// Extension methods for AgentConfig
[<AutoOpen>]
module AgentExtensions =

    type AgentConfig with
        /// Builds the agent with the provided chat completion service
        member this.Build(chatService: IChatCompletionService) : AgentNet.Agent =
            SK.build chatService this
