cmake_minimum_required(VERSION 3.20)
project(sitegrab CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# No external dependencies — uses WinHTTP (built into Windows SDK)
add_executable(sitegrab sitegrab.cpp)

target_compile_definitions(sitegrab PRIVATE
    _WIN32_WINNT=0x0A00
    WIN32_LEAN_AND_MEAN
    UNICODE
    _UNICODE)

target_link_libraries(sitegrab PRIVATE winhttp ws2_32)

# MSVC: portable static-runtime EXE (runs without VC++ redist)
if(MSVC)
    set_target_properties(sitegrab PROPERTIES
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(sitegrab PRIVATE /W3 /O2)
endif()
