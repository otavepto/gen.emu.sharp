using System;
using System.Text;
using System.Globalization;
using gen.emu.types.Models;
using common.utils;
using console.gen.emu.cfg;
using common.utils.Logging;
using gen.emu.cfg;
using gen.emu.cfg.SteamNetwork;
using gen.emu.cfg.SteamNetwork.CustomMsgHandler;
using gen.emu.types.Generators;
using generator.gse;
using gen.emu.types.Models.MediaAssets;
using generator.achievement.watcher;


const string BACKUPS_FOLDER_NAME = "backup";

ToolArgs.Instance.ParseCmdline(args);

if (ToolArgs.Instance.HelpOrVersion)
{
  return;
}

if (ToolArgs.Instance.GetOptions.DisableLogging)
{
  Log.Instance.TurnOff();
}
Log.Instance.SetColoredConsole(!ToolArgs.Instance.GetOptions.NoColoredConsoleLog);
if (ToolArgs.Instance.GetOptions.VerboseLogging)
{
  Log.Instance.AllowKind(Log.Kind.Debug, true);
  Log.Instance.Write(Log.Kind.Debug, $"verbose logging is on!");
}

if (ToolArgs.Instance.GetAppIds.Count == 0)
{
  throw new ArgumentException($"No valid appids were provided");
}

bool isRestoreFromBackup = ToolArgs.Instance.GetOptions.RestoreFromBackup || ToolArgs.Instance.GetOptions.IsOfflineMode;

string baseFolder = Utils.GetExeDir(ToolArgs.Instance.GetOptions.UseRelativeOutputDir);

if (!ToolArgs.Instance.GetOptions.IsOfflineMode)
{
  await OnlineLoginAsync().ConfigureAwait(false);
}

TopOwners.Instance.Init(baseFolder);

var backupFolder = Path.Combine(baseFolder, BACKUPS_FOLDER_NAME);
IGenerator[] generators = [
  new AchievementWatcherGenerator(),
  new GseGenerator(),
];

foreach (var appid in ToolArgs.Instance.GetAppIds)
{
  var filepath = Path.Combine(
    backupFolder,
    appid.ToString(CultureInfo.InvariantCulture),
    $"app_model-{appid}.json"
  );

  var appModel = new AppInfoModel
  {
    AppId = appid
  };

  var lglvapp = Log.Instance.StartSteps($"Finding info for appid {appid}");
  if (isRestoreFromBackup)
  {
    bool gotBackup = false;
    var lglvbackup = Log.Instance.StartSteps($"Restoring info from backup file '{filepath}'");
    try
    {
      var oldModel = Utils.LoadJson<AppInfoModel>(filepath);
      if (oldModel is null)
      {
        throw new InvalidOperationException($"Deserialized instance is null/empty");
      }

      appModel = oldModel;
      appModel.AppId = appid;

      gotBackup = true;
      Log.Instance.Write(Log.Kind.Success, $"Success");
    }
    catch (Exception ex)
    {
      Log.Instance.WriteException(ex);
      Log.Instance.Write(Log.Kind.Error, $"Failed to restore from backup, skipping");
    }
    Log.Instance.EndSteps(lglvbackup);

    if (!gotBackup)
    {
      continue;
    }
  }

  try
  {
    await GetAllAppInfoAsync(appModel).ConfigureAwait(false);
    if (!isRestoreFromBackup)
    {
      SaveBackup(appModel, filepath);
    }
  }
  catch (Exception e)
  {
    Log.Instance.WriteException(e);
  }

  // TODO EXTRA ARGS
  await GeneratorsRunner.Instance.RunForAppAsync(appModel, Path.Combine(baseFolder, "generated"), [], generators).ConfigureAwait(false);

  Log.Instance.EndSteps(lglvapp, $"done");
}

await Auth.Instance.ShutdownAsync().ConfigureAwait(false);

Log.Instance.Write(Log.Kind.Warning, "Disconnected!");

return;





async Task OnlineLoginAsync()
{
  Client.Instance.Init(baseFolder);
  Auth.Instance.Init(baseFolder);

  // this must be done before the background callbacks handling thread is spawned
  // otherwise we won't get any callbacks/callresults
  UserStatsCustomHandler.RegisterHandler(Client.Instance.GetSteamClient);

  var res = await Auth.Instance.LoginAsync(ToolArgs.Instance.GetOptions.Username, ToolArgs.Instance.GetOptions.Password, ToolArgs.Instance.GetOptions.AnonLogin).ConfigureAwait(false);

  Log.Instance.Write(Log.Kind.Info, "Connected!");

}

async Task GetAllAppInfoAsync(AppInfoModel appModel)
{
  if (!isRestoreFromBackup)
  {
    await GetProductInfoAsync(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping product info");
  }

  ParseAppName(appModel);
  ParseSupportedLanguages(appModel);
  ParseLaunchConfigurations(appModel);
  ParseDepots(appModel);
  ParseBranches(appModel);

  if (!ToolArgs.Instance.GetOptions.SkipAdditionalInfo && !isRestoreFromBackup)
  {
    await GetAppDetailsAsync(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping app details");
  }

  if (!ToolArgs.Instance.GetOptions.SkipInventoryItems && !isRestoreFromBackup)
  {
    await DownloadInventoryItems(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping inventory items");
  }

  if (!ToolArgs.Instance.GetOptions.SkipDemos && !isRestoreFromBackup)
  {
    await GetDemosAsync(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping demos");
  }

  if (!ToolArgs.Instance.GetOptions.SkipDlcs && !isRestoreFromBackup)
  {
    await GetDlcsAsync(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping DLCs");
  }

  if (!ToolArgs.Instance.GetOptions.SkipController && !isRestoreFromBackup)
  {
    await GetControllersInfoAsync(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping controller info");
  }

  if (!ToolArgs.Instance.GetOptions.SkipReviewers && !isRestoreFromBackup)
  {
    await GettTopReviewersAsync(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping top reviewers");
  }

  if (!ToolArgs.Instance.GetOptions.SkipStatsAndAchievements && !isRestoreFromBackup)
  {
    await GetStatsAndAchievementsSchema(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping stats & achievements schema");
  }

  if (!ToolArgs.Instance.GetOptions.SkipAchievementsIcons && !ToolArgs.Instance.GetOptions.IsOfflineMode)
  {
    await DownloadAchievementsIcons(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping achievements icons");
  }

  if (!ToolArgs.Instance.GetOptions.IsOfflineMode)
  {
    await DownloadMediaAssets(appModel).ConfigureAwait(false);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Debug, $"skipping media assets");
  }

}


async Task GetProductInfoAsync(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Getting product info");
  appModel.Product.ProductInfo = await AppDetails.Instance.GetProductInfoAsync(appModel.AppId).ConfigureAwait(false);
  Log.Instance.Write(Log.Kind.Success, $"Success");
  Log.Instance.EndSteps(lglvl);
}

void ParseAppName(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Parsing app name");
  (string nameInStore, string nameOnDisk) = AppDetails.Instance.GetName(appModel.Product.ProductInfo);
  appModel.Product.NameInStore = nameInStore;
  appModel.Product.NameOnDisk = nameOnDisk;

  if (!string.IsNullOrEmpty(appModel.Product.NameInStore))
  {
    Log.Instance.Write(Log.Kind.Success, $"App name on store: '{appModel.Product.NameInStore}'");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Warning, $"Couldn't parse app name on store");
  }

  Log.Instance.EndSteps(lglvl);
}

void ParseSupportedLanguages(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Parsing supported languages");
  appModel.SupportedLanguages.CopyFrom(
    AppDetails.Instance.GetSupportedLanguages(appModel.Product.ProductInfo)
  );

  if (appModel.SupportedLanguages.Count > 0)
  {
    Log.Instance.Write(Log.Kind.Success, $"Parsed [{appModel.SupportedLanguages.Count}] supported languages");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Error, $"Couldn't parse supported languages, app might be hidden");
  }

  Log.Instance.EndSteps(lglvl);
}

void ParseLaunchConfigurations(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Parsing launch configurations");
  appModel.LaunchConfigurations =
    AppDetails.Instance.GetLaunchConfig(appModel.Product.ProductInfo);

  if (appModel.LaunchConfigurations.Count > 0)
  {
    Log.Instance.Write(Log.Kind.Success, $"Parsed [{appModel.LaunchConfigurations.Count}] launch configurations");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Warning, $"No launch configurations were found");
  }

  Log.Instance.EndSteps(lglvl);
}

void ParseDepots(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Parsing depots");
  var (obj, list) = AppDetails.Instance.GetDepots(appModel.Product.ProductInfo);
  appModel.Depots.OriginalSchema = obj;
  appModel.Depots.Depots.CopyFrom(list);

  if (appModel.Depots.Depots.Count > 0)
  {
    Log.Instance.Write(Log.Kind.Success, $"Parsed [{appModel.Depots.Depots.Count}] depots");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Error, $"Couldn't parse depots");
  }

  Log.Instance.EndSteps(lglvl);
}

void ParseBranches(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Parsing branches");
  var (obj, list) = AppDetails.Instance.GetBranches(appModel.Depots.OriginalSchema);
  appModel.Branches.OriginalSchema = obj;
  appModel.Branches.Branches.CopyFrom(list);

  if (appModel.Branches.Branches.Count > 0)
  {
    Log.Instance.Write(Log.Kind.Success, $"Parsed [{appModel.Branches.Branches.Count}] branches");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Error, $"Couldn't parse branches");
  }

  Log.Instance.EndSteps(lglvl);
}

async Task GetAppDetailsAsync(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Getting app details");
  try
  {
    appModel.Product.AppDetails = await AppDetails.Instance.GetAppDetailsAsync(appModel.AppId).ConfigureAwait(false);
    Log.Instance.Write(Log.Kind.Success, $"Success");
  }
  catch (Exception e)
  {
    Log.Instance.Write(Log.Kind.Warning, $"Couldn't get app details, app might be not released yet, removed from store, or hidden: '{e.Message}'");
  }
  Log.Instance.EndSteps(lglvl);
}

async Task DownloadInventoryItems(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Downloading inventory items definitions");
  try
  {
    var (inventoryDefs, inventoryIcons) =
      await AppInventory.Instance.GetInventoryItemsAsync(
        appModel.AppId,
        ToolArgs.Instance.GetOptions.DownloadInventoryIcons,
        ToolArgs.Instance.GetOptions.DownloadInventoryLargeIcons
      ).ConfigureAwait(false);
    appModel.InventoryItems.OriginalSchema = inventoryDefs;
    appModel.InventoryItems.Icons.CopyFrom(inventoryIcons);

    if (appModel.InventoryItems.OriginalSchema.Count > 0)
    {
      Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.InventoryItems.OriginalSchema.Count}] inventory items definitions");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have any inventory items");
    }

    { 
      var iconsCount = appModel.InventoryItems.Icons.Count(ic => ic.Icon.Data.Count > 0);
      if (iconsCount > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{iconsCount}] inventory icons");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No inventory icons were downloaded");
      }
    }
    
    { 
      var iconsLargeCount = appModel.InventoryItems.Icons.Count(ic => ic.IconLarge.Data.Count > 0);
      if (iconsLargeCount > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{iconsLargeCount}] inventory large icons");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No inventory large icons were downloaded");
      }
    }

  }
  catch (Exception ex)
  {
    Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have any inventory items: '{ex.Message}'");
  }
  Log.Instance.EndSteps(lglvl);
}

async Task GetDemosAsync(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Getting demos");
  try
  {
    appModel.Demos.CopyFrom(
      await AppDetails.Instance.GetDemosAsync(appModel.Product.AppDetails).ConfigureAwait(false)
    );

    if (appModel.Demos.Count > 0)
    {
      Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.Demos.Count}] demos");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have any demos");
    }
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
    Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have any demos");
  }
  Log.Instance.EndSteps(lglvl);
}

async Task GetDlcsAsync(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Getting DLCs");
  try
  {
    appModel.Dlcs.CopyFrom(
      await AppDetails.Instance.GetDlcsAsync(
        appModel.Product.AppDetails, appModel.Product.ProductInfo,
        appModel.LaunchConfigurations, appModel.Depots.OriginalSchema
      ).ConfigureAwait(false)
    );

    if (appModel.Dlcs.Count > 0)
    {
      Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.Dlcs.Count}] DLCs");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have any DLCs");
    }
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
    Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have any DLCs");
  }
  Log.Instance.EndSteps(lglvl);
}

async Task GetControllersInfoAsync(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Getting controllers info");
  try
  {
    var controllersData = await ControllerData.Instance.GetControllerDataAsync(appModel.Product.ProductInfo, appModel.Demos.Select(d => d.Value)).ConfigureAwait(false);
    appModel.ControllerInfo.CopyFrom(controllersData);

    if (appModel.ControllerInfo.Count > 0)
    {
      Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.ControllerInfo.Count}] controllers info");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have public controllers info");
    }
  }
  catch (Exception ex)
  {
    Log.Instance.Write(Log.Kind.Warning, $"App doesn't seem to have public controllers info: '{ex.Message}'");
  }
  Log.Instance.EndSteps(lglvl);
}

async Task GettTopReviewersAsync(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Getting IDs of top app reviewers on the store");
  try
  {
    appModel.TopReviewers.CopyFrom(await TopReviewers.GetTopReviewersAsync(appModel.AppId, 50).ConfigureAwait(false));

    if (appModel.TopReviewers.Count > 0)
    {
      Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.TopReviewers.Count}] reviewers");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"Couldn't get reviewers, app might be recently added, removed from store, or hidden");
    }
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
    Log.Instance.Write(Log.Kind.Warning, $"Couldn't get reviewers, app might be recently added, removed from store, or hidden");
  }
  Log.Instance.EndSteps(lglvl);
}

async Task GetStatsAndAchievementsSchema(AppInfoModel appModel)
{
  List<ulong> ownersIds = [];
  if (!ToolArgs.Instance.GetOptions.AnonLogin)
  {
    ownersIds.Add(Client.Instance.GetSteamClient.SteamID.ConvertToUInt64()); // current user id
  }
  ownersIds.AddRange(appModel.TopReviewers); // steam reviewers
  ownersIds.AddRange(TopOwners.Instance.GetOwnersIds); // parsed from top_ownners.txt
  ownersIds.AddRange(TopOwners.GetBuiltInTopOwners); // builtin list
  var lglvl = Log.Instance.StartSteps($"Finding stats/achievements schema from {ownersIds.Count} owners");
  foreach (var item in ownersIds)
  {
    try
    {
      Log.Instance.Write(Log.Kind.Debug, $"Trying to get info from owner ID {item}");
      var (obj, stats, achs) = await AppStats.Instance.GetUserStatsAsync(appModel.AppId, item).ConfigureAwait(false);
      appModel.StatsAndAchievements.OriginalSchema = obj;
      appModel.StatsAndAchievements.Stats.CopyFrom(stats);
      appModel.StatsAndAchievements.Achievements.CopyFrom(achs);
      Log.Instance.StartSteps();
      Log.Instance.Write(Log.Kind.Debug, $"done");
      Log.Instance.EndSteps();
      break; // exit on success
    }
    catch
    {

    }
  }

  if (appModel.StatsAndAchievements.Stats.Count > 0)
  {
    Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.StatsAndAchievements.Stats.Count}] stats");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Warning, $"No stats were found");
  }

  if (appModel.StatsAndAchievements.Achievements.Count > 0)
  {
    Log.Instance.Write(Log.Kind.Success, $"Found [{appModel.StatsAndAchievements.Achievements.Count}] achievements");
  }
  else
  {
    Log.Instance.Write(Log.Kind.Warning, $"No achievements were found");
  }

  Log.Instance.EndSteps(lglvl);
}

async Task DownloadAchievementsIcons(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps("Downloading achievements icons");
  try
  {
    await AppStats.Instance.DownloadAchievementsIconsAsync(appModel.StatsAndAchievements.Achievements, appModel.AppId).ConfigureAwait(false);

    {
      int countUnlocked = appModel.StatsAndAchievements.Achievements.Count(ach => ach.IconUnlocked.Data.Count > 0);
      if (countUnlocked > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{countUnlocked}] unlocked achievements icons");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No unlocked achievements icons were downloaded");
      }
    }

    {
      int countLocked = appModel.StatsAndAchievements.Achievements.Count(ach => ach.IconLocked.Data.Count > 0);
      if (countLocked > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{countLocked}] locked achievements icons");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No locked achievements icons were downloaded");
      }
    }
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
  }
  Log.Instance.EndSteps(lglvl);
}

async Task DownloadMediaAssets(AppInfoModel appModel)
{
  var lglvl = Log.Instance.StartSteps($"Downloading media assets");
  try
  {
    var (resicons, resimgs) = AppMedia.Instance.DownloadIconsAndImagesAsync(appModel, ToolArgs.Instance.GetOptions.DownloadIcons, ToolArgs.Instance.GetOptions.DownloadCommonImages);
    var (respics, respicsth) = AppMedia.Instance.DownloadScreenshotsAsync(appModel, ToolArgs.Instance.GetOptions.DownloadScreenshots, ToolArgs.Instance.GetOptions.DownloadScreenshotsThumbnails);
    var resvid = ToolArgs.Instance.GetOptions.DownloadVideo
      ? AppMedia.Instance.DownloadVideoAsync(appModel.Product.AppDetails)
      : Task.FromResult<MediaAssetItemModel?>(null);
    await Task.WhenAll(respics, respicsth, resicons, resimgs, resvid).ConfigureAwait(false);

    appModel.MediaAssets.Video = resvid.Result;

    int countIcons = appModel.MediaAssets.Icons.Count(ico => ico.Data.Count > 0);
    if (countIcons > 0)
    {
      Log.Instance.Write(Log.Kind.Success, $"Downloaded [{countIcons}] icons");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"No icons were downloaded");
    }

    {
      int countCommonImages = appModel.MediaAssets.CommonImages.Count(ico => ico.Data.Count > 0);
      if (countCommonImages > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{countCommonImages}] common app images");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No common app images were downloaded");
      }
    }

    {
      int countScrn = appModel.MediaAssets.Screenshots.Count(ico => ico.Data.Count > 0);
      if (countScrn > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{countScrn}] screenshots");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No screenshots were downloaded");
      }
    }

    {
      int countThumbs = appModel.MediaAssets.ScreenshotsThumbnails.Count(ico => ico.Data.Count > 0);
      if (countThumbs > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded [{countThumbs}] screenshots thumbnails");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No screenshots thumbnails were downloaded");
      }
    }

    {
      if (appModel.MediaAssets.Video is not null && appModel.MediaAssets.Video.Data.Count > 0)
      {
        Log.Instance.Write(Log.Kind.Success, $"Downloaded video '{appModel.MediaAssets.Video.NameOnDisk}'");
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"No video asset was downloaded");
      }
    }
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
  }
  Log.Instance.EndSteps(lglvl);
}

void SaveBackup(AppInfoModel appModel, string filepath)
{
  var lglvl = Log.Instance.StartSteps($"Saving backup in: '{filepath}'");
  try
  {
    var directory = Path.GetDirectoryName(filepath);
    if (directory is null)
    {
      throw new InvalidOperationException($"Failed to get directory from filepath");
    }
    Directory.CreateDirectory(directory);
    Utils.WriteJson(appModel, filepath);
    Log.Instance.Write(Log.Kind.Success, "Success");
  }
  catch (Exception e)
  {
    Log.Instance.WriteException(e);
    Log.Instance.Write(Log.Kind.Error, $"Failed to save a backup: '{e.Message}'");
  }
  Log.Instance.EndSteps(lglvl);
}
