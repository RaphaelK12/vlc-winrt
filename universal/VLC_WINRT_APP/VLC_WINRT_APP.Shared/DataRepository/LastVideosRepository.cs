﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using VLC_WINRT_APP.Helpers.MusicLibrary.EchoNest;
using VLC_WINRT_APP.Model.Video;
using VLC_WINRT_APP.ViewModels.VideoVM;

namespace VLC_WINRT_APP.DataRepository
{
    public class LastVideosRepository : IDataRepository
    {
        private static readonly string _dbPath =
    Path.Combine(
    Windows.Storage.ApplicationData.Current.LocalFolder.Path,
    "mediavlcVideos.sqlite");
        public LastVideosRepository()
        {
            Initialize();
        }

        public void Initialize()
        {
            using (var db = new SQLiteConnection(_dbPath))
            {
                db.CreateTable<VideoItem>();
            }
        }

        public void Drop()
        {
            throw new System.NotImplementedException();
        }

        public
            async Task<ObservableCollection<VideoItem>> Load()
        {
            var connection = new SQLiteAsyncConnection(_dbPath);

            return new ObservableCollection<VideoItem>(
               await connection.QueryAsync<VideoItem>(
                     "select * from VideoItem"));
        }

        public async Task<VideoItem> LoadViaToken(string token)
        {
            var connection = new SQLiteAsyncConnection(_dbPath);
            var query = connection.Table<VideoItem>().Where(x => x.Token.Equals(token));
            var result = await query.ToListAsync();
            return result.FirstOrDefault();
        }

        public Task Update(VideoItem video)
        {
            var connection = new SQLiteAsyncConnection(_dbPath);
            return connection.UpdateAsync(video);
        }

        public Task Add(VideoItem video)
        {
            // If TimeWatched is zero, no need to add it to the history
            if (video.TimeWatched == null || video.TimeWatched < TimeSpan.FromMinutes(1))
                return null;
            var connection = new SQLiteAsyncConnection(_dbPath);
            return connection.InsertAsync(video);
        }
    }
}
