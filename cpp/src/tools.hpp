#pragma once

#include "runtime.hpp"

#include <functional>
#include <string>
#include <vector>

namespace superagent {

class Toolset {
public:
    Toolset(const fs::path& workspace, const std::map<std::string, bool>& enabled, StructuredLogger& logger,
            std::function<bool(const std::string&)> confirm_git_mutation = {});
    json definitions() const;
    std::string execute(const std::string& name, const json& arguments);
    bool enabled(const std::string& name) const;

private:
    fs::path resolve(const std::string& requested) const;
    std::string execute_file_search(const json& arguments) const;
    std::string execute_code_analysis(const json& arguments) const;
    std::string execute_test_generation(const json& arguments) const;
    std::string execute_debugger(const json& arguments) const;
    std::string execute_refactoring(const json& arguments) const;
    std::string execute_performance_monitor(const json& arguments) const;
    std::string execute_logging(const json& arguments);
    std::string execute_database_query(const json& arguments) const;
    std::string execute_shell(const json& arguments) const;
    std::string execute_git(const json& arguments) const;

    fs::path workspace_;
    std::map<std::string, bool> enabled_;
    StructuredLogger& logger_;
    std::function<bool(const std::string&)> confirm_git_mutation_;
};

} // namespace superagent