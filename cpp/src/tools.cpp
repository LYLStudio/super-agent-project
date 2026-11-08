#include "tools.hpp"
#include "process.hpp"

#include <sqlite3.h>

#include <algorithm>
#include <chrono>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <regex>
#include <sstream>
#include <stdexcept>

#ifdef SUPERAGENT_HAS_ODBC
#include <sql.h>
#include <sqlext.h>
#endif

namespace superagent {
namespace {

const std::vector<std::string> tool_names{
    "code_reader", "code_writer", "file_search", "code_analyzer", "test_generator",
    "debugger", "refactoring", "performance_monitor", "logging", "database_query",
    "shell_exec", "git_tool"
};

json function_tool(const std::string& name, const std::string& description, const json& properties,
                   const json& required) {
    return {{"type", "function"}, {"function", {
        {"name", name}, {"description", description},
        {"parameters", {{"type", "object"}, {"properties", properties}, {"required", required}}}
    }}};
}

std::string value(const json& object, const std::string& key, const std::string& fallback = {}) {
    return object.contains(key) && object.at(key).is_string() ? object.at(key).get<std::string>() : fallback;
}

std::string lowercase(std::string text) {
    std::transform(text.begin(), text.end(), text.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return text;
}

std::size_t utf16_length(const std::string& text) {
    std::size_t length = 0;
    for (const auto ch : text) {
        const auto byte = static_cast<unsigned char>(ch);
        if ((byte & 0xC0) == 0x80) continue;
        ++length;
        if ((byte & 0xF8) == 0xF0) ++length;
    }
    return length;
}

bool contains_ci(const std::string& text, const std::string& needle) {
    return lowercase(text).find(lowercase(needle)) != std::string::npos;
}

std::vector<std::string> split_lines(const std::string& text) {
    std::istringstream input(text);
    std::vector<std::string> lines;
    std::string line;
    while (std::getline(input, line)) lines.push_back(line);
    if (text.empty() || text.back() == '\n') lines.emplace_back();
    return lines;
}

std::vector<std::string> parse_safe_arguments(const std::string& text) {
    std::vector<std::string> arguments;
    std::string current;
    char quote = '\0';
    bool token_started = false;
    for (const char ch : text) {
        if (ch == '\n' || ch == '\r' || ch == ';' || ch == '|' || ch == '&' || ch == '<' || ch == '>' || ch == '$' || ch == '`')
            throw std::runtime_error("shell operators and expansions are not supported");
        if (quote != '\0') {
            if (ch == quote) quote = '\0';
            else current.push_back(ch);
            token_started = true;
        } else if (ch == '\'' || ch == '"') {
            quote = ch;
            token_started = true;
        } else if (std::isspace(static_cast<unsigned char>(ch))) {
            if (token_started) {
                arguments.push_back(std::move(current));
                current.clear();
                token_started = false;
            }
        } else {
            current.push_back(ch);
            token_started = true;
        }
    }
    if (quote != '\0') throw std::runtime_error("unclosed quote in command");
    if (token_started) arguments.push_back(std::move(current));
    return arguments;
}

int sqlite_read_only_authorizer(void*, int action, const char*, const char*, const char*, const char*) {
    return action == SQLITE_SELECT || action == SQLITE_READ || action == SQLITE_FUNCTION || action == SQLITE_RECURSIVE
        ? SQLITE_OK : SQLITE_DENY;
}

struct SqliteDeadline {
    std::chrono::steady_clock::time_point deadline;
};

int sqlite_timeout(void* context) {
    const auto* limit = static_cast<SqliteDeadline*>(context);
    return std::chrono::steady_clock::now() >= limit->deadline ? 1 : 0;
}

#ifdef SUPERAGENT_HAS_ODBC
std::string odbc_error(SQLSMALLINT type, SQLHANDLE handle) {
    SQLCHAR state[6]{};
    SQLCHAR message[1024]{};
    SQLINTEGER native = 0;
    SQLSMALLINT length = 0;
    if (SQLGetDiagRecA(type, handle, 1, state, &native, message, sizeof(message), &length) == SQL_SUCCESS)
        return reinterpret_cast<const char*>(message);
    return "ODBC operation failed";
}

bool odbc_targets_loopback(const std::string& connection) {
    const auto text = lowercase(connection);
    bool found_host = false;
    for (const auto& key : {std::string("server="), std::string("host=")}) {
        std::size_t offset = 0;
        while ((offset = text.find(key, offset)) != std::string::npos) {
            found_host = true;
            const auto start = offset + key.size();
            const auto end = text.find(';', start);
            auto host = text.substr(start, end == std::string::npos ? std::string::npos : end - start);
            while (!host.empty() && std::isspace(static_cast<unsigned char>(host.front()))) host.erase(host.begin());
            while (!host.empty() && std::isspace(static_cast<unsigned char>(host.back()))) host.pop_back();
            if (host != "localhost" && host != "127.0.0.1" && host != "::1" && host != "[::1]") return false;
            offset = start;
        }
    }
    return found_host;
}

std::string execute_odbc_read_query(const std::string& connection, const std::string& query,
                                    const json& parameters, int timeout, int max_rows, int max_characters) {
    const auto normalized = lowercase(query);
    const auto first = normalized.find_first_not_of(" \t\r\n");
    if (first == std::string::npos || (normalized.compare(first, 6, "select") != 0 && normalized.compare(first, 4, "with") != 0))
        return "Error: ODBC accepts only SELECT or WITH queries";
    if (query.find(';') != std::string::npos) return "Error: one SQL statement without semicolons is allowed";
    if (!odbc_targets_loopback(connection)) return "Error: ODBC connections are restricted to localhost";
    if (!parameters.is_null() && !parameters.empty() && !parameters.is_array())
        return "Error: ODBC parameters must be a positional JSON array for '?' placeholders";

    SQLHENV environment = SQL_NULL_HENV;
    SQLHDBC connection_handle = SQL_NULL_HDBC;
    SQLHSTMT statement = SQL_NULL_HSTMT;
    auto cleanup = [&] {
        if (statement != SQL_NULL_HSTMT) SQLFreeHandle(SQL_HANDLE_STMT, statement);
        if (connection_handle != SQL_NULL_HDBC) {
            SQLDisconnect(connection_handle);
            SQLFreeHandle(SQL_HANDLE_DBC, connection_handle);
        }
        if (environment != SQL_NULL_HENV) SQLFreeHandle(SQL_HANDLE_ENV, environment);
    };
    if (SQLAllocHandle(SQL_HANDLE_ENV, SQL_NULL_HANDLE, &environment) != SQL_SUCCESS) return "Error: cannot allocate ODBC environment";
    SQLSetEnvAttr(environment, SQL_ATTR_ODBC_VERSION, reinterpret_cast<SQLPOINTER>(SQL_OV_ODBC3), 0);
    if (SQLAllocHandle(SQL_HANDLE_DBC, environment, &connection_handle) != SQL_SUCCESS) {
        cleanup();
        return "Error: cannot allocate ODBC connection";
    }
    SQLSetConnectAttr(connection_handle, SQL_ATTR_ACCESS_MODE, reinterpret_cast<SQLPOINTER>(SQL_MODE_READ_ONLY), 0);
    SQLSetConnectAttr(connection_handle, SQL_LOGIN_TIMEOUT, reinterpret_cast<SQLPOINTER>(static_cast<std::uintptr_t>(timeout)), 0);
    SQLCHAR completed[1024]{};
    SQLSMALLINT completed_length = 0;
    const auto connected = SQLDriverConnectA(connection_handle, nullptr,
        reinterpret_cast<SQLCHAR*>(const_cast<char*>(connection.c_str())), SQL_NTS,
        completed, sizeof(completed), &completed_length, SQL_DRIVER_NOPROMPT);
    if (!SQL_SUCCEEDED(connected)) {
        const auto error = odbc_error(SQL_HANDLE_DBC, connection_handle);
        cleanup();
        return "Error connecting to localhost database: " + error;
    }
    if (SQLAllocHandle(SQL_HANDLE_STMT, connection_handle, &statement) != SQL_SUCCESS) {
        const auto error = odbc_error(SQL_HANDLE_DBC, connection_handle);
        cleanup();
        return "Error allocating query: " + error;
    }
    SQLSetStmtAttr(statement, SQL_ATTR_QUERY_TIMEOUT, reinterpret_cast<SQLPOINTER>(static_cast<std::uintptr_t>(timeout)), 0);
    if (parameters.is_array()) {
        for (std::size_t index = 0; index < parameters.size(); ++index) {
            const auto text = parameters[index].is_string() ? parameters[index].get<std::string>() : parameters[index].dump();
            SQLLEN length = static_cast<SQLLEN>(text.size());
            const auto bound = SQLBindParameter(statement, static_cast<SQLUSMALLINT>(index + 1), SQL_PARAM_INPUT,
                SQL_C_CHAR, SQL_VARCHAR, text.size(), 0, const_cast<char*>(text.c_str()), length + 1, &length);
            if (!SQL_SUCCEEDED(bound)) {
                const auto error = odbc_error(SQL_HANDLE_STMT, statement);
                cleanup();
                return "Error binding ODBC parameter: " + error;
            }
        }
    }
    const auto executed = SQLExecDirectA(statement, reinterpret_cast<SQLCHAR*>(const_cast<char*>(query.c_str())), SQL_NTS);
    if (!SQL_SUCCEEDED(executed)) {
        const auto error = odbc_error(SQL_HANDLE_STMT, statement);
        cleanup();
        return "Error executing ODBC query: " + error;
    }
    SQLSMALLINT column_count = 0;
    SQLNumResultCols(statement, &column_count);
    std::ostringstream output;
    int rows = 0;
    bool truncated = false;
    while (true) {
        const auto fetch_result = SQLFetch(statement);
        if (fetch_result == SQL_NO_DATA) break;
        if (!SQL_SUCCEEDED(fetch_result)) {
            const auto error = odbc_error(SQL_HANDLE_STMT, statement);
            cleanup();
            return "Error fetching ODBC results: " + error;
        }
        if (rows >= max_rows) { truncated = true; break; }
        json row = json::object();
        for (SQLUSMALLINT column = 1; column <= static_cast<SQLUSMALLINT>(column_count); ++column) {
            SQLCHAR name[256]{};
            SQLSMALLINT name_length = 0;
            SQLDescribeColA(statement, column, name, sizeof(name), &name_length, nullptr, nullptr, nullptr, nullptr);
            SQLCHAR buffer[4097]{};
            SQLLEN indicator = 0;
            const auto fetched = SQLGetData(statement, column, SQL_C_CHAR, buffer, sizeof(buffer), &indicator);
            std::string text = indicator == SQL_NULL_DATA ? "null" :
                (SQL_SUCCEEDED(fetched) ? reinterpret_cast<const char*>(buffer) : "");
            if (fetched == SQL_SUCCESS_WITH_INFO) text += "... [cell truncated]";
            row[reinterpret_cast<const char*>(name)] = std::move(text);
        }
        const auto serialized = row.dump();
        if (static_cast<int>(output.tellp()) + static_cast<int>(serialized.size()) + 1 > max_characters) {
            truncated = true;
            break;
        }
        output << serialized << '\n';
        ++rows;
    }
    cleanup();
    auto result = "Query executed successfully. " + std::to_string(rows) + " row(s) returned:\n" + output.str();
    if (truncated) result += "Results truncated at the configured row or character limit.";
    return result;
}
#endif

std::string sqlite_value(sqlite3_stmt* statement, int column) {
    const auto type = sqlite3_column_type(statement, column);
    if (type == SQLITE_NULL) return "null";
    if (type == SQLITE_INTEGER) return std::to_string(sqlite3_column_int64(statement, column));
    if (type == SQLITE_FLOAT) {
        std::ostringstream value;
        value << std::setprecision(15) << sqlite3_column_double(statement, column);
        return value.str();
    }
    const auto* bytes = reinterpret_cast<const char*>(sqlite3_column_text(statement, column));
    const auto length = sqlite3_column_bytes(statement, column);
    if (type == SQLITE_BLOB) return "<blob " + std::to_string(length) + " bytes>";
    std::string value(bytes == nullptr ? "" : bytes, static_cast<std::size_t>(length));
    if (value.size() > 4096) value.resize(4096), value += "... [cell truncated]";
    return value;
}

} // namespace

Toolset::Toolset(const fs::path& workspace, const std::map<std::string, bool>& enabled, StructuredLogger& logger,
                                 std::function<bool(const std::string&)> confirm_git_mutation)
        : workspace_(fs::canonical(workspace)), enabled_(enabled), logger_(logger),
            confirm_git_mutation_(std::move(confirm_git_mutation)) {}

bool Toolset::enabled(const std::string& name) const {
    const auto found = enabled_.find(name);
    return found == enabled_.end() || found->second;
}

json Toolset::definitions() const {
    const json code = {{"type", "string"}, {"description", "要分析的程式碼"}};
    const json language = {{"type", "string"}, {"description", "程式語言 (csharp, javascript, python, etc.)"}};
    json result = json::array();
    auto add = [&](json definition) {
        const auto name = definition.at("function").at("name").get<std::string>();
        if (enabled(name)) result.push_back(std::move(definition));
    };
    add(function_tool("code_writer", "在工作區內撰寫或覆寫程式碼文件。工作區根目錄：" + workspace_.string() + "；相對路徑以此為準。",
        {{"file_path", {{"type", "string"}, {"description", "要撰寫的文件路徑"}}},
         {"content", {{"type", "string"}, {"description", "程式碼內容"}}}}, {"file_path", "content"}));
    add(function_tool("code_reader", "讀取工作區內的程式碼文件。工作區根目錄：" + workspace_.string() + "；相對路徑以此為準。",
        {{"file_path", {{"type", "string"}, {"description", "要讀取的文件路徑"}}}}, {"file_path"}));
    add(function_tool("database_query", "執行資料庫查詢。支援 SQLite、MySQL、PostgreSQL。結果最多回傳 100 列與 20000 字元。",
        {{"connection_string", {{"type", "string"}, {"description", "資料庫連接字串"}}},
         {"query", {{"type", "string"}, {"description", "SQL 查詢語句"}}},
         {"parameters", {{"type", "object"}, {"description", "查詢參數 (預設: {})"}}},
         {"timeout_seconds", {{"type", "integer"}, {"description", "查詢超時秒數 (預設 30，範圍 1 到 300)"}}},
         {"max_rows", {{"type", "integer"}, {"description", "最大回傳資料列數 (預設 100，範圍 1 到 1000)"}}},
         {"max_output_characters", {{"type", "integer"}, {"description", "最大回傳字元數 (預設 20000，範圍 256 到 100000)"}}}},
        {"connection_string", "query"}));
    add(function_tool("code_analyzer", "分析程式碼品質，檢測潛在問題並提供改善建議",
        {{"code", code}, {"language", language},
         {"analysisType", {{"type", "string"}, {"description", "分析類型 (quality, security, performance, all)"}}}},
        {"code", "language"}));
    add(function_tool("test_generator", "根據程式碼自動產生單元測試代碼",
        {{"code", code}, {"language", language},
         {"testFramework", {{"type", "string"}, {"description", "測試框架 (xunit, nunit, mstest)"}}}},
        {"code", "language"}));
    add(function_tool("debugger", "分析錯誤訊息並提供修復建議與除錯步驟",
        {{"errorMessage", {{"type", "string"}, {"description", "錯誤訊息"}}},
         {"stackTrace", {{"type", "string"}, {"description", "堆疊追蹤 (可選)"}}},
         {"code", {{"type", "string"}, {"description", "相關程式碼 (可選)"}}},
         {"language", language}}, {"errorMessage"}));
    add(function_tool("refactoring", "分析程式碼並提供重構建議，包括命名改善、方法抽取、類別拆分等",
        {{"code", code}, {"language", language},
         {"refactoringType", {{"type", "string"}, {"description", "重構類型 (naming, extract-method, split-class, all)"}}}},
        {"code", "language"}));
    add(function_tool("performance_monitor", "分析程式碼複雜度並量測分析耗時；不會執行輸入的程式碼。",
        {{"operation", {{"type", "string"}, {"description", "要記錄的操作名稱"}}},
         {"code", {{"type", "string"}, {"description", "要分析複雜度的程式碼片段，不會執行"}}},
         {"iterations", {{"type", "integer"}, {"description", "分析次數 (範圍 1 到 100，預設 1)"}}},
         {"metricType", {{"type", "string"}, {"description", "效能指標類型 (time, memory, all)"}}}}, {"operation"}));
    add(function_tool("logging", "結構化日誌系統，支援多級別日誌記錄與查詢",
        {{"action", {{"type", "string"}, {"description", "日誌操作 (write, query, clear, level)"}}},
         {"level", {{"type", "string"}, {"description", "日誌級別 (debug, info, warning, error, critical)"}}},
         {"message", {{"type", "string"}, {"description", "日誌訊息內容"}}},
         {"source", {{"type", "string"}, {"description", "日誌來源 (module/function)"}}},
         {"query", {{"type", "string"}, {"description", "日誌查詢條件"}}},
         {"limit", {{"type", "integer"}, {"description", "查詢結果數量限制 (預設 50，範圍 1 到 200)"}}}}, {"action"}));
    add(function_tool("file_search", "在工作區內搜尋檔案內容，支援關鍵字與路徑篩選。工作區根目錄：" + workspace_.string() + "；相對路徑以此為準。",
        {{"directory", {{"type", "string"}, {"description", "要搜尋的目錄路徑"}}},
         {"pattern", {{"type", "string"}, {"description", "搜尋關鍵字或檔案模式"}}},
         {"file_extension", {{"type", "string"}, {"description", "檔案副檔名（例如 .cs, .txt）"}}},
         {"max_results", {{"type", "integer"}, {"description", "最大回傳結果數，範圍 1 到 100 (預設 20)"}}}},
        {"directory", "pattern"}));
    add(function_tool("shell_exec", "在 " + workspace_.string() + " 工作區執行 shell 命令。",
        {{"command", {{"type", "string"}, {"description", "要執行的 shell 命令"}}},
         {"timeout_seconds", {{"type", "number"}, {"description", "超時秒數，預設 30"}}},
         {"working_directory", {{"type", "string"}, {"description", "命令工作目錄，預設為工作區根目錄；相對路徑以工作區為基準"}}}},
        {"command"}));
    add(function_tool("git_tool", "在 " + workspace_.string() + " 工作區執行 Git 命令。",
        {{"command", {{"type", "string"}, {"description", "Git 命令 (例如 status, log, diff)"}}},
         {"args", {{"type", "string"}, {"description", "額外參數"}}},
         {"working_directory", {{"type", "string"}, {"description", "Git 工作目錄，預設為工作區根目錄；相對路徑以工作區為基準"}}}},
        {"command"}));
    return result;
}

fs::path Toolset::resolve(const std::string& requested) const {
    const fs::path input(requested);
    const auto candidate = fs::weakly_canonical(input.is_absolute() ? input : workspace_ / input);
    const auto relative = candidate.lexically_relative(workspace_);
    if (relative.empty() || (relative != "." && *relative.begin() == "..")) {
        throw std::runtime_error("path is outside the workspace");
    }
    return candidate;
}

std::string Toolset::execute(const std::string& name, const json& arguments) {
    if (!enabled(name)) return "Error: tool is disabled: " + name;
    try {
        if (name == "code_reader") {
            const auto path = resolve(arguments.at("file_path").get<std::string>());
            std::ifstream input(path, std::ios::binary);
            if (!input) return "Error: File not found: " + path.string();
            const std::string content{std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
            return "File content (" + std::to_string(utf16_length(content)) + " bytes):\n" + content;
        }
        if (name == "code_writer") {
            const auto path = resolve(arguments.at("file_path").get<std::string>());
            fs::create_directories(path.parent_path());
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            const auto content = arguments.at("content").get<std::string>();
            output.write(content.data(), static_cast<std::streamsize>(content.size()));
            return output
                ? "Successfully wrote " + path.string() + " (" + std::to_string(utf16_length(content)) + " bytes)"
                : "Error writing file: " + path.string();
        }
        if (name == "file_search") return execute_file_search(arguments);
        if (name == "code_analyzer") return execute_code_analysis(arguments);
        if (name == "test_generator") return execute_test_generation(arguments);
        if (name == "debugger") return execute_debugger(arguments);
        if (name == "refactoring") return execute_refactoring(arguments);
        if (name == "performance_monitor") return execute_performance_monitor(arguments);
        if (name == "logging") return execute_logging(arguments);
        if (name == "database_query") return execute_database_query(arguments);
        if (name == "shell_exec") return execute_shell(arguments);
        if (name == "git_tool") return execute_git(arguments);
        return "Error: unknown tool: " + name;
    } catch (const std::exception& error) {
        return "Error: " + std::string(error.what());
    }
}

std::string Toolset::execute_file_search(const json& arguments) const {
    const auto directory = resolve(value(arguments, "directory", "."));
    const auto pattern = value(arguments, "pattern");
    const auto extension = value(arguments, "file_extension");
    const auto limit = arguments.value("max_results", 20);
    if (pattern.empty()) return "Error: pattern cannot be empty";
    if (limit < 1 || limit > 100) return "Error: max_results must be between 1 and 100";
    if (!fs::is_directory(directory)) return "Error: directory not found: " + directory.string();

    std::vector<std::string> matches;
    std::size_t scanned = 0;
    std::size_t skipped = 0;
    bool scan_limit_reached = false;
    bool result_limit_reached = false;
    for (auto iterator = fs::recursive_directory_iterator(directory, fs::directory_options::skip_permission_denied);
         iterator != fs::recursive_directory_iterator(); ++iterator) {
        const auto& entry = *iterator;
        if (entry.is_symlink()) {
            if (entry.is_directory()) iterator.disable_recursion_pending();
            continue;
        }
        if (!entry.is_regular_file()) continue;
        if (scanned == 1000) {
            scan_limit_reached = true;
            break;
        }
        ++scanned;
        if (!extension.empty() && entry.path().extension() != extension) continue;
        std::error_code error;
        if (entry.file_size(error) > 1024 * 1024 || error) {
            ++skipped;
            continue;
        }
        std::ifstream input(entry.path(), std::ios::binary);
        if (!input) {
            ++skipped;
            continue;
        }
        const std::string contents{std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
        if (contains_ci(contents, pattern)) {
            matches.push_back(entry.path().string());
            if (matches.size() >= static_cast<std::size_t>(limit)) {
                result_limit_reached = true;
                break;
            }
        }
    }
    std::ostringstream result;
    result << "搜尋結果: " << pattern << "\n找到 " << matches.size() << " 個文件:\n"
           << "已掃描 " << scanned << " 個文件，略過 " << skipped << " 個無法讀取或超過 1 MB 的文件。\n";
    if (scan_limit_reached) result << "已達到掃描上限 (1000 個文件)，結果可能不完整。\n";
    if (result_limit_reached) result << "已達到結果上限 (" << limit << " 個文件)，可能還有符合結果未列出。\n";
    result << '\n';
    for (const auto& match : matches) result << "  - " << match << '\n';
    return result.str();
}

std::string Toolset::execute_code_analysis(const json& arguments) const {
    const auto code = value(arguments, "code");
    const auto language = value(arguments, "language", "csharp");
    const auto normalized_language = lowercase(language);
    const auto lines = split_lines(code);
    std::vector<std::string> issues;
    std::ostringstream result;
    result << "=== 程式碼品質分析結果 ===\n程式語言: " << language
           << "\n程式碼長度: " << utf16_length(code) << " 字元\n行數: " << lines.size() << " 行\n\n";
    for (std::size_t index = 0; index < lines.size(); ++index) {
        auto line = lines[index];
        const auto first = line.find_first_not_of(" \t\r\n");
        const auto last = line.find_last_not_of(" \t\r\n");
        line = first == std::string::npos ? std::string{} : line.substr(first, last - first + 1);
        const auto line_number = std::to_string(index + 1);
        if (utf16_length(line) > 120) {
            issues.push_back("第 " + line_number + " 行: 行長過長 (" +
                std::to_string(utf16_length(line)) + " 字元 > 120)");
        }
        if (normalized_language == "csharp") {
            if (line.find("void ") != std::string::npos &&
                line.find('{') != std::string::npos && line.find('}') != std::string::npos) {
                const auto open = line.find('{');
                const auto close = line.find('}', open + 1);
                if (close != std::string::npos &&
                    line.substr(open + 1, close - open - 1).find_first_not_of(" \t\r\n") == std::string::npos) {
                    issues.push_back("第 " + line_number + " 行: 空方法建議添加實作");
                }
            }
            if (line.find('"') != std::string::npos && line.find('=') != std::string::npos &&
                line.find("//") == std::string::npos) {
                std::smatch match;
                if (std::regex_search(line, match, std::regex("\"[^\"]+\"")) && match.str().size() > 10) {
                    issues.push_back("第 " + line_number + " 行: 發現長字符串常數，建議提取為變數");
                }
            }
        }
        if (line.find("TODO") != std::string::npos || line.find("FIXME") != std::string::npos) {
            issues.push_back("第 " + line_number + " 行: 發現待處理註解 (" + line + ")");
        }
    }
    if (normalized_language == "csharp") {
        if (code.find("namespace") == std::string::npos) issues.push_back("建議：程式碼應包含命名空間定義");
        if (code.find("using") == std::string::npos) issues.push_back("建議：檢查是否缺少必要的 using 陳述句");
    }
    if (!issues.empty()) {
        result << "=== 發現的問題 ===\n";
        for (const auto& issue : issues) result << "⚠️ " << issue << '\n';
        result << '\n';
    } else {
        result << "✅ 未發現明顯品質問題\n\n";
    }
    if (language == "csharp" || language == "CSharp" || language == "C#") {
        result << "=== 改善建議 ===\n"
               << "💡 考慮使用 nullable 類型提升型別安全性\n"
               << "💡 檢查是否有適當的錯誤處理 (try-catch)\n"
               << "💡 考慮將大型方法拆分為較小的單一職責方法\n";
    }
    result << "💡 編寫單元測試以確保程式碼正確性\n"
           << "💡 添加適當的日誌記錄以便除錯";
    return result.str();
}

std::string Toolset::execute_test_generation(const json& arguments) const {
    const auto code = value(arguments, "code");
    if (code.find_first_not_of(" \t\r\n") == std::string::npos) return "程式碼為空，無法產生測試。";
    const auto language = lowercase(value(arguments, "language", "csharp"));
    if (language != "csharp") return "目前僅支援 C# 程式碼的測試產生。程式碼:\n" + code;
    std::smatch match;
    const std::regex class_pattern(R"(class\s+(\w+))");
    const auto class_name = std::regex_search(code, match, class_pattern) ? match[1].str() : "UnknownClass";
    const std::regex method_pattern(R"((?:public|private|protected|internal)\s+(?:static\s+)?[\w<>]+\s+(\w+)\s*\()");
    std::ostringstream result;
    result << "using System;\nusing Xunit;\nusing System.Threading;\nusing System.Threading.Tasks;\n\n"
           << "namespace SuperAgent.Core.Tests;\n\npublic class " << class_name << "Tests\n{\n";
    for (auto it = std::sregex_iterator(code.begin(), code.end(), method_pattern); it != std::sregex_iterator(); ++it) {
        const auto method_name = (*it)[1].str();
        result << "    [Fact]\n    public void " << method_name << "_Test()\n    {\n"
               << "        // Arrange\n        var " << class_name << " = new " << class_name << "();\n\n"
               << "        // Act\n        // TODO: 請根據方法簽名填入正確的參數\n"
               << "        // var result = " << class_name << "." << method_name << "();\n\n"
               << "        // Assert\n        // TODO: 請根據預期結果填入斷言\n"
               << "        // Assert.True(result);\n    }\n\n";
    }
    result << "}\n";
    return result.str();
}

std::string Toolset::execute_debugger(const json& arguments) const {
    const auto message = value(arguments, "errorMessage");
    const auto lower = lowercase(message);
    std::string category = "Unknown error";
    std::string advice = "Check the full error message, identify the failing operation, and reproduce it with a focused test.";
    if (lower.find("null") != std::string::npos) category = "Null or missing value";
    else if (lower.find("out of range") != std::string::npos || lower.find("index") != std::string::npos) category = "Index or bounds error";
    else if (lower.find("not found") != std::string::npos) category = "Missing file or resource";
    else if (lower.find("timeout") != std::string::npos) category = "Timeout";
    else if (lower.find("permission") != std::string::npos || lower.find("unauthorized") != std::string::npos) category = "Permission error";
    return "=== Debugging notes ===\nError: " + message + "\nCategory: " + category + "\nSuggested next step: " + advice + "\n";
}

std::string Toolset::execute_refactoring(const json& arguments) const {
    const auto code = value(arguments, "code");
    const auto lines = split_lines(code);
    std::size_t long_lines = 0;
    for (const auto& line : lines) if (line.size() > 120) ++long_lines;
    const std::regex method_pattern(R"((?:public|private|protected|internal)\s+[\w<>?]+\s+\w+\s*\()");
    const auto methods = static_cast<std::size_t>(std::distance(
        std::sregex_iterator(code.begin(), code.end(), method_pattern), std::sregex_iterator()));
    std::ostringstream result;
    result << "=== Refactoring review ===\nLanguage: " << value(arguments, "language", "unknown")
           << "\nLines: " << lines.size() << "\nMethods detected: " << methods << '\n';
    if (long_lines) result << long_lines << " long line(s); consider extracting focused helpers or wrapping expressions.\n";
    if (methods > 10) result << "More than 10 methods detected; review class responsibilities.\n";
    result << "Review naming clarity, duplicated logic, and whether each function has one responsibility.\n";
    return result.str();
}

std::string Toolset::execute_performance_monitor(const json& arguments) const {
    const auto operation = value(arguments, "operation", "analysis");
    const auto code = value(arguments, "code");
    const int iterations = arguments.value("iterations", 1);
    if (iterations < 1 || iterations > 100) return "Error: iterations must be between 1 and 100";
    const std::regex complexity(R"(\b(if|for|while|foreach|switch|catch)\b|&&|\|\|)", std::regex::icase);
    const auto start = std::chrono::steady_clock::now();
    std::size_t score = 0;
    for (int index = 0; index < iterations; ++index)
        score = static_cast<std::size_t>(std::distance(std::sregex_iterator(code.begin(), code.end(), complexity), std::sregex_iterator()));
    const auto elapsed = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
    std::ostringstream result;
    result << "Operation: " << operation << "\nIterations: " << iterations << "\nComplexity score: " << score
           << "\nAnalysis time: " << elapsed << " ms\nNote: this measures the analysis, not execution of the supplied code.\n";
    return result.str();
}

std::string Toolset::execute_logging(const json& arguments) {
    const auto action = lowercase(value(arguments, "action", "write"));
    if (action == "write") {
        const auto level = value(arguments, "level", "info");
        const auto message = value(arguments, "message");
        const auto source = value(arguments, "source", "agent");
        logger_.write(level, message, source);
        return "Log entry recorded.";
    }
    if (action == "query") {
        const auto entries = logger_.recent(arguments.value("limit", 50), value(arguments, "query"));
        std::ostringstream result;
        for (const auto& entry : entries) result << entry.dump() << '\n';
        return entries.empty() ? "No matching log entries." : result.str();
    }
    if (action == "clear") {
        logger_.clear();
        return "Application log files cleared.";
    }
    return "Error: action must be write, query, or clear";
}

std::string Toolset::execute_database_query(const json& arguments) const {
    const auto connection = value(arguments, "connection_string");
    const auto query = value(arguments, "query");
    if (query.empty()) return "Error: query cannot be empty";
    const int timeout = arguments.value("timeout_seconds", 30);
    const int max_rows = arguments.value("max_rows", 100);
    const int max_characters = arguments.value("max_output_characters", 20000);
    if (timeout < 1 || timeout > 300) return "Error: timeout_seconds must be between 1 and 300";
    if (max_rows < 1 || max_rows > 1000) return "Error: max_rows must be between 1 and 1000";
    if (max_characters < 256 || max_characters > 100000) return "Error: max_output_characters must be between 256 and 100000";

    const auto lowered = lowercase(connection);
    const auto data_source = lowered.find("data source=");
    if (data_source != 0) {
#ifdef SUPERAGENT_HAS_ODBC
        return execute_odbc_read_query(connection, query, arguments.value("parameters", json::object()),
                                       timeout, max_rows, max_characters);
#else
        return "Error: this build supports workspace SQLite; ODBC support must be enabled to use SQL Server/MySQL/PostgreSQL drivers";
#endif
    }
    auto database_name = connection.substr(std::string("Data Source=").size());
    const auto separator = database_name.find(';');
    if (separator != std::string::npos) database_name.resize(separator);
    if (database_name.empty() || database_name == ":memory:") return "Error: a persistent workspace SQLite database path is required";

    try {
        const auto database_path = resolve(database_name);
        sqlite3* database = nullptr;
        const auto open_result = sqlite3_open_v2(database_path.string().c_str(), &database, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nullptr);
        if (open_result != SQLITE_OK) {
            const std::string message = database == nullptr ? "cannot open database" : sqlite3_errmsg(database);
            if (database != nullptr) sqlite3_close(database);
            return "Error opening database: " + message;
        }
        struct DatabaseGuard {
            sqlite3* value;
            ~DatabaseGuard() { sqlite3_close(value); }
        } guard{database};
        sqlite3_set_authorizer(database, sqlite_read_only_authorizer, nullptr);
        SqliteDeadline limit{std::chrono::steady_clock::now() + std::chrono::seconds(timeout)};
        sqlite3_progress_handler(database, 1000, sqlite_timeout, &limit);
        sqlite3_busy_timeout(database, timeout * 1000);

        sqlite3_stmt* statement = nullptr;
        const char* tail = nullptr;
        const auto prepare_result = sqlite3_prepare_v2(database, query.c_str(), static_cast<int>(query.size()), &statement, &tail);
        if (prepare_result != SQLITE_OK) return "Error preparing query: " + std::string(sqlite3_errmsg(database));
        struct StatementGuard {
            sqlite3_stmt* value;
            ~StatementGuard() { sqlite3_finalize(value); }
        } statement_guard{statement};
        while (tail != nullptr && *tail != '\0' && std::isspace(static_cast<unsigned char>(*tail))) ++tail;
        if (tail != nullptr && *tail != '\0') return "Error: exactly one SQL statement is allowed";
        if (statement == nullptr || !sqlite3_stmt_readonly(statement)) return "Error: only read-only SQL statements are allowed";

        if (arguments.contains("parameters") && arguments.at("parameters").is_object()) {
            for (auto item = arguments.at("parameters").begin(); item != arguments.at("parameters").end(); ++item) {
                int index = 0;
                for (const char prefix : {':', '@', '$'}) {
                    const auto parameter_name = std::string(1, prefix) + item.key();
                    index = sqlite3_bind_parameter_index(statement, parameter_name.c_str());
                    if (index != 0) break;
                }
                if (index == 0) continue;
                const auto& parameter = item.value();
                int bind_result = SQLITE_OK;
                if (parameter.is_null()) bind_result = sqlite3_bind_null(statement, index);
                else if (parameter.is_boolean()) bind_result = sqlite3_bind_int(statement, index, parameter.get<bool>() ? 1 : 0);
                else if (parameter.is_number_integer()) bind_result = sqlite3_bind_int64(statement, index, parameter.get<sqlite3_int64>());
                else if (parameter.is_number()) bind_result = sqlite3_bind_double(statement, index, parameter.get<double>());
                else {
                    const auto text = parameter.is_string() ? parameter.get<std::string>() : parameter.dump();
                    bind_result = sqlite3_bind_text(statement, index, text.c_str(), static_cast<int>(text.size()), SQLITE_TRANSIENT);
                }
                if (bind_result != SQLITE_OK) return "Error binding query parameter: " + std::string(sqlite3_errmsg(database));
            }
        }

        std::ostringstream output;
        int rows = 0;
        bool truncated = false;
        while (true) {
            const auto step_result = sqlite3_step(statement);
            if (step_result == SQLITE_DONE) break;
            if (step_result != SQLITE_ROW) return "Error executing query: " + std::string(sqlite3_errmsg(database));
            if (rows >= max_rows) {
                truncated = true;
                break;
            }
            json row = json::object();
            for (int column = 0; column < sqlite3_column_count(statement); ++column)
                row[sqlite3_column_name(statement, column)] = sqlite_value(statement, column);
            const auto serialized = row.dump();
            if (static_cast<int>(output.tellp()) + static_cast<int>(serialized.size()) + 1 > max_characters) {
                truncated = true;
                break;
            }
            output << serialized << '\n';
            ++rows;
        }
        if (rows == 0 && !truncated) return "Query executed successfully. No rows returned.";
        auto result = "Query executed successfully. " + std::to_string(rows) + " row(s) returned:\n" + output.str();
        if (truncated) result += "Results truncated at the configured row or character limit.";
        return result;
    } catch (const std::exception& error) {
        return "Error executing database query: " + std::string(error.what());
    }
}

std::string Toolset::execute_shell(const json& arguments) const {
    try {
        const auto tokens = parse_safe_arguments(value(arguments, "command"));
        if (tokens.empty()) return "Error: command cannot be empty";
        const auto working_directory = resolve(value(arguments, "working_directory", "."));
        if (!fs::is_directory(working_directory)) return "Error: working directory does not exist";
        const auto& command = tokens.front();
        std::ostringstream output;
        if (command == "pwd" && tokens.size() == 1) return working_directory.string();
        if (command == "ls") {
            bool show_hidden = false;
            std::vector<fs::path> paths;
            for (std::size_t index = 1; index < tokens.size(); ++index) {
                if (tokens[index] == "-a" || tokens[index] == "-la" || tokens[index] == "-al") show_hidden = true;
                else if (!tokens[index].empty() && tokens[index][0] == '-') return "Error: unsupported ls option";
                else paths.push_back(resolve((working_directory / tokens[index]).string()));
            }
            if (paths.empty()) paths.push_back(working_directory);
            std::size_t count = 0;
            for (const auto& path : paths) {
                if (fs::is_directory(path)) {
                    for (const auto& entry : fs::directory_iterator(path, fs::directory_options::skip_permission_denied)) {
                        if (!show_hidden && entry.path().filename().string().front() == '.') continue;
                        output << entry.path().filename().string() << (entry.is_directory() ? "/" : "") << '\n';
                        if (++count >= 500) break;
                    }
                } else if (fs::exists(path)) output << path.filename().string() << '\n';
                else return "Error: path not found: " + path.string();
            }
            return output.str();
        }
        if (command == "cat") {
            if (tokens.size() < 2 || tokens.size() > 11) return "Error: cat requires 1 to 10 file paths";
            for (std::size_t index = 1; index < tokens.size(); ++index) {
                const auto path = resolve((working_directory / tokens[index]).string());
                if (!fs::is_regular_file(path) || fs::file_size(path) > 1024 * 1024) return "Error: file missing or exceeds 1 MiB";
                std::ifstream input(path, std::ios::binary);
                output << input.rdbuf();
            }
            return output.str();
        }
        if (command == "grep") {
            std::size_t index = 1;
            bool ignore_case = false;
            bool line_numbers = false;
            while (index < tokens.size() && tokens[index].size() > 1 && tokens[index][0] == '-') {
                if (tokens[index] == "-i") ignore_case = true;
                else if (tokens[index] == "-n") line_numbers = true;
                else if (tokens[index] == "-in" || tokens[index] == "-ni") ignore_case = line_numbers = true;
                else return "Error: grep supports only -i and -n";
                ++index;
            }
            if (tokens.size() - index < 2 || tokens.size() - index > 21) return "Error: grep requires a pattern and 1 to 20 files";
            const auto pattern = tokens[index++];
            for (; index < tokens.size(); ++index) {
                const auto path = resolve((working_directory / tokens[index]).string());
                if (!fs::is_regular_file(path) || fs::file_size(path) > 1024 * 1024) continue;
                std::ifstream input(path);
                std::string line;
                std::size_t number = 0;
                while (std::getline(input, line)) {
                    ++number;
                    const bool matched = ignore_case ? contains_ci(line, pattern) : line.find(pattern) != std::string::npos;
                    if (matched) output << path.lexically_relative(workspace_).generic_string()
                                        << (line_numbers ? ":" + std::to_string(number) : "") << ":" << line << '\n';
                }
            }
            return output.str();
        }
        return "Error: allowed commands are pwd, ls, cat, and grep; shell operators are disabled";
    } catch (const std::exception& error) {
        return "Error: " + std::string(error.what());
    }
}

std::string Toolset::execute_git(const json& arguments) const {
    try {
        const auto command = value(arguments, "command");
        const auto requested_directory = resolve(value(arguments, "working_directory", "."));
        if (!fs::is_directory(requested_directory)) return "Error: Git working directory does not exist";
        std::vector<std::string> args{
            "--no-pager", "-c", "core.pager=cat", "-c", "core.fsmonitor=false",
            "-c", "diff.external=", "-c", "diff.trustExitCode=false", command
        };
        const auto extra = parse_safe_arguments(value(arguments, "args"));
        bool requires_confirmation = false;
        if (command == "status") {
            for (const auto& arg : extra)
                if (arg != "--short" && arg != "--branch" && arg != "--porcelain" && arg != "--untracked-files=no")
                    return "Error: unsupported read-only git status option";
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "diff") {
            bool paths_only = false;
            for (const auto& arg : extra) {
                if (arg == "--") { paths_only = true; continue; }
                if (paths_only) resolve((requested_directory / arg).string());
                else if (arg != "--stat" && arg != "--name-only" && arg != "--name-status" && arg != "--cached" && arg != "--staged" && arg != "--no-ext-diff" && arg != "--no-textconv")
                    return "Error: unsupported read-only git diff option";
            }
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "log") {
            for (std::size_t index = 0; index < extra.size(); ++index) {
                const auto& arg = extra[index];
                if (arg == "--oneline" || arg == "--decorate") {
                    args.push_back(arg);
                    continue;
                }
                if (arg == "-n" || arg == "--max-count") {
                    if (++index >= extra.size()) return "Error: git log count is missing";
                    int count = 0;
                    if (extra[index].empty() || extra[index].find_first_not_of("0123456789") != std::string::npos ||
                        (count = std::stoi(extra[index])) < 1 || count > 50) return "Error: git log count must be between 1 and 50";
                    args.push_back(arg);
                    args.push_back(std::to_string(count));
                    continue;
                }
                if (arg.rfind("-n", 0) == 0 && arg.size() > 2 && arg.substr(2).find_first_not_of("0123456789") == std::string::npos) {
                    const auto count = std::stoi(arg.substr(2));
                    if (count < 1 || count > 50) return "Error: git log count must be between 1 and 50";
                    args.push_back(arg);
                    continue;
                }
                return "Error: git log supports --oneline, --decorate, and -n 1..50";
            }
            if (extra.empty()) args.insert(args.end(), {"-n", "20", "--oneline"});
        } else if (command == "show") {
            if (extra.size() > 1) return "Error: git show accepts at most one revision";
            if (!extra.empty() && extra.front().find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789^~:._-/") != std::string::npos)
                return "Error: invalid git revision";
            args.push_back("--no-ext-diff");
            args.push_back("--no-textconv");
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "branch") {
            if (!extra.empty() && extra.front() != "--list" && extra.front() != "-a" && extra.front() != "--all") {
                if (extra.size() != 1 || extra.front().find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._/-") != std::string::npos)
                    return "Error: only creating a new branch is supported";
                requires_confirmation = true;
                args.insert(args.end(), extra.begin(), extra.end());
            } else {
                for (const auto& arg : extra)
                    if (arg != "--list" && arg != "-a" && arg != "--all") return "Error: unsupported read-only git branch option";
                args.insert(args.end(), extra.begin(), extra.end());
            }
        } else if (command == "rev-parse") {
            for (const auto& arg : extra)
                if (arg != "--show-toplevel" && arg != "--abbrev-ref" && arg != "HEAD") return "Error: unsupported read-only git rev-parse option";
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "add") {
            if (extra.empty()) return "Error: git add requires --all, -A, or workspace paths";
            for (const auto& arg : extra) {
                if (arg == "--all" || arg == "-A") continue;
                if (arg == "--") continue;
                if (!arg.empty() && arg.front() == '-') return "Error: unsupported git add option";
                resolve((requested_directory / arg).string());
            }
            requires_confirmation = true;
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "commit") {
            if (extra.size() != 2 || extra[0] != "-m" || extra[1].empty())
                return "Error: git commit requires exactly -m and a non-empty message";
            requires_confirmation = true;
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "switch" || command == "checkout") {
            const auto branch_index = extra.size() == 2 && (extra[0] == "-c" || (command == "checkout" && extra[0] == "-b")) ? 1U : 0U;
            if (extra.size() != branch_index + 1 || extra[branch_index].empty() ||
                extra[branch_index].find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._/-") != std::string::npos)
                return "Error: only switching to or creating a named branch is supported";
            requires_confirmation = true;
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "restore") {
            if (extra.size() < 2 || extra.front() != "--") return "Error: git restore requires -- followed by workspace paths";
            for (std::size_t index = 1; index < extra.size(); ++index) resolve((requested_directory / extra[index]).string());
            requires_confirmation = true;
            args.insert(args.end(), extra.begin(), extra.end());
        } else if (command == "stash") {
            if (!extra.empty() && extra.front() == "list") {
                if (extra.size() != 1) return "Error: git stash list accepts no additional options";
                args.insert(args.end(), extra.begin(), extra.end());
            } else if (!extra.empty() && extra.front() == "push") {
                if (extra.size() != 1 && (extra.size() != 3 || extra[1] != "-m" || extra[2].empty()))
                    return "Error: git stash push accepts an optional -m message";
                requires_confirmation = true;
                args.insert(args.end(), extra.begin(), extra.end());
            } else {
                return "Error: git stash supports list or confirmed push";
            }
        } else {
            return "Error: unsupported Git command; destructive and remote commands are disabled";
        }
        if (requires_confirmation) {
            std::ostringstream display;
            display << "git " << command;
            for (const auto& arg : extra) display << ' ' << arg;
            if (!confirm_git_mutation_ || !confirm_git_mutation_(display.str()))
                return "Error: Git mutation requires explicit user confirmation";
        }
        const auto timeout = arguments.value("timeout_seconds", 15);
        const auto result = run_process("git", args, requested_directory, timeout);
        std::string output = result.output;
        if (result.timed_out) output += "\nCommand timed out.";
        output += "\nExit code: " + std::to_string(result.exit_code);
        return output;
    } catch (const std::exception& error) {
        return "Error executing git query: " + std::string(error.what());
    }
}

} // namespace superagent