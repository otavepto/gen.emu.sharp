using common.utils;
using common.utils.Logging;
using gen.emu.cfg.SteamNetwork.CustomMsgHandler;
using gen.emu.types.Models.Inventory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace gen.emu.cfg;

public class AppInventory
{
  private static AppInventory? _instance;
  public static AppInventory Instance => _instance ??= new AppInventory();


  Dictionary<string, IList<(string Url, IReadOnlyList<byte> Data)>> DedupIcons(IEnumerable<(string Url, IReadOnlyList<byte> Data)> iconsFiltered)
  {
    var iconsDedup = new Dictionary<string, IList<(string Url, IReadOnlyList<byte> Data)>>();

    foreach (var item in iconsFiltered)
    {
      if (string.IsNullOrEmpty(item.Url))
      {
        continue;
      }

      if (!iconsDedup.TryGetValue(item.Url, out var iconTargetsList))
      {
        iconTargetsList = new List<(string Url, IReadOnlyList<byte> Data)>();
        iconsDedup.Add(item.Url, iconTargetsList);
      }
      iconTargetsList.Add(item);
    }

    return iconsDedup;
  }

  async Task DownloadIconsAsync(Dictionary<string, IList<(string Url, IReadOnlyList<byte> Data)>> iconsDedup, int maxParallelJobs, uint jobTrialsOnFailure, CancellationToken cancelToken)
  {
    await Utils.ParallelJobsAsync(iconsDedup, async (iconDetails, _, _, ct) =>
    {
      var data = await Utils.WebRequestAsync(
        url: iconDetails.Key,
        method: Utils.WebMethod.Get,
        cancelToken: ct
      ).ConfigureAwait(false);

      if (data is not null && data.Length > 0)
      {
        foreach (var icon in iconDetails.Value)
        {
          icon.Data.TryClear();
          icon.Data.CopyFrom(data);
        }
      }
    }, maxParallelJobs, jobTrialsOnFailure, cancelToken).ConfigureAwait(false);
  }


  public async Task<(JsonArray InventoryDefs, IEnumerable<InventoryIconsModel> IconsList)> GetInventoryItemsAsync(uint appid, bool downloadIcons, bool downloadLargeIcons, CancellationToken cancelToken = default)
  {
    if (appid == 0)
    {
      throw new ArgumentException($"Invalid appid");
    }

    var inventoryDefs = await InventoryCustomHandler.Instance.GetInventoryItemsDefinitionsAsync(appid, cancelToken).ConfigureAwait(false);
    Log.Instance.Write(Log.Kind.Debug, $"got inventory items schema, items count={inventoryDefs.Count}");

    if (!downloadIcons && !downloadLargeIcons)
    {
      Log.Instance.Write(Log.Kind.Debug, $"skipping downloading inventory icons");
      return (inventoryDefs, []);
    }

    var icons = inventoryDefs
      .Select(inv =>
      {
        var url = inv.GetKeyIgnoreCase("icon_url").ToStringSafe();
        var urlLarge = inv.GetKeyIgnoreCase("icon_url_large").ToStringSafe();

        var model = new InventoryIconsModel();
        model.Icon.Name = Utils.GetLastUrlComponent(url);
        model.Icon.NameOnDisk = Utils.SanitizeFilename(model.Icon.Name);

        model.IconLarge.Name = Utils.GetLastUrlComponent(urlLarge);
        model.IconLarge.NameOnDisk = Utils.SanitizeFilename(model.IconLarge.Name);

        return (
          Url: url,
          UrlLarge: urlLarge,
          Model: model
        );
      })
      .ToList();

    var iconsFiltered = icons
      .Select(ico => (ico.Url, ico.Model.Icon.Data))
      .Where(ic => ic.Url.Length > 0);
    // in case there are duplicate URLs
    var iconsDedup = DedupIcons(iconsFiltered);

    var iconsLargeFiltered = icons
      .Select(ico => (ico.UrlLarge, ico.Model.IconLarge.Data))
      .Where(ic => ic.UrlLarge.Length > 0);
    // in case there are duplicate URLs
    var iconsLargeDedup = DedupIcons(iconsLargeFiltered);

    Log.Instance.Write(Log.Kind.Debug, $"reduced icons URLs duplication: [{iconsFiltered.Count()}] -> [{iconsDedup.Count}]");
    Log.Instance.Write(Log.Kind.Debug, $"reduced large icons URLs duplication: [{iconsLargeFiltered.Count()}] -> [{iconsLargeDedup.Count}]");
    
    var iconsTask = DownloadIconsAsync(iconsDedup, 20, 2, cancelToken);
    var iconsLargeTask = DownloadIconsAsync(iconsLargeDedup, 5, 2, cancelToken);

    Log.Instance.Write(Log.Kind.Debug, $"downloading inventory items icons (WARNING takes a long time)");
    try
    {
      await Task.WhenAll(iconsTask, iconsLargeTask).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      Log.Instance.Write(Log.Kind.Debug, $"failed while downloading inventory icons: '{ex.Message}'");
    }

    var iconsList = icons.Select(ic => ic.Model);
    return (inventoryDefs, iconsList);
  }

}
