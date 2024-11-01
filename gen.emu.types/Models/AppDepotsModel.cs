using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models;

public class AppDepotsModel
{
  // backup of the original object
  public JsonObject OriginalSchema { get; set; } = new();

  [JsonInclude]
  public IReadOnlyList<uint> Depots { get; private set; } = new List<uint>();

}
