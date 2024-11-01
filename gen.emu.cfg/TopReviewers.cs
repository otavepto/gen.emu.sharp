using common.utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace gen.emu.cfg;

// https://partner.steamgames.com/doc/store/getreviews
public static class TopReviewers
{
  public static async Task<ulong[]> GetTopReviewersAsync(uint appid, uint maxCount = 20)
  {
    if (maxCount > 100)
    {
      maxCount = 100;
    }
    else if (maxCount == 0)
    {
      maxCount = 1;
    }

    var detailsBytes = await Utils.WebRequestAsync(
      url: $"https://store.steampowered.com/appreviews/{appid}",
      urlParams: new JsonObject
      {
        ["json"] =  1,
        ["filter"] =  "all",
        ["language"] =  "all",
        //["day_range"] =  365, // causes a problem for appid 730
        ["cursor"] =  '*',
        ["review_type"] =  "all",
        ["purchase_type"] =  "all",
        ["num_per_page"] =  maxCount,
      },
      method: Utils.WebMethod.Get
    ).ConfigureAwait(false);
    var detailsStr = Encoding.UTF8.GetString(detailsBytes);
    var jobj = JsonObject.Parse(detailsStr).ToObjSafe();
    bool isOk = jobj.GetKeyIgnoreCase("success").ToBoolSafe();
    if (!isOk)
    {
      throw new InvalidDataException($"'success' property is false");
    }

    var reviewsArray = jobj.GetKeyIgnoreCase("reviews").ToArraySafe();
    return reviewsArray
      .Select(obj => (ulong)obj.GetKeyIgnoreCase("author", "steamid").ToNumSafe())
      .ToArray();
  }

}
