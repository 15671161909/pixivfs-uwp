﻿using PixivCS;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;
using System.Web;
using Windows.Data.Json;

namespace PixivFSUWP.Data.Collections
{
    public class RecommendIllustsCollection : ObservableCollection<ViewModels.WaterfallItemViewModel>, ISupportIncrementalLoading
    {
        string nexturl = "begin";
        bool _busy = false;
        bool _emergencyStop = false;
        EventWaitHandle pause = new ManualResetEvent(true);

        public bool HasMoreItems
        {
            get => !string.IsNullOrEmpty(nexturl);
        }

        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            if (_busy)
                throw new InvalidOperationException("Only one operation in flight at a time");
            _busy = true;
            return AsyncInfo.Run((c) => LoadMoreItemsAsync(c, count));
        }

        public void StopLoading()
        {
            _emergencyStop = true;
            if (_busy)
            {
                ResumeLoading();
            }
            else
            {
                Clear();
                GC.Collect();
            }
        }

        public void PauseLoading()
        {
            pause.Reset();
        }

        public void ResumeLoading()
        {
            pause.Set();
        }

        protected async Task<LoadMoreItemsResult> LoadMoreItemsAsync(CancellationToken c, uint count)
        {
            try
            {
                if (!HasMoreItems) return new LoadMoreItemsResult() { Count = 0 };
                LoadMoreItemsResult toret = new LoadMoreItemsResult() { Count = 0 };
                PixivCS.Objects.IllustRecommended recommendres = null;
                try
                {
                    if (nexturl == "begin")
                        recommendres = await new PixivAppAPI(OverAll.GlobalBaseAPI)
                            .GetIllustRecommendedAsync();
                    else
                    {
                        Uri next = new Uri(nexturl);
                        string getparam(string param) => HttpUtility.ParseQueryString(next.Query).Get(param);
                        recommendres = await new PixivAppAPI(OverAll.GlobalBaseAPI)
                            .GetIllustRecommendedAsync(ContentType:
                                getparam("content_type"),
                                IncludeRankingLabel: bool.Parse(getparam("include_ranking_label")),
                                Filter: getparam("filter"),
                                MinBookmarkIDForRecentIllust: getparam("min_bookmark_id_for_recent_illust"),
                                MaxBookmarkIDForRecommended: getparam("max_bookmark_id_for_recommend"),
                                Offset: getparam("offset"),
                                IncludeRankingIllusts: bool.Parse(getparam("include_ranking_illusts")),
                                IncludePrivacyPolicy: getparam("include_privacy_policy"));
                    }
                }
                catch
                {
                    return toret;
                }
                nexturl = recommendres.NextUrl?.ToString() ?? "";
                foreach (var recillust in recommendres.Illusts)
                {
                    await Task.Run(() => pause.WaitOne());
                    if (_emergencyStop)
                    {
                        nexturl = "";
                        Clear();
                        return new LoadMoreItemsResult() { Count = 0 };
                    }
                    WaterfallItem recommendi = WaterfallItem.FromObject(recillust);
                    var recommendmodel = ViewModels.WaterfallItemViewModel.FromItem(recommendi);
                    await recommendmodel.LoadImageAsync();
                    Add(recommendmodel);
                    toret.Count++;
                }
                return toret;
            }
            finally
            {
                _busy = false;
                if (_emergencyStop)
                {
                    nexturl = "";
                    Clear();
                    GC.Collect();
                }
            }
        }
    }
}

