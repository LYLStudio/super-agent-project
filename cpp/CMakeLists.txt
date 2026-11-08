cmake_minimum_required(VERSION 3.20)
project(SuperAgentCpp VERSION 1.0.0 LANGUAGES C CXX)

include(FetchContent)

FetchContent_Declare(
    httplib
    GIT_REPOSITORY https://github.com/yhirose/cpp-httplib.git
    GIT_TAG v0.18.3
    GIT_SHALLOW TRUE
)
FetchContent_Declare(
    nlohmann_json
    URL https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)
FetchContent_Declare(
    sqlite3
    URL https://www.sqlite.org/2025/sqlite-amalgamation-3500400.zip
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)
FetchContent_MakeAvailable(httplib nlohmann_json sqlite3)

add_library(superagent-sqlite STATIC ${sqlite3_SOURCE_DIR}/sqlite3.c)
target_include_directories(superagent-sqlite PUBLIC ${sqlite3_SOURCE_DIR})
target_compile_definitions(superagent-sqlite PRIVATE SQLITE_THREADSAFE=1 SQLITE_OMIT_LOAD_EXTENSION)

find_package(ODBC QUIET)

add_executable(superagent-cpp src/main.cpp src/runtime.cpp src/tools.cpp src/process.cpp src/console.cpp)
target_include_directories(superagent-cpp PRIVATE src)
target_compile_features(superagent-cpp PRIVATE cxx_std_17)
target_link_libraries(superagent-cpp PRIVATE httplib::httplib nlohmann_json::nlohmann_json superagent-sqlite)
if(ODBC_FOUND)
    target_link_libraries(superagent-cpp PRIVATE ODBC::ODBC)
    target_compile_definitions(superagent-cpp PRIVATE SUPERAGENT_HAS_ODBC=1)
endif()
include(CTest)
if(BUILD_TESTING)
    add_test(NAME superagent_help COMMAND superagent-cpp --help)
    add_executable(superagent-runtime-tests tests/runtime_tests.cpp src/runtime.cpp src/tools.cpp src/process.cpp)
    target_include_directories(superagent-runtime-tests PRIVATE src)
    target_compile_features(superagent-runtime-tests PRIVATE cxx_std_17)
    target_link_libraries(superagent-runtime-tests PRIVATE nlohmann_json::nlohmann_json superagent-sqlite)
    if(ODBC_FOUND)
        target_link_libraries(superagent-runtime-tests PRIVATE ODBC::ODBC)
        target_compile_definitions(superagent-runtime-tests PRIVATE SUPERAGENT_HAS_ODBC=1)
    endif()
    add_test(NAME superagent_runtime COMMAND superagent-runtime-tests)
endif()