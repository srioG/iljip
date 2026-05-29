#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <pathcch.h>

#include <string>
#include <string_view>
#include <vector>
#include <memory>

#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>
