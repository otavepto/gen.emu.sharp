using common.utils;
using gen.emu.types.Generators;
using gen.emu.types.Models;
using gen.emu.types.Models.MediaAssets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;


namespace generator.media.assets;

public class MediaAssetsGenerator : IGenerator
{
  const string ACHIEVEMENT_IMAGE_FOLDER_NAME = "ach_images";
  const string ACHIEVEMENT_IMAGE_LOCKED_FOLDER_NAME = "locked";

  const string DEFAULT_ACH_ICON_RSRC_UNLOCKED = "ach_unlocked_default.jpg";
  const string DEFAULT_ACH_ICON_RSRC_LOCKED = "ach_locked_default.jpg";

  AppInfoModel appInfoModel = default!;

  string baseFolder = string.Empty;

  string iconsFolder = string.Empty;
  string commonImagesFolder = string.Empty;
  string screenshotsFolder = string.Empty;
  string screenshotsThumbnailsFolder = string.Empty;
  string promoVidFolder = string.Empty;
  string inventoryImagesFolder = string.Empty;
  string inventoryImagesLargeFolder = string.Empty;
  string achievementsIconsUnlockedFolder = string.Empty;
  string achievementsIconsLockedFolder = string.Empty;

  public string GenerateVersion()
  {
    return typeof(MediaAssetsGenerator).Assembly?.GetName()?.Version?.ToString() ?? string.Empty;
  }

  public string GenerateHelpPage()
  {
    return string.Empty;
  }

  public Task ParseArgs(IEnumerable<string> args)
  {
    // TODO
    return Task.CompletedTask;
    throw new NotImplementedException();
  }

  public Task Setup(string basepath)
  {
    baseFolder = Path.Combine(basepath, "media_assets");
    return Task.CompletedTask;
  }

  public async Task Generate(AppInfoModel appInfoModel)
  {
    this.appInfoModel = appInfoModel;

    iconsFolder = Path.Combine(baseFolder, "icons");
    commonImagesFolder = Path.Combine(baseFolder, "backgrounds");
    
    screenshotsFolder = Path.Combine(baseFolder, "screenshots", "pics");
    screenshotsThumbnailsFolder = Path.Combine(baseFolder, "screenshots", "thumbnails");
    
    promoVidFolder = Path.Combine(baseFolder, "promo_video");
    
    inventoryImagesFolder = Path.Combine(baseFolder, "inventory_images", "small");
    inventoryImagesLargeFolder = Path.Combine(baseFolder, "inventory_images", "large");
    
    achievementsIconsUnlockedFolder = Path.Combine(baseFolder, "achievements_icons", "unlocked");
    achievementsIconsLockedFolder = Path.Combine(baseFolder, "achievements_icons", "locked");

    var mediaTask = SaveMedia();
    var invTask = SaveAchievementsIcons();
    var achTask = SaveInventoryItemsIcons();

    await Task.WhenAll(mediaTask, invTask, achTask).ConfigureAwait(false);

  }

  public Task Cleanup()
  {
    appInfoModel = null!;

    iconsFolder = string.Empty;
    commonImagesFolder = string.Empty;
    screenshotsFolder = string.Empty;
    screenshotsThumbnailsFolder = string.Empty;
    promoVidFolder = string.Empty;
    inventoryImagesFolder = string.Empty;
    inventoryImagesLargeFolder = string.Empty;
    achievementsIconsUnlockedFolder = string.Empty;
    achievementsIconsLockedFolder = string.Empty;

    return Task.CompletedTask;
  }


  async Task SaveMedia()
  {
    var icons = appInfoModel.MediaAssets.Icons
      .Where(sc => sc.Data.Count > 0 && sc.NameOnDisk.Length > 0);

    var imgs = appInfoModel.MediaAssets.CommonImages
      .Where(sc => sc.Data.Count > 0 && sc.NameOnDisk.Length > 0);

    var scrn = appInfoModel.MediaAssets.Screenshots
      .Where(sc => sc.Data.Count > 0 && sc.NameOnDisk.Length > 0);

    var thumbs = appInfoModel.MediaAssets.ScreenshotsThumbnails
      .Where(sc => sc.Data.Count > 0 && sc.NameOnDisk.Length > 0);

    var assetsPack = new[] { icons, imgs, scrn, thumbs }
      .Zip([iconsFolder, commonImagesFolder, screenshotsFolder, screenshotsThumbnailsFolder]);

    foreach (var (assetCollection, assetFolder) in assetsPack)
    {
      if (assetCollection.Any())
      {
        Directory.CreateDirectory(assetFolder);
      }
    }

    {
      bool gotVid = appInfoModel.MediaAssets.Video is not null
        && appInfoModel.MediaAssets.Video.Data.Count > 0
        && appInfoModel.MediaAssets.Video.NameOnDisk.Length > 0;

      if (gotVid)
      {
        Directory.CreateDirectory(promoVidFolder);
      }
    }

    static async Task WriteAssetAsync(string basePath, MediaAssetItemModel? assetItem, CancellationToken ct)
    {
      if (assetItem is null)
      {
        return;
      }

      var filepath = Path.Combine(basePath, assetItem.NameOnDisk);
      await File.WriteAllBytesAsync(filepath, [.. assetItem.Data], ct).ConfigureAwait(false);
    }

    List<Task> tasks = [];
    foreach (var (assetCollection, assetFolder) in assetsPack)
    {
      var task = Utils.ParallelJobsAsync(assetCollection, async (assetItem, _, _, ct) =>
      {
        await WriteAssetAsync(assetFolder, assetItem, ct).ConfigureAwait(false);
      }, 4, 5);
      tasks.Add(task);
    }

    tasks.Add(WriteAssetAsync(promoVidFolder, appInfoModel.MediaAssets.Video, default));

    await Task.WhenAll(tasks).ConfigureAwait(false);

  }

  Task SaveInventoryItemsIcons()
  {
    Task iconTask = Task.CompletedTask;
    Task iconLargeTask = Task.CompletedTask;

    var icons = appInfoModel.InventoryItems.Icons.Select(ico => ico.Icon);
    if (icons.Any(ico => ico.Data.Count > 0))
    {
      Directory.CreateDirectory(inventoryImagesFolder);
      iconTask = Utils.ParallelJobsAsync(icons, async (item, _, _, ct) =>
      {
        if (item.Data.Count == 0)
        {
          return;
        }

        var filepath = Path.Combine(inventoryImagesFolder, item.NameOnDisk);
        await File.WriteAllBytesAsync(filepath, [.. item.Data], ct).ConfigureAwait(false);
      }, 30, 5);

    }

    var iconsLarge = appInfoModel.InventoryItems.Icons.Select(ico => ico.IconLarge);
    if (iconsLarge.Any(ico => ico.Data.Count > 0))
    {
      Directory.CreateDirectory(inventoryImagesLargeFolder);
      iconLargeTask = Utils.ParallelJobsAsync(iconsLarge, async (item, _, _, ct) =>
      {
        if (item.Data.Count == 0)
        {
          return;
        }

        var filepath = Path.Combine(inventoryImagesLargeFolder, item.NameOnDisk);
        await File.WriteAllBytesAsync(filepath, [.. item.Data], ct).ConfigureAwait(false);
      }, 30, 5);

    }

    return Task.WhenAll(iconTask, iconLargeTask);
  }

  Task SaveAchievementsIcons()
  {
    var achs = appInfoModel.StatsAndAchievements.Achievements;

    Task iconsUnlockedTask = Task.CompletedTask;
    Task iconsLockedTask = Task.CompletedTask;

    if (achs.Any(ach => ach.IconUnlocked.Data.Count > 0))
    {
      Directory.CreateDirectory(achievementsIconsUnlockedFolder);
      iconsUnlockedTask = Utils.ParallelJobsAsync(achs, async (item, _, _, ct) =>
      {
        if (item.IconUnlocked.Data.Count == 0)
        {
          return;
        }

        var friendlyName = Utils.SanitizeFilename(item.InternalName);
        var friendlyExt = Path.GetExtension(item.IconUnlocked.NameOnDisk);
        var filepath = Path.Combine(achievementsIconsUnlockedFolder, string.IsNullOrEmpty(friendlyName) ? item.IconUnlocked.NameOnDisk : friendlyName + friendlyExt);
        await File.WriteAllBytesAsync(filepath, [.. item.IconUnlocked.Data], ct).ConfigureAwait(false);
      }, 30, 5);
    }

    if (achs.Any(ach => ach.IconLocked.Data.Count > 0))
    {
      Directory.CreateDirectory(achievementsIconsLockedFolder);
      iconsLockedTask = Utils.ParallelJobsAsync(achs, async (item, _, _, ct) =>
      {
        if (item.IconLocked.Data.Count == 0)
        {
          return;
        }

        var friendlyName = Utils.SanitizeFilename(item.InternalName);
        var friendlyExt = Path.GetExtension(item.IconLocked.NameOnDisk);
        var filepath = Path.Combine(achievementsIconsLockedFolder, string.IsNullOrEmpty(friendlyName) ? item.IconLocked.NameOnDisk : friendlyName + friendlyExt);
        await File.WriteAllBytesAsync(filepath, [.. item.IconLocked.Data], ct).ConfigureAwait(false);
      }, 30, 5);
    }

    return Task.WhenAll(iconsUnlockedTask, iconsLockedTask);
  }

}
