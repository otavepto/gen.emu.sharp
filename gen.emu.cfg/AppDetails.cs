using SteamKit2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using gen.emu.types.Models.Entitlements;
using gen.emu.types.Models.Branches;
using common.utils;
using common.utils.Logging;
using gen.emu.shared;
using gen.emu.cfg.SteamNetwork;

namespace gen.emu.cfg;

public class AppDetails
{
  private static AppDetails? _instance;
  public static AppDetails Instance => _instance ??= new AppDetails();

  public async Task<JsonObject> GetProductInfoAsync(uint appid)
  {
    if (appid == 0)
    {
      throw new InvalidOperationException($"Invalid appid");
    }

    var apps = Client.Instance.GetSteamClient.GetHandler<SteamApps>();
    if (apps is null)
    {
      throw new InvalidOperationException($"Null apps instance");
    }

    var resApps = await apps.PICSGetProductInfo(new SteamApps.PICSRequest(appid), null).ToTask().ConfigureAwait(false);
    if (resApps is null || resApps.Failed || !resApps.Complete)
    {
      throw new InvalidOperationException($"Failed to get product info for appid {appid}");
    }

    var kvObj =
      resApps.Results?.FirstOrDefault()
      ?.Apps?.Values?.FirstOrDefault()
      ?.KeyValues?.ToJsonObj();

    return kvObj.GetKeyIgnoreCase("appinfo").ToObjSafe();
  }

  public async Task<JsonObject> GetAppDetailsAsync(uint appid)
  {
    if (appid == 0)
    {
      throw new InvalidOperationException($"Invalid appid");
    }

    var detailsBytes = await Utils.WebRequestAsync(
      url: $"http://store.steampowered.com/api/appdetails",
      urlParams: new JsonObject
      {
        ["appids"] = appid,
        ["format"] = "json",
      },
      method: Utils.WebMethod.Get
    ).ConfigureAwait(false);
    if (detailsBytes is null || detailsBytes.Length == 0)
    {
      throw new InvalidOperationException($"Failed to get a web response");
    }

    var detailsStr = Encoding.UTF8.GetString(detailsBytes);
    var jnode = JsonObject.Parse(detailsStr);
    var appidJobj = jnode?.AsObject().GetKeyIgnoreCase($"{appid}");
    bool isOk = appidJobj.GetKeyIgnoreCase("success").ToBoolSafe();
    if (!isOk)
    {
      throw new InvalidDataException($"'success' property is false for appid {appid}");
    }

    return appidJobj.GetKeyIgnoreCase("data").ToObjSafe();
  }

  public (string NameInStore, string NameOnDisk) GetName(JsonObject productInfo)
  {
    ArgumentNullException.ThrowIfNull(productInfo);

    var storeName = productInfo.GetKeyIgnoreCase("common", "name").ToStringSafe();
    return (
      storeName,
      Utils.SanitizeFilename(storeName)
    );
  }

  public IReadOnlyList<string> GetSupportedLanguages(JsonObject productInfo)
  {
    ArgumentNullException.ThrowIfNull(productInfo);

    var langs_1 = productInfo
      .GetKeyIgnoreCase("common", "languages").ToObjSafe()
      .Where(o => o.Value.ToBoolSafe())
      .Select(o => o.Key);
    
    var langs_2 = productInfo
      .GetKeyIgnoreCase("common", "supported_languages").ToObjSafe()
      .Where(o => o.Value.GetKeyIgnoreCase("supported").ToBoolSafe())
      .Select(o => o.Key);

    var langs_3 = productInfo
      .GetKeyIgnoreCase("depots", "baselanguages").ToStringSafe()
      .Split(',', StringSplitOptions.RemoveEmptyEntries);

    var all_langs = langs_1
      .Concat(langs_2)
      .Concat(langs_3)
      ;

    var langs_dedup = all_langs
      .Select(s => s.Trim())
      .Where(s => s.Length > 0)
      .GroupBy(s => s.ToUpperInvariant()) // remove duplicates
      .Select(g => g.First())
      ;
      
    return langs_dedup.ToList();
  }

  public JsonObject GetLaunchConfig(JsonObject productInfo)
  {
    ArgumentNullException.ThrowIfNull(productInfo);

    return productInfo.GetKeyIgnoreCase("config", "launch").ToObjSafe();
  }

  public async Task<IDictionary<uint, ExtendedEntitlementModel>> GetDemosAsync(JsonObject appDetails, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(appDetails);

    var ids = appDetails
      .GetKeyIgnoreCase("demos")
      .ToArraySafe()
      .Select(o => (uint)o.GetKeyIgnoreCase("appid").ToNumSafe())
      .Where(id => id != 0);

    var demos = ids
      .GroupBy(id => id)
      .Select(id => id.Key)
      .ToDictionary(
      id => id,
      id =>
      {
        var model = new ExtendedEntitlementModel
        {
          NameInStore = $"Unknown demo (appid {id})"
        };
        model.NameOnDisk = Utils.SanitizeFilename(model.NameInStore);
        return model;
      }
    );

    await RetrieveEntitlementDetailsAsync(demos, cancellationToken).ConfigureAwait(false);
    return demos;
  }

  public (JsonObject OriginalSchema, IReadOnlyList<uint> Depots) GetDepots(JsonObject productInfo)
  {
    ArgumentNullException.ThrowIfNull(productInfo);

    var originalSchema = productInfo
      .GetKeyIgnoreCase("depots")
      .ToObjSafe();

    var depots = originalSchema
      .Select(o => uint.TryParse(o.Key.Trim(), CultureInfo.InvariantCulture, out var id) ? id : 0)
      .Where(id => id != 0)
      .ToHashSet()
      .ToList();

    return (originalSchema, depots);
  }

  public (JsonObject OriginalSchema, IReadOnlyList<BranchItemModel> Branches) GetBranches(JsonObject depotsOriginalSchema)
  {
    ArgumentNullException.ThrowIfNull(depotsOriginalSchema);

    var originalSchema = depotsOriginalSchema
      .GetKeyIgnoreCase("branches")
      .ToObjSafe();

    List<BranchItemModel> branches = [];
    foreach (var item in originalSchema)
    {
      ulong timeupdated = (ulong)item.Value.GetKeyIgnoreCase("timeupdated").ToNumSafe();

      var branch = new BranchItemModel
      {
        Name = item.Key,
        Description = item.Value.GetKeyIgnoreCase("description").ToStringSafe(),
        IsProtected = item.Value.GetKeyIgnoreCase("pwdrequired").ToBoolSafe(),
        BuildId = (uint)item.Value.GetKeyIgnoreCase("buildid").ToNumSafe(),
        TimeUpdated = timeupdated != 0 ? timeupdated : Utils.GetUnixEpoch(),
      };

      branches.Add(branch);
    }

    return (originalSchema, branches);
  }

  public async Task<IDictionary<uint, ExtendedEntitlementModel>> GetDlcsAsync(
    JsonObject appDetails, JsonObject productInfo,
    JsonObject launchConfigurations, JsonObject depotsOriginalSchema,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(appDetails);
    ArgumentNullException.ThrowIfNull(productInfo);

    Dictionary<uint, ExtendedEntitlementModel> dlcs = new();

    {
      var dlcsNoName = appDetails
        .GetKeyIgnoreCase("dlc")
        .ToArraySafe()
        .Select(oid => (uint)oid.ToNumSafe())
        .Where(id => id != 0);

      foreach (var item in dlcsNoName)
      {
        var model = new ExtendedEntitlementModel
        {
          NameInStore = $"Unknown DLC (common - appid {item})",
        };
        model.NameOnDisk = Utils.SanitizeFilename(model.NameInStore);
        dlcs[item] = model;
      }
    }

    {
      var dlcsExtended = productInfo
        .GetKeyIgnoreCase("extended", "listofdlc")
        .ToStringSafe()
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .Select(s => uint.TryParse(s, CultureInfo.InvariantCulture, out var id) ? id : 0)
        .Where(id => id != 0);

      foreach (var item in dlcsExtended)
      {
        var model = new ExtendedEntitlementModel
        {
          NameInStore = $"Unknown DLC (extended - appid {item})",
        };
        model.NameOnDisk = Utils.SanitizeFilename(model.NameInStore);
        dlcs[item] = model;
      }
    }

    {
      var dlcsLaunchConfig = launchConfigurations
        .Select(o => (uint)o.Value.GetKeyIgnoreCase("config", "ownsdlc").ToNumSafe())
        .Where(id => id != 0);

      foreach (var item in dlcsLaunchConfig)
      {
        var model = new ExtendedEntitlementModel
        {
          NameInStore = $"Unknown DLC (launch config - appid {item})",
        };
        model.NameOnDisk = Utils.SanitizeFilename(model.NameInStore);
        dlcs[item] = model;
      }
    }

    {
      var dlcDepots = depotsOriginalSchema
        .Select(o => (uint)o.Value.GetKeyIgnoreCase("dlcappid").ToNumSafe())
        .Where(id => id != 0);

      foreach (var item in dlcDepots)
      {
        var model = new ExtendedEntitlementModel
        {
          NameInStore = $"Unknown DLC (depot - appid {item})",
        };
        model.NameOnDisk = Utils.SanitizeFilename(model.NameInStore);
        dlcs[item] = model;
      }
    }

    {
      var dlcDepotsOptional = depotsOriginalSchema
        .Select(o => (uint)o.Value.GetKeyIgnoreCase("config", "optionaldlc").ToNumSafe())
        .Where(id => id != 0);

      foreach (var item in dlcDepotsOptional)
      {
        var model = new ExtendedEntitlementModel
        {
          NameInStore = $"Unknown DLC (depot optional - appid {item})",
        };
        model.NameOnDisk = Utils.SanitizeFilename(model.NameInStore);
        dlcs[item] = model;
      }
    }

    await RetrieveEntitlementDetailsAsync(dlcs, cancellationToken).ConfigureAwait(false);
    return dlcs;
  }


  async Task RetrieveEntitlementDetailsAsync(IDictionary<uint, ExtendedEntitlementModel> ents, CancellationToken cancellationToken)
  {
    Log.Instance.Write(Log.Kind.Debug, $"retrieving product info for [{ents.Count}] appids");
    Log.Instance.StartSteps();

    await Utils.ParallelJobsAsync(ents, async (item, _, _, cancellationToken) =>
    {
      try
      {
        item.Value.ProductInfo = await GetProductInfoAsync(item.Key).ConfigureAwait(false);
        //Log.Instance.Write(Log.Kind.Debug, $"got product info for entitlement appid [{item.Key}]");
        var (nameInStore, nameOnDisk) = GetName(item.Value.ProductInfo);
        if (!string.IsNullOrEmpty(nameInStore))
        {
          item.Value.NameInStore = nameInStore;
          item.Value.NameOnDisk = nameOnDisk;
          //Log.Instance.Write(Log.Kind.Debug, $"entitlement appid [{item.Key}] = '{nameInStore}'");
        }
        else
        {
          Log.Instance.Write(Log.Kind.Debug, $"got empty app name for entitlement appid [{item.Key}]");
        }
      }
      catch (Exception ex)
      {
        Log.Instance.Write(Log.Kind.Debug, $"failed to retrieve product info for entitlement appid [{item.Key}]: {ex.Message}");
      }

      try
      {
        item.Value.AppDetails = await GetAppDetailsAsync(item.Key).ConfigureAwait(false);
        //Log.Instance.Write(Log.Kind.Debug, $"got app details for appid [{item.Key}]");
      }
      catch (Exception ex)
      {
        Log.Instance.Write(Log.Kind.Debug, $"failed to retrieve app details for entitlement appid [{item.Key}]: {ex.Message}");
      }
    }, 10, 2, cancellationToken).ConfigureAwait(false);

    Log.Instance.EndSteps();
  }

}
