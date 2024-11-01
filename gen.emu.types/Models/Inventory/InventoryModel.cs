using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.Inventory;

public class InventoryModel
{
  public JsonArray OriginalSchema { get; set; } = new();

  [JsonInclude]
  public IReadOnlyList<InventoryIconsModel> Icons { get; private set; } = new List<InventoryIconsModel>();

}
