cmake_minimum_required(VERSION 3.20)

project(Zengine VERSION 1.0)

set(CMAKE_CXX_STANDARD 17)

add_library(Zengine SHARED
    src/DLLExports.cpp
    src/VoicePlayer/VoicePlayer.cpp
    src/Sequencer/Sequencer.cpp
    src/Effects/Effects.cpp
)

target_include_directories(Zengine PRIVATE include)

set_target_properties(Zengine PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded"
    OUTPUT_NAME "ZTrackerEngine"
)

target_compile_options(Zengine PRIVATE
    $<$<CONFIG:Release>:/O2 /fp:fast>
    $<$<CONFIG:Debug>:/Od /Zi>
)
