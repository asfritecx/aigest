using System.CommandLine;
using Aigest.Cli.Commands;
using Aigest.Cli.Core;
using Aigest.Cli.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aigest.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        using var host = CliHost.Build();

        // ask subcommand
        var askPathsOption = new Option<string[]>("--paths")
        {
            Description = "File/glob paths to include in the corpus",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var askQuestionOption = new Option<string>("--question")
        {
            Description = "Question to ask about the corpus",
            Required = true,
        };
        var askMaxTokensOption = new Option<int>("--max-tokens")
        {
            Description = "Maximum output tokens. Overrides AIGEST_MAX_TOKENS env var; default 8192.",
            DefaultValueFactory = _ => 0,
        };
        var askDenyOption = new Option<string[]>("--deny")
        {
            Description = "Additional glob deny patterns",
            AllowMultipleArgumentsPerToken = true,
        };
        var askPerFolderOption = new Option<bool>("--per-folder")
        {
            Description = "Group matched files by their immediate subfolder under the common base, then dispatch one chat call per folder in parallel (bounded by AIGEST_MAX_PARALLEL_FOLDERS).",
        };

        var askCmd = new Command("ask", "Ask a question about source files")
        {
            askPathsOption,
            askQuestionOption,
            askMaxTokensOption,
            askDenyOption,
            askPerFolderOption,
        };

        askCmd.SetAction(async (parseResult, ct) =>
        {
            var paths = parseResult.GetValue(askPathsOption) ?? [];
            var question = parseResult.GetValue(askQuestionOption)!;
            var explicitMaxTokens = parseResult.GetValue(askMaxTokensOption);
            var deny = parseResult.GetValue(askDenyOption);
            var perFolder = parseResult.GetValue(askPerFolderOption);

            var client = host.Services.GetRequiredService<IChatClient>();
            var config = host.Services.GetRequiredService<AigestConfig>();
            var maxTokens = ResolveMaxTokens(explicitMaxTokens, config, fallback: 8192);
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aigest.Cli.Commands.AskCommand");
            return await AskCommand.RunAsync(paths, question, maxTokens, deny, config, client, logger, ct, perFolder);
        });

        // write subcommand
        var writeSpecOption = new Option<string>("--spec")
        {
            Description = "Specification for the file to generate",
            Required = true,
        };
        var writeContextOption = new Option<string[]>("--context")
        {
            Description = "Context file/glob paths",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var writeTargetOption = new Option<string>("--target")
        {
            Description = "Output file path",
            Required = true,
        };
        var writeMaxTokensOption = new Option<int>("--max-tokens")
        {
            Description = "Maximum output tokens. Overrides AIGEST_MAX_TOKENS env var; default 16384.",
            DefaultValueFactory = _ => 0,
        };
        var writeOverwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Allow overwriting existing target file",
        };
        var writeAllowOutsideCwdOption = new Option<bool>("--allow-outside-cwd")
        {
            Description = "Allow writing to paths outside the current working directory",
        };
        var writeDenyOption = new Option<string[]>("--deny")
        {
            Description = "Additional glob deny patterns",
            AllowMultipleArgumentsPerToken = true,
        };

        var writeCmd = new Command("write", "Generate a file from a spec and context")
        {
            writeSpecOption,
            writeContextOption,
            writeTargetOption,
            writeMaxTokensOption,
            writeOverwriteOption,
            writeAllowOutsideCwdOption,
            writeDenyOption,
        };

        writeCmd.SetAction(async (parseResult, ct) =>
        {
            var spec = parseResult.GetValue(writeSpecOption)!;
            var context = parseResult.GetValue(writeContextOption) ?? [];
            var target = parseResult.GetValue(writeTargetOption)!;
            var explicitMaxTokens = parseResult.GetValue(writeMaxTokensOption);
            var overwrite = parseResult.GetValue(writeOverwriteOption);
            var allowOutsideCwd = parseResult.GetValue(writeAllowOutsideCwdOption);
            var deny = parseResult.GetValue(writeDenyOption);

            var client = host.Services.GetRequiredService<IChatClient>();
            var config = host.Services.GetRequiredService<AigestConfig>();
            var maxTokens = ResolveMaxTokens(explicitMaxTokens, config, fallback: 16384);
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aigest.Cli.Commands.WriteCommand");
            return await WriteCommand.RunAsync(spec, context, target, overwrite, allowOutsideCwd, maxTokens, deny, config, client, logger, ct);
        });

        // extract-chat subcommand
        var extractInputArg = new Argument<string>("input")
        {
            Description = "Input JSONL chat file",
        };
        var extractOutputOption = new Option<string>("--output", new[] { "-o" })
        {
            Description = "Output markdown file path",
            Required = true,
        };

        var extractCmd = new Command("extract-chat", "Extract readable text from a JSONL chat log")
        {
            extractInputArg,
            extractOutputOption,
        };

        extractCmd.SetAction((ParseResult parseResult) =>
        {
            var input = parseResult.GetValue(extractInputArg)!;
            var output = parseResult.GetValue(extractOutputOption)!;
            return ExtractChatCommand.Run(input, output);
        });

        // Root command — SCL auto-adds --version (reads AssemblyInformationalVersion) and --help
        var rootCmd = new RootCommand("Aigest CLI") { askCmd, writeCmd, extractCmd };

        // Bare invocation (no subcommand): run environment/config check
        rootCmd.SetAction((ParseResult parseResult) => CheckCommand.Run());

        return await rootCmd.Parse(args).InvokeAsync();
    }

    public static int ResolveMaxTokens(int explicitValue, AigestConfig config, int fallback) =>
        explicitValue > 0 ? explicitValue
      : config.MaxOutputTokens > 0 ? config.MaxOutputTokens
      : fallback;
}
