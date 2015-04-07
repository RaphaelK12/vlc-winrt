﻿using Windows.UI.Core;
using VLC_WINRT.Common;
using VLC_WinRT.Views.MusicPages;
using System;
using VLC_WinRT.Model;
using VLC_WinRT.ViewModels;

namespace VLC_WinRT.Commands.Music
{
    public class ShowCreateNewPlaylistPane : AlwaysExecutableCommand
    {
        public override async void Execute(object parameter)
        {
            await App.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Locator.MainVM.NavigationService.Go(VLCPage.CreateNewPlaylistDialog);
            });
        }
    }
}
