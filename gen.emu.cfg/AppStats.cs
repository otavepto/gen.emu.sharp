using SteamKit2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using gen.emu.types.Models.StatsAndAchievements;
using gen.emu.types.Models.MediaAssets;
using common.utils;
using common.utils.Logging;
using gen.emu.shared;
using gen.emu.cfg.SteamNetwork.CustomMsgHandler;
using gen.emu.cfg.SteamNetwork;

namespace gen.emu.cfg;

public class AppStats
{
  private static AppStats? _instance;
  public static AppStats Instance => _instance ??= new AppStats();

  enum VdfStatType
  {
    Int = 1,
    Float = 2,
    AverageRate = 3,
    Map = 4, // achievements
  }

  public async Task<(
    JsonObject OriginalSchema, IReadOnlyList<StatModel> Stats, IReadOnlyList<AchievementModel> Achievements
  )> GetUserStatsAsync(uint appid, ulong userid)
  {
    // now get an instance of our custom handler
    var myHandler = Client.Instance.GetSteamClient.GetHandler<UserStatsCustomHandler>();
    if (myHandler is null)
    {
      throw new InvalidOperationException($"Null stats handler instance");
    }

    var res = await myHandler.GetUserStats(appid, userid).ToTask().ConfigureAwait(false);
    if (res.Result != EResult.OK)
    {
      throw new InvalidDataException($"Bad stats response for appid {appid} (userid = {userid}): {res.Result}");
    }

    JsonObject jobj = [];
    using (var vdfStream = new MemoryStream(res.Msg.schema, false))
    {
      // { "730":{...}, "420":{...} }
      jobj = Helpers.LoadVdf(vdfStream, Helpers.VdfType.Binary);
    }

    var statsObjs = jobj
      // SelectMany will expand each json object to list of key-value pair
      .SelectMany(appidObj => appidObj.Value.GetKeyIgnoreCase("stats").ToObjSafe());

    var stats = await ParseStatsAsync(statsObjs, appid).ConfigureAwait(false);
    var achs = ParseAchievements(statsObjs);

    return (jobj, stats, achs);
  }

  public async Task<(IList<StatModel> Stats, IList<AchievementModel> Achievements)> ParseStatsSchemaAsync(JsonObject vdfObj)
  {
    var statsObjs = vdfObj
      // SelectMany will expand each json object to list of key-value pair
      .SelectMany(appidObj => appidObj.Value.GetKeyIgnoreCase("stats").ToObjSafe());

    var stats = await ParseStatsAsync(statsObjs).ConfigureAwait(false);
    var achs = ParseAchievements(statsObjs);
    return (stats, achs);
  }

  public Task DownloadAchievementsIconsAsync(IEnumerable<AchievementModel> achievements, uint appid, CancellationToken cancelToken = default)
  {
    var iconsUnlockedFiltered = achievements
      .Select(ach => ach.IconUnlocked)
      .Where(ic => ic.Name.Length > 0);
    var iconsLockedFiltered = achievements
      .Select(ach => ach.IconLocked)
      .Where(ic => ic.Name.Length > 0);

    // many achievements share the same icon URL hash (which is icon name here)
    var iconsDedup = new Dictionary<string, List<MediaAssetItemModel>>();
    foreach (var list in new[] { iconsUnlockedFiltered, iconsLockedFiltered })
    {
      foreach (var item in list)
      {
        if (!iconsDedup.TryGetValue(item.Name, out var iconTargetsList))
        {
          iconTargetsList = [];
          iconsDedup.Add(item.Name, iconTargetsList);
        }
        iconTargetsList.Add(item);
      }
    }

    Log.Instance.Write(Log.Kind.Debug, $"reduced icons URLs duplication: [{iconsUnlockedFiltered.Count() + iconsLockedFiltered.Count()}] -> [{iconsDedup.Count}]");

    return Utils.ParallelJobsAsync(iconsDedup, async (iconPair, _, _, ct) =>
    {
      bool gotData = false;
      foreach (var baseUrl in iconsBaseUrls)
      {
        try
        {
          var url = $"{baseUrl}/{appid}/{iconPair.Key}";
          var data = await Utils.WebRequestAsync(
            url: url,
            method: Utils.WebMethod.Get,
            cancelToken: ct
          ).ConfigureAwait(false);

          if (data is not null && data.Length > 0)
          {
            foreach (var icon in iconPair.Value)
            {
              icon.Data.TryClear();
              icon.Data.CopyFrom(data);
            }
            gotData = true;
            break; // exit on success
          }
        }
        catch
        {

        }
      }

      // throw exception to allow the parallel job function to go for another trial
      if (!gotData)
      {
        throw new InvalidOperationException($"Failed to download achievement icon, appid={appid}, icon name = '{iconPair.Key}'");
      }
    }, 30, 2, cancelToken);
  }

  async Task<List<StatModel>> ParseStatsAsync(IEnumerable<KeyValuePair<string, JsonNode?>> statsObjs, uint appid = 0, CancellationToken ct = default)
  {
    List<StatModel> results = [];
    foreach (var kv in statsObjs)
    {
      var statObj = kv.Value.ToObjSafe();
      var name = statObj.GetKeyIgnoreCase("name").ToStringSafe();
      if (string.IsNullOrEmpty(name))
      {
        continue;
      }

      var statType = (int)statObj.GetKeyIgnoreCase("type").ToNumSafe();
      StatType? type = null;
      switch ((VdfStatType)statType)
      {
        case VdfStatType.Int:
          type = StatType.Int;
          break;
        case VdfStatType.Float:
          type = StatType.Float;
          break;
        case VdfStatType.AverageRate:
          type = StatType.AverageRate;
          break;
        case VdfStatType.Map:
          // ignored
          break;
        default:
          Log.Instance.Write(Log.Kind.Error, $"Unknown stat type {statType}");
          break;
      }

      if (type is null) // unsupported type
      {
        continue;
      }

      StatModel stat = new()
      {
        InternalName = name,
        FriendlyDisplayName = statObj.GetKeyIgnoreCase("display", "name").ToStringSafe(),
        DefaultValue = statObj.GetKeyIgnoreCase("default").ToNumSafe(),
        Type = type.Value,
        IsValueIncreasesOnly = statObj.GetKeyIgnoreCase("incrementonly").ToBoolSafe(),
        IsAggregated = statObj.GetKeyIgnoreCase("aggregated").ToBoolSafe(),
      };

      {
        var idProp = statObj.GetKeyIgnoreCase("id");
        if (idProp is not null)
        {
          stat.Id = (int)idProp.ToNumSafe();
        }
        else if (int.TryParse(kv.Key, CultureInfo.InvariantCulture, out var idKey))
        {
          stat.Id = idKey;
        }
      }

      {
        // fixup default value, <= max and >= min
        var maxProp = statObj.GetKeyIgnoreCase("max");
        if (maxProp is not null)
        {
          var maxValue = maxProp.ToNumSafe();
          stat.DefaultValue = Math.Min(stat.DefaultValue, maxValue);
          stat.MaxValue = maxValue;
        }
      }

      {
        var minProp = statObj.GetKeyIgnoreCase("min");
        if (minProp is not null)
        {
          var minValue = minProp.ToNumSafe();
          stat.DefaultValue = Math.Max(stat.DefaultValue, minValue);
          stat.MinValue = minValue;
        }
      }

      {
        var maxChangesProp = statObj.GetKeyIgnoreCase("maxchange");
        if (maxChangesProp is not null)
        {
          stat.MaxChangesPerUpdate = (int)maxChangesProp.ToNumSafe();
        }
      }

      {
        var permissionsProp = statObj.GetKeyIgnoreCase("permission");
        if (permissionsProp is not null)
        {
          stat.ReadWritePermissions = (int)permissionsProp.ToNumSafe();
        }
      }

      results.Add(stat);
    }

    if (appid == 0)
    {
      Log.Instance.Write(Log.Kind.Warning, $"Skipping GlobalStats");
    }
    else
    {
      var userStatsCustomHandler = Client.Instance.GetSteamClient.GetHandler<UserStatsCustomHandler>();
      if (userStatsCustomHandler is null)
      {
        Log.Instance.Write(Log.Kind.Error, $"Failed to get UserStatsCustomHandler");
      }
      else
      {
        var lglvl = Log.Instance.StartSteps($"Downloading global total aggregate for stats");
        try
        {
          var statsNames = results.Select(ss => ss.InternalName).ToArray();
          var globalStats = await userStatsCustomHandler.GetGlobalStatsForGameAsync(appid, statsNames, ct).ConfigureAwait(false);
          foreach (var (statName, globalTotal) in globalStats)
          {
            var statObj = results.Find(ss => ss.InternalName.Equals(statName, StringComparison.OrdinalIgnoreCase));
            if (statObj is not null)
            {
              statObj.GlobalTotalValue = globalTotal;
            }
          }
          if (globalStats.Length > 0)
          {
            Log.Instance.Write(Log.Kind.Success, $"Success");
          }
          else
          {
            Log.Instance.Write(Log.Kind.Warning, $"Nothing was downloaded");
          }
        }
        catch (Exception ex)
        {
          Log.Instance.Write(Log.Kind.Error, $"Failed to get GlobalStats for appid {appid} '{ex.Message}'");
        }
        Log.Instance.EndSteps(lglvl);
      }
    }

    return results;
  }

  List<AchievementModel> ParseAchievements(IEnumerable<KeyValuePair<string, JsonNode?>> statsObjs)
  {
    var achs = statsObjs
      .Where(kv => VdfStatType.Map == (VdfStatType)kv.Value.GetKeyIgnoreCase("type").ToNumSafe()|| "ACHIEVEMENTS" == kv.Value.GetKeyIgnoreCase("type").ToStringSafe())
      .SelectMany(kv => kv.Value.GetKeyIgnoreCase("bits").ToObjSafe());

    List<AchievementModel> results = [];
    foreach (var kv in achs)
    {
      var achObj = kv.Value.ToObjSafe();
      var name = achObj.GetKeyIgnoreCase("name").ToStringSafe();
      if (string.IsNullOrEmpty(name))
      {
        continue;
      }

      var ach = new AchievementModel
      {
        InternalName = name,
        IsHidden = achObj.GetKeyIgnoreCase("display", "hidden").ToBoolSafe(),
      };


      {
        var idProp = achObj.GetKeyIgnoreCase("bit");
        if (idProp is not null)
        {
          ach.Id = (int)idProp.ToNumSafe();
        }
        else if (int.TryParse(kv.Key, CultureInfo.InvariantCulture, out var idKey))
        {
          ach.Id = idKey;
        }
      }

      {
        ach.FriendlyNameTranslations = achObj.GetKeyIgnoreCase("display", "name") ?? string.Empty;
      }
      
      {
        ach.DescriptionTranslations = achObj.GetKeyIgnoreCase("display", "desc") ?? string.Empty;
      }
      
      {
        var progressProp = achObj.GetKeyIgnoreCase("progress");
        ach.ProgressDetails = progressProp is null ? null : progressProp.ToObjSafe();
      }
      
      {
        ach.IconUnlocked.Name = achObj.GetKeyIgnoreCase("display", "icon").ToStringSafe();
        ach.IconUnlocked.NameOnDisk = Utils.SanitizeFilename(ach.IconUnlocked.Name);

        ach.IconLocked.Name = achObj.GetKeyIgnoreCase("display", "icon_gray").ToStringSafe();
        ach.IconLocked.NameOnDisk = Utils.SanitizeFilename(ach.IconLocked.Name);
      }
      
      results.Add(ach);
    }

    return results;
  }

  static readonly string[] iconsBaseUrls = [
    "https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps",
    "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps",
  ];

}
