#include <windows.h>
#include "nvapi.h"
#include "NvApiDriverSettings.h"

static int withProfile(bool write, int value)
{
    NvAPI_Status status = NvAPI_Initialize();
    if (status != NVAPI_OK) return status;

    NvDRSSessionHandle session = nullptr;
    status = NvAPI_DRS_CreateSession(&session);
    if (status != NVAPI_OK) return status;

    status = NvAPI_DRS_LoadSettings(session);
    if (status != NVAPI_OK) {
        NvAPI_DRS_DestroySession(session);
        return status;
    }

    NvDRSProfileHandle profile = nullptr;
    status = NvAPI_DRS_GetBaseProfile(session, &profile);
    if (status != NVAPI_OK) {
        NvAPI_DRS_DestroySession(session);
        return status;
    }

    NVDRS_SETTING setting = {};
    setting.version = NVDRS_SETTING_VER;
    setting.settingId = FRL_FPS_ID;
    setting.settingType = NVDRS_DWORD_TYPE;

    int result = 0;
    if (write) {
        setting.u32CurrentValue = static_cast<NvU32>(value);
        status = NvAPI_DRS_SetSetting(session, profile, &setting);
        if (status == NVAPI_OK) status = NvAPI_DRS_SaveSettings(session);
        result = status;
    } else {
        status = NvAPI_DRS_GetSetting(session, profile, FRL_FPS_ID, &setting);
        if (status == NVAPI_SETTING_NOT_FOUND) result = 0;
        else if (status != NVAPI_OK) result = status;
        else result = static_cast<int>(setting.u32CurrentValue);
    }

    NvAPI_DRS_DestroySession(session);
    return result;
}

extern "C" __declspec(dllexport) int __stdcall GetMaxFrameRate()
{
    return withProfile(false, 0);
}

extern "C" __declspec(dllexport) int __stdcall SetMaxFrameRate(int maxFrameRate)
{
    if (maxFrameRate < 0 || maxFrameRate > 1023) return -5;
    return withProfile(true, maxFrameRate);
}
