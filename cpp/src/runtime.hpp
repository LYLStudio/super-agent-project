#pragma once

#include <nlohmann/json.hpp>

#include <filesystem>
#include <map>
#include <string>
#include <vector>

namespace superagent {

using json = nlohmann::json;
namespace fs = std::filesystem;

struct AppConfig {
    std::string model_provider = "OpenAICompatible";
    std::string base_url = "http://192.168.1.102:11434/v1";
    std::string model = "muse-glimmer:30b-q4_K_M-dflash";
    std::string openai_model = "muse-glimmer:30b-q4_K_M-dflash";
    std::string copilot_model = "auto";
    std::string api_key = "dummy";
    fs::path workspace = fs::current_path();
    int max_iterations = 20;
    int max_tokens = 8000;
    int max_context_characters = 32000;
    int request_timeout_seconds = 120;
    double temperature = 0.7;
    bool show_thinking_process = true;
    std::string system_prompt;
    std::string region = "台灣";
    std::string language = "繁體中文（台灣）";
    bool logging_enabled = true;
    fs::path log_path = "logs/superagent.log";
    std::string log_rolling_interval = "day";
    std::uintmax_t log_file_size_limit = 10 * 1024 * 1024;
    fs::path config_path = "superagent-cpp.json";
    fs::path conversation_directory = "conversations";
    std::string conversation_id;
    std::map<std::string, bool> tools;

    static AppConfig load(const fs::path& path);
    void save() const;
};

class StructuredLogger {
public:
    explicit StructuredLogger(const AppConfig& config);
    void write(const std::string& level, const std::string& message, const std::string& source);
    std::vector<json> recent(std::size_t limit = 50, const std::string& filter = {}) const;
    void clear();

private:
    fs::path next_log_path();
    AppConfig config_;
    std::string current_period_;
    std::uintmax_t current_segment_ = 0;
};

class ConversationStore {
public:
    explicit ConversationStore(const AppConfig& config);
    std::vector<json> load() const;
    void save(const std::vector<json>& messages) const;
    fs::path path() const;

private:
    AppConfig config_;
};

std::string utc_timestamp();
void upsert_system_message(std::vector<json>& messages, const std::string& content);

} // namespace superagent