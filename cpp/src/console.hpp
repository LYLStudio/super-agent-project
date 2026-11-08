#pragma once

#include <string>
#include <vector>

namespace superagent {

struct PromptInput {
    std::string text;
    std::string shortcut;
    bool submitted = false;
};

class ConsoleView {
public:
    explicit ConsoleView(std::string header);
    ~ConsoleView();
    ConsoleView(const ConsoleView&) = delete;
    ConsoleView& operator=(const ConsoleView&) = delete;

    static bool is_interactive();
    void set_header(std::string header);
    void append_text(const std::string& text);
    void append_section_header(const std::string& title, const std::string& color);
    void suspend();
    void resume();
    PromptInput read_input(const std::vector<std::string>& history);
    static int select_menu(const std::string& title, const std::vector<std::string>& options);
    static std::string read_secret(const std::string& prompt);

private:
    void render();
    std::string header_;
    std::vector<std::string> output_lines_;
    std::vector<std::string> output_styles_;
    std::string input_;
    std::string current_output_style_;
    std::size_t cursor_ = 0;
    std::size_t scroll_from_bottom_ = 0;
    bool interactive_ = false;
    bool suspended_ = false;
    bool raw_mode_ = false;
};

} // namespace superagent
