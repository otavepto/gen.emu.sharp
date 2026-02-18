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

    Exception? lastErr = null;
    var results = await Utils.ParallelJobsAsync(
    [
      $"https://store.steampowered.com/appreviews/{appid}",
      $"https://store.steampowered.com/ajaxappreviews/{appid}", // from browser network tab
    ], async (url, _, _, ctx) =>
    {
      var detailsBytes = await Utils.WebRequestAsync(
        url: url,
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
          // ["filter_offtopic_activity"] =  0, // this might return more reviewers
        },
        method: Utils.WebMethod.Get,
        cancelToken: ctx
      ).ConfigureAwait(false);
      var detailsStr = Encoding.UTF8.GetString(detailsBytes);
      var jobj = JsonObject.Parse(detailsStr).ToObjSafe();
      bool isOk = jobj.GetKeyIgnoreCase("success").ToBoolSafe();
      if (!isOk)
      {
        lastErr = new InvalidDataException($"'success' property is false");
        throw lastErr;
      }
      var reviewsArray = jobj.GetKeyIgnoreCase("reviews").ToArraySafe();
      return reviewsArray
        .Select(obj => (ulong)obj.GetKeyIgnoreCase("author", "steamid").ToNumSafe())
        .ToArray();
    }, 5, 2);

    var ids = new HashSet<ulong>(
      results.Where(idlist => idlist is not null).SelectMany(idlist => idlist)
    );
    if (ids.Count == 0 && lastErr is not null) // nothing was found due to errors
    {
      throw lastErr;
    }
    return ids.ToArray();
  }

}
