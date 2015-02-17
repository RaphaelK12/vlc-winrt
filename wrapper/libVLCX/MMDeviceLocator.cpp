/*****************************************************************************
 * Copyright � 2013 VideoLAN
 *
 * Authors: Kellen Sunderland
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation; either version 2.1 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston MA 02110-1301, USA.
 *****************************************************************************/

#include <wrl.h>
#include <wrl/client.h>

#include <dxgi.h>
#include <dxgi1_2.h>
#include <dxgi1_3.h>
#include <d3d11_1.h>
#include <d2d1_2.h>
#include <ppltasks.h>


#include "windows.ui.xaml.media.dxinterop.h"

#include "MMDeviceLocator.h"

HRESULT MMDeviceLocator::RegisterForWASAPI(){
    HRESULT hr = S_OK;
    IActivateAudioInterfaceAsyncOperation *asyncOp;

    Platform::String^ id = MediaDevice::GetDefaultAudioRenderId(Windows::Media::Devices::AudioDeviceRole::Default);
    hr = ActivateAudioInterfaceAsync(id->Data(), __uuidof(IAudioClient2), nullptr, this, &asyncOp);

    SafeRelease(&asyncOp);

    return hr;
}

HRESULT MMDeviceLocator::ActivateCompleted(IActivateAudioInterfaceAsyncOperation *operation)
{
    HRESULT hr = S_OK;
    HRESULT hrActivateResult = S_OK;
    IUnknown *audioInterface = nullptr;

    hr = operation->GetActivateResult(&hrActivateResult, &audioInterface);
    if (SUCCEEDED(hr) && SUCCEEDED(hrActivateResult) && audioInterface != nullptr)
    {
        audioInterface->QueryInterface(IID_PPV_ARGS(&m_AudioClient));
        if (nullptr == m_AudioClient)
        {
            hr = E_FAIL;
        }
        else
        {
            AudioClientProperties props = AudioClientProperties{
                sizeof(props),
                FALSE,
                AudioCategory_BackgroundCapableMedia,
                AUDCLNT_STREAMOPTIONS_NONE
            };
            auto res = m_AudioClient->SetClientProperties(&props);
            if (res != S_OK) {
                OutputDebugString(TEXT("Failed to set audio client properties"));
            }
            SetEvent(m_audioClientReady);
        }
    }

    return hr;
}

namespace libVLCX
{
    Windows::Foundation::IAsyncOperation<uint32>^ MMDeviceLoader::GetAudioClient()
    {
        return concurrency::create_async([]()
        {
            MMDeviceLocator locator;
            locator.m_audioClientReady = CreateEventEx(NULL, TEXT("AudioClientReady"), 0, EVENT_ALL_ACCESS);
            locator.RegisterForWASAPI();
            DWORD res;
            while ((res = WaitForSingleObjectEx(locator.m_audioClientReady, 1000, TRUE)) == WAIT_TIMEOUT) {
                OutputDebugStringW(L"Waiting for audio\n");
            }
            CloseHandle(locator.m_audioClientReady);
            if (res != WAIT_OBJECT_0) {
                OutputDebugString(TEXT("Failure while waiting for audio client"));
                return 0u;
            }
            return (uint32)locator.m_AudioClient;
        });
    }
}