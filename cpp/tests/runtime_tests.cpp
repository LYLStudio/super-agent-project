#include "runtime.hpp"
#include "tools.hpp"
#include "process.hpp"

#include <sqlite3.h>

#include <chrono>
#include <fstream>
#include <iostream>
#include <stdexcept>

namespace {

void require(bool condition, const std::string& message) {
    if (!condition) throw std::runtime_error(message);
}

} // namespace

int main() {
    const auto unique = std::chrono::steady_clock::now().time_since_epoch().count();
    const auto temporary = superagent::fs::temp_directory_path() /
        ("superagent-cpp-test-" + std::to_string(unique));
    try {
        const auto workspace = temporary / "workspace";
        superagent::fs::create_directories(workspace);
        superagent::AppConfig config;
        config.config_path = temporary / "settings.json";
        config.workspace = workspace;
        config.conversation_directory = temporary / "conversations";
        config.conversation_id = "test-session";
        config.log_path = temporary / "logs" / "agent.log";
        config.log_file_size_limit = 160;
        config.tools["code_reader"] = false;
        config.save();

        const auto loaded = superagent::AppConfig::load(config.config_path);
        require(loaded.model == config.model, "configuration model did not round-trip");
        require(!loaded.tools.at("code_reader"), "tool setting did not round-trip");

        const auto dotnet_config_path = temporary / "appsettings.json";
        {
            std::ofstream output(dotnet_config_path);
            output << R"({
                "OpenAI": {
                    "BaseUrl": "http://localhost:9000/v1",
                    "ApiKey": "test-key",
                    "Model": "test-model",
                    "RequestTimeoutSeconds": 45
                },
                "Agent": {
                    "WorkspaceRoot": ".",
                    "UnmodeledSetting": "preserve-me",
                    "MaxIterations": 20,
                    "MaxTokens": 8000,
                    "MaxContextCharacters": 12345,
                    "Temperature": 0.4,
                    "ShowThinkingProcess": true,
                    "DefaultSystemPrompt": "custom",
                    "Region": "台灣",
                    "Language": "繁體中文（台灣）",
                    "Tools": { "code_reader": false }
                },
                "Logging": {
                    "Enabled": false,
                    "Path": "logs/app.log",
                    "RollingInterval": "Hour",
                    "FileSizeLimitMB": 12
                }
            })";
        }
        auto dotnet_config = superagent::AppConfig::load(dotnet_config_path);
        require(dotnet_config.base_url == "http://localhost:9000/v1", ".NET BaseUrl setting was not loaded");
        require(dotnet_config.api_key == "test-key", ".NET ApiKey setting was not loaded");
        require(dotnet_config.max_iterations == 20 && dotnet_config.max_tokens == 8000,
            ".NET agent limits were not loaded");
        require(dotnet_config.show_thinking_process && dotnet_config.system_prompt == "custom",
            ".NET thinking or system prompt setting was not loaded");
        require(dotnet_config.log_rolling_interval == "hour" &&
            dotnet_config.log_file_size_limit == 12 * 1024 * 1024,
            ".NET logging settings were not normalized");
        dotnet_config.model = "updated-model";
        dotnet_config.save();
        {
            std::ifstream saved(dotnet_config_path);
            const auto record = superagent::json::parse(saved);
            require(record.at("OpenAI").at("Model") == "updated-model",
                "saving settings did not preserve .NET configuration naming");
            require(record.at("Agent").at("UnmodeledSetting") == "preserve-me",
                "saving settings discarded an unmodeled .NET property");
        }

        std::vector<superagent::json> system_messages;
        superagent::upsert_system_message(system_messages, "first prompt");
        require(system_messages.size() == 1 && system_messages.front().is_object(),
            "system message was not inserted as a JSON object");
        superagent::upsert_system_message(system_messages, "updated prompt");
        require(system_messages.size() == 1 && system_messages.front().at("content") == "updated prompt",
            "system message update added a duplicate or failed to update");

        const std::vector<superagent::json> messages{
            {{"role", "system"}, {"content", "system"}},
            {{"role", "user"}, {"content", "hello"}},
            {{"role", "assistant"}, {"content", ""}, {"tool_calls", {{
                {"id", "call-1"}, {"type", "function"},
                {"function", {{"name", "code_reader"}, {"arguments", R"({"file_path":"sample.txt"})"}}}
            }}}},
            {{"role", "tool"}, {"tool_call_id", "call-1"}, {"name", "code_reader"}, {"content", "result"}}
        };
        superagent::ConversationStore(config).save(messages);
        require(superagent::ConversationStore(config).load() == messages, "conversation did not round-trip");
        {
            std::ifstream saved(superagent::ConversationStore(config).path());
            const auto record = superagent::json::parse(saved);
            require(record.contains("CreatedAt") && record.contains("UpdatedAt"),
                "conversation metadata did not match .NET storage shape");
            require(record.at("Messages").at(2).contains("ToolCalls") &&
                record.at("Messages").at(2).at("ToolCalls").at(0).at("Arguments").is_object(),
                "tool call did not serialize in .NET conversation shape");
        }

        superagent::StructuredLogger logger(config);
        logger.write("info", std::string(220, 'x'), "test");
        logger.write("warning", "second entry", "test");
        require(logger.recent(10).size() == 2, "log query did not return both entries");
        require(logger.recent(10, "warning").size() == 1, "log filter did not match");

        superagent::Toolset tools(workspace, config.tools, logger);
        require(tools.definitions().size() == 11, "disabled tool still appears in definitions");
        require(tools.execute("code_reader", {{"file_path", "sample.txt"}}).find("disabled") != std::string::npos,
                "disabled tool was executable");
        require(tools.execute("code_writer", {{"file_path", "sample.txt"}, {"content", "TODO check"}}).find("Successfully") != std::string::npos,
                "workspace file write failed");
        config.tools["code_reader"] = true;
        superagent::Toolset enabled_tools(workspace, config.tools, logger);
        require(enabled_tools.execute("code_reader", {{"file_path", "sample.txt"}}).find("File content (10 bytes):\nTODO check") == 0,
                "workspace file read failed");
        require(enabled_tools.execute("code_reader", {{"file_path", "../outside.txt"}}).find("outside") != std::string::npos,
                "outside-workspace path was not rejected");
        require(enabled_tools.execute("file_search", {{"directory", "."}, {"pattern", "TODO"}}).find("sample.txt") != std::string::npos,
                "workspace file search failed");
        require(enabled_tools.execute("code_analyzer", {{"code", "// TODO"}, {"language", "cpp"}}).find("TODO") != std::string::npos,
                "code analyzer did not report TODO");

        sqlite3* database = nullptr;
        require(sqlite3_open((workspace / "sample.db").string().c_str(), &database) == SQLITE_OK,
            "could not create test SQLite database");
        char* sql_error = nullptr;
        const auto setup_result = sqlite3_exec(database,
            "CREATE TABLE items(value TEXT); INSERT INTO items VALUES('native');", nullptr, nullptr, &sql_error);
        const std::string setup_error = sql_error == nullptr ? "" : sql_error;
        sqlite3_free(sql_error);
        sqlite3_close(database);
        require(setup_result == SQLITE_OK, "could not populate test SQLite database: " + setup_error);
        const auto query_result = enabled_tools.execute("database_query", {
            {"connection_string", "Data Source=sample.db"},
            {"query", "SELECT value FROM items WHERE value = @value"},
            {"parameters", {{"value", "native"}}}
        });
        require(query_result.find("native") != std::string::npos, "parameterized read-only query failed");
        require(enabled_tools.execute("database_query", {
            {"connection_string", "Data Source=sample.db"}, {"query", "DELETE FROM items"}
        }).find("Error") != std::string::npos, "write query was not rejected");
        require(enabled_tools.execute("database_query", {
            {"connection_string", "Data Source=../outside.db"}, {"query", "SELECT 1"}
        }).find("Error") != std::string::npos, "outside-workspace database was not rejected");

        require(enabled_tools.execute("shell_exec", {{"command", "pwd"}}).find(workspace.string()) != std::string::npos,
            "safe shell pwd command failed");
        require(enabled_tools.execute("shell_exec", {{"command", "cat sample.txt"}}) == "TODO check",
            "safe shell cat command failed");
        require(enabled_tools.execute("shell_exec", {{"command", "pwd; ls"}}).find("operators") != std::string::npos,
            "shell operator was not rejected");
        require(enabled_tools.execute("shell_exec", {{"command", "cat ../outside.txt"}}).find("outside") != std::string::npos,
            "shell path outside workspace was not rejected");

        const auto git_init = superagent::run_process("git", {"init", "--quiet"}, workspace, 10);
        require(git_init.exit_code == 0, "could not initialize temporary Git repository");
        require(superagent::run_process("git", {"config", "user.name", "SuperAgent Test"}, workspace, 10).exit_code == 0,
            "could not configure test Git identity");
        require(superagent::run_process("git", {"config", "user.email", "test@example.invalid"}, workspace, 10).exit_code == 0,
            "could not configure test Git email");
        require(superagent::run_process("git", {"add", "sample.txt"}, workspace, 10).exit_code == 0,
            "could not stage initial test file");
        require(superagent::run_process("git", {"-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "initial"}, workspace, 10).exit_code == 0,
            "could not create initial test commit");
        require(enabled_tools.execute("git_tool", {{"command", "status"}}).find("Exit code: 0") != std::string::npos,
            "read-only git status failed");
        require(enabled_tools.execute("git_tool", {{"command", "log"}, {"args", "--oneline"}}).find("Exit code: 0") != std::string::npos,
            "read-only git log option was not passed through");
        {
            std::ofstream changed_file(workspace / "sample.txt", std::ios::trunc);
            changed_file << "TODO changed";
        }
        require(enabled_tools.execute("git_tool", {{"command", "add"}, {"args", "sample.txt"}}).find("confirmation") != std::string::npos,
            "unconfirmed Git mutation was not rejected");
        superagent::Toolset approved_tools(workspace, config.tools, logger,
            [](const std::string&) { return true; });
        require(approved_tools.execute("git_tool", {{"command", "add"}, {"args", "sample.txt"}}).find("Exit code: 0") != std::string::npos,
            "confirmed Git add failed");
        require(enabled_tools.execute("git_tool", {{"command", "reset"}}).find("disabled") != std::string::npos,
            "unsupported destructive Git command was not rejected");

        superagent::fs::remove_all(temporary);
        std::cout << "runtime tests passed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        std::error_code ignored;
        superagent::fs::remove_all(temporary, ignored);
        return 1;
    }
}