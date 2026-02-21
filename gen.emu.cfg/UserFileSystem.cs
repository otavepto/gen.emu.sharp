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
using gen.emu.types.Models.UserFileSystem;

namespace gen.emu.cfg;

public class UserFileSystem
{
  private static UserFileSystem? _instance;
  public static UserFileSystem Instance => _instance ??= new UserFileSystem();

  public (IReadOnlyList<SaveFileModel> SaveFiles, IReadOnlyList<SaveFileOverrideModel> SaveFileOverrides) ParseUserFileSystem(JsonObject productInfo)
  {
    ArgumentNullException.ThrowIfNull(productInfo);

    var saveFilesRaw = productInfo
      .GetKeyIgnoreCase("ufs", "savefiles")
      .ToObjSafe()
      .Select(kv => kv.Value.ToObjSafe());

    var saveFiles = new List<SaveFileModel>();
    foreach (var item in saveFilesRaw)
    {
      var root = item.GetKeyIgnoreCase("root").ToStringSafe();
      if (string.IsNullOrWhiteSpace(root))
      {
        continue;
      }

      var path = item.GetKeyIgnoreCase("path").ToStringSafe();
      var pattern = item.GetKeyIgnoreCase("pattern").ToStringSafe();
      var siblings = item.GetKeyIgnoreCase("siblings").ToStringSafe();
      bool recursive = item.GetKeyIgnoreCase("recursive").ToBoolSafe();
      var platforms = item
        .GetKeyIgnoreCase("platforms")
        .ToObjSafe()
        .Select(kv => kv.Value.ToStringSafe())
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .ToHashSet();

      var newModel = new SaveFileModel
      {
        Root = root,
        PathAfterRoot = path,
        Pattern = pattern,
        Siblings = siblings,
        Recursive = recursive,
      };
      newModel.Platforms.CopyFrom(platforms);
      saveFiles.Add(newModel);
    }


    if (saveFiles.Count == 0)
    {
      return ([], []);
    }

    var rootOverridesRaw = productInfo
      .GetKeyIgnoreCase("ufs", "rootoverrides")
      .ToObjSafe()
      .Select(kv => kv.Value.ToObjSafe());

    var rootOverrides = new List<SaveFileOverrideModel>();
    foreach (var item in rootOverridesRaw)
    {
      var rootOriginal = item.GetKeyIgnoreCase("root").ToStringSafe();
      var rootNew = item.GetKeyIgnoreCase("useinstead").ToStringSafe();
      var platform = item.GetKeyIgnoreCase("os").ToStringSafe();
      if (string.IsNullOrWhiteSpace(rootOriginal) || string.IsNullOrWhiteSpace(rootNew) || string.IsNullOrWhiteSpace(platform))
      {
        Log.Instance.Write(Log.Kind.Warning, $"UFS override has empty root original/new, or empty platform");
        continue;
      }

      var osCmpOperator = item.GetKeyIgnoreCase("oscompare").ToStringSafe();
      if (!string.Equals("=", osCmpOperator, StringComparison.Ordinal))
      {
        Log.Instance.Write(Log.Kind.Warning, $"UFS override for {rootOriginal}@{platform} >> {rootNew} has unknown OS comparison operation '{osCmpOperator}'");
      }

      var pathAfterRootNew = item.GetKeyIgnoreCase("addpath").ToStringSafe();
      var pathsToTransform = item
        .GetKeyIgnoreCase("pathtransforms")
        .ToObjSafe()
        .Select(kv => kv.Value.ToObjSafe()) // 1: {...} => we want the value object
        .Select(obj =>
          new SavePathTransformModel
          {
            Find = obj.GetKeyIgnoreCase("find").ToStringSafe(),
            Replace = obj.GetKeyIgnoreCase("replace").ToStringSafe(),
          }
        );

      var newModel = new SaveFileOverrideModel
      {
        RootOriginal = rootOriginal,
        RootNew = rootNew,
        PathAfterRootNew = pathAfterRootNew,
        Platform = platform,
      };
      newModel.PathsToTransform.CopyFrom(pathsToTransform);
      rootOverrides.Add(newModel);
    }

    return (saveFiles, rootOverrides);
  }

}
