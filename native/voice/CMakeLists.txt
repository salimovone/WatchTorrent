cmake_minimum_required(VERSION 3.25)
project(VoiceConnection LANGUAGES CXX)
if(NOT WIN32)
  message(FATAL_ERROR "Use the Android AAR for Android. This bridge builds the Windows adapter.")
endif()
set(VOICE_NATIVE_PACKAGE "" CACHE PATH "Extracted NTgCalls 3.0.7 Windows shared package")
if(NOT EXISTS "${VOICE_NATIVE_PACKAGE}/include/ntgcalls.h")
  message(FATAL_ERROR "Set VOICE_NATIVE_PACKAGE to the extracted, pinned NTgCalls shared package.")
endif()
add_library(ntgcalls SHARED IMPORTED)
set_target_properties(ntgcalls PROPERTIES
  IMPORTED_IMPLIB "${VOICE_NATIVE_PACKAGE}/lib/ntgcalls.lib"
  IMPORTED_LOCATION "${VOICE_NATIVE_PACKAGE}/lib/ntgcalls.dll"
  INTERFACE_INCLUDE_DIRECTORIES "${VOICE_NATIVE_PACKAGE}/include")
add_library(voiceconnection_native SHARED bridge.cpp bridge.h)
target_compile_features(voiceconnection_native PRIVATE cxx_std_20)
target_link_libraries(voiceconnection_native PRIVATE ntgcalls)
add_custom_command(TARGET voiceconnection_native POST_BUILD
  COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:ntgcalls>" "$<TARGET_FILE_DIR:voiceconnection_native>"
  VERBATIM)
