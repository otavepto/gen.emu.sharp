using common.utils;
using common.utils.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gen.emu.cfg;

public class TopOwners
{
  private static TopOwners? _instance;
  public static TopOwners Instance => _instance ??= new TopOwners();

  const string TOP_OWNERS_FILENAME = @"top_owners_ids.txt";

  public static IReadOnlyList<ulong> GetBuiltInTopOwners { get; } = new HashSet<ulong>([
    76561198213148949,
    76561198108581917,
    76561198028121353,
    76561197979911851,
    76561198355625888,
    76561198237402290,
    76561197969050296,
    76561198152618007,
    76561198001237877,
    76561198037867621,
    76561198001678750,
    76561198217186687,
    76561198094227663,
    76561197993544755,
    76561197963550511,
    76561198095049646,
    76561197973009892,
    76561197969810632,
    76561198388522904,
    76561198864213876,
    76561198166734878,
  ]).ToArray();

  bool loaded;

  readonly List<ulong> ownersIds = [];
  public IReadOnlyList<ulong> GetOwnersIds => ownersIds;


  public void Init(string baseFolder)
  {
    if (loaded)
    {
      return;
    }

    loaded = true;

    var topOwnersFilepath = Path.Combine(baseFolder, TOP_OWNERS_FILENAME);
    if (File.Exists(topOwnersFilepath))
    {
      var ids = File.ReadAllLines(topOwnersFilepath);
      foreach (var id in ids)
      {
        if (ulong.TryParse(id, CultureInfo.InvariantCulture, out var id_num))
        {
          ownersIds.Add(id_num);
        }
      }
      Log.Instance.Write(Log.Kind.Info, $"Parsed {ids.Length} owners IDs from file '{topOwnersFilepath}'");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"Top owners file '{topOwnersFilepath}' wasn't found");
    }
  }

}
