#include "process.hpp"

#include <algorithm>
#include <chrono>
#include <stdexcept>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#else
#include <cerrno>
#include <csignal>
#include <cstring>
#include <fcntl.h>
#include <poll.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

namespace superagent {
namespace {

constexpr std::size_t maximum_output = 64 * 1024;

#ifdef _WIN32
std::wstring widen(const std::string& text) {
    if (text.empty()) return {};
    const auto size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (size <= 0) throw std::runtime_error("invalid UTF-8 process argument");
    std::wstring result(static_cast<std::size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), result.data(), size);
    return result;
}

std::wstring quote_argument(const std::wstring& argument) {
    std::wstring result = L"\"";
    std::size_t slashes = 0;
    for (const auto ch : argument) {
        if (ch == L'\\') {
            ++slashes;
        } else if (ch == L'"') {
            result.append(slashes * 2 + 1, L'\\');
            result.push_back(ch);
            slashes = 0;
        } else {
            result.append(slashes, L'\\');
            result.push_back(ch);
            slashes = 0;
        }
    }
    result.append(slashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}
#endif

} // namespace

ProcessResult run_process(const std::string& executable, const std::vector<std::string>& arguments,
                          const std::filesystem::path& working_directory, int timeout_seconds) {
    if (timeout_seconds < 1 || timeout_seconds > 300) throw std::invalid_argument("timeout must be between 1 and 300 seconds");
    ProcessResult result;
#ifdef _WIN32
    SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE};
    HANDLE read_pipe = nullptr;
    HANDLE write_pipe = nullptr;
    if (!CreatePipe(&read_pipe, &write_pipe, &security, 0)) throw std::runtime_error("cannot create process output pipe");
    SetHandleInformation(read_pipe, HANDLE_FLAG_INHERIT, 0);
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdOutput = write_pipe;
    startup.hStdError = write_pipe;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    PROCESS_INFORMATION process{};
    std::wstring command = quote_argument(widen(executable));
    for (const auto& argument : arguments) command += L" " + quote_argument(widen(argument));
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');
    const auto directory = widen(working_directory.u8string());
    const auto created = CreateProcessW(nullptr, mutable_command.data(), nullptr, nullptr, TRUE,
        CREATE_NO_WINDOW, nullptr, directory.c_str(), &startup, &process);
    CloseHandle(write_pipe);
    if (!created) {
        CloseHandle(read_pipe);
        throw std::runtime_error("cannot start process: " + executable);
    }
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(timeout_seconds);
    char buffer[4096];
    bool exited = false;
    while (!exited) {
        DWORD available = 0;
        if (PeekNamedPipe(read_pipe, nullptr, 0, nullptr, &available, nullptr) && available > 0) {
            DWORD read = 0;
            if (ReadFile(read_pipe, buffer, static_cast<DWORD>(std::min<std::size_t>(sizeof(buffer), available)), &read, nullptr) && read > 0) {
                const auto remaining = maximum_output > result.output.size() ? maximum_output - result.output.size() : 0;
                result.output.append(buffer, std::min<std::size_t>(read, remaining));
            }
        }
        const auto wait = WaitForSingleObject(process.hProcess, 50);
        exited = wait == WAIT_OBJECT_0;
        if (std::chrono::steady_clock::now() >= deadline && !exited) {
            result.timed_out = true;
            TerminateProcess(process.hProcess, 124);
            WaitForSingleObject(process.hProcess, INFINITE);
            exited = true;
        }
    }
    DWORD available = 0;
    while (PeekNamedPipe(read_pipe, nullptr, 0, nullptr, &available, nullptr) && available > 0) {
        DWORD read = 0;
        if (!ReadFile(read_pipe, buffer, static_cast<DWORD>(std::min<std::size_t>(sizeof(buffer), available)), &read, nullptr) || read == 0) break;
        const auto remaining = maximum_output > result.output.size() ? maximum_output - result.output.size() : 0;
        result.output.append(buffer, std::min<std::size_t>(read, remaining));
    }
    DWORD exit_code = 1;
    GetExitCodeProcess(process.hProcess, &exit_code);
    result.exit_code = static_cast<int>(exit_code);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    CloseHandle(read_pipe);
#else
    int pipe_fds[2];
    if (pipe(pipe_fds) != 0) throw std::runtime_error("cannot create process output pipe");
    std::vector<char*> argv;
    argv.reserve(arguments.size() + 2);
    argv.push_back(const_cast<char*>(executable.c_str()));
    for (const auto& argument : arguments) argv.push_back(const_cast<char*>(argument.c_str()));
    argv.push_back(nullptr);
    const auto child = fork();
    if (child < 0) {
        close(pipe_fds[0]);
        close(pipe_fds[1]);
        throw std::runtime_error("cannot fork process");
    }
    if (child == 0) {
        setpgid(0, 0);
        close(pipe_fds[0]);
        dup2(pipe_fds[1], STDOUT_FILENO);
        dup2(pipe_fds[1], STDERR_FILENO);
        close(pipe_fds[1]);
        if (chdir(working_directory.c_str()) != 0) _exit(126);
        execvp(executable.c_str(), argv.data());
        _exit(errno == ENOENT ? 127 : 126);
    }
    close(pipe_fds[1]);
    setpgid(child, child);
    const auto flags = fcntl(pipe_fds[0], F_GETFL, 0);
    fcntl(pipe_fds[0], F_SETFL, flags | O_NONBLOCK);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(timeout_seconds);
    bool exited = false;
    bool pipe_closed = false;
    int status = 0;
    char buffer[4096];
    while (!exited || !pipe_closed) {
        pollfd descriptor{pipe_fds[0], POLLIN | POLLHUP, 0};
        poll(&descriptor, 1, 50);
        if (descriptor.revents & (POLLIN | POLLHUP)) {
            while (true) {
                const auto count = read(pipe_fds[0], buffer, sizeof(buffer));
                if (count > 0) {
                    const auto remaining = maximum_output > result.output.size() ? maximum_output - result.output.size() : 0;
                    result.output.append(buffer, std::min<std::size_t>(static_cast<std::size_t>(count), remaining));
                } else {
                    if (count == 0) pipe_closed = true;
                    break;
                }
            }
        }
        if (!exited) {
            const auto waited = waitpid(child, &status, WNOHANG);
            exited = waited == child;
        }
        if (!exited && std::chrono::steady_clock::now() >= deadline) {
            result.timed_out = true;
            kill(-child, SIGKILL);
            waitpid(child, &status, 0);
            exited = true;
        }
    }
    close(pipe_fds[0]);
    result.exit_code = WIFEXITED(status) ? WEXITSTATUS(status) : 128 + (WIFSIGNALED(status) ? WTERMSIG(status) : 0);
#endif
    if (result.output.size() == maximum_output) result.output += "\n[output truncated at 64 KiB]\n";
    return result;
}

} // namespace superagent