using common.utils.Logging;
using common.utils;
using gen.emu.types.Generators;
using gen.emu.types.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace generator.achievement.watcher;

public class AchievementWatcherGenerator : IGenerator
{
  AppInfoModel appInfoModel = default!;

  string baseFolder = string.Empty;

  string appExe = string.Empty;
  string smallIconHash = string.Empty;
  List<string> supportedLangs = [];


  public Task ParseArgs(IEnumerable<string> args)
  {
    // TODO
    return Task.CompletedTask;
    throw new NotImplementedException();
  }

  public Task Setup(string basepath)
  {
    baseFolder = Path.Combine(basepath, "Achievement Watcher", "steam_cache", "schema");

    return Task.CompletedTask;
  }

  public Task Generate(AppInfoModel appInfoModel)
  {
    if (appInfoModel.StatsAndAchievements.Achievements.Count == 0)
    {
      Log.Instance.Write(Log.Kind.Warning, $"App [{appInfoModel.AppId}] has no achievements, skipping");
      return Task.CompletedTask;
    }

    this.appInfoModel = appInfoModel;

    FindAppExe();
    ParseSupportedLangs();
    FindSmallIconHash();
    GenerateAllSchemas();

    return Task.CompletedTask;
  }

  public Task Cleanup()
  {
    appInfoModel = null!;
    appExe = string.Empty;
    smallIconHash = string.Empty;
    supportedLangs.Clear();
    return Task.CompletedTask;
  }

  readonly static HashSet<string> unwanted_app_exes = [
    "launch", "start", "play", "try", "demo", "_vr",
  ];
  void FindAppExe()
  {
    string? preferredAppExe = default;
    foreach (var item in appInfoModel.LaunchConfigurations)
    {
      var exe = item.Value.GetKeyIgnoreCase("executable").ToStringSafe();
      if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      exe = exe.Split(['\\', '/']).Last().Trim();
      preferredAppExe ??= exe;
      if (!unwanted_app_exes.Any(un => exe.Contains(un, StringComparison.OrdinalIgnoreCase)))
      {
        preferredAppExe = exe;
        break;
      }
    }

    if (preferredAppExe is not null)
    {
      Log.Instance.Write(Log.Kind.Debug, $"detected app exe '{preferredAppExe}'");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Debug, $"couldn't detect app exe");
    }

    appExe = preferredAppExe ?? string.Empty;
  }

  void ParseSupportedLangs()
  {
    // lang uppercase <> actual lang string
    Dictionary<string, string> langs = [];

    foreach (var item in appInfoModel.StatsAndAchievements.Achievements)
    {
      foreach (var kv in item.FriendlyNameTranslations.ToObjSafe())
      {
        langs.TryAdd(kv.Key.ToUpperInvariant(), kv.Key);
      }
      
      foreach (var kv in item.DescriptionTranslations.ToObjSafe())
      {
        langs.TryAdd(kv.Key.ToUpperInvariant(), kv.Key);
      }
    }

    langs.Remove("TOKEN");

    if (langs.Count == 0)
    {
      if (appInfoModel.SupportedLanguages.Count == 0)
      {
        Log.Instance.Write(Log.Kind.Debug, $"no supported languages were detected, forcing english as a supported language");
        langs.TryAdd("english", "english");
      }
      else
      {
        var firstLang = appInfoModel.SupportedLanguages.First();
        Log.Instance.Write(Log.Kind.Debug, $"no languages were detected in the achievements schema, adding the first available/supported language '{firstLang}'");
        langs.TryAdd(firstLang, firstLang);
      }
    }

    supportedLangs.AddRange(langs.Values);
  }

  void FindSmallIconHash()
  {
    smallIconHash = appInfoModel.Product.ProductInfo.GetKeyIgnoreCase("common", "icon").ToStringSafe();
  }

  void GenerateAllSchemas()
  {
    foreach (var lang in supportedLangs)
    {
      var folder = Path.Combine(baseFolder, lang);
      Directory.CreateDirectory(folder);

      var schema  = GetSchemaForLang(lang);
      var filepath = Path.Combine(folder, $"{appInfoModel.AppId}.db");
      Utils.WriteJson(schema, filepath);
    }
  }

  JsonObject GetSchemaForLang(string lang)
  {
    const string ICONS_BASE_URL = "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps";

    JsonArray achList = [];
    foreach (var ach in appInfoModel.StatsAndAchievements.Achievements.OrderBy(ach => ach.Id))
    {
      var displayName = ach.FriendlyNameTranslations.GetKeyIgnoreCase(lang).ToStringSafe();
      if (string.IsNullOrEmpty(displayName)) // lang not found
      {
        // try to get first lang in the obj
        displayName = ach.FriendlyNameTranslations.ToObjSafe().FirstOrDefault().Value.ToStringSafe();
        if (string.IsNullOrEmpty(displayName)) // old format (single string) used by appid 480 for example
        {
          displayName = ach.FriendlyNameTranslations.ToStringSafe();
        }
        
        if (string.IsNullOrEmpty(displayName))
        {
          Log.Instance.Write(Log.Kind.Debug, $"achievement '{ach.InternalName}' is missing displayName");
        }
        else
        {
          Log.Instance.Write(Log.Kind.Debug, $"achievement '{ach.InternalName}' is missing displayName for lang '{lang}', using first available string");
        }
      }

      var description = ach.DescriptionTranslations.GetKeyIgnoreCase(lang).ToStringSafe();
      if (string.IsNullOrEmpty(description)) // lang not found
      {
        // try to get first lang in the obj
        description = ach.DescriptionTranslations.ToObjSafe().FirstOrDefault().Value.ToStringSafe();
        if (string.IsNullOrEmpty(description)) // old format (single string) used by appid 480 for example
        {
          description = ach.DescriptionTranslations.ToStringSafe();
        }
        
        if (string.IsNullOrEmpty(description))
        {
          Log.Instance.Write(Log.Kind.Debug, $"achievement '{ach.InternalName}' is missing description");
        }
        else
        {
          Log.Instance.Write(Log.Kind.Debug, $"achievement '{ach.InternalName}' is missing description for lang '{lang}', using first available string");
        }
      }

      var iconUnlockedUrl = string.IsNullOrEmpty(ach.IconUnlocked.Name)
        ? string.Empty
        : $"{ICONS_BASE_URL}/{appInfoModel.AppId}/{ach.IconUnlocked.Name}"
        ;
      if (string.IsNullOrEmpty (iconUnlockedUrl))
      {
        Log.Instance.Write(Log.Kind.Debug, $"achievement '{ach.InternalName}' is missing the unlocked icon");
      }
      
      var iconLockedUrl = string.IsNullOrEmpty(ach.IconLocked.Name)
        ? string.Empty
        : $"{ICONS_BASE_URL}/{appInfoModel.AppId}/{ach.IconLocked.Name}"
        ;
      if (string.IsNullOrEmpty(iconLockedUrl))
      {
        Log.Instance.Write(Log.Kind.Debug, $"achievement '{ach.InternalName}' is missing the locked icon");
      }

      var achObj = new JsonObject
      {
        ["name"] = ach.InternalName,
        ["hidden"] = ach.IsHidden ? 1 : 0,
        
        ["displayName"] = displayName,
        ["description"] = description,
        
        ["icon"] = iconUnlockedUrl,
        ["icongray"] = iconLockedUrl,
      };
      if (ach.ProgressDetails is not null )
      {
        achObj["progress"] = ach.ProgressDetails.DeepClone();
      }

      achList.Add(achObj);
    }

    var smallIconUrl = string.IsNullOrEmpty(smallIconHash)
      ? string.Empty
      : $"{ICONS_BASE_URL}/{appInfoModel.AppId}/{smallIconHash}.jpg";

    const string IMAGES_BASE_URL = "https://cdn.cloudflare.steamstatic.com/steam/apps";
    var schema = new JsonObject
    {
      ["appid"] = appInfoModel.AppId,
      ["name"] = appInfoModel.Product.NameInStore,
      ["binary"] = appExe,
      ["achievement"] = new JsonObject
      {
        ["total"] = achList.Count,
        ["list"] = achList,
      },
      ["img"] = new JsonObject
      {
        ["header"] = $"{IMAGES_BASE_URL}/{appInfoModel.AppId}/header.jpg",
        ["background"] = $"{IMAGES_BASE_URL}/{appInfoModel.AppId}/page_bg_generated_v6b.jpg",
        ["portrait"] = $"{IMAGES_BASE_URL}/{appInfoModel.AppId}/library_600x900.jpg",
        ["hero"] = $"{IMAGES_BASE_URL}/{appInfoModel.AppId}/library_hero.jpg",
        ["icon"] = smallIconUrl,
      },
      ["apiVersion"] = 1,
    };

    return schema;
  }

}
