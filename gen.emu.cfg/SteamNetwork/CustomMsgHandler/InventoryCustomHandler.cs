using common.utils;
using SteamKit2;
using SteamKit2.Internal;
using System.Text;
using System.Text.Json.Nodes;


// https://github.com/SteamRE/SteamKit/blob/master/Samples/010_Extending
namespace gen.emu.cfg.SteamNetwork.CustomMsgHandler;

class InventoryCustomHandler
{
  private static InventoryCustomHandler? _instance;
  public static InventoryCustomHandler Instance => _instance ??= new InventoryCustomHandler();


  public async Task<JsonArray> GetInventoryItemsDefinitionsAsync(uint appid, CancellationToken ct = default)
  {
    if (appid == 0)
    {
      throw new ArgumentException($"bad appid");
    }

    var unifiedMessages = Client.Instance.GetSteamClient.GetHandler<SteamUnifiedMessages>();
    if (unifiedMessages is null)
    {
      throw new InvalidOperationException($"Null unified messages instance");
    }

    var requestMsg = new CInventory_GetItemDefMeta_Request
    {
      appid = appid
    };

    var responseMsg = await unifiedMessages.CreateService<Inventory>().GetItemDefMeta(requestMsg).ToTask().ConfigureAwait(false);
    if (responseMsg is null || responseMsg.Result != EResult.OK)
    {
      throw new InvalidDataException($"Bad inventory definition response for appid {appid}: {responseMsg?.Result}");
    }

    var result = responseMsg.Body;
    if (result is null || string.IsNullOrEmpty(result.digest))
    {
      throw new InvalidOperationException($"Failed to deserialize inventory definition for appid {appid}: {responseMsg?.Result}");
    }

    var webRes = await Utils.WebRequestAsync(
      url: @"https://api.steampowered.com/IGameInventory/GetItemDefArchive/v1",
      method: Utils.WebMethod.Get,
      urlParams: new JsonObject
      {
        ["appid"] = appid,
        ["digest"] = result.digest,
      },
      cancelToken: ct
    ).ConfigureAwait(false);
    if (webRes is null || webRes.Length == 0)
    {
      throw new InvalidOperationException($"Bad web response for IGameInventory/GetItemDefArchive");
    }

    var resultStr = Encoding.UTF8.GetString(webRes.Reverse().SkipWhile(b => b == 0).Reverse().ToArray());
    return JsonArray.Parse(resultStr).ToArraySafe();
  }

}
