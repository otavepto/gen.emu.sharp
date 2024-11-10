using common.utils;
using common.utils.Logging;
using gen.emu.types.Models;
using gen.emu.types.Models.MediaAssets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace gen.emu.cfg;

public class AppMedia
{
  private static AppMedia? _instance;
  public static AppMedia Instance => _instance ??= new AppMedia();

  public (Task iconsTask, Task commonImagesTask) DownloadIconsAndImagesAsync(AppInfoModel model, bool downloadIcons, bool downloadCommonImages, CancellationToken cancelToken = default)
  {
    ArgumentNullException.ThrowIfNull(model);

    var iconsTask = Task.CompletedTask;
    var commonImagesTask = Task.CompletedTask;

    if (!downloadIcons && !downloadCommonImages)
    {
      return (iconsTask, commonImagesTask);
    }

    var commonProductInfo = model.Product.ProductInfo.GetKeyIgnoreCase("common");

    if (downloadIcons)
    {
      iconsTask = Utils.ParallelJobsAsync(model.MediaAssets.Icons, async (item, _, _, ct) =>
      {
        var elements = item.Name.Split('|', 2);
        var key = elements[0];
        var extension = elements[1];
        var hash = commonProductInfo.GetKeyIgnoreCase(key).ToStringSafe();
        if (string.IsNullOrEmpty(hash))
        {
          return;
        }
        var communityImagesBaseUrl = $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{model.AppId}";
        var url = $"{communityImagesBaseUrl}/{hash}.{extension}";
        var data = await Utils.WebRequestAsync(
          url: url,
          method: Utils.WebMethod.Get,
          cancelToken: ct
        ).ConfigureAwait(false);

        if (data is not null && data.Length > 0)
        {
          item.Data.TryClear();
          item.Data.CopyFrom(data);
        }
      }, 10, 2, cancelToken);
    }

    if (downloadCommonImages)
    {
      commonImagesTask = Utils.ParallelJobsAsync(model.MediaAssets.CommonImages, async (item, _, _, ct) =>
      {
        var appImagesBaseUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{model.AppId}";
        var url = $"{appImagesBaseUrl}/{item.Name}";
        var data = await Utils.WebRequestAsync(
          url: url,
          method: Utils.WebMethod.Get,
          cancelToken: ct
        ).ConfigureAwait(false);

        if (data is not null && data.Length > 0)
        {
          item.Data.TryClear();
          item.Data.CopyFrom(data);
        }
      }, 5, 2, cancelToken);
    }

    return (iconsTask, commonImagesTask);
  }

  public (Task picsTask, Task picsThumbnailsTask) DownloadScreenshotsAsync(AppInfoModel model, bool downloadScreenshots, bool downloadScreenshotsThumbnails, CancellationToken cancelToken = default)
  {
    ArgumentNullException.ThrowIfNull(model);

    var picsTask = Task.CompletedTask;
    var thumbnailsTask = Task.CompletedTask;

    if (!downloadScreenshots && !downloadScreenshotsThumbnails)
    {
      return (picsTask, thumbnailsTask);
    }

    var screenshotsArr = model.Product.AppDetails.GetKeyIgnoreCase("screenshots").ToArraySafe();
    var screenshots = screenshotsArr
      .Select(s => s.GetKeyIgnoreCase("path_full").ToStringSafe())
      .Where(s => !string.IsNullOrEmpty(s));
    var thumbnails = screenshotsArr
      .Select(s => s.GetKeyIgnoreCase("path_thumbnail").ToStringSafe())
      .Where(s => !string.IsNullOrEmpty(s));

    if (downloadScreenshots)
    {
      picsTask = Task.Run(async () =>
      {
        var results = await Utils.ParallelJobsAsync(screenshots, async (url, jobIdx, attemptIdx, ct) =>
        {
          IReadOnlyList<byte> data = await Utils.WebRequestAsync(
            url: url,
            method: Utils.WebMethod.Get,
            cancelToken: ct
          ).ConfigureAwait(false);

          if (data is not null && data.Count > 0)
          {
            var name = Utils.GetLastUrlComponent(url);
            var filename = Utils.SanitizeFilename(name);
            var model = new MediaAssetItemModel
            {
              Name = name,
              NameOnDisk = filename,
            };
            model.Data.CopyFrom(data);
            return model;
          }

          return null;
        }, 5, 2, cancelToken).ConfigureAwait(false);

        model.MediaAssets.Screenshots.TryClear();
        model.MediaAssets.Screenshots.CopyFrom(
          results.Where(model => model is not null && model.Data.Count > 0 && model.NameOnDisk.Length > 0)
        );
      }, cancelToken);
    }

    if (downloadScreenshotsThumbnails)
    {
      thumbnailsTask = Task.Run(async () =>
      {
        var results = await Utils.ParallelJobsAsync(thumbnails, async (url, jobIdx, attemptIdx, ct) =>
        {
          IReadOnlyList<byte> data = await Utils.WebRequestAsync(
            url: url,
            method: Utils.WebMethod.Get,
            cancelToken: ct
          ).ConfigureAwait(false);

          if (data is not null && data.Count > 0)
          {
            var name = Utils.GetLastUrlComponent(url);
            var filename = Utils.SanitizeFilename(name);
            var model = new MediaAssetItemModel
            {
              Name = name,
              NameOnDisk = filename,
            };
            model.Data.CopyFrom(data);
            return model;
          }

          return null;
        }, 5, 2, cancelToken).ConfigureAwait(false);

        model.MediaAssets.ScreenshotsThumbnails.TryClear();
        model.MediaAssets.ScreenshotsThumbnails.CopyFrom(
          results.Where(model => model is not null && model.Data.Count > 0 && model.NameOnDisk.Length > 0)
        );
      }, cancelToken);
    }

    return (picsTask, thumbnailsTask);
  }

  static readonly HashSet<string> PreferredVidsNames = [
    "trailer",
    "gameplay",
    "announcement",
    "teaser",
    "promo",
    "anniversary",
  ];

  public async Task<MediaAssetItemModel?> DownloadVideoAsync(JsonObject appDetails, CancellationToken cancelToken = default)
  {
    ArgumentNullException.ThrowIfNull(appDetails);

    var vidsArr = appDetails.GetKeyIgnoreCase("movies")
      .ToArraySafe()
      .Select(vobj =>
      (
        Id: (ulong)vobj.GetKeyIgnoreCase("id").ToNumSafe(),
        Name: vobj.GetKeyIgnoreCase("name").ToStringSafe(),
        UrlWebm480: vobj.GetKeyIgnoreCase("webm", "480").ToStringSafe(),
        UrlMp4480: vobj.GetKeyIgnoreCase("mp4", "480").ToStringSafe()
      ))
      .Where(item => item.UrlMp4480.Length > 0 || item.UrlWebm480.Length > 0);
    
    (ulong Id, string Name, string UrlWebm480, string UrlMp4480)? vid = null;
    foreach (var item in vidsArr)
    {
      vid ??= item;
      if (PreferredVidsNames.Any(goodname => item.Name.Contains(goodname, StringComparison.OrdinalIgnoreCase)))
      {
        vid = item;
        break;
      }
    }

    if (vid is null)
    {
      Log.Instance.Write(Log.Kind.Debug, $"no video was found");
      return null;
    }

    var extension = ".mp4";
    var url = vid.Value.UrlMp4480;
    if (url.Length == 0)
    {
      extension = ".webm";
      url = vid.Value.UrlWebm480;
    }

    var name = Utils.SanitizeFilename(vid.Value.Name);
    if (name.Length == 0)
    {
      name = Utils.SanitizeFilename(Utils.GetLastUrlComponent(url));
      if (name.Length > 0)
      {
        extension = string.Empty;
      }
    }
    if (name.Length == 0)
    {
      name = vid.Value.Id.ToString(CultureInfo.InvariantCulture);
    }

    var data = await Utils.WebRequestAsync(
      url: url,
      method: Utils.WebMethod.Get,
      cancelToken: cancelToken
    ).ConfigureAwait(false);

    if (data is null || data.Length == 0)
    {
      Log.Instance.Write(Log.Kind.Debug, $"failed to get a web response");
      return null;
    }

    var vidAsset = new MediaAssetItemModel
    {
      Name = name,
      NameOnDisk = $"{name}{extension}",
    };
    vidAsset.Data.CopyFrom(data);
    return vidAsset;
  }

}
