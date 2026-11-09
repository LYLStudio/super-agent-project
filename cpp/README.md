# SuperAgent C++

This is the native C++ implementation, maintained independently from the .NET solution at the repository root. It supports OpenAI-compatible chat-completions endpoints and GitHub Copilot models through the community-maintained `copilot-sdk-cpp` SDK (GitHub does not provide an official C++ SDK). Its interactive terminal follows the .NET console flow: full-screen output, multiline prompt editing and history, streamed model text, tool-call/result sections, F2 settings, F3 logs, F5 configuration reload, and the same request status summary. It supports persistent conversations, rotating structured logs, and these workspace-scoped tools: `code_writer`, `code_reader`, `database_query`, `code_analyzer`, `test_generator`, `debugger`, `refactoring`, `performance_monitor`, `logging`, `file_search`, `shell_exec`, and `git_tool`.

## Requirements

- CMake 3.20 or newer
- A C++20 compiler
- OpenSSL development files for HTTPS support (optional for HTTP endpoints)
- ODBC development files and a local ODBC driver for optional MySQL/PostgreSQL access
- Network access on the first CMake configure to fetch pinned HTTP, JSON, SQLite, and Copilot SDK sources
- For GitHub Copilot: a compatible Copilot CLI runtime (the pinned SDK targets CLI 1.0.102-1) authenticated with an account that has Copilot access; see the [Copilot CLI installation guide](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli)

## Build

From the repository root:

```sh
cmake -S cpp -B build/cpp -DCMAKE_BUILD_TYPE=Release
cmake --build build/cpp --config Release
ctest --test-dir build/cpp --output-on-failure
```

## Run

OpenAI-compatible endpoint:

```sh
export OPENAI_API_KEY="your-api-key"
build/cpp/superagent-cpp --base-url=http://localhost:11434/v1 --model=gpt-oss:20b
```

GitHub Copilot, after authenticating the Copilot CLI runtime:

```sh
build/cpp/superagent-cpp --provider=GitHubCopilot --model=auto
```

The agent also reads `OPENAI_BASE_URL`, `OPENAI_MODEL`, `OPENAI_API_KEY`, `SUPERAGENT_API_KEY`, `SUPERAGENT_MODEL_PROVIDER`, `GITHUB_COPILOT_MODEL`, `SUPERAGENT_WORKSPACE`, and `SUPERAGENT_CONFIG`. Options are `--api-key=KEY`, `--base-url=URL`, `--provider=OpenAICompatible|GitHubCopilot`, `--model=NAME`, `--workspace=PATH`, `--config=PATH`, `--conversation-id=ID`, `--max-iterations=N`, and `--help`. Provider and model can also be stored as `agent.model_provider` and `github_copilot.model` in `superagent-cpp.json` (or as `Agent.ModelProvider` and `GitHubCopilot.Model` in .NET-style `appsettings.json`). Use `--provider=GitHubCopilot` or set `SUPERAGENT_MODEL_PROVIDER=GitHubCopilot`; `GITHUB_COPILOT_MODEL` overrides the Copilot model, whose default is `auto`. Copilot model selection in F2 queries the CLI model catalog. In an interactive terminal, use `F2` for settings, `F3` to filter recent logs, `F5` to reload configuration, and `F4` to submit a multiline prompt (`Enter` inserts a newline; terminals that report modified Enter separately can also use `Ctrl+Enter`). The commands `help`, `/settings`, `/logs`, `/new`, `exit`, `quit`, and `q` are also available. Settings can select models from the configured endpoint and save the API key, like the .NET console. Conversation files are written in the .NET-compatible record shape and existing C++ conversation files remain readable.

The workspace defaults to the current directory; file tools reject paths that resolve outside it. To retain the C++ version's additional safety boundaries, `shell_exec` is restricted to `pwd`, `ls`, `cat`, and `grep`, and Git mutations require explicit confirmation; destructive and remote Git operations remain disabled. `database_query` is read-only and restricts ODBC to loopback hosts. Web search is intentionally omitted.

`shell_exec` is intentionally limited to native `pwd`, `ls`, `cat`, and `grep` commands inside the workspace; it does not interpret shell operators, expansions, pipelines, or redirects. `git_tool` supports `status`, `diff`, `log`, `show`, `branch`, and `rev-parse`; supported local changes (`add`, `commit`, `switch`, `checkout`, `restore`, `stash push`, and branch creation) require an interactive confirmation. Destructive commands such as `reset`/`clean` and remote operations are disabled. `database_query` supports workspace-local SQLite files by default; when CMake finds ODBC and an appropriate driver is installed, MySQL/PostgreSQL ODBC connections are additionally accepted only for loopback hosts. Queries are read-only and bounded. Web search is not included. The lightweight analysis, debugging, refactoring, and test-generation tools are heuristic helpers, not replacements for compiler or language-server diagnostics.

On Windows, run `superagent-cpp.exe` from the generated build directory. CMake and the installed C++ compiler can be selected using the usual Visual Studio, Ninja, Unix Makefiles, or Xcode generators.