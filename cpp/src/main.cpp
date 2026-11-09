#include <httplib.h>
#include <nlohmann/json.hpp>
#include <copilot/copilot.hpp>

#include <algorithm>
#include <chrono>
#include <charconv>
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <functional>
#include <iostream>
#include <map>
#include <mutex>
#include <optional>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <type_traits>
#include <vector>

#ifdef _WIN32
#include <io.h>
#else
#include <unistd.h>
#endif

#include "runtime.hpp"
#include "tools.hpp"
#include "console.hpp"

namespace {

using json = nlohmann::json;
namespace fs = std::filesystem;

std::string environment(const char* name, const std::string& fallback) {
    const char* value = std::getenv(name);
    return value == nullptr || *value == '\0' ? fallback : value;
}

std::string option_value(const std::string& argument, const std::string& name) {
    return argument.substr(name.size() + 1);
}

bool starts_with_case_insensitive(const std::string& value, const std::string& prefix) {
    if (value.size() < prefix.size()) return false;
    return std::equal(prefix.begin(), prefix.end(), value.begin(), [](unsigned char left, unsigned char right) {
        return std::tolower(left) == std::tolower(right);
    });
}

bool equals_case_insensitive(const std::string& left, const std::string& right) {
    return left.size() == right.size() && starts_with_case_insensitive(left, right);
}

bool is_copilot_provider(const std::string& provider) {
    return equals_case_insensitive(provider, "GitHubCopilot") ||
        equals_case_insensitive(provider, "github-copilot") ||
        equals_case_insensitive(provider, "copilot");
}

bool is_openai_provider(const std::string& provider) {
    return equals_case_insensitive(provider, "OpenAICompatible") ||
        equals_case_insensitive(provider, "openai-compatible") ||
        equals_case_insensitive(provider, "openai");
}

std::string request_path(const std::string& base_url) {
    const auto scheme = base_url.find("://");
    if (scheme == std::string::npos) {
        throw std::runtime_error("base URL must include http:// or https://");
    }
    const auto path_start = base_url.find('/', scheme + 3);
    if (path_start == std::string::npos) {
        return "/chat/completions";
    }
    auto prefix = base_url.substr(path_start);
    while (!prefix.empty() && prefix.back() == '/') {
        prefix.pop_back();
    }
    return prefix + "/chat/completions";
}

std::string endpoint_origin(const std::string& base_url) {
    const auto scheme = base_url.find("://");
    if (scheme == std::string::npos) {
        throw std::runtime_error("base URL must include http:// or https://");
    }
    const auto authority_start = scheme + 3;
    const auto path_start = base_url.find('/', authority_start);
    const auto origin = base_url.substr(0, path_start);
    if (origin.size() == authority_start) {
        throw std::runtime_error("base URL must include a host");
    }
    return origin;
}

void print_help() {
    std::cout << "SuperAgent C++\n"
              << "Usage: superagent-cpp [options]\n\n"
              << "  --base-url=URL       OpenAI-compatible API base URL\n"
              << "  --api-key=KEY        API key\n"
              << "  --provider=NAME     OpenAICompatible or GitHubCopilot\n"
              << "  --model=NAME         Model name\n"
              << "  --workspace=PATH     File-tool workspace root\n"
              << "  --config=PATH        Configuration JSON path\n"
              << "  --conversation-id=ID Conversation identifier\n"
              << "  --max-iterations=N  Maximum tool/response turns\n"
              << "\nEnvironment: OPENAI_API_KEY, SUPERAGENT_API_KEY, OPENAI_BASE_URL, OPENAI_MODEL,\n"
              << "             GITHUB_COPILOT_MODEL, SUPERAGENT_MODEL_PROVIDER,\n"
              << "             SUPERAGENT_WORKSPACE, SUPERAGENT_CONFIG\n"
              << "Interactive keys: F2 settings, F3 logs, F4 submit, F5 reload\n"
              << "Commands: help, /settings, /logs, /new, exit, quit, q\n"
              << "  --help, -h            Show this help\n";
}

bool is_interactive_terminal() {
#ifdef _WIN32
    return _isatty(_fileno(stdin)) != 0;
#else
    return isatty(STDIN_FILENO) != 0;
#endif
}

std::string read_input(const std::string& prompt) {
    std::cout << prompt;
    std::string value;
    std::getline(std::cin, value);
    return value;
}

template <typename Number>
bool parse_number(const std::string& text, Number& value) {
    if constexpr (std::is_integral_v<Number>) {
        Number candidate{};
        const auto result = std::from_chars(text.data(), text.data() + text.size(), candidate);
        if (result.ec != std::errc{} || result.ptr != text.data() + text.size()) return false;
        value = candidate;
        return true;
    } else {
      try {
        std::size_t parsed = 0;
        const auto candidate = std::stod(text, &parsed);
        if (parsed != text.size() || !std::isfinite(candidate)) return false;
        value = static_cast<Number>(candidate);
        return true;
      } catch (const std::exception&) {
        return false;
      }
    }
}

std::string new_conversation_id() {
    auto id = superagent::utc_timestamp();
    for (auto& ch : id) {
        if (ch == ':' || ch == '.') ch = '-';
    }
    return "session-" + id;
}

std::size_t utf8_character_count(const std::string& value) {
    return static_cast<std::size_t>(std::count_if(value.begin(), value.end(), [](unsigned char ch) {
        return (ch & 0xC0) != 0x80;
    }));
}

std::size_t utf8_prefix_bytes(const std::string& value, std::size_t characters) {
    if (characters == 0) return 0;
    std::size_t count = 0;
    for (std::size_t index = 0; index < value.size(); ++index) {
        if ((static_cast<unsigned char>(value[index]) & 0xC0) != 0x80 && ++count > characters) {
            return index;
        }
    }
    return value.size();
}

void trim_context(std::vector<json>& messages, int max_characters) {
    if (messages.empty() || max_characters <= 0) return;
    auto system = std::find_if(messages.begin(), messages.end(), [](const json& message) {
        return message.value("role", std::string{}) == "system";
    });
    if (system == messages.end()) return;

    auto message_size = [](const json& message) {
        std::size_t size = utf8_character_count(message.value("role", std::string{})) +
            utf8_character_count(message.value("content", std::string{})) +
            utf8_character_count(message.value("name", std::string{}));
        if (message.contains("tool_call_id") && message.at("tool_call_id").is_string()) {
            size += utf8_character_count(message.at("tool_call_id").get<std::string>());
        }
        const auto calls = message.value("tool_calls", json::array());
        if (calls.is_array()) {
            for (const auto& call : calls) {
                size += utf8_character_count(call.value("id", std::string{}));
                const auto function = call.value("function", json::object());
                if (function.is_object()) {
                    size += utf8_character_count(function.value("name", std::string{}));
                    const auto arguments = function.value("arguments", json("{}"));
                    size += utf8_character_count(arguments.is_string() ? arguments.get<std::string>() : arguments.dump());
                }
            }
        }
        return size;
    };

    std::vector<std::vector<json>> turns;
    std::vector<json>* current_turn = nullptr;
    for (const auto& message : messages) {
        if (message.value("role", std::string{}) == "system") continue;
        if (current_turn == nullptr || message.value("role", std::string{}) == "user") {
            turns.emplace_back();
            current_turn = &turns.back();
        }
        current_turn->push_back(message);
    }

    const std::string marker = "\n[較早的訊息內容因上下文預算限制而截斷]";
    if (!turns.empty()) {
        int excess = 0;
        for (const auto& message : turns.back()) excess += static_cast<int>(message_size(message));
        excess += static_cast<int>(message_size(*system));
        excess -= max_characters;
        for (auto& message : turns.back()) {
            if (excess <= 0 || message.value("role", std::string{}) == "user" ||
                !message.contains("content") || !message.at("content").is_string()) continue;
            auto content = message.at("content").get<std::string>();
            const bool already_truncated = content.size() >= marker.size() &&
                content.compare(content.size() - marker.size(), marker.size(), marker) == 0;
            if (already_truncated) content.resize(content.size() - marker.size());
            const auto content_size = utf8_character_count(content);
            const auto marker_size = utf8_character_count(marker);
            if (!already_truncated && content_size <= marker_size) continue;
            const auto max_reduction = already_truncated ? content_size : content_size - marker_size;
            const auto reduction = std::min<std::size_t>(static_cast<std::size_t>(excess), max_reduction);
            const auto keep = already_truncated ? content_size - reduction : content_size - reduction - marker_size;
            content.resize(utf8_prefix_bytes(content, keep));
            message["content"] = content + marker;
            excess -= static_cast<int>(reduction);
        }
    }

    std::vector<std::vector<json>> selected_turns;
    std::size_t used = message_size(*system);
    std::size_t omitted_turns = 0;
    for (std::size_t index = turns.size(); index > 0; --index) {
        const auto& turn = turns[index - 1];
        std::size_t turn_size = 0;
        for (const auto& message : turn) turn_size += message_size(message);
        if (!selected_turns.empty() && used + turn_size > static_cast<std::size_t>(max_characters)) {
            omitted_turns = index;
            break;
        }
        selected_turns.push_back(turn);
        used += turn_size;
    }

    if (omitted_turns == 0 && used <= static_cast<std::size_t>(max_characters) &&
        std::count_if(messages.begin(), messages.end(), [](const json& message) {
            return message.value("role", std::string{}) == "system";
        }) <= 1) return;

    json system_message = *system;
    messages.clear();
    if (omitted_turns > 0) {
        const std::string omitted_marker = "\n\n較早的對話回合因上下文預算限制而省略。";
        auto content = system_message.value("content", std::string{});
        if (content.find("較早的對話回合因上下文預算限制而省略") == std::string::npos) {
            system_message["content"] = content + omitted_marker;
        }
    }
    messages.push_back(system_message);
    for (auto turn = selected_turns.rbegin(); turn != selected_turns.rend(); ++turn) {
        messages.insert(messages.end(), turn->begin(), turn->end());
    }
}

json request_completion(const superagent::AppConfig& config, const std::string& api_key,
                       const std::vector<json>& messages, const json& tool_definitions,
                       const std::function<void(const std::string&)>& on_chunk,
                       const std::function<void(const std::string&)>& on_retry,
                       const std::function<std::string(const std::string&, const json&)>& execute_tool) {
    if (is_copilot_provider(config.model_provider)) {
        copilot::Client client;
        try {
            client.start().get();
            std::vector<copilot::Tool> tools;
            copilot::ToolSet allowed_tools;
            std::mutex tool_execution_mutex;
            if (tool_definitions.is_array()) {
                for (const auto& definition : tool_definitions) {
                    if (!definition.is_object() || !definition.contains("function")) continue;
                    const auto& function = definition.at("function");
                    if (!function.is_object() || !function.contains("name")) continue;
                    const auto name = function.at("name").get<std::string>();
                    copilot::Tool tool;
                    tool.name = name;
                    tool.description = function.value("description", std::string{});
                    tool.parameters_schema = function.value("parameters", json::object());
                    tool.skip_permission = true;
                    tool.handler = [name, &execute_tool, &tool_execution_mutex](
                        const copilot::ToolInvocation& invocation) {
                        copilot::ToolResultObject result;
                        const auto arguments = invocation.arguments.value_or(json::object());
                        try {
                            std::lock_guard<std::mutex> lock(tool_execution_mutex);
                            result.text_result_for_llm = execute_tool(name, arguments);
                        } catch (const std::exception& error) {
                            result.result_type = copilot::ToolResultType::Failure;
                            result.error = error.what();
                            result.text_result_for_llm = "Error: " + std::string(error.what());
                        }
                        return result;
                    };
                    allowed_tools.add_custom(name);
                    tools.push_back(std::move(tool));
                }
            }

            copilot::SessionConfig session_config;
            session_config.model = config.model;
            session_config.working_directory = config.workspace.string();
            session_config.streaming = config.show_thinking_process;
            session_config.tools = std::move(tools);
            session_config.available_tools = allowed_tools.to_vector();

            const auto system_message = std::find_if(messages.begin(), messages.end(), [](const json& message) {
                return message.value("role", std::string{}) == "system";
            });
            if (system_message != messages.end()) {
                session_config.system_message = copilot::SystemMessageConfig{
                    .mode = copilot::SystemMessageMode::Append,
                    .content = system_message->value("content", std::string{})
                };
            }

            std::ostringstream prompt;
            prompt << "Conversation history:\n";
            for (const auto& message : messages) {
                const auto role = message.value("role", std::string{});
                if (role == "system") continue;
                prompt << role << ": " << message.value("content", std::string{}) << '\n';
                if (message.contains("tool_calls")) {
                    prompt << "Tool calls: " << message.at("tool_calls").dump() << '\n';
                }
            }

            auto session = client.create_session(std::move(session_config)).get();
            auto subscription = session->on([&](const copilot::SessionEvent& event) {
                if (const auto* delta = event.try_as<copilot::AssistantMessageDeltaData>();
                    delta != nullptr && on_chunk && !delta->delta_content.empty()) {
                    on_chunk(delta->delta_content);
                }
            });
            copilot::MessageOptions options;
            options.prompt = prompt.str();
            const auto response = session->send_and_wait(
                std::move(options), std::chrono::seconds(config.request_timeout_seconds)).get();
            if (!response) {
                throw std::runtime_error("Copilot SDK completed without an assistant response.");
            }
            const auto* message = response->try_as<copilot::AssistantMessageData>();
            if (message == nullptr) {
                throw std::runtime_error("Copilot SDK returned an unexpected response event.");
            }

            session->destroy().get();
            const auto stop_errors = client.stop().get();
            if (!stop_errors.empty()) {
                throw std::runtime_error("Copilot SDK failed to stop cleanly.");
            }
            return json{{"role", "assistant"}, {"content", message->content}};
        } catch (...) {
            client.force_stop();
            throw;
        }
    }

    const bool stream = config.show_thinking_process;
    json body = {
        {"model", config.model},
        {"messages", messages},
        {"temperature", config.temperature},
        {"max_tokens", config.max_tokens},
        {"stream", stream}
    };
    if (!tool_definitions.empty()) body["tools"] = tool_definitions;
    const auto request_body = body.dump();
    const auto url = config.base_url + "/chat/completions";
    std::string last_error;

    for (int attempt = 1; attempt <= 4; ++attempt) {
        last_error.clear();
        httplib::Client client(endpoint_origin(config.base_url));
        client.set_connection_timeout(5, 0);
        client.set_read_timeout(config.request_timeout_seconds, 0);
        client.set_write_timeout(config.request_timeout_seconds, 0);
        httplib::Headers headers{{"Authorization", "Bearer " + api_key}};
        httplib::Response response;
        httplib::Error error = httplib::Error::Success;
        json streamed_message = {{"role", "assistant"}, {"content", ""}, {"tool_calls", json::array()}};
        std::map<int, json> streamed_calls;
        std::string response_content;
        std::string event_buffer;
        auto consume_event = [&](const std::string& line) {
            if (line.rfind("data:", 0) != 0) return;
            auto data = line.substr(5);
            const auto first = data.find_first_not_of(" \t");
            if (first == std::string::npos) return;
            data.erase(0, first);
            if (data == "[DONE]") return;
            const auto event = json::parse(data);
            const auto choices = event.value("choices", json::array());
            if (!choices.is_array() || choices.empty()) return;
            const auto delta = choices.at(0).value("delta", json::object());
            if (delta.contains("content") && delta.at("content").is_string()) {
                const auto chunk = delta.at("content").get<std::string>();
                response_content += chunk;
                if (on_chunk && !chunk.empty()) on_chunk(chunk);
            }
            if (!delta.contains("tool_calls") || !delta.at("tool_calls").is_array()) return;
            for (const auto& call_delta : delta.at("tool_calls")) {
                const int index = call_delta.value("index", 0);
                auto& call = streamed_calls[index];
                if (!call.is_object()) call = {{"id", ""}, {"type", "function"}, {"function", {{"name", ""}, {"arguments", ""}}}};
                if (call_delta.contains("id") && call_delta.at("id").is_string()) {
                    call["id"] = call["id"].get<std::string>() + call_delta.at("id").get<std::string>();
                }
                if (call_delta.contains("function")) {
                    const auto& function = call_delta.at("function");
                    if (function.contains("name") && function.at("name").is_string()) {
                        call["function"]["name"] = call["function"]["name"].get<std::string>() +
                            function.at("name").get<std::string>();
                    }
                    if (function.contains("arguments") && function.at("arguments").is_string()) {
                        call["function"]["arguments"] = call["function"]["arguments"].get<std::string>() +
                            function.at("arguments").get<std::string>();
                    }
                }
            }
        };

        httplib::Result result;
        if (stream) {
            httplib::Request request;
            request.method = "POST";
            request.path = request_path(config.base_url);
            request.headers = headers;
            request.set_header("Content-Type", "application/json");
            request.body = request_body;
            request.response_handler = [](const httplib::Response&) { return true; };
            request.content_receiver = [&](const char* data, std::size_t length, std::uint64_t, std::uint64_t) {
                if (response.status < 200 || response.status >= 300) {
                    response.body.append(data, length);
                    return true;
                }
                event_buffer.append(data, length);
                std::size_t newline = 0;
                while ((newline = event_buffer.find('\n')) != std::string::npos) {
                    auto line = event_buffer.substr(0, newline);
                    if (!line.empty() && line.back() == '\r') line.pop_back();
                    try {
                        consume_event(line);
                    } catch (const json::exception& exception) {
                        last_error = "invalid streaming response: " + std::string(exception.what());
                        return false;
                    }
                    event_buffer.erase(0, newline + 1);
                }
                return true;
            };
            const bool sent = client.send(request, response, error);
            if (sent && response.status >= 200 && response.status < 300) {
                bool valid_stream = true;
                try {
                    if (!event_buffer.empty()) consume_event(event_buffer);
                } catch (const json::exception& exception) {
                    last_error = "invalid streaming response: " + std::string(exception.what());
                    valid_stream = false;
                }
                if (valid_stream) {
                    streamed_message["content"] = response_content;
                    for (auto& [index, call] : streamed_calls) {
                        (void)index;
                        streamed_message["tool_calls"].push_back(std::move(call));
                    }
                    if (streamed_message["tool_calls"].empty()) streamed_message.erase("tool_calls");
                    return streamed_message;
                }
            }
            if (last_error.empty()) {
                last_error = sent
                    ? "HTTP " + std::to_string(response.status) + ": " + response.body
                    : httplib::to_string(error);
            }
        } else {
            const auto response_result = client.Post(request_path(config.base_url), headers, request_body, "application/json");
            if (response_result && response_result->status >= 200 && response_result->status < 300) {
                const auto response_json = json::parse(response_result->body);
                return response_json.at("choices").at(0).at("message");
            }
            last_error = response_result
                ? "HTTP " + std::to_string(response_result->status) + ": " + response_result->body
                : httplib::to_string(response_result.error());
        }

        if (attempt < 4) {
            if (on_retry) on_retry(last_error);
            std::this_thread::sleep_for(std::chrono::seconds(1 << (attempt - 1)));
        }
    }
    throw std::runtime_error("API request to " + url + " failed after 3 retries: " + last_error);
}

std::vector<std::string> recent_log_lines(superagent::StructuredLogger& logger) {
    const auto filter = read_input("日誌篩選文字（可留空）: ");
    const auto entries = logger.recent(50, filter);
    std::vector<std::string> lines;
    if (entries.empty()) {
        return {"找不到符合條件的日誌紀錄。"};
    }
    for (const auto& entry : entries) {
        auto message = entry.value("message", std::string{});
        if (message.size() > 180) message = message.substr(0, 177) + "...";
        lines.push_back(entry.value("timestamp", std::string{}) + " [" +
            entry.value("level", std::string{"info"}) + "] [" +
            entry.value("source", std::string{}) + "] " + message);
    }
    return lines;
}

std::vector<std::string> fetch_model_names(const superagent::AppConfig& config) {
    try {
        if (is_copilot_provider(config.model_provider)) {
            copilot::Client client;
            try {
                client.start().get();
                const auto catalog = client.list_models().get();
                std::vector<std::string> models;
                std::set<std::string> unique;
                for (const auto& model : catalog) {
                    auto normalized = model.id;
                    std::transform(normalized.begin(), normalized.end(), normalized.begin(),
                        [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
                    if (!model.id.empty() && unique.insert(normalized).second) {
                        models.push_back(model.id);
                    }
                }
                const auto stop_errors = client.stop().get();
                if (!stop_errors.empty()) {
                    throw std::runtime_error("Copilot SDK failed to stop cleanly.");
                }
                return models;
            } catch (...) {
                client.force_stop();
                throw;
            }
        }

        httplib::Client client(endpoint_origin(config.base_url));
        client.set_connection_timeout(5, 0);
        client.set_read_timeout(std::min(config.request_timeout_seconds, 10), 0);
        httplib::Headers headers{{"Authorization", "Bearer " + config.api_key}};
        auto path = request_path(config.base_url);
        constexpr std::string_view suffix = "/chat/completions";
        if (path.size() >= suffix.size()) path.resize(path.size() - suffix.size());
        path += "/models";
        const auto response = client.Get(path, headers);
        if (!response) {
            std::cerr << "無法從端點取得模型清單: " << httplib::to_string(response.error()) << '\n';
            return {};
        }
        if (response->status < 200 || response->status >= 300) {
            std::cerr << "無法從端點取得模型清單: HTTP " << response->status << '\n';
            return {};
        }
        const auto data = json::parse(response->body).value("data", json::array());
        if (!data.is_array()) {
            std::cerr << "無法從端點取得模型清單: 回應缺少 data 陣列。\n";
            return {};
        }
        std::vector<std::string> models;
        std::set<std::string> unique;
        for (const auto& model : data) {
            std::string name;
            if (model.contains("id") && model.at("id").is_string()) name = model.at("id").get<std::string>();
            else if (model.contains("name") && model.at("name").is_string()) name = model.at("name").get<std::string>();
            auto normalized = name;
            std::transform(normalized.begin(), normalized.end(), normalized.begin(),
                [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
            if (!name.empty() && unique.insert(normalized).second) models.push_back(std::move(name));
        }
        return models;
    } catch (const std::exception& error) {
        std::cerr << "無法取得模型清單: " << error.what() << '\n';
        return {};
    }
}

void edit_settings(superagent::AppConfig& config, superagent::StructuredLogger& logger) {
    const std::vector<std::string> names{
        "code_reader", "code_writer", "file_search", "code_analyzer", "test_generator",
        "debugger", "refactoring", "performance_monitor", "logging", "database_query",
        "shell_exec", "git_tool"
    };
    const std::vector<std::string> settings{
        "API 端點 URL", "API 密鑰", "模型名稱", "請求逾時秒數", "最大迭代次數",
        "Temperature", "最大輸出 Token", "上下文字元上限", "顯示處理過程",
        "系統預設提示詞", "地區", "語系", "Agent 工作區路徑",
        "日誌記錄啟用", "日誌切檔週期", "日誌檔案大小上限 (MiB)",
        "管理工具啟用狀態", "推論供應者", "返回對話"
    };
    while (true) {
        auto options = settings;
        options[0] += " (" + config.base_url + ")";
        options[1] += config.api_key.empty() ? " (未設定)" : " (已設定)";
        options[2] += " (" + config.model + ")";
        options[3] += " (" + std::to_string(config.request_timeout_seconds) + ")";
        options[4] += " (" + std::to_string(config.max_iterations) + ")";
        options[5] += " (" + std::to_string(config.temperature) + ")";
        options[6] += " (" + std::to_string(config.max_tokens) + ")";
        options[7] += " (" + std::to_string(config.max_context_characters) + ")";
        options[8] += config.show_thinking_process ? " (開啟)" : " (關閉)";
        options[9] += config.system_prompt.empty() ? " (未設定)" : " (已設定)";
        options[10] += " (" + config.region + ")";
        options[11] += " (" + config.language + ")";
        options[12] += " (" + config.workspace.string() + ")";
        options[13] += config.logging_enabled ? " (開啟)" : " (關閉)";
        options[14] += " (" + config.log_rolling_interval + ")";
        options[15] += " (" + std::to_string(config.log_file_size_limit / (1024 * 1024)) + " MiB)";
        options[16] += " (" + std::to_string(names.size()) + " 個工具)";
        options[17] += " (" + config.model_provider + ")";

        const auto selected = superagent::ConsoleView::select_menu(
            "設定 (儲存後返回會立即套用)", options);
        if (selected < 0 || selected == static_cast<int>(settings.size() - 1)) return;
        std::cout << "\033[2J\033[H";
        if (selected == 16) {
            while (true) {
                std::vector<std::string> tool_options;
                tool_options.reserve(names.size() + 1);
                for (std::size_t index = 0; index < names.size(); ++index) {
                    const auto found = config.tools.find(names[index]);
                    const bool enabled = found == config.tools.end() || found->second;
                    tool_options.push_back(names[index] + (enabled ? " (啟用)" : " (停用)"));
                }
                tool_options.push_back("返回設定");
                const auto tool_choice = superagent::ConsoleView::select_menu("設定工具啟用狀態", tool_options);
                if (tool_choice < 0 || static_cast<std::size_t>(tool_choice) == names.size()) break;
                const auto previous_config = config;
                const auto& name = names[static_cast<std::size_t>(tool_choice)];
                const auto found = config.tools.find(name);
                config.tools[name] = found == config.tools.end() || !found->second;
                try {
                    config.save();
                } catch (const std::exception& error) {
                    config = previous_config;
                    std::cerr << "無法保存設定: " << error.what() << '\n';
                    continue;
                }
                logger = superagent::StructuredLogger(config);
            }
            continue;
        }
        const auto previous_config = config;
        bool changed = true;
        if (selected == 0) {
            config.base_url = read_input("API 端點 URL: ");
        } else if (selected == 1) {
            const auto key = superagent::ConsoleView::read_secret("輸入新的 API 密鑰 (留空保持不變): ");
            if (key.empty()) changed = false;
            else config.api_key = key;
        } else if (selected == 2) {
            auto model_names = fetch_model_names(config);
            if (model_names.empty()) {
                std::cout << "無法取得模型清單，請手動輸入模型名稱。\n";
                const auto model = read_input("模型名稱: ");
                if (model.empty()) changed = false;
                else config.model = model;
            } else {
                if (std::find_if(model_names.begin(), model_names.end(), [&](const std::string& model) {
                        return model == config.model;
                    }) == model_names.end()) {
                    model_names.push_back(config.model + " (目前，端點清單外)");
                }
                model_names.push_back("手動輸入模型名稱…");
                const auto model_choice = superagent::ConsoleView::select_menu("選擇模型", model_names);
                if (model_choice < 0) changed = false;
                else if (static_cast<std::size_t>(model_choice) == model_names.size() - 1) {
                    const auto model = read_input("模型名稱: ");
                    if (model.empty()) changed = false;
                    else config.model = model;
                } else {
                    config.model = model_names[static_cast<std::size_t>(model_choice)];
                    constexpr const char* current_suffix = " (目前，端點清單外)";
                    const std::string suffix = current_suffix;
                    if (config.model.size() >= suffix.size() &&
                        config.model.compare(config.model.size() - suffix.size(), suffix.size(), suffix) == 0) {
                        config.model.resize(config.model.size() - suffix.size());
                    }
                }
            }
        } else if (selected == 3) {
            int value = 0;
            if (parse_number(read_input("請求逾時秒數（大於 0）: "), value) && value > 0) config.request_timeout_seconds = value;
            else changed = false;
        } else if (selected == 4) {
            int value = 0;
            if (parse_number(read_input("最大迭代次數（1-100）: "), value) && value >= 1 && value <= 100) config.max_iterations = value;
            else changed = false;
        } else if (selected == 5) {
            double value = 0;
            if (parse_number(read_input("Temperature（0-2）: "), value) && value >= 0 && value <= 2) config.temperature = value;
            else changed = false;
        } else if (selected == 6) {
            int value = 0;
            if (parse_number(read_input("最大輸出 Token（1-64000）: "), value) && value >= 1 && value <= 64000) config.max_tokens = value;
            else changed = false;
        } else if (selected == 7) {
            int value = 0;
            if (parse_number(read_input("上下文字元上限（大於 0）: "), value) && value > 0) config.max_context_characters = value;
            else changed = false;
        } else if (selected == 8) {
            config.show_thinking_process = !config.show_thinking_process;
        } else if (selected == 9) {
            std::cout << "目前系統預設提示詞：" <<
                (config.system_prompt.empty() ? "（未設定）" : "\n" + config.system_prompt) << '\n';
            std::cout << "輸入新的多行提示詞；單獨輸入 :save 儲存，單獨輸入 :clear 清除。\n";
            std::vector<std::string> lines;
            while (true) {
                std::string line;
                if (!std::getline(std::cin, line) || line == ":save") break;
                if (line == ":clear") {
                    lines.clear();
                    break;
                }
                lines.push_back(std::move(line));
            }
            config.system_prompt.clear();
            for (std::size_t index = 0; index < lines.size(); ++index) {
                if (index > 0) config.system_prompt.push_back('\n');
                config.system_prompt += lines[index];
            }
        } else if (selected == 10) {
            const auto value = read_input("地區: ");
            if (!value.empty()) config.region = value;
            else changed = false;
        } else if (selected == 11) {
            const auto value = read_input("語系: ");
            if (!value.empty()) config.language = value;
            else changed = false;
        } else if (selected == 12) {
            const auto path = read_input("Agent 工作區路徑: ");
            if (!path.empty() && fs::is_directory(path)) config.workspace = fs::canonical(path);
            else changed = false;
        } else if (selected == 13) {
            config.logging_enabled = !config.logging_enabled;
        } else if (selected == 14) {
            const auto interval = superagent::ConsoleView::select_menu(
                "日誌切檔週期", {"每日切檔 (Day)", "每小時切檔 (Hour)"});
            if (interval < 0) changed = false;
            else config.log_rolling_interval = interval == 0 ? "day" : "hour";
        } else if (selected == 15) {
            std::uintmax_t mib = 0;
            if (parse_number(read_input("檔案大小上限 MiB（1-1024）: "), mib) && mib >= 1 && mib <= 1024)
                config.log_file_size_limit = mib * 1024 * 1024;
            else changed = false;
        } else if (selected == 17) {
            const auto provider = superagent::ConsoleView::select_menu(
                "推論供應者", {"OpenAI 相容 API", "GitHub Copilot"});
            if (provider < 0) {
                changed = false;
            } else {
                config.model_provider = provider == 0 ? "OpenAICompatible" : "GitHubCopilot";
                config.model = provider == 0 ? config.openai_model : config.copilot_model;
            }
        }
        if (!changed) {
            std::cout << "設定未變更：輸入無效或已取消。\n";
            continue;
        }
        try {
            if (is_copilot_provider(config.model_provider)) config.copilot_model = config.model;
            else config.openai_model = config.model;
            config.save();
            logger = superagent::StructuredLogger(config);
            std::cout << "設定已保存並套用。\n";
        } catch (const std::exception& error) {
            config = previous_config;
            std::cerr << "無法保存設定: " << error.what() << '\n';
        }
    }
}

} // namespace

int main(int argc, char** argv) {
    try {
        std::string config_path = environment("SUPERAGENT_CONFIG", "superagent-cpp.json");
        std::optional<std::string> base_url_override;
        std::optional<std::string> model_override;
        std::optional<std::string> provider_override;
        std::optional<std::string> api_key_override;
        std::optional<std::string> workspace_override;
        std::optional<std::string> conversation_override;
        std::optional<int> max_iterations_override;
        for (int index = 1; index < argc; ++index) {
            const std::string argument(argv[index]);
            if (equals_case_insensitive(argument, "--help") || equals_case_insensitive(argument, "-h")) {
                print_help();
                return 0;
            }
            if (starts_with_case_insensitive(argument, "--config=")) config_path = option_value(argument, "--config");
            else if (starts_with_case_insensitive(argument, "--base-url=")) base_url_override = option_value(argument, "--base-url");
            else if (starts_with_case_insensitive(argument, "--api-key=")) api_key_override = option_value(argument, "--api-key");
            else if (starts_with_case_insensitive(argument, "--model=")) model_override = option_value(argument, "--model");
            else if (starts_with_case_insensitive(argument, "--provider=")) provider_override = option_value(argument, "--provider");
            else if (starts_with_case_insensitive(argument, "--workspace=")) workspace_override = option_value(argument, "--workspace");
            else if (starts_with_case_insensitive(argument, "--conversation-id=")) conversation_override = option_value(argument, "--conversation-id");
            else if (starts_with_case_insensitive(argument, "--max-iterations=")) max_iterations_override = std::stoi(option_value(argument, "--max-iterations"));
            else throw std::runtime_error("unknown option: " + argument);
        }

        auto config = superagent::AppConfig::load(config_path);
        if (const char* value = std::getenv("OPENAI_BASE_URL")) config.base_url = value;
        if (const char* value = std::getenv("OPENAI_MODEL")) config.openai_model = value;
        if (const char* value = std::getenv("SUPERAGENT_WORKSPACE")) config.workspace = value;
        if (const char* value = std::getenv("SUPERAGENT_API_KEY")) config.api_key = value;
        if (const char* value = std::getenv("OPENAI_API_KEY")) config.api_key = value;
        if (const char* value = std::getenv("SUPERAGENT_MODEL_PROVIDER")) config.model_provider = value;
        if (const char* value = std::getenv("GITHUB_COPILOT_MODEL")) config.copilot_model = value;
        if (provider_override) config.model_provider = *provider_override;
        if (base_url_override) config.base_url = *base_url_override;
        if (is_copilot_provider(config.model_provider)) {
            config.model = config.copilot_model;
        } else if (is_openai_provider(config.model_provider)) {
            config.model = config.openai_model;
        } else {
            throw std::runtime_error("model provider must be OpenAICompatible or GitHubCopilot");
        }
        if (model_override) config.model = *model_override;
        if (workspace_override) config.workspace = *workspace_override;
        if (conversation_override) config.conversation_id = *conversation_override;
        if (max_iterations_override) config.max_iterations = *max_iterations_override;
        if (api_key_override && !api_key_override->empty()) config.api_key = *api_key_override;
        if (config.max_iterations < 1 || config.max_iterations > 100) {
            throw std::runtime_error("max iterations must be between 1 and 100");
        }
        if (config.max_tokens < 1 || config.max_tokens > 64000 || config.max_context_characters < 1 ||
            config.request_timeout_seconds < 1 || config.temperature < 0 || config.temperature > 2 ||
            !std::isfinite(config.temperature) || (config.log_rolling_interval != "day" && config.log_rolling_interval != "hour") ||
            config.log_file_size_limit == 0 || config.log_path.empty() ||
            config.language.empty() || config.region.empty()) {
            throw std::runtime_error("configuration contains an invalid agent or logging value");
        }
        fs::create_directories(config.workspace);
        config.workspace = fs::canonical(config.workspace);
        if (config.conversation_id.empty()) config.conversation_id = "default";
        superagent::StructuredLogger logger(config);
        std::vector<json> messages = superagent::ConversationStore(config).load();
        const auto system_prompt = [&]() {
            std::string text = R"(你是一個強悍的 AI Agent，名為 SuperAgent。你有以下能力：

1. **思考與規劃**: 你可以分析複雜任務，將其拆解為小步驟。
2. **工具使用**: 你可以選擇並執行適當的工具來完成任務。
3. **程式開發**: 你可以撰寫、修改與維護程式碼。
4. **自主學習**: 你可以從工具結果中學習並調整策略。

你的目標是協助使用者完成各種任務，包括但不限於：
- 撰寫程式碼
- 除錯與修复程式
- 系統管理與自動化
- 資料分析與處理

請始終：
1. 先思考任務需要什麼工具
2. 規劃執行順序
3. 執行工具並觀察結果
4. 根據結果調整策略

當任務完成時，提供清晰的總結。)";
            text += "\n\n回覆語系：" + config.language + "。請使用此語系回覆使用者。";
            text += "\n\n地區脈絡：" + config.region +
                "。涉及日期、時間、貨幣、單位或地區慣例時，優先採用此地區標準；不確定時明確說明。";
            if (!config.system_prompt.empty()) {
                auto custom_prompt = config.system_prompt;
                const auto first = custom_prompt.find_first_not_of(" \t\r\n");
                const auto last = custom_prompt.find_last_not_of(" \t\r\n");
                custom_prompt = first == std::string::npos
                    ? std::string{} : custom_prompt.substr(first, last - first + 1);
                if (!custom_prompt.empty()) {
                    text += "\n\n使用者設定的系統預設提示詞：\n" + custom_prompt;
                }
            }
            return text;
        };
        superagent::upsert_system_message(messages, system_prompt());
        logger.write("info", "Application started", "SuperAgent");
        superagent::ConsoleView console("SuperAgent | " + config.model + " | " + config.workspace.string());
        console.append_text("SuperAgent AI Agent CLI\n");
        console.append_text("Model provider: " + config.model_provider + "\n");
        console.append_text("Model: " + config.model + "\n");
        if (!is_copilot_provider(config.model_provider)) {
            console.append_text("Endpoint: " + config.base_url + "\n");
        }
        console.append_text("Max iterations: " + std::to_string(config.max_iterations) + "\n");
        console.append_text("Show thinking process: " + std::string(config.show_thinking_process ? "True" : "False") + "\n");
        console.append_text("Agent 工作區: " + config.workspace.string() + "\n");
        console.append_text("允許工作區外存取: False\n");
        console.append_text("工作區外命令存取: None\n");
        console.append_text("日誌記錄: " + std::string(config.logging_enabled ? "啟用" : "停用") +
            " (" + config.log_path.string() + ", " +
            (config.log_rolling_interval == "hour" ? "Hour" : "Day") + ", " +
            std::to_string(config.log_file_size_limit / (1024 * 1024)) + " MiB)\n");
        console.append_text("F2 設定 | F3 日誌 | F5 重載 | help 說明 | exit/quit/q 離開\n\n");

        std::vector<std::string> prompt_history;
        auto handle_settings = [&]() {
            console.suspend();
            try {
                edit_settings(config, logger);
            } catch (const std::exception& error) {
                std::cerr << "設定操作失敗: " << error.what() << '\n';
            }
            console.resume();
            superagent::upsert_system_message(messages, system_prompt());
            console.set_header("SuperAgent | " + config.model + " | " + config.workspace.string());
            console.append_text("設定選單已關閉，目前設定將用於後續對話。\n");
        };
        auto handle_logs = [&]() {
            console.suspend();
            std::vector<std::string> lines;
            try {
                lines = recent_log_lines(logger);
            } catch (const std::exception& error) {
                lines.push_back("讀取日誌失敗: " + std::string(error.what()));
            }
            console.resume();
            console.append_section_header("最近日誌 (" + std::to_string(lines.size()) + " 筆)", "magenta");
            for (const auto& line : lines) console.append_text(line + "\n");
        };
        auto reload_config = [&]() {
            try {
                auto next = superagent::AppConfig::load(config.config_path);
                next.config_path = config.config_path;
                next.conversation_id = config.conversation_id;
                if (const char* value = std::getenv("OPENAI_BASE_URL")) next.base_url = value;
                if (const char* value = std::getenv("OPENAI_MODEL")) next.openai_model = value;
                if (const char* value = std::getenv("SUPERAGENT_WORKSPACE")) next.workspace = value;
                if (const char* value = std::getenv("SUPERAGENT_API_KEY")) next.api_key = value;
                if (const char* value = std::getenv("OPENAI_API_KEY")) next.api_key = value;
                if (const char* value = std::getenv("SUPERAGENT_MODEL_PROVIDER")) next.model_provider = value;
                if (const char* value = std::getenv("GITHUB_COPILOT_MODEL")) next.copilot_model = value;
                if (provider_override) next.model_provider = *provider_override;
                if (base_url_override) next.base_url = *base_url_override;
                if (is_copilot_provider(next.model_provider)) {
                    next.model = next.copilot_model;
                } else if (is_openai_provider(next.model_provider)) {
                    next.model = next.openai_model;
                } else {
                    throw std::runtime_error("model provider must be OpenAICompatible or GitHubCopilot");
                }
                if (model_override) next.model = *model_override;
                if (workspace_override) next.workspace = *workspace_override;
                if (api_key_override && !api_key_override->empty()) next.api_key = *api_key_override;
                if (max_iterations_override) next.max_iterations = *max_iterations_override;
                if (next.max_iterations < 1 || next.max_iterations > 100 ||
                    next.max_tokens < 1 || next.max_tokens > 64000 ||
                    next.max_context_characters < 1 || next.request_timeout_seconds < 1 ||
                    !std::isfinite(next.temperature) || next.temperature < 0 || next.temperature > 2 ||
                    next.log_file_size_limit == 0 || next.log_path.empty() ||
                    next.language.empty() || next.region.empty() ||
                    (next.log_rolling_interval != "day" && next.log_rolling_interval != "hour")) {
                    throw std::runtime_error("設定中含有無效的 API、Agent 或日誌值。");
                }
                fs::create_directories(next.workspace);
                next.workspace = fs::canonical(next.workspace);
                config = std::move(next);
                logger = superagent::StructuredLogger(config);
                superagent::upsert_system_message(messages, system_prompt());
                console.set_header("SuperAgent | " + config.model + " | " + config.workspace.string());
                logger.write("info", "Configuration reloaded", "SuperAgent");
                console.append_text("設定已熱重載並套用至後續對話。\n");
            } catch (const std::exception& error) {
                console.append_text("設定重載失敗，保留目前設定: " + std::string(error.what()) + "\n");
            }
        };

        while (true) {
            auto prompt_result = console.read_input(prompt_history);
            if (prompt_result.shortcut == "settings") {
                handle_settings();
                continue;
            }
            if (prompt_result.shortcut == "logs") {
                handle_logs();
                continue;
            }
            if (prompt_result.shortcut == "reload") {
                reload_config();
                continue;
            }
            if (!prompt_result.submitted) break;
            const auto prompt = prompt_result.text;
            const auto command = [&]() {
                auto value = prompt;
                const auto first = value.find_first_not_of(" \t\r\n");
                if (first == std::string::npos) return std::string{};
                const auto last = value.find_last_not_of(" \t\r\n");
                return value.substr(first, last - first + 1);
            }();
            if (command.empty()) continue;
            if (command == "/exit" || command == "exit" || command == "quit" || command == "q") break;
            if (command == "/settings") {
                handle_settings();
                continue;
            }
            if (command == "/logs") {
                handle_logs();
                continue;
            }
            if (command == "help") {
                console.append_section_header("可用命令", "cyan");
                console.append_text("exit/quit/q: 結束程式\n");
                console.append_text("help: 顯示此訊息\n");
                console.append_text("F2: 開啟設定選單\nF3: 查看日誌\nF5: 熱重載設定檔\n");
                console.append_text("Enter 換行，F4 或 Ctrl+Enter 送出提示。\n\n");
                continue;
            }
            if (command == "/new") {
                config.conversation_id = new_conversation_id();
                messages.clear();
                superagent::upsert_system_message(messages, system_prompt());
                config.save();
                superagent::ConversationStore(config).save(messages);
                logger.write("info", "Started a new conversation", "SuperAgent");
                console.append_text("新會話：" + config.conversation_id + "\n");
                continue;
            }

            prompt_history.push_back(prompt);
            console.append_section_header("正在處理任務", "cyan");
            logger.write("info", "Agent request started", "Agent");
            logger.write("info", "User message received", "Agent");
            messages.push_back({{"role", "user"}, {"content", prompt}});
            bool completed = false;
            int iteration = 0;
            std::string error_message;
            try {
                for (; iteration < config.max_iterations; ++iteration) {
                    const int current_iteration = iteration + 1;
                    trim_context(messages, config.max_context_characters);
                    superagent::Toolset tools(config.workspace, config.tools, logger,
                        [&](const std::string& requested) {
                            if (!is_interactive_terminal()) return false;
                            console.suspend();
                            std::cout << "\n[確認] Agent 要執行 Git 變更：" << requested << "\n允許？(y/N): ";
                            std::string answer;
                            const bool read = static_cast<bool>(std::getline(std::cin, answer));
                            console.resume();
                            return read && (answer == "y" || answer == "Y");
                        });
                    bool stream_header_written = false;
                    const auto assistant = request_completion(
                        config, config.api_key, messages, tools.definitions(),
                        [&](const std::string& chunk) {
                            if (!stream_header_written) {
                                console.append_section_header("模型串流 / 第 " +
                                    std::to_string(current_iteration) + " 輪", "cyan");
                                stream_header_written = true;
                            }
                            console.append_text(chunk);
                        },
                        [&](const std::string& error) {
                            logger.write("warning", "API retry: " + error, "Agent");
                            if (superagent::ConsoleView::is_interactive()) {
                                console.append_text("\n[重試] GenerateResponse: " + error + "\n");
                            } else {
                                std::cerr << "[重試] GenerateResponse: " << error << '\n';
                            }
                        },
                        [&](const std::string& name, const json& arguments) {
                            logger.write("info", "Tool started: " + name, "Agent");
                            const auto result = tools.execute(name, arguments);
                            logger.write("info", "Tool completed: " + name, "Agent");
                            return result;
                        });

                    messages.push_back(assistant);
                    const auto content = assistant.contains("content") && assistant.at("content").is_string()
                        ? assistant.at("content").get<std::string>() : std::string{};
                    if (content.rfind("錯誤:", 0) == 0) {
                        break;
                    }

                    const auto calls = assistant.value("tool_calls", json::array());
                    if (calls.is_array() && !calls.empty()) {
                        for (const auto& call : calls) {
                            const auto& function = call.at("function");
                            const auto name = function.at("name").get<std::string>();
                            const json raw_arguments = function.value("arguments", json("{}"));
                            const auto arguments = raw_arguments.is_string()
                                ? (raw_arguments.get<std::string>().empty()
                                    ? json::object() : json::parse(raw_arguments.get<std::string>()))
                                : raw_arguments;
                            if (config.show_thinking_process) {
                                console.append_section_header("▶ 工具執行：" + name, "yellow");
                                console.append_text("實際參數:\n" + arguments.dump(2) + "\n");
                            }
                            logger.write("info", "Tool started: " + name, "Agent");
                            const auto result = tools.execute(name, arguments);
                            logger.write("info", "Tool completed: " + name, "Agent");
                            if (config.show_thinking_process) {
                                console.append_section_header("工具結果：" + name, "magenta");
                                console.append_text(result + "\n");
                            }
                            messages.push_back(json{
                                {"role", "tool"}, {"tool_call_id", call.at("id")},
                                {"name", name}, {"content", result}
                            });
                        }
                        continue;
                    }
                    completed = true;
                    break;
                }
            } catch (const std::exception& error) {
                error_message = "API 請求重試後仍然失敗。請檢查 API 端點與密鑰設定。詳細資訊: " +
                    std::string(error.what());
                logger.write("error", error_message, "Agent");
                messages.push_back({{"role", "assistant"}, {"content", "錯誤: " + error_message}});
            }

            const auto final_content = !messages.empty() && messages.back().contains("content") &&
                messages.back().at("content").is_string()
                ? messages.back().at("content").get<std::string>() : std::string{};
            if (completed) logger.write("info", "Assistant response completed", "Agent");
            else if (error_message.empty()) error_message = "超過最大迭代次數";
            logger.write(completed ? "info" : "warning",
                "Agent request completed: success=" + std::string(completed ? "true" : "false") +
                ", iterations=" + std::to_string(iteration + (iteration < config.max_iterations ? 1 : 0)),
                "Agent");

            const int total_iterations = std::min(iteration + 1, config.max_iterations);
            console.append_text("\n狀態: " + std::string(completed ? "Completed" : "Failed") + "\n");
            console.append_text("迭代次數: " + std::to_string(total_iterations) + "\n");
            console.append_text("成功: " + std::string(completed ? "是" : "否") + "\n");
            if (!error_message.empty()) console.append_text("錯誤: " + error_message + "\n");
            console.append_section_header("最終回答", "green");
            if (final_content.empty()) console.append_text("（模型未返回文字內容）\n");
            else console.append_text(final_content + "\n");
            console.append_text("\n");

            try {
                superagent::ConversationStore(config).save(messages);
            } catch (const std::exception& error) {
                console.append_text("[警告] 儲存會話失敗: " + std::string(error.what()) + "\n");
            }
        }
        logger.write("info", "Application stopped", "SuperAgent");
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "Error: " << error.what() << '\n';
        return 1;
    }
}