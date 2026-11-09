#include "runtime.hpp"

#include <algorithm>
#include <chrono>
#include <cctype>
#include <fstream>
#include <iomanip>
#include <initializer_list>
#include <sstream>
#include <stdexcept>

namespace superagent {
namespace {

template <typename T>
T setting(const json& object, std::initializer_list<const char*> names, const T& fallback) {
    for (const auto* name : names) {
        if (object.contains(name) && !object.at(name).is_null()) {
            return object.at(name).get<T>();
        }
    }
    return fallback;
}

json property(const json& object, std::initializer_list<const char*> names, const json& fallback = nullptr) {
    for (const auto* name : names) {
        if (object.contains(name)) return object.at(name);
    }
    return fallback;
}

std::string string_property(const json& object, std::initializer_list<const char*> names,
                            const std::string& fallback = {}) {
    const auto value = property(object, names);
    return value.is_string() ? value.get<std::string>() : fallback;
}

json normalize_chat_message(const json& source) {
    if (!source.is_object()) return json::object();
    json message = {
        {"role", string_property(source, {"role", "Role"})},
        {"content", string_property(source, {"content", "Content"})}
    };
    const auto tool_call_id = string_property(source, {"tool_call_id", "ToolCallId"});
    const auto name = string_property(source, {"name", "Name"});
    if (!tool_call_id.empty()) message["tool_call_id"] = tool_call_id;
    if (!name.empty()) message["name"] = name;

    const auto calls = property(source, {"tool_calls", "ToolCalls"});
    if (calls.is_array() && !calls.empty()) {
        message["tool_calls"] = json::array();
        for (const auto& source_call : calls) {
            const auto function = property(source_call, {"function"});
            const auto call_id = string_property(source_call, {"id", "Id"});
            const auto function_name = string_property(function.is_object() ? function : source_call, {"name", "Name"});
            const auto arguments = property(function.is_object() ? function : source_call, {"arguments", "Arguments"}, json::object());
            const auto raw_arguments = arguments.is_string() ? arguments.get<std::string>() : arguments.dump();
            message["tool_calls"].push_back({
                {"id", call_id},
                {"type", "function"},
                {"function", {{"name", function_name}, {"arguments", raw_arguments}}}
            });
        }
    }
    return message;
}

json to_dotnet_message(const json& source) {
    const auto message = normalize_chat_message(source);
    json result = {
        {"Role", message.value("role", std::string{})},
        {"Content", message.value("content", std::string{})},
        {"Timestamp", utc_timestamp()}
    };
    if (message.contains("tool_call_id")) result["ToolCallId"] = message.at("tool_call_id");
    if (message.contains("name")) result["Name"] = message.at("name");
    if (message.contains("tool_calls")) {
        result["ToolCalls"] = json::array();
        for (const auto& call : message.at("tool_calls")) {
            const auto& function = call.at("function");
            const auto raw_arguments = function.value("arguments", std::string("{}"));
            result["ToolCalls"].push_back({
                {"Id", call.value("id", std::string{})},
                {"Name", function.value("name", std::string{})},
                {"Arguments", json::parse(raw_arguments.empty() ? "{}" : raw_arguments)}
            });
        }
    } else {
        result["ToolCalls"] = json::array();
    }
    return result;
}

std::string period_stamp(const std::string& interval) {
    const auto now = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
    std::tm time{};
#ifdef _WIN32
    gmtime_s(&time, &now);
#else
    gmtime_r(&now, &time);
#endif
    std::ostringstream result;
    result << std::put_time(&time, interval == "hour" ? "%Y%m%d-%H" : "%Y%m%d");
    return result.str();
}

bool contains_case_insensitive(const std::string& text, const std::string& query) {
    auto lower = [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); };
    std::string haystack(text.size(), '\0');
    std::string needle(query.size(), '\0');
    std::transform(text.begin(), text.end(), haystack.begin(), lower);
    std::transform(query.begin(), query.end(), needle.begin(), lower);
    return haystack.find(needle) != std::string::npos;
}

} // namespace

std::string utc_timestamp() {
    const auto now = std::chrono::system_clock::now();
    const auto time = std::chrono::system_clock::to_time_t(now);
    const auto milliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()).count() % 1000;
    std::tm utc{};
#ifdef _WIN32
    gmtime_s(&utc, &time);
#else
    gmtime_r(&time, &utc);
#endif
    std::ostringstream result;
    result << std::put_time(&utc, "%Y-%m-%dT%H:%M:%S") << '.'
           << std::setw(3) << std::setfill('0') << milliseconds << 'Z';
    return result.str();
}

void upsert_system_message(std::vector<json>& messages, const std::string& content) {
    if (!messages.empty() && messages.front().is_object() &&
        messages.front().value("role", std::string{}) == "system") {
        messages.front()["content"] = content;
        return;
    }
    json system_message = json::object({{"role", "system"}, {"content", content}});
    messages.insert(messages.begin(), std::move(system_message));
}

AppConfig AppConfig::load(const fs::path& path) {
    AppConfig config;
    config.config_path = path;
    if (!fs::exists(path)) {
        return config;
    }

    std::ifstream input(path);
    if (!input) {
        throw std::runtime_error("cannot open configuration: " + path.string());
    }
    const auto data = json::parse(input);
    const auto openai = setting<json>(data, {"openai", "OpenAI"}, json::object());
    const auto agent = setting<json>(data, {"agent", "Agent"}, json::object());
    const auto copilot = setting<json>(data, {"github_copilot", "GitHubCopilot"}, json::object());
    const auto logging = setting<json>(data, {"logging", "Logging"}, json::object());
    const auto storage = setting<json>(data, {"storage", "Storage"}, json::object());
    config.base_url = setting<std::string>(openai, {"base_url", "BaseUrl"}, config.base_url);
    config.openai_model = setting<std::string>(openai, {"model", "Model"}, config.openai_model);
    config.copilot_model = setting<std::string>(copilot, {"model", "Model"}, config.copilot_model);
    config.model_provider = setting<std::string>(agent, {"model_provider", "ModelProvider"}, config.model_provider);
    auto normalized_provider = config.model_provider;
    std::transform(normalized_provider.begin(), normalized_provider.end(), normalized_provider.begin(),
        [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    config.model = normalized_provider == "githubcopilot" ||
        normalized_provider == "github-copilot" || normalized_provider == "copilot"
        ? config.copilot_model : config.openai_model;
    config.api_key = setting<std::string>(openai, {"api_key", "ApiKey"}, config.api_key);
    config.workspace = setting<std::string>(agent, {"workspace", "WorkspaceRoot"}, config.workspace.string());
    config.max_iterations = setting<int>(agent, {"max_iterations", "MaxIterations"}, config.max_iterations);
    config.max_tokens = setting<int>(agent, {"max_tokens", "MaxTokens"}, config.max_tokens);
    config.max_context_characters = setting<int>(agent, {"max_context_characters", "MaxContextCharacters"}, config.max_context_characters);
    config.request_timeout_seconds = setting<int>(openai, {"request_timeout_seconds", "RequestTimeoutSeconds"}, config.request_timeout_seconds);
    config.temperature = setting<double>(agent, {"temperature", "Temperature"}, config.temperature);
    config.show_thinking_process = setting<bool>(agent, {"show_thinking_process", "ShowThinkingProcess"}, config.show_thinking_process);
    config.system_prompt = setting<std::string>(agent, {"system_prompt", "DefaultSystemPrompt"}, config.system_prompt);
    config.region = setting<std::string>(agent, {"region", "Region"}, config.region);
    config.language = setting<std::string>(agent, {"language", "Language"}, config.language);
    config.tools = setting<std::map<std::string, bool>>(agent, {"tools", "Tools"}, {});
    config.logging_enabled = setting<bool>(logging, {"enabled", "Enabled"}, config.logging_enabled);
    config.log_path = setting<std::string>(logging, {"path", "Path"}, config.log_path.string());
    config.log_rolling_interval = setting<std::string>(logging, {"rolling_interval", "RollingInterval"}, config.log_rolling_interval);
    std::transform(config.log_rolling_interval.begin(), config.log_rolling_interval.end(),
        config.log_rolling_interval.begin(), [](unsigned char ch) {
            return static_cast<char>(std::tolower(ch));
        });
    config.log_file_size_limit = setting<std::uintmax_t>(logging, {"file_size_limit_bytes", "FileSizeLimitBytes"}, config.log_file_size_limit);
    if (!logging.contains("file_size_limit_bytes") && !logging.contains("FileSizeLimitBytes") &&
        (logging.contains("FileSizeLimitMB") || logging.contains("file_size_limit_mb"))) {
        const auto size_mib = setting<std::uintmax_t>(logging, {"FileSizeLimitMB", "file_size_limit_mb"}, 10);
        config.log_file_size_limit = size_mib * 1024 * 1024;
    }
    config.conversation_directory = setting<std::string>(storage, {"directory", "Directory"}, config.conversation_directory.string());
    config.conversation_id = setting<std::string>(storage, {"conversation_id", "ConversationId"}, config.conversation_id);
    return config;
}

void AppConfig::save() const {
    if (!config_path.parent_path().empty()) {
        fs::create_directories(config_path.parent_path());
    }
    json data = json::object();
    if (fs::exists(config_path)) {
        std::ifstream input(config_path);
        if (!input) throw std::runtime_error("cannot read configuration before saving: " + config_path.string());
        data = json::parse(input);
        if (!data.is_object()) throw std::runtime_error("configuration root must be a JSON object");
    }

    auto normalized_provider = model_provider;
    std::transform(normalized_provider.begin(), normalized_provider.end(), normalized_provider.begin(),
        [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    const bool copilot_provider = normalized_provider == "githubcopilot" ||
        normalized_provider == "github-copilot" || normalized_provider == "copilot";
    const auto saved_openai_model = copilot_provider ? openai_model : model;
    const auto saved_copilot_model = copilot_provider ? model : copilot_model;
    const bool dotnet_format = data.contains("OpenAI") || data.contains("Agent") || data.contains("Logging");
    if (dotnet_format) {
        data["OpenAI"]["BaseUrl"] = base_url;
        data["OpenAI"]["Model"] = saved_openai_model;
        data["OpenAI"]["ApiKey"] = api_key;
        data["OpenAI"]["RequestTimeoutSeconds"] = request_timeout_seconds;
        data["Agent"]["ModelProvider"] = model_provider;
        data["GitHubCopilot"]["Model"] = saved_copilot_model;
        data["Agent"]["WorkspaceRoot"] = workspace.string();
        data["Agent"]["MaxIterations"] = max_iterations;
        data["Agent"]["MaxTokens"] = max_tokens;
        data["Agent"]["MaxContextCharacters"] = max_context_characters;
        data["Agent"]["Temperature"] = temperature;
        data["Agent"]["ShowThinkingProcess"] = show_thinking_process;
        data["Agent"]["DefaultSystemPrompt"] = system_prompt;
        data["Agent"]["Region"] = region;
        data["Agent"]["Language"] = language;
        data["Agent"]["Tools"] = tools;
        data["Logging"]["Enabled"] = logging_enabled;
        data["Logging"]["Path"] = log_path.string();
        data["Logging"]["RollingInterval"] = log_rolling_interval == "hour" ? "Hour" : "Day";
        data["Logging"]["FileSizeLimitMB"] = log_file_size_limit / (1024 * 1024);
        data["Storage"]["Directory"] = conversation_directory.string();
        data["Storage"]["ConversationId"] = conversation_id;
    } else {
        data["openai"]["base_url"] = base_url;
        data["openai"]["model"] = saved_openai_model;
        data["openai"]["api_key"] = api_key;
        data["openai"]["request_timeout_seconds"] = request_timeout_seconds;
        data["agent"]["model_provider"] = model_provider;
        data["github_copilot"]["model"] = saved_copilot_model;
        data["agent"]["workspace"] = workspace.string();
        data["agent"]["max_iterations"] = max_iterations;
        data["agent"]["max_tokens"] = max_tokens;
        data["agent"]["max_context_characters"] = max_context_characters;
        data["agent"]["temperature"] = temperature;
        data["agent"]["show_thinking_process"] = show_thinking_process;
        data["agent"]["system_prompt"] = system_prompt;
        data["agent"]["region"] = region;
        data["agent"]["language"] = language;
        data["agent"]["tools"] = tools;
        data["logging"]["enabled"] = logging_enabled;
        data["logging"]["path"] = log_path.string();
        data["logging"]["rolling_interval"] = log_rolling_interval;
        data["logging"]["file_size_limit_bytes"] = log_file_size_limit;
        data["storage"]["directory"] = conversation_directory.string();
        data["storage"]["conversation_id"] = conversation_id;
    }
    const auto temporary_path = config_path.string() + ".tmp";
    {
        std::ofstream output(temporary_path, std::ios::trunc);
        if (!output) {
            throw std::runtime_error("cannot write configuration: " + temporary_path);
        }
        output << data.dump(2) << '\n';
        if (!output) {
            throw std::runtime_error("failed to write configuration: " + temporary_path);
        }
    }
    std::error_code error;
    fs::rename(temporary_path, config_path, error);
    if (error) {
        fs::remove(config_path, error);
        error.clear();
        fs::rename(temporary_path, config_path, error);
        if (error) {
            throw std::runtime_error("cannot replace configuration: " + error.message());
        }
    }
}

StructuredLogger::StructuredLogger(const AppConfig& config) : config_(config) {}

fs::path StructuredLogger::next_log_path() {
    const auto period = period_stamp(config_.log_rolling_interval);
    if (period != current_period_) {
        current_period_ = period;
        current_segment_ = 0;
    }
    auto directory = config_.log_path.parent_path();
    if (directory.empty()) directory = ".";
    fs::create_directories(directory);
    const auto stem = config_.log_path.stem().string();
    const auto extension = config_.log_path.extension().string();
    auto candidate = directory / (stem + "-" + period + "-" + std::to_string(current_segment_) + extension);
    while (fs::exists(candidate) && fs::file_size(candidate) >= config_.log_file_size_limit) {
        ++current_segment_;
        candidate = directory / (stem + "-" + period + "-" + std::to_string(current_segment_) + extension);
    }
    return candidate;
}

void StructuredLogger::write(const std::string& level, const std::string& message, const std::string& source) {
    if (!config_.logging_enabled) return;
    const auto path = next_log_path();
    std::ofstream output(path, std::ios::app);
    if (!output) throw std::runtime_error("cannot write log file: " + path.string());
    output << json{
        {"timestamp", utc_timestamp()}, {"level", level}, {"source", source}, {"message", message}
    }.dump() << '\n';
}

std::vector<json> StructuredLogger::recent(std::size_t limit, const std::string& filter) const {
    std::vector<json> entries;
    auto directory = config_.log_path.parent_path();
    if (directory.empty()) directory = ".";
    if (!fs::exists(directory)) return entries;
    const auto prefix = config_.log_path.stem().string() + "-";
    for (const auto& item : fs::directory_iterator(directory)) {
        if (!item.is_regular_file()) continue;
        const auto filename = item.path().filename().string();
        if (filename.rfind(prefix, 0) != 0 || item.path().extension() != config_.log_path.extension()) continue;
        std::ifstream input(item.path());
        std::string line;
        while (std::getline(input, line)) {
            if (line.empty()) continue;
            try {
                auto entry = json::parse(line);
                if (filter.empty() || contains_case_insensitive(entry.dump(), filter)) entries.push_back(std::move(entry));
            } catch (const json::exception&) {
            }
        }
    }
    std::sort(entries.begin(), entries.end(), [](const json& left, const json& right) {
        return left.value("timestamp", std::string{}) > right.value("timestamp", std::string{});
    });
    if (entries.size() > limit) entries.resize(limit);
    return entries;
}

void StructuredLogger::clear() {
    auto directory = config_.log_path.parent_path();
    if (directory.empty()) directory = ".";
    if (!fs::exists(directory)) return;
    const auto prefix = config_.log_path.stem().string() + "-";
    for (const auto& item : fs::directory_iterator(directory)) {
        const auto filename = item.path().filename().string();
        if (item.is_regular_file() && filename.rfind(prefix, 0) == 0 && item.path().extension() == config_.log_path.extension()) {
            fs::remove(item.path());
        }
    }
}

ConversationStore::ConversationStore(const AppConfig& config) : config_(config) {}

fs::path ConversationStore::path() const {
    auto id = config_.conversation_id.empty() ? "default" : config_.conversation_id;
    for (auto& ch : id) {
        if (!std::isalnum(static_cast<unsigned char>(ch)) && ch != '-' && ch != '_') ch = '_';
    }
    return config_.conversation_directory / (id + ".json");
}

std::vector<json> ConversationStore::load() const {
    const auto file = path();
    if (!fs::exists(file)) return {};
    std::ifstream input(file);
    if (!input) throw std::runtime_error("cannot read conversation: " + file.string());
    const auto record = json::parse(input);
    const auto id = string_property(record, {"id", "Id"});
    if (id != (config_.conversation_id.empty() ? "default" : config_.conversation_id)) return {};
    const auto saved_messages = property(record, {"messages", "Messages"}, json::array());
    if (!saved_messages.is_array()) throw std::runtime_error("conversation messages must be a JSON array");
    std::vector<json> messages;
    messages.reserve(saved_messages.size());
    for (const auto& message : saved_messages) messages.push_back(normalize_chat_message(message));
    return messages;
}

void ConversationStore::save(const std::vector<json>& messages) const {
    const auto file = path();
    fs::create_directories(file.parent_path());
    const auto temporary_path = file.string() + ".tmp";
    {
        std::ofstream output(temporary_path, std::ios::trunc);
        if (!output) throw std::runtime_error("cannot write conversation: " + temporary_path);
        json serialized_messages = json::array();
        for (const auto& message : messages) serialized_messages.push_back(to_dotnet_message(message));
        output << json{
            {"id", config_.conversation_id.empty() ? "default" : config_.conversation_id},
            {"CreatedAt", utc_timestamp()}, {"UpdatedAt", utc_timestamp()},
            {"Messages", std::move(serialized_messages)}
        }.dump(2) << '\n';
        if (!output) throw std::runtime_error("failed to write conversation: " + temporary_path);
    }
    std::error_code error;
    fs::rename(temporary_path, file, error);
    if (error) {
        fs::remove(file, error);
        error.clear();
        fs::rename(temporary_path, file, error);
        if (error) throw std::runtime_error("cannot replace conversation: " + error.message());
    }
}

} // namespace superagent