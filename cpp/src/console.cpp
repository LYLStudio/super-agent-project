#include "console.hpp"

#include <algorithm>
#include <cstdint>
#include <iostream>

#ifdef _WIN32
#include <conio.h>
#include <io.h>
#include <windows.h>
#else
#include <sys/ioctl.h>
#include <termios.h>
#include <unistd.h>
#endif

namespace superagent {
namespace {

constexpr const char* reset = "\033[0m";

std::string color_code(const std::string& color) {
    if (color == "green") return "\033[1;32m";
    if (color == "yellow") return "\033[1;33m";
    if (color == "magenta") return "\033[1;35m";
    if (color == "red") return "\033[1;31m";
    return "\033[1;36m";
}

std::size_t line_start_at(const std::string& text, std::size_t cursor) {
    if (cursor == 0) return 0;
    const auto newline = text.rfind('\n', cursor - 1);
    return newline == std::string::npos ? 0 : newline + 1;
}

std::pair<int, int> terminal_size() {
#ifdef _WIN32
    CONSOLE_SCREEN_BUFFER_INFO info{};
    if (GetConsoleScreenBufferInfo(GetStdHandle(STD_OUTPUT_HANDLE), &info)) {
        return {info.srWindow.Right - info.srWindow.Left + 1,
                info.srWindow.Bottom - info.srWindow.Top + 1};
    }
#else
    winsize size{};
    if (ioctl(STDOUT_FILENO, TIOCGWINSZ, &size) == 0 && size.ws_col > 0 && size.ws_row > 0) {
        return {size.ws_col, size.ws_row};
    }
#endif
    return {80, 24};
}

bool terminal_is_interactive() {
#ifdef _WIN32
    return _isatty(_fileno(stdin)) && _isatty(_fileno(stdout));
#else
    return isatty(STDIN_FILENO) && isatty(STDOUT_FILENO);
#endif
}

#ifndef _WIN32
class RawTerminal {
public:
    explicit RawTerminal(bool& active) {
        if (tcgetattr(STDIN_FILENO, &original_) != 0) return;
        auto raw = original_;
        raw.c_lflag &= static_cast<tcflag_t>(~(ECHO | ICANON | IEXTEN));
        raw.c_iflag &= static_cast<tcflag_t>(~(IXON | ICRNL | INLCR));
        raw.c_cc[VMIN] = 1;
        raw.c_cc[VTIME] = 0;
        if (tcsetattr(STDIN_FILENO, TCSAFLUSH, &raw) == 0) {
            active_ = true;
            active = true;
        }
    }

    ~RawTerminal() {
        if (active_) tcsetattr(STDIN_FILENO, TCSAFLUSH, &original_);
    }

private:
    termios original_{};
    bool active_ = false;
};
#endif

std::size_t previous_character(const std::string& text, std::size_t cursor) {
    if (cursor == 0) return 0;
    auto position = cursor - 1;
    while (position > 0 &&
           (static_cast<unsigned char>(text[position]) & 0xC0) == 0x80) {
        --position;
    }
    return position;
}

std::size_t next_character(const std::string& text, std::size_t cursor) {
    if (cursor >= text.size()) return text.size();
    auto position = cursor + 1;
    while (position < text.size() &&
           (static_cast<unsigned char>(text[position]) & 0xC0) == 0x80) {
        ++position;
    }
    return position;
}

std::size_t utf8_bytes(const std::string& text, std::size_t index) {
    const auto leading = static_cast<unsigned char>(text[index]);
    return std::min<std::size_t>(
        (leading & 0xF8) == 0xF0 ? 4U : (leading & 0xF0) == 0xE0 ? 3U :
        (leading & 0xE0) == 0xC0 ? 2U : 1U,
        text.size() - index);
}

int character_width(const std::string& text, std::size_t index, std::size_t bytes) {
    const auto first = static_cast<unsigned char>(text[index]);
    std::uint32_t codepoint = first;
    if (bytes == 2) codepoint = ((first & 0x1F) << 6) | (static_cast<unsigned char>(text[index + 1]) & 0x3F);
    if (bytes == 3) codepoint = ((first & 0x0F) << 12) |
        ((static_cast<unsigned char>(text[index + 1]) & 0x3F) << 6) |
        (static_cast<unsigned char>(text[index + 2]) & 0x3F);
    if (bytes == 4) codepoint = ((first & 0x07) << 18) |
        ((static_cast<unsigned char>(text[index + 1]) & 0x3F) << 12) |
        ((static_cast<unsigned char>(text[index + 2]) & 0x3F) << 6) |
        (static_cast<unsigned char>(text[index + 3]) & 0x3F);
    if ((codepoint >= 0x0300 && codepoint <= 0x036F) ||
        (codepoint >= 0x1AB0 && codepoint <= 0x1AFF) ||
        (codepoint >= 0xFE00 && codepoint <= 0xFE0F)) return 0;
    if (codepoint >= 0x1100 &&
        (codepoint <= 0x115F || codepoint == 0x2329 || codepoint == 0x232A ||
         (codepoint >= 0x2E80 && codepoint <= 0xA4CF) ||
         (codepoint >= 0xAC00 && codepoint <= 0xD7A3) ||
         (codepoint >= 0xF900 && codepoint <= 0xFAFF) ||
         (codepoint >= 0xFE10 && codepoint <= 0xFE19) ||
         (codepoint >= 0xFE30 && codepoint <= 0xFE6F) ||
         (codepoint >= 0xFF00 && codepoint <= 0xFF60) ||
         (codepoint >= 0x1F300 && codepoint <= 0x1FAFF))) return 2;
    return 1;
}

std::vector<std::string> wrap_display(const std::string& text, int maximum_width) {
    std::vector<std::string> lines(1);
    int width = 0;
    for (std::size_t index = 0; index < text.size();) {
        if (static_cast<unsigned char>(text[index]) == 0x1B && index + 1 < text.size() && text[index + 1] == '[') {
            auto end = index + 2;
            while (end < text.size() && !(text[end] >= '@' && text[end] <= '~')) ++end;
            if (end < text.size()) {
                lines.back().append(text, index, end - index + 1);
                index = end + 1;
                continue;
            }
        }
        const auto bytes = utf8_bytes(text, index);
        const auto columns = character_width(text, index, bytes);
        if (width > 0 && width + columns > maximum_width) {
            lines.emplace_back();
            width = 0;
        }
        lines.back().append(text, index, bytes);
        index += bytes;
        width += columns;
    }
    return lines;
}

int display_width(const std::string& text) {
    int width = 0;
    for (std::size_t index = 0; index < text.size();) {
        if (static_cast<unsigned char>(text[index]) == 0x1B && index + 1 < text.size() && text[index + 1] == '[') {
            auto end = index + 2;
            while (end < text.size() && !(text[end] >= '@' && text[end] <= '~')) ++end;
            if (end < text.size()) {
                index = end + 1;
                continue;
            }
        }
        const auto bytes = utf8_bytes(text, index);
        index += bytes;
        width += character_width(text, index - bytes, bytes);
    }
    return width;
}

} // namespace

ConsoleView::ConsoleView(std::string header)
    : header_(std::move(header)), interactive_(is_interactive()) {
#ifdef _WIN32
    if (interactive_) {
        DWORD mode = 0;
        const auto output = GetStdHandle(STD_OUTPUT_HANDLE);
        if (GetConsoleMode(output, &mode)) SetConsoleMode(output, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }
#endif
    output_lines_.emplace_back();
    output_styles_.emplace_back();
    if (interactive_) {
        std::cout << "\033[?1049h\033[?25l";
        render();
    }
}

ConsoleView::~ConsoleView() {
    if (interactive_) {
        std::cout << "\033[?25h\033[?1049l" << std::flush;
    }
}

bool ConsoleView::is_interactive() {
    return terminal_is_interactive();
}

void ConsoleView::set_header(std::string header) {
    header_ = std::move(header);
    render();
}

void ConsoleView::append_text(const std::string& text) {
    if (text.empty()) return;
    std::string sanitized;
    sanitized.reserve(text.size());
    for (std::size_t index = 0; index < text.size(); ++index) {
        const auto character = static_cast<unsigned char>(text[index]);
        if (character == '\r') {
            if (index + 1 < text.size() && text[index + 1] == '\n') ++index;
            sanitized.push_back('\n');
        } else if (character == '\n') {
            sanitized.push_back('\n');
        } else if (character == '\t') {
            sanitized += "    ";
        } else if (character >= 0x20 && character != 0x7f) {
            sanitized.push_back(static_cast<char>(character));
        }
    }
    if (!interactive_) {
        std::cout << sanitized << std::flush;
    }
    for (const auto character : sanitized) {
        if (character == '\n') {
            output_lines_.emplace_back();
            output_styles_.push_back(current_output_style_);
        } else {
            output_lines_.back().push_back(character);
        }
    }
    render();
}

void ConsoleView::append_section_header(const std::string& title, const std::string& color) {
    current_output_style_ = color;
    append_text("\n── " + title + " ──\n");
}

void ConsoleView::suspend() {
    if (!interactive_ || suspended_) return;
    suspended_ = true;
    std::cout << "\033[?25h\033[?1049l" << std::flush;
}

void ConsoleView::resume() {
    if (!interactive_ || !suspended_) return;
    suspended_ = false;
    std::cout << "\033[?1049h\033[?25l";
    render();
}

void ConsoleView::render() {
    if (!interactive_ || suspended_) return;
    const auto [width, height] = terminal_size();
    const auto input_rows = static_cast<int>(std::count(input_.begin(), input_.end(), '\n')) + 1;
    const auto viewport_height = std::max(1, height - input_rows - 3);
    std::cout << "\033[H\033[2J\033[1;36m" << header_ << reset << '\n';
    std::vector<std::pair<std::string, std::string>> visible_output;
    for (std::size_t index = 0; index < output_lines_.size(); ++index) {
        const auto& line = output_lines_[index];
        auto wrapped = wrap_display(line, std::max(1, width - 1));
        for (auto& row : wrapped) visible_output.emplace_back(std::move(row), output_styles_[index]);
    }
    const auto maximum_start = visible_output.size() > static_cast<std::size_t>(viewport_height)
        ? visible_output.size() - static_cast<std::size_t>(viewport_height) : 0;
    const auto start = maximum_start > scroll_from_bottom_
        ? maximum_start - scroll_from_bottom_ : 0;
    for (int row = 0; row < viewport_height; ++row) {
        const auto index = start + static_cast<std::size_t>(row);
        if (index < visible_output.size()) {
            const auto& [line, style] = visible_output[index];
            if (!style.empty()) {
                const auto color = color_code(style);
                std::cout << color;
                if (line.rfind("── ", 0) == 0) std::cout << "\033[1m";
            }
            std::cout << line;
            if (!style.empty()) std::cout << reset;
        }
        std::cout << "\033[K\n";
    }
    std::cout << "\033[2mEnter 換行  F4/Ctrl+Enter 送出  F2 設定  F3 日誌  F5 重載  PageUp/Down 捲動\033[0m\n";
    const auto input_line_start = line_start_at(input_, cursor_);
    std::cout << "> ";
    for (std::size_t index = 0; index < input_.size(); ++index) {
        if (input_[index] == '\n') std::cout << "\n  ";
        else std::cout.put(input_[index]);
    }
    std::cout << "\033[K";
    const auto last_line_start = input_.rfind('\n');
    const auto end_line_start = last_line_start == std::string::npos ? 0 : last_line_start + 1;
    const auto cursor_line = static_cast<int>(std::count(input_.begin(), input_.begin() + static_cast<std::ptrdiff_t>(cursor_), '\n'));
    const auto end_line = input_rows - 1;
    if (end_line > cursor_line) std::cout << "\033[" << end_line - cursor_line << 'A';
    const auto cursor_column = cursor_ - input_line_start;
    const auto cursor_cells = display_width(input_.substr(input_line_start, cursor_column));
    std::cout << "\033[" << cursor_cells + 3 << 'G';
    (void)end_line_start;
    std::cout << std::flush;
}

PromptInput ConsoleView::read_input(const std::vector<std::string>& history) {
    input_.clear();
    cursor_ = 0;
    scroll_from_bottom_ = 0;
    std::size_t history_index = history.size();
    std::string draft;
    if (!interactive_) {
        std::cout << "> ";
        std::getline(std::cin, input_);
        return {input_, {}, static_cast<bool>(std::cin)};
    }

#ifndef _WIN32
    RawTerminal raw(raw_mode_);
    if (!raw_mode_) {
        std::string line;
        std::cout << "> ";
        std::getline(std::cin, line);
        return {std::move(line), {}, static_cast<bool>(std::cin)};
    }
    auto read_byte = []() {
        char byte = 0;
        return ::read(STDIN_FILENO, &byte, 1) == 1 ? static_cast<unsigned char>(byte) : 0;
    };
    auto submit = [&]() {
        append_text("> " + input_ + "\n");
        return PromptInput{input_, {}, true};
    };
    while (true) {
        render();
        const auto key = read_byte();
        if (key == 0) return {{}, {}, false};
        if (key == '\r') {
            input_.insert(cursor_, "\n");
            ++cursor_;
        } else if (key == '\n' || key == 4) {
            auto result = submit();
            return result;
        } else if (key == 127 || key == 8) {
            if (cursor_ > 0) {
                const auto previous = previous_character(input_, cursor_);
                input_.erase(previous, cursor_ - previous);
                cursor_ = previous;
            }
        } else if (key == 1) {
            cursor_ = line_start_at(input_, cursor_);
        } else if (key == 5) {
            const auto end = input_.find('\n', cursor_);
            cursor_ = end == std::string::npos ? input_.size() : end;
        } else if (key == 27) {
            const auto first = read_byte();
            if (first == '[') {
                std::string sequence;
                auto second = read_byte();
                while ((second >= '0' && second <= '9') || second == ';') {
                    sequence.push_back(static_cast<char>(second));
                    second = read_byte();
                }
                if (second == 'A' || second == 'B') {
                    const bool up = second == 'A';
                    const auto current_start = line_start_at(input_, cursor_);
                    const auto column = cursor_ - current_start;
                    const auto line_end = input_.find('\n', cursor_);
                    if (up && current_start > 0) {
                        const auto previous_end = current_start - 1;
                        const auto previous_start_nl = input_.rfind('\n', previous_end == 0 ? 0 : previous_end - 1);
                        const auto previous_start = previous_start_nl == std::string::npos ? 0 : previous_start_nl + 1;
                        cursor_ = previous_start + std::min(column, previous_end - previous_start);
                    } else if (!up && line_end != std::string::npos) {
                        const auto next_end = input_.find('\n', line_end + 1);
                        const auto next_line_end = next_end == std::string::npos ? input_.size() : next_end;
                        cursor_ = line_end + 1 + std::min(column, next_line_end - line_end - 1);
                    } else if (up && history_index > 0) {
                        if (history_index == history.size()) draft = input_;
                        input_ = history[--history_index];
                        cursor_ = input_.size();
                    } else if (!up && history_index < history.size()) {
                        ++history_index;
                        input_ = history_index == history.size() ? draft : history[history_index];
                        cursor_ = input_.size();
                    }
                } else if (second == 'C') {
                    cursor_ = next_character(input_, cursor_);
                } else if (second == 'D') {
                    cursor_ = previous_character(input_, cursor_);
                } else if (second == 'H' || sequence == "1") {
                    cursor_ = line_start_at(input_, cursor_);
                } else if (second == 'F' || sequence == "4") {
                    const auto end = input_.find('\n', cursor_);
                    cursor_ = end == std::string::npos ? input_.size() : end;
                } else if (second == '~') {
                    if (sequence == "14" || sequence == "13;5" || sequence == "27;5;13") return submit();
                    if (sequence == "12") return {{}, "settings", false};
                    if (sequence == "13") return {{}, "logs", false};
                    if (sequence == "15") return {{}, "reload", false};
                    if (sequence == "2") return {{}, "settings", false};
                    if (sequence == "1") cursor_ = line_start_at(input_, cursor_);
                    if (sequence == "4") {
                        const auto end = input_.find('\n', cursor_);
                        cursor_ = end == std::string::npos ? input_.size() : end;
                    }
                    if (sequence == "3" && cursor_ < input_.size()) {
                        input_.erase(cursor_, next_character(input_, cursor_) - cursor_);
                    } else if (sequence == "5" || sequence == "6") {
                        const auto page = static_cast<std::size_t>(std::max(1, terminal_size().second - 5));
                        if (sequence == "5") scroll_from_bottom_ += page;
                        else scroll_from_bottom_ = scroll_from_bottom_ > page ? scroll_from_bottom_ - page : 0;
                    }
                } else if (second == 'u' && sequence == "13;5") {
                    return submit();
                }
            } else if (first == 'O') {
                const auto second = read_byte();
                if (second == 'Q') return {{}, "settings", false};
                if (second == 'R') return {{}, "logs", false};
                if (second == 'S') return submit();
                if (second == 'T') return {{}, "reload", false};
            }
        } else if (key == '\t') {
            continue;
        } else if (key >= 0x20 && key != 0x7f) {
            input_.insert(cursor_, 1, static_cast<char>(key));
            ++cursor_;
            while (cursor_ < input_.size() &&
                   (static_cast<unsigned char>(input_[cursor_]) & 0xC0) == 0x80) {
                ++cursor_;
            }
        }
    }
#else
    while (true) {
        render();
        const int key = _getch();
        if (key == 13) {
            input_.insert(cursor_, "\n");
            ++cursor_;
        } else if (key == 10) {
            append_text("> " + input_ + "\n");
            return {input_, {}, true};
        } else if (key == 1) {
            cursor_ = line_start_at(input_, cursor_);
        } else if (key == 5) {
            const auto end = input_.find('\n', cursor_);
            cursor_ = end == std::string::npos ? input_.size() : end;
        } else if (key == 19) {
            return {{}, "settings", false};
        } else if (key == 12) {
            return {{}, "logs", false};
        } else if (key == 20) {
            return {{}, "reload", false};
        } else if (key == 4) {
            append_text("> " + input_ + "\n");
            return {input_, {}, true};
        } else if (key == 8) {
            if (cursor_ > 0) {
                const auto previous = previous_character(input_, cursor_);
                input_.erase(previous, cursor_ - previous);
                cursor_ = previous;
            }
        } else if (key == 0 || key == 224) {
            const int extended = _getch();
            if (extended == 60) return {{}, "settings", false};
            if (extended == 61) return {{}, "logs", false};
            if (extended == 62) {
                append_text("> " + input_ + "\n");
                return {input_, {}, true};
            }
            if (extended == 63) return {{}, "reload", false};
            if (extended == 72 || extended == 80) {
                const bool up = extended == 72;
                const auto current_start = line_start_at(input_, cursor_);
                const auto column = cursor_ - current_start;
                if (up && current_start > 0) {
                    const auto previous_end = current_start - 1;
                    const auto previous_start = line_start_at(input_, previous_end);
                    cursor_ = previous_start + std::min(column, previous_end - previous_start);
                } else if (!up) {
                    const auto current_end = input_.find('\n', cursor_);
                    if (current_end != std::string::npos) {
                        const auto next_end = input_.find('\n', current_end + 1);
                        const auto end = next_end == std::string::npos ? input_.size() : next_end;
                        cursor_ = current_end + 1 + std::min(column, end - current_end - 1);
                    }
                } else if (history_index > 0) {
                    if (history_index == history.size()) draft = input_;
                    input_ = history[--history_index];
                    cursor_ = input_.size();
                } else if (history_index < history.size()) {
                    ++history_index;
                    input_ = history_index == history.size() ? draft : history[history_index];
                    cursor_ = input_.size();
                }
            } else if (extended == 75) cursor_ = previous_character(input_, cursor_);
            else if (extended == 77) cursor_ = next_character(input_, cursor_);
            else if (extended == 71) cursor_ = line_start_at(input_, cursor_);
            else if (extended == 79) {
                const auto end = input_.find('\n', cursor_);
                cursor_ = end == std::string::npos ? input_.size() : end;
            } else if (extended == 83 && cursor_ < input_.size()) {
                input_.erase(cursor_, next_character(input_, cursor_) - cursor_);
            } else if (extended == 73) {
                scroll_from_bottom_ += static_cast<std::size_t>(std::max(1, terminal_size().second - 5));
            } else if (extended == 81) {
                const auto page = static_cast<std::size_t>(std::max(1, terminal_size().second - 5));
                scroll_from_bottom_ = scroll_from_bottom_ > page ? scroll_from_bottom_ - page : 0;
            }
        } else if (key >= 0x20 && key <= 0xFF) {
            input_.insert(cursor_++, 1, static_cast<char>(key));
            while (cursor_ < input_.size() &&
                   (static_cast<unsigned char>(input_[cursor_]) & 0xC0) == 0x80) {
                ++cursor_;
            }
        }
    }
#endif
}

int ConsoleView::select_menu(const std::string& title, const std::vector<std::string>& options) {
    if (!terminal_is_interactive()) {
        std::cout << '\n' << title << '\n';
        for (std::size_t index = 0; index < options.size(); ++index) {
            std::cout << index + 1 << ". " << options[index] << '\n';
        }
        std::cout << "0. 返回\n> " << std::flush;
        std::string choice;
        if (!std::getline(std::cin, choice)) return -1;
        int selected = -1;
        try {
            std::size_t parsed = 0;
            selected = std::stoi(choice, &parsed);
            if (parsed != choice.size() || selected < 1 ||
                static_cast<std::size_t>(selected) > options.size()) return -1;
        } catch (const std::exception&) {
            return -1;
        }
        return selected - 1;
    }

    std::size_t selected = 0;
#ifndef _WIN32
    bool raw_mode = false;
    RawTerminal raw(raw_mode);
    if (!raw_mode) return -1;
    auto read_byte = []() {
        char byte = 0;
        return ::read(STDIN_FILENO, &byte, 1) == 1 ? static_cast<unsigned char>(byte) : 0;
    };
    while (true) {
        std::cout << "\033[H\033[2J\033[1m" << title << "\033[0m\n";
        for (std::size_t index = 0; index < options.size(); ++index) {
            std::cout << (index == selected ? "\033[1;36m> " : "  ")
                      << options[index] << (index == selected ? "\033[0m" : "") << "\n";
        }
        std::cout << "\n↑/↓ 選擇，Enter 確認，Esc 返回" << std::flush;
        const auto key = read_byte();
        if (key == '\r' || key == '\n') return static_cast<int>(selected);
        if (key == 27) {
            const auto first = read_byte();
            if (first == '[') {
                const auto arrow = read_byte();
                if (arrow == 'A' && selected > 0) --selected;
                if (arrow == 'B' && selected + 1 < options.size()) ++selected;
            } else {
                return -1;
            }
        }
    }
#else
    while (true) {
        std::cout << "\033[H\033[2J\033[1m" << title << "\033[0m\n";
        for (std::size_t index = 0; index < options.size(); ++index) {
            std::cout << (index == selected ? "\033[1;36m> " : "  ")
                      << options[index] << (index == selected ? "\033[0m" : "") << "\n";
        }
        const int key = _getch();
        if (key == 13) return static_cast<int>(selected);
        if (key == 27) return -1;
        if (key == 0 || key == 224) {
            const int extended = _getch();
            if (extended == 72 && selected > 0) --selected;
            if (extended == 80 && selected + 1 < options.size()) ++selected;
        }
    }
#endif
}

std::string ConsoleView::read_secret(const std::string& prompt) {
    std::cout << prompt << std::flush;
#ifdef _WIN32
    if (!terminal_is_interactive()) {
        std::string value;
        std::getline(std::cin, value);
        return value;
    }
    std::string value;
    while (true) {
        const int key = _getch();
        if (key == 13) break;
        if (key == 8) {
            if (!value.empty()) value.pop_back();
        } else if (key >= 0x20 && key <= 0xFF) {
            value.push_back(static_cast<char>(key));
        }
    }
    std::cout << '\n';
    return value;
#else
    termios original{};
    if (!terminal_is_interactive() || tcgetattr(STDIN_FILENO, &original) != 0) {
        std::string value;
        std::getline(std::cin, value);
        return value;
    }
    auto hidden = original;
    hidden.c_lflag &= static_cast<tcflag_t>(~ECHO);
    if (tcsetattr(STDIN_FILENO, TCSAFLUSH, &hidden) != 0) {
        std::string value;
        std::getline(std::cin, value);
        return value;
    }
    std::string value;
    std::getline(std::cin, value);
    tcsetattr(STDIN_FILENO, TCSAFLUSH, &original);
    std::cout << '\n';
    return value;
#endif
}

} // namespace superagent
