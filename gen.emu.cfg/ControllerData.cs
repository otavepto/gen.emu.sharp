using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Globalization;
using gen.emu.types.Models;
using gen.emu.types.Models.Entitlements;
using common.utils;
using gen.emu.shared;
using gen.emu.cfg.SteamNetwork.CustomMsgHandler;

namespace gen.emu.cfg;

public class ControllerData
{
  private static ControllerData? _instance;
  public static ControllerData Instance => _instance ??= new ControllerData();

  public async Task<IEnumerable<ControllerInfoModel>> GetControllerDataAsync(JsonObject productInfo, IEnumerable<EntitlementModel> demos, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(productInfo);

    var controllersTouchObj = productInfo
      .GetKeyIgnoreCase("config", "steamcontrollertouchconfigdetails")
      .ToObjSafe();

    var controllersRegularObj = productInfo
      .GetKeyIgnoreCase("config", "steamcontrollerconfigdetails")
      .ToObjSafe();

    var demosControllersTouch = demos
      .Select(d => d.ProductInfo
        .GetKeyIgnoreCase("config", "steamcontrollertouchconfigdetails")
        .ToObjSafe()
      );
    
    var demosControllersRegular = demos
      .Select(d => d.ProductInfo
        .GetKeyIgnoreCase("config", "steamcontrollerconfigdetails")
        .ToObjSafe()
      );

    Dictionary<ulong, ControllerInfoModel> controllersDetails = new();
    foreach (JsonObject item in new[] {
      controllersTouchObj, controllersRegularObj
    }
    .Concat(demosControllersTouch)
    .Concat(demosControllersRegular))
    {
      controllersDetails.CopyFrom(
        item.Select(kv =>
        {
          var newModel = new ControllerInfoModel
          {
            Id = ulong.TryParse(kv.Key, CultureInfo.InvariantCulture, out var id) ? id : 0,
            ControllerType = kv.Value.GetKeyIgnoreCase("controller_type").ToStringSafe(),
            UseActionBlock = kv.Value.GetKeyIgnoreCase("use_action_block").ToBoolSafe(),
          };
          newModel.EnabledBranches.CopyFrom(
            kv.Value.GetKeyIgnoreCase("enabled_branches")
              .ToStringSafe()
              .Split(',', StringSplitOptions.RemoveEmptyEntries)
              .Select(s => s.Trim())
              .Where(s => s.Length > 0)
          );
          return newModel;
        })
        .Where(con => con.Id != 0)
        .GroupBy(con => con.Id)
        .Select(con => con.First())
        .ToDictionary(c => c.Id, c => c)
      );
    }

    var controllersFullInfo = await PublishedFileCustomHandler.Instance.GetDetailsUgcAsync(controllersDetails.Select(kv => kv.Key)).ConfigureAwait(false);
    var controllersFullInfoFiltered = controllersFullInfo.Where(ct => !string.IsNullOrEmpty(ct.file_url));
    var results = await Utils.ParallelJobsAsync(controllersFullInfoFiltered, async (item, _, _, ct) =>
    {
      var data = await Utils.WebRequestAsync(
        url: item.file_url,
        method: Utils.WebMethod.Get,
        cancelToken: ct
      ).ConfigureAwait(false);
      
      if (data is null || data.Length == 0)
      {
        throw new InvalidDataException($"Bad response for controller published file: {item.publishedfileid}");
      }
      return (
        PublishedFileId: item.publishedfileid,
        VdfData: data,
        Filename: Utils.SanitizeFilename(item.filename)
      );
    }, 5, 1, ct).ConfigureAwait(false);

    foreach (var (publishedFileId, vdfData, filename) in results)
    {
      var target = controllersDetails[publishedFileId];
      target.Filename = filename;


      using (var vdfStream = new MemoryStream(vdfData, false))
      {
        target.VdfData = Helpers.LoadVdf(vdfStream, Helpers.VdfType.Text);
      }

    }

    return controllersDetails.Select(kv => kv.Value);
  }

}
