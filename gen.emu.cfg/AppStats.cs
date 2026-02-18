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
using System.Text.Json;

namespace gen.emu.cfg;

public class AppStats
{
  private static AppStats? _instance;
  public static AppStats Instance => _instance ??= new AppStats();

  enum VdfStatType
  {
    Int = 1,
    Float = 2,
    AverageRate = 3, // used by appids: 480, 500, 207140
    Map = 4, // achievements
  }

  static bool TryParseStatType(JsonNode? obj, out VdfStatType statType)
  {
    if (obj is null)
    {
      statType = default;
      return false;
    }

    bool TryParseFromNum(out VdfStatType statType)
    {
      if (!obj.TryConvertToNum(out var num))
      {
        statType = default;
        return false;
      }
      switch ((VdfStatType)num)
      {
        case VdfStatType.Int:
        case VdfStatType.Float:
        case VdfStatType.AverageRate:
        case VdfStatType.Map:
          statType = (VdfStatType)num;
          return true;
        default:
          statType = default;
          return false;
      }
    }

    bool TryParseFromStr(out VdfStatType statType)
    {
      switch (obj.ToString().Trim().ToUpperInvariant())
      {
        case "INT":
          statType = VdfStatType.Int;
          break;
        case "FLOAT":
          statType = VdfStatType.Float;
          break;
        case "AVGRATE":
          statType = VdfStatType.AverageRate;
          break;
        case "ACHIEVEMENTS":
          statType = VdfStatType.Map;
          break;
        default:
          statType = default;
          return false;
      }

      return true;
    }

    switch (obj.GetValueKind())
    {
      case JsonValueKind.Number:
        return TryParseFromNum(out statType);

      case JsonValueKind.String:
        {
          if (TryParseFromNum(out statType))
          {
            return true;
          }
          if (TryParseFromStr(out statType))
          {
            return true;
          }
        }
        break;
    }

    statType = default;
    return false;
  }

  static bool TryParseStatNumericValue(JsonNode? statVal, out double val)
  {
    if (statVal is null)
    {
      val = double.NaN;
      return false;
    }
    switch (statVal.GetValueKind())
    {
      case JsonValueKind.Number:
      case JsonValueKind.True:
      case JsonValueKind.False:
        val = statVal.ToNumSafe();
        return true;
      case JsonValueKind.String:
        {
          var statValStr = statVal.ToStringSafe()
            .Normalize(NormalizationForm.FormKC) // appid 971620 (uses FULLWIDTH DIGIT ZERO "\uFF10")
            .Trim().ToUpperInvariant()
            .Replace("O", "0", StringComparison.InvariantCultureIgnoreCase) // appid 1951780
            ;

          // ---
          // NOTE: from this point onwards the char 'O' is replaced with the number '0'
          // ---

          int mathSign = statValStr.Length > 0 && statValStr[0] == '-'
            ? -1
            : 1;
          if (mathSign == -1)
          {
            statValStr = statValStr.Substring(1);
          }
          if (new[] {"INT_MIN", "MIN_INT"}.Contains(statValStr)) // appid 2218320, 3262610
          {
            val = int.MinValue * mathSign;
            return true;
          }
          else if (new[] {"INT_MAX", "MAX_INT"}.Contains(statValStr)) // appid 2218320, 3262610
          {
            val = int.MaxValue * mathSign;
            return true;
          }
          // --- notice how the word "FLOAT" is written with a '0' -> "FL0AT"
          else if (new[] {"FL0AT_MIN", "FLT_MIN", "MIN_FL0AT", "MIN_FLT"}.Contains(statValStr)) // appid 3262610, 2721750
          {
            val = float.MinValue * mathSign;
            return true;
          }
          else if (new[] {"FL0AT_MAX", "FLT_MAX", "MAX_FL0AT", "MAX_FLT"}.Contains(statValStr)) // appid 3262610, 2721750
          {
            val = float.MaxValue * mathSign;
            return true;
          }
          // ---
          else if ("INF" == statValStr) // appid 3082220
          {
            val = mathSign == -1
              ? double.NegativeInfinity
              : double.PositiveInfinity;
            return true;
          }
          else if (JsonValue.Create(statValStr).TryConvertToNum(out val)) // appid 2721750 ("0.0")
          {
            val *= mathSign;
            return true;
          }
          else if (statValStr.Length >= 3 && statValStr.Substring(0, 2) == "0X") // appid 1402320 ("0x7FFFFFFF")
          {
            if (long.TryParse(
              statValStr.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexVal
            ))
            {
              val = hexVal * mathSign;
              return true;
            }
          }
        }
        break;
    }
    val = double.NaN;
    return false;
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

      var statTypeProp = statObj.GetKeyIgnoreCase("type");
      if (!TryParseStatType(statTypeProp, out var statType))
      {
        Log.Instance.Write(Log.Kind.Error, $"Unknown stat type '{statTypeProp}'");
        continue;
      }
      StatType? type = null;
      switch (statType)
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
          Log.Instance.Write(Log.Kind.Error, $"Unknown stat type '{statTypeProp}'");
          break;
      }

      if (type is null) // unsupported type
      {
        continue;
      }

      bool hasDefaultValue = false;
      double defaultStatValue = 0;
      var defaultProp = statObj.GetKeyIgnoreCase("default");
      if (defaultProp is not null) // appid 1520330 doesn't have a default value for some stats
      {
        hasDefaultValue = true;
        if (!TryParseStatNumericValue(defaultProp, out defaultStatValue))
        {
          defaultStatValue = 0;
          Log.Instance.Write(Log.Kind.Error, $"Stat '{name}' default value '{defaultProp}' is not convertible to a number");
        }
      }
      StatModel stat = new()
      {
        InternalName = name,
        FriendlyDisplayName = statObj.GetKeyIgnoreCase("display", "name").ToStringSafe(),
        DefaultValue = defaultStatValue,
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
        var maxChangesProp = statObj.GetKeyIgnoreCase("maxchange");
        if (maxChangesProp is not null)
        {
          if (TryParseStatNumericValue(maxChangesProp, out double maxChangesValue))
          {
            stat.MaxChangesPerUpdate = (int)maxChangesValue;
          }
          else
          {
            Log.Instance.Write(Log.Kind.Error, $"Stat '{name}' max changes per value '{maxChangesProp}' is not convertible to a number");
          }
        }
      }

      {
        var minProp = statObj.GetKeyIgnoreCase("min");
        if (minProp is not null)
        {
          if (!TryParseStatNumericValue(minProp, out double minValue))
          {
            minValue = 0;
            Log.Instance.Write(Log.Kind.Error, $"Stat '{name}' min value '{minProp}' is not convertible to a number");
          }
          stat.MinValue = minValue;
        }
      }

      {
        // fixup default value, <= max and >= min
        var maxProp = statObj.GetKeyIgnoreCase("max");
        if (maxProp is not null)
        {
          if (!TryParseStatNumericValue(maxProp, out double maxValue))
          {
            maxValue = 0;
            Log.Instance.Write(Log.Kind.Error, $"Stat '{name}' max value '{maxProp}' is not convertible to a number");
          }
          stat.MaxValue = maxValue;
        }
        else if (stat.MaxChangesPerUpdate > 0) // appid 381750 stat "NumRacesTOTAL" defines "min" prop and "maxchange", but not "max"
        {
          stat.MaxValue = stat.MinValue + stat.MaxChangesPerUpdate;
        }
      }

      // sanity check, this happens in appid 1892030 for example because
      // stat "INFLUENCE": min = "0,001", max = undefined
      // that string is translated to (int)1 while max stays 0 (the default)
      if (
        !double.IsNaN(stat.MaxValue) && !double.IsNaN(stat.MinValue) &&
        stat.MaxValue < stat.MinValue
      )
      {
        Log.Instance.Write(Log.Kind.Error, $"Stat '{name}' min value '{stat.MinValue}' is greater than its max value '{stat.MaxValue}'");
        // swap them
        (stat.MaxValue, stat.MinValue) = (stat.MinValue, stat.MaxValue);
      }

      if (!hasDefaultValue) // manually set it if none was provided
      {
        stat.DefaultValue = stat.MinValue;
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
      .Where(kv => TryParseStatType(kv.Value.GetKeyIgnoreCase("type"), out var statType) && VdfStatType.Map == statType)
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
