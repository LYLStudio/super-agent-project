#pragma once

#include <filesystem>
#include <string>
#include <vector>

namespace superagent {

struct ProcessResult {
    std::string output;
    int exit_code = -1;
    bool timed_out = false;
};

ProcessResult run_process(const std::string& executable, const std::vector<std::string>& arguments,
                          const std::filesystem::path& working_directory, int timeout_seconds);

} // namespace superagent