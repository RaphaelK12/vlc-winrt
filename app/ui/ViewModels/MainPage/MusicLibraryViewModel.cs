﻿/**********************************************************************
 * VLC for WinRT
 **********************************************************************
 * Copyright © 2013-2014 VideoLAN and Authors
 *
 * Licensed under GPLv2+ and MPLv2
 * Refer to COPYING file of the official project for license
 **********************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Newtonsoft.Json;
using VLC_WINRT.Common;
using VLC_WINRT.Model;
using VLC_WINRT.Utility.Commands;
using VLC_WINRT.Utility.Commands.MusicPlayer;
using VLC_WINRT.Utility.Helpers;
using VLC_WINRT.Utility.Helpers.MusicLibrary.LastFm;
using VLC_WINRT.Utility.Helpers.MusicLibrary.xboxmusic.Models;
using VLC_WINRT.Views.Controls.MainPage;
using XboxMusicLibrary;
using Panel = VLC_WINRT.Model.Panel;
using VLC_WINRT.Utility.Helpers.MusicLibrary;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace VLC_WINRT.ViewModels.MainPage
{
    public class MusicLibraryViewModel : BindableBase
    {
        public int nbOfFiles = 0;
        private ObservableCollection<Panel> _panels = new ObservableCollection<Panel>();
        private ObservableCollection<ArtistItemViewModel> _artists = new ObservableCollection<ArtistItemViewModel>();
        private ObservableCollection<string> _albumsCover = new ObservableCollection<string>();
        private ObservableCollection<TrackItem> _tracks = new ObservableCollection<TrackItem>();
        private ObservableCollection<AlbumItem> _favoriteAlbums = new ObservableCollection<AlbumItem>();
        private ObservableCollection<AlbumItem> _randomAlbums = new ObservableCollection<AlbumItem>();

        private StopVideoCommand _goBackCommand;
        private bool _isLoaded;
        private bool _isBusy;
        private bool _isMusicLibraryEmpty = true;

        int _numberOfTracks;
        ThreadPoolTimer _periodicTimer;
        AsyncLock _artistLock = new AsyncLock();

        // XBOX Music Stuff
        // REMOVE: Do we need this stuff anymore?
        public MusicHelper XboxMusicHelper = new MusicHelper();
        public Authenication XboxMusicAuthenication;
        ObservableCollection<string> _imgCollection = new ObservableCollection<string>();
        public MusicLibraryViewModel()
        {
            var resourceLoader = new ResourceLoader();
            _goBackCommand = new StopVideoCommand();
            Panels.Add(new Panel(resourceLoader.GetString("Artist").ToUpper(), 0, 1));
            Panels.Add(new Panel(resourceLoader.GetString("Tracks").ToUpper(), 1, 0.4));
            Panels.Add(new Panel(resourceLoader.GetString("FavoriteAlbums").ToUpper(), 2, 0.4));
        }

        public async Task Initialize()
        {
            await GetMusicFromLibrary();
        }

        public bool IsLoaded
        {
            get { return _isLoaded; }
            set { SetProperty(ref _isLoaded, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set { SetProperty(ref _isBusy, value); }
        }

        public bool IsMusicLibraryEmpty
        {
            get { return _isMusicLibraryEmpty; }
            set { SetProperty(ref _isMusicLibraryEmpty, value); }
        }

        public ObservableCollection<string> ImgCollection
        {
            get { return _imgCollection; }
            set
            {
                SetProperty(ref _imgCollection, value);
            }
        }

        public ObservableCollection<AlbumItem> FavoriteAlbums
        {
            get { return _favoriteAlbums; }
            set { SetProperty(ref _favoriteAlbums, value); }
        }

        public ObservableCollection<AlbumItem> RandomAlbums
        {
            get { return _randomAlbums; }
            set { SetProperty(ref _randomAlbums, value); }
        }
        public ObservableCollection<Panel> Panels
        {
            get { return _panels; }
            set
            {
                SetProperty(ref _panels, value);
            }
        }
        public StopVideoCommand GoBack
        {
            get { return _goBackCommand; }
            set { SetProperty(ref _goBackCommand, value); }
        }
        public ObservableCollection<ArtistItemViewModel> Artist
        {
            get { return _artists; }
            set { SetProperty(ref _artists, value); }
        }
        public ObservableCollection<string> AlbumCover
        {
            get { return _albumsCover; }
            set { SetProperty(ref _albumsCover, value); }
        }

        public ObservableCollection<TrackItem> Track
        {
            get { return _tracks; }
            set { SetProperty(ref _tracks, value); }
        }

        public async Task GetMusicFromLibrary()
        {
            nbOfFiles = (await KnownVLCLocation.MusicLibrary.GetItemsAsync()).Count;
            bool isMusicLibraryChanged = await IsMusicLibraryChanged();
            if (isMusicLibraryChanged)
            {
                await StartIndexing();
            }
            else
            {
                await DeserializeAndLoad();
            }
        }

        private async Task StartIndexing()
        {
            var musicFolder = await
                KnownVLCLocation.MusicLibrary.GetFoldersAsync(CommonFolderQuery.GroupByArtist);
            TimeSpan period = TimeSpan.FromSeconds(10);

            _periodicTimer = ThreadPoolTimer.CreatePeriodicTimer(async (source) =>
            {

                using (await _artistLock.LockAsync()) 
                    await SerializeArtistsDataBase();

                if (Locator.MusicLibraryVM.Track.Count > _numberOfTracks)
                {
                    await ImgCollection.SerializeAsJson("Artist_Img_Collection.json", null, CreationCollisionOption.ReplaceExisting);
                    await DispatchHelper.InvokeAsync(() => Locator.MusicLibraryVM._numberOfTracks = Track.Count);
                }
                else
                {
                    _periodicTimer.Cancel();
                    await DispatchHelper.InvokeAsync(() => 
                    {
                        IsLoaded = true;
                        IsBusy = false;
                    });
                }
            }, period);

            using (await _artistLock.LockAsync()) 
            foreach (var artistItem in musicFolder)
            {
                IsMusicLibraryEmpty = false;
                MusicProperties artistProperties = null;
                try
                {
                    artistProperties = await artistItem.Properties.GetMusicPropertiesAsync();
                }
                catch
                {
                }
                if (artistProperties != null && artistProperties.Artist != "")
                {
                    StorageFolderQueryResult albumQuery =
                        artistItem.CreateFolderQuery(CommonFolderQuery.GroupByAlbum);
                    var artist = new ArtistItemViewModel();
                    await artist.Initialize(albumQuery, artistProperties.Artist);
                    OnPropertyChanged("Track");
                    OnPropertyChanged("Artist");
                    Artist.Add(artist);
                }
            }
            OnPropertyChanged("Artist");
        }

        private async Task DeserializeAndLoad()
        {
            IsLoaded = true;
            var foundException = false;
            try
            {
                var artists =
                    await
                        SerializationHelper.LoadFromJsonFile<ObservableCollection<ArtistItemViewModel>>(
                            "MusicDB.json");
                if(artists != null)
                    Artist = artists;
            }
            catch (SerializationException)
            {
                foundException = true;
            }
            catch (COMException)
            {
                foundException = true;
            }

            if (Artist.Count == 0 || foundException)
            {
                await StartIndexing();
                return;
            }

            IsMusicLibraryEmpty = false;
            // If the image collection fails to load, it sets ImgCollection to null.
            // In case this happens, set the collection back to a new string collection to prevent further errors
            // if another process tries to access the collection.
            ImgCollection =
                await
                    SerializationHelper.LoadFromJsonFile<ObservableCollection<string>>("Artist_Img_Collection.json") ??
                new ObservableCollection<string>();

            foreach (AlbumItem album in Artist.SelectMany(artist => artist.Albums))
            {
                if (album.Favorite)
                {
                    RandomAlbums.Add(album);
                    FavoriteAlbums.Add(album);
                    OnPropertyChanged("FavoriteAlbums");
                }

                if (RandomAlbums.Count < 12)
                {
                    if (!album.Favorite)
                        RandomAlbums.Add(album);
                }
                foreach (TrackItem trackItem in album.Tracks)
                {
                    Track.Add(trackItem);
                }
            }
            OnPropertyChanged("Artist");
            OnPropertyChanged("Albums");
            OnPropertyChanged("Tracks");

            IsBusy = false;
        }
        public void ExecuteSemanticZoom()
        {
            var page = App.ApplicationFrame.Content as Views.MainPage;
            if (page != null)
            {
                var musicColumn = page.GetFirstDescendantOfType<MusicColumn>() as MusicColumn;
                var albumsByArtistSemanticZoom = musicColumn.GetDescendantsOfType<SemanticZoom>();
                var albumsCollection = musicColumn.Resources["albumsCollection"] as CollectionViewSource;
                if (albumsByArtistSemanticZoom != null)
                {
                    var firstlistview = albumsByArtistSemanticZoom.ElementAt(0).ZoomedOutView as ListViewBase;
                    var secondlistview = albumsByArtistSemanticZoom.ElementAt(1).ZoomedOutView as ListViewBase;
                    if (albumsCollection != null)
                    {
                        firstlistview.ItemsSource = albumsCollection.View.CollectionGroups;
                        secondlistview.ItemsSource = albumsCollection.View.CollectionGroups;
                    }
                }
            }
        }

        async Task<bool> IsMusicLibraryChanged()
        {
            var doesDBExists = await DoesFileExistHelper.DoesFileExistAsync("MusicDB.json");
            if (doesDBExists)
            {
                if (App.LocalSettings.ContainsKey("nbOfFiles"))
                {
                    if ((int)App.LocalSettings["nbOfFiles"] == nbOfFiles)
                    {
                        return false;
                    }
                    App.LocalSettings.Remove("nbOfFiles");
                }
                App.LocalSettings.Add("nbOfFiles", nbOfFiles);
            }
            return true;
        }

        public async Task SerializeArtistsDataBase()
        {
            await DispatchHelper.InvokeAsync(() => IsBusy = true);
            await SerializationHelper.SerializeAsJson(Artist, "MusicDB.json",
                null,
                CreationCollisionOption.ReplaceExisting);
            await DispatchHelper.InvokeAsync(() => IsBusy = false);
        }

        public class ArtistItemViewModel : BindableBase
        {
            private string _name;
            private string _picture;
            private bool _isPictureLoaded = false;
            private ObservableCollection<AlbumItem> _albumItems = new ObservableCollection<AlbumItem>();
            private int _currentAlbumIndex = 0;

            // more informations
            private bool _isFavorite;
            private bool _isOnlinePopularAlbumItemsLoaded = false;
            private List<Utility.Helpers.MusicLibrary.MusicEntities.Album> _onlinePopularAlbumItems;
            private bool _isOnlineRelatedArtistsLoaded = false;
            private List<Utility.Helpers.MusicLibrary.MusicEntities.Artist> _onlineRelatedArtists;
            private string _biography;

            [JsonIgnore()]
            public bool IsOnlinePopularAlbumItemsLoaded
            {
                get { return _isOnlinePopularAlbumItemsLoaded; }
                set { SetProperty(ref _isOnlinePopularAlbumItemsLoaded, value); }
            }

            [JsonIgnore()]
            public bool IsOnlineRelatedArtistsLoaded
            {
                get { return _isOnlineRelatedArtistsLoaded; }
                set { SetProperty(ref _isOnlineRelatedArtistsLoaded, value); }
            }

            public string Name
            {
                get { return _name; }
                set { SetProperty(ref _name, value); }
            }

            [JsonIgnore()]
            public string Picture
            {
                get
                {
                    if (!_isPictureLoaded)
                    {
                        if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                        {
                            // Get Artist Picture via XBOX Music
                            ArtistInformationsHelper.GetArtistPicture(this);
                            _isPictureLoaded = true;
                        }
                    }
                    return _picture;
                }
                set
                {
                    OnPropertyChanged("Picture");
                    SetProperty(ref _picture, value);
                }
            }

            public ObservableCollection<AlbumItem> Albums
            {
                get { return _albumItems; }
                set { SetProperty(ref _albumItems, value); }
            }

            [JsonIgnore()]
            public int CurrentAlbumIndex
            {
                set { SetProperty(ref _currentAlbumIndex, value); }
            }

            [JsonIgnore()]
            public AlbumItem CurrentAlbumItem
            {
                get { return _albumItems[_currentAlbumIndex]; }
            }

            [JsonIgnore()]
            public string Biography
            {
                get
                {
                    if (_biography != null)
                    {
                        return _biography;
                    }
                    if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                        return "Please verify your internet connection";
                    ArtistInformationsHelper.GetArtistBiography(this);
                    return "Loading";
                }
                set { SetProperty(ref _biography, value); }
            }

            [JsonIgnore()]
            public List<Utility.Helpers.MusicLibrary.MusicEntities.Album> OnlinePopularAlbumItems
            {
                get
                {
                    if (_onlinePopularAlbumItems != null)
                        return _onlinePopularAlbumItems;
                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                        ArtistInformationsHelper.GetArtistTopAlbums(this);
                    return null;
                }
                set { SetProperty(ref _onlinePopularAlbumItems, value); }
            }

            [JsonIgnore()]
            public List<Utility.Helpers.MusicLibrary.MusicEntities.Artist> OnlineRelatedArtists
            {
                get
                {
                    if (_onlineRelatedArtists != null)
                        return _onlineRelatedArtists;
                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                        ArtistInformationsHelper.GetArtistSimilarsArtist(this);
                    return null;
                }
                set { SetProperty(ref _onlineRelatedArtists, value); }
            }

            public bool IsFavorite
            {
                get { return _isFavorite; }
                set { SetProperty(ref _isFavorite, value); }
            }

            public async Task Initialize(StorageFolderQueryResult albumQueryResult, string artistName)
            {
                await DispatchHelper.InvokeAsync(() => Name = artistName);
                await LoadAlbums(albumQueryResult);
            }


            public ArtistItemViewModel()
            {
            }

            private async Task LoadAlbums(StorageFolderQueryResult albumQueryResult)
            {
                IReadOnlyList<StorageFolder> albumFolders = null;
                try
                {
                    albumFolders = await albumQueryResult.GetFoldersAsync();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.ToString());
                }
                if (albumFolders != null)
                {
                    foreach (var item in albumFolders)
                    {
                        AlbumItem albumItem = await GetInformationsFromMusicFile.GetAlbumItemFromFolder(item, albumQueryResult);
                        await albumItem.GetCover();
                        await DispatchHelper.InvokeAsync(() => 
                        {
                            Albums.Add(albumItem);
                            if (Locator.MusicLibraryVM.RandomAlbums.Count < 12)
                            {
                                Locator.MusicLibraryVM.RandomAlbums.Add(albumItem);
                            }
                        });
                        Locator.MusicLibraryVM.AlbumCover.Add(albumItem.Picture);
                    }
                }
            }
        }

        public class AlbumItem : BindableBase
        {
            private string _name;
            private string _artist;
            private int _currentTrackPosition;
            private string _picture = "/Assets/GreyPylon/280x156.jpg";
            private uint _year;
            private bool _favorite;
            private ObservableCollection<TrackItem> _trackItems = new ObservableCollection<TrackItem>();
            private PlayAlbumCommand _playAlbumCommand = new PlayAlbumCommand();
            private FavoriteAlbumCommand _favoriteAlbumCommand = new FavoriteAlbumCommand();
            private StorageItemThumbnail _storageItemThumbnail;

            public string Name
            {
                get { return _name; }
                set { SetProperty(ref _name, value); }
            }

            public string Artist
            {
                get { return _artist; }
                set
                {
                    SetProperty(ref _artist, value);
                }
            }

            [JsonIgnore()]
            public int CurrentTrackPosition
            {
                get { return _currentTrackPosition; }
                set { SetProperty(ref _currentTrackPosition, value); }
            }

            public bool Favorite
            {
                get { return _favorite; }
                set
                {
                    SetProperty(ref _favorite, value);
                }
            }

            public ObservableCollection<TrackItem> Tracks
            {
                get { return _trackItems; }
                set { SetProperty(ref _trackItems, value); }
            }

            public string Picture
            {
                get { return _picture; }
                set { SetProperty(ref _picture, value); }
            }

            public uint Year
            {
                get { return _year; }
                set { SetProperty(ref _year, value); }
            }

            [JsonIgnore()]
            public TrackItem CurrentTrack
            {
                get { return _trackItems[CurrentTrackPosition]; }
                set
                {
                    CurrentTrackPosition = (value == null) ? 0 : _trackItems.IndexOf(value);
                }
            }

            public void NextTrack()
            {
                if (CurrentTrackPosition < _trackItems.Count)
                    CurrentTrackPosition++;
            }

            public void PreviousTrack()
            {
                if (CurrentTrackPosition > 0)
                    CurrentTrackPosition--;
            }

            public AlbumItem(StorageItemThumbnail thumbnail, string name, string artist)
            {
                //DispatchHelper.Invoke(() =>
                //{
                Name = (name.Length == 0) ? "Album without title" : name;
                Artist = artist;
                //});
                _storageItemThumbnail = thumbnail;
            }

            public async Task GetCover()
            {
                string fileName = Artist + "_" + Name;

                // fileName needs to be scrubbed of some punctuation.
                // For example, Windows does not like question marks in file names.
                fileName = System.IO.Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, '_'));
                bool hasFoundCover = false;
                if (_storageItemThumbnail != null)
                {
                    var file =
                        await
                            ApplicationData.Current.LocalFolder.CreateFileAsync(
                                fileName + ".jpg",
                                CreationCollisionOption.ReplaceExisting);
                    var raStream = await file.OpenAsync(FileAccessMode.ReadWrite);

                    using (var thumbnailStream = _storageItemThumbnail.GetInputStreamAt(0))
                    {
                        using (var stream = raStream.GetOutputStreamAt(0))
                        {
                            await RandomAccessStream.CopyAsync(thumbnailStream, stream);
                            hasFoundCover = true;
                        }
                    }
                }
                else
                {
                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                    {
                        try
                        {
                            HttpClient lastFmClient = new HttpClient();
                            var reponse =
                                await
                                    lastFmClient.GetStringAsync(
                                        string.Format("http://ws.audioscrobbler.com/2.0/?method=album.getinfo&format=json&api_key=a8eba7d40559e6f3d15e7cca1bfeaa1c&artist={0}&album={1}", Artist, Name));
                            {
                                var albumInfo = JsonConvert.DeserializeObject<AlbumInformation>(reponse);
                                if (albumInfo.Album == null)
                                {
                                    return;
                                }
                                if (albumInfo.Album.Image == null)
                                {
                                    return;
                                }
                                // Last.FM returns images from small to 'mega',
                                // So try and get the largest image possible.
                                // If we don't get any album art, or can't find the album, return.
                                var largestImage = albumInfo.Album.Image.LastOrDefault(url => !string.IsNullOrEmpty(url.Text));
                                if (largestImage != null)
                                {
                                    hasFoundCover = true;
                                    await DownloadAndSaveHelper.SaveAsync(
                                        new Uri(largestImage.Text, UriKind.RelativeOrAbsolute),
                                        ApplicationData.Current.LocalFolder,
                                        fileName + ".jpg");
                                }
                            }
                        }
                        catch
                        {
                            Debug.WriteLine("Unable to get album Cover from LastFM API");
                        }
                    }
                }
                if (hasFoundCover)
                {
                    await DispatchHelper.InvokeAsync(() =>
                    {
                        Picture = "ms-appdata:///local/" + Artist + "_" + Name + ".jpg";
                        OnPropertyChanged("Picture");
                    });
                }
            }

            public async Task LoadTracks(IReadOnlyList<StorageFile> tracks)
            {
                if (tracks == null)
                    return;
                int i = 0;
                foreach (var track in tracks)
                {
                    i++;
                    var trackItem = await GetInformationsFromMusicFile.GetTrackItemFromFile(track, Artist, Name, i);
                    Tracks.Add(trackItem);
                    await DispatchHelper.InvokeAsync(() =>
                    {
                        Locator.MusicLibraryVM.Track.Add(trackItem);
                        OnPropertyChanged("Track");
                    });
                }
            }

            [JsonIgnore()]
            public PlayAlbumCommand PlayAlbum
            {
                get { return _playAlbumCommand; }
                set { SetProperty(ref _playAlbumCommand, value); }
            }

            [JsonIgnore()]
            public FavoriteAlbumCommand FavoriteAlbum
            {
                get { return _favoriteAlbumCommand; }
                set { SetProperty(ref _favoriteAlbumCommand, value); }
            }
        }

        public class TrackItem : BindableBase
        {
            private string _artistName;
            private string _albumName;
            private string _name;
            private string _path;
            private int _index;
            private TimeSpan _duration;
            private bool _favorite;
            private int _currentPosition;
            private PlayTrackCommand _playTrackCommand = new PlayTrackCommand();
            private FavoriteTrackCommand _favoriteTrackCommand = new FavoriteTrackCommand();

            public string ArtistName
            {
                get { return _artistName; }
                set { SetProperty(ref _artistName, value); }
            }
            public string AlbumName
            {
                get { return _albumName; }
                set { SetProperty(ref _albumName, value); }
            }

            public string Name
            {
                get { return _name; }
                set { SetProperty(ref _name, value); }
            }

            public string Path
            {
                get { return _path; }
                set { SetProperty(ref _path, value); }
            }

            public int Index
            {
                get { return _index; }
                set { SetProperty(ref _index, value); }
            }

            public TimeSpan Duration
            {
                get { return _duration; }
                set { SetProperty(ref _duration, value); }
            }
            public bool Favorite { get { return _favorite; } set { SetProperty(ref _favorite, value); } }

            [JsonIgnore()]
            public int CurrentPosition
            {
                get { return _currentPosition; }
                set { SetProperty(ref _currentPosition, value); }
            }

            [JsonIgnore()]
            public PlayTrackCommand PlayTrack
            {
                get { return _playTrackCommand; }
                set { SetProperty(ref _playTrackCommand, value); }
            }

            [JsonIgnore()]
            public FavoriteTrackCommand FavoriteTrack
            {
                get { return _favoriteTrackCommand; }
                set { SetProperty(ref _favoriteTrackCommand, value); }
            }
        }
    }
}
